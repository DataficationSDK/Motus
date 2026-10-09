using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Motus;

/// <summary>
/// A protocol log file that any number of writers, in this process or in others, can append to
/// without overwriting each other.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FileMode.Append"/> is not enough for that. .NET opens such a file without
/// <c>O_APPEND</c> or <c>FILE_APPEND_DATA</c>, seeks to its end once, and from then on writes at an
/// offset it tracks itself. Two writers holding the same file each write from where they think its
/// end is, over the top of one another, and lines vanish without an error.
/// </para>
/// <para>
/// So each append takes a named mutex derived from the file's full path, reads the file's current
/// length and writes there, all in one call. Every writer to the same path takes the same mutex,
/// whichever process it is in. Should the mutex be unavailable on a platform, or be held past a few
/// seconds by a writer that has stopped, the append goes ahead without it rather than stall the log.
/// </para>
/// </remarks>
internal sealed class ProtocolLogFile : IDisposable
{
    /// <summary>How long an append waits for another writer before going ahead without the mutex.</summary>
    private static readonly TimeSpan s_lockTimeout = TimeSpan.FromSeconds(5);

    private readonly FileStream _stream;
    private readonly SafeFileHandle _handle;
    private readonly Mutex? _mutex;

    private ProtocolLogFile(FileStream stream, Mutex? mutex)
    {
        _stream = stream;
        _handle = stream.SafeFileHandle;
        _mutex = mutex;
    }

    /// <summary>
    /// Opens <paramref name="fullPath"/> for appending, creating it and its directory when needed.
    /// </summary>
    /// <exception cref="IOException">The file could not be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be opened.</exception>
    internal static ProtocolLogFile Open(string fullPath)
    {
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.Write,

            // Other writers append alongside this one, and the file can still be read, moved or
            // deleted while it is open.
            Share = FileShare.ReadWrite | FileShare.Delete,

            // Every write goes straight to the handle, at an offset chosen under the mutex.
            BufferSize = 0,
        };

        // The log can hold whatever a test typed, so a new file is readable by its owner only.
        // Windows has no such mode and throws if one is set; there the file takes the ACL of its
        // directory.
        if (!OperatingSystem.IsWindows())
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var stream = new FileStream(fullPath, fileOptions);
        return new ProtocolLogFile(stream, TryCreateMutex(fullPath));
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> at the end of the file as one write, so nothing another
    /// writer appends at the same moment lands inside it or over it.
    /// </summary>
    internal void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;

        var held = Enter();
        try
        {
            RandomAccess.Write(_handle, bytes, RandomAccess.GetLength(_handle));
        }
        finally
        {
            if (held)
                _mutex!.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        _mutex?.Dispose();
        _stream.Dispose();
    }

    /// <summary>
    /// The name of the mutex guarding a file. Hashed, because a path holds characters a mutex name
    /// may not, and folded to one case where the file system ignores case, so two spellings of one
    /// path meet on one mutex.
    /// </summary>
    internal static string MutexName(string fullPath)
    {
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? fullPath.ToUpperInvariant()
            : fullPath;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "Motus.ProtocolLog." + Convert.ToHexString(hash, 0, 16);
    }

    private static Mutex? TryCreateMutex(string fullPath)
    {
        try
        {
            return new Mutex(initiallyOwned: false, MutexName(fullPath));
        }
        catch (Exception ex)
        {
            // Some platforms have no named mutexes, and a sandbox can deny the files they live in.
            // Appends then go unguarded, which is only a problem when another writer shares the path.
            Debug.WriteLine(ex);
            return null;
        }
    }

    private bool Enter()
    {
        if (_mutex is null)
            return false;

        try
        {
            return _mutex.WaitOne(s_lockTimeout);
        }
        catch (AbandonedMutexException)
        {
            // A writer that held it has exited. The mutex is this thread's now.
            return true;
        }
    }
}
