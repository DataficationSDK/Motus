using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// The boundaries the server keeps around the machine it runs on: where tools may write, where they
/// may read from, whether a page may be opened from the local filesystem, and whether the session
/// may be pointed at another browser while it runs.
/// </summary>
/// <remarks>
/// <para>
/// An agent driving this server acts on instructions that partly come from the pages it visits, so
/// the defaults assume the instructions cannot be trusted. Writes land in one directory, reads come
/// from the directories the client said it was working in, <c>file:</c> URLs are refused, and
/// attaching to a browser that is already running is something the operator opts into rather than
/// something the agent decides.
/// </para>
/// <para>
/// Every rule here answers with a message rather than an exception, so a tool returns guidance the
/// model can act on (restart the server with the flag, or pass a relative path) instead of a
/// protocol error.
/// </para>
/// </remarks>
public sealed class SecurityPolicy
{
    /// <summary>The option that lifts the read, write, and <c>file:</c> boundaries together.</summary>
    public const string UnrestrictedFileAccessOption = "--allow-unrestricted-file-access";

    /// <summary>The option that lets the attach tool point the session at a running browser.</summary>
    public const string AllowAttachOption = "--allow-attach";

    private static readonly Lazy<SecurityPolicy> LazyDefault = new(() => new SecurityPolicy(new McpServerLaunchOptions()));

    /// <summary>Derives the boundaries from the options the server was started with.</summary>
    public SecurityPolicy(McpServerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        AllowUnrestrictedFileAccess = options.AllowUnrestrictedFileAccess;
        AllowAttach = options.AllowAttach || !string.IsNullOrEmpty(options.Endpoint);
        OutputDirectory = Path.GetFullPath(options.OutputDirectory ?? CreateDefaultOutputDirectory());
    }

    /// <summary>
    /// The boundaries a server started with no options at all would keep. Tools fall back to this
    /// when they are called outside a host that supplies one, so a missing registration is
    /// restrictive rather than permissive.
    /// </summary>
    public static SecurityPolicy Default => LazyDefault.Value;

    /// <summary>The directory that tools writing a file resolve their paths inside.</summary>
    public string OutputDirectory { get; }

    /// <summary>Whether reads, writes, and <c>file:</c> URLs are unbounded.</summary>
    public bool AllowUnrestrictedFileAccess { get; }

    /// <summary>Whether the session may be pointed at a browser that is already running.</summary>
    public bool AllowAttach { get; }

    /// <summary>
    /// Supplies the directories reads are confined to instead of asking the client for its roots.
    /// For tests, which have no client to ask.
    /// </summary>
    internal Func<CancellationToken, ValueTask<IReadOnlyList<string>>>? ReadRootsOverride { get; init; }

    /// <summary>
    /// Names a directory under the system temporary directory for this run. The directory is not
    /// created here; the first tool that writes creates it.
    /// </summary>
    public static string CreateDefaultOutputDirectory()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var unique = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(Path.GetTempPath(), $"motus-mcp-{stamp}-{unique}");
    }

    /// <summary>
    /// Says why attaching to a running browser is refused, or null when it is allowed.
    /// </summary>
    public string? RefuseAttach()
        => AllowAttach
            ? null
            : "Attaching to a running browser is disabled. Start the server with " + AllowAttachOption
                + " to allow it during a session, or with --connect <endpoint> to attach at startup.";

    /// <summary>
    /// Says why a URL may not be opened, or null when it may. Only the local filesystem scheme is
    /// refused; a value with no scheme is left alone, because the browser resolves it the same way
    /// a typed address would be.
    /// </summary>
    public string? RefuseUrl(string? url)
        => !AllowUnrestrictedFileAccess && IsFileUrl(url)
            ? "file:// navigation is disabled. Start the server with " + UnrestrictedFileAccessOption
                + " to enable it."
            : null;

    /// <summary>
    /// Says why a set of response headers may not be used, or null when they may. A redirect names
    /// its destination in a header rather than in a URL argument, so the same rule has to reach it.
    /// </summary>
    public string? RefuseHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || AllowUnrestrictedFileAccess)
            return null;

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase)
                && RefuseUrl(header.Value) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves where a tool that writes a file should write it, creating the output directory if
    /// this is the first write.
    /// </summary>
    /// <param name="requested">
    /// The path the caller asked for, relative to the output directory, or null for a generated
    /// name.
    /// </param>
    /// <param name="prefix">Filename prefix for a generated name.</param>
    /// <param name="extension">Filename extension, including the dot.</param>
    /// <param name="resolved">The absolute path to write to, when this returns true.</param>
    /// <param name="refusal">The message to return to the caller, when this returns false.</param>
    public bool TryResolveOutputPath(
        string? requested,
        string prefix,
        string extension,
        out string resolved,
        out string? refusal)
    {
        refusal = null;

        if (string.IsNullOrWhiteSpace(requested))
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var unique = Guid.NewGuid().ToString("N")[..8];
            resolved = Path.Combine(EnsureOutputDirectory(), $"{prefix}-{stamp}-{unique}{extension}");
            return true;
        }

        if (AllowUnrestrictedFileAccess)
        {
            resolved = Path.GetFullPath(requested);
            return true;
        }

        var root = EnsureOutputDirectory();

        if (Path.IsPathRooted(requested))
        {
            resolved = string.Empty;
            refusal = OutsideOutputDirectory();
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, requested));
        if (!IsInside(RealPath(candidate), RealPath(root)))
        {
            resolved = string.Empty;
            refusal = OutsideOutputDirectory();
            return false;
        }

        resolved = candidate;
        return true;
    }

    /// <summary>
    /// Says why a file may not be read, or null when it may. Reads are confined to the directories
    /// the client reports as its roots; a client that reports none leaves the server's own working
    /// directory and its output directory.
    /// </summary>
    public async ValueTask<string?> RefuseReadAsync(string path, McpServer? server, CancellationToken cancellationToken)
    {
        if (AllowUnrestrictedFileAccess)
            return null;

        var roots = await ReadRootsAsync(server, cancellationToken).ConfigureAwait(false);
        return RefuseRead(path, roots);
    }

    /// <summary>
    /// Says why a file outside <paramref name="roots"/> may not be read, or null when it may. For a
    /// tool holding several paths: it reads the roots once and checks every path against that one
    /// answer, so one tool call asks the client once.
    /// </summary>
    internal string? RefuseRead(string path, IReadOnlyList<string> roots)
    {
        if (AllowUnrestrictedFileAccess)
            return null;

        var real = RealPath(Path.GetFullPath(path));

        foreach (var root in roots)
        {
            if (IsInside(real, RealPath(root)))
                return null;
        }

        return $"Reading '{path}' is not allowed. Reads are confined to {string.Join(", ", roots)}. "
            + "Start the server with " + UnrestrictedFileAccessOption + " to lift this.";
    }

    /// <summary>
    /// The directories reads are currently confined to, asked of the client every time they are
    /// needed.
    /// </summary>
    /// <remarks>
    /// A client may move its roots while the session is running, and an answer kept from an earlier
    /// call would go on allowing a directory the client has since let go of. The only tool that
    /// reads a file is one a person asked for, so a round trip per call costs nothing anyone
    /// notices.
    /// </remarks>
    internal async ValueTask<IReadOnlyList<string>> ReadRootsAsync(McpServer? server, CancellationToken cancellationToken)
    {
        var roots = await AskForRootsAsync(server, cancellationToken).ConfigureAwait(false);
        if (roots.Count > 0)
            return roots;

        // No client roots: the server's own working directory is what the operator started it in,
        // and the output directory holds what this session produced, so a file the agent was told
        // to upload is reachable without opening the whole machine.
        return [Path.GetFullPath(Directory.GetCurrentDirectory()), OutputDirectory];
    }

    private async ValueTask<IReadOnlyList<string>> AskForRootsAsync(McpServer? server, CancellationToken cancellationToken)
    {
        if (ReadRootsOverride is { } supplied)
            return await supplied(cancellationToken).ConfigureAwait(false);

        if (server?.ClientCapabilities?.Roots is null)
            return [];

        try
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken)
                .ConfigureAwait(false);

            return [.. result.Roots
                .Select(root => LocalPathOf(root.Uri))
                .Where(path => path is not null)
                .Select(path => path!)];
        }
        catch (Exception)
        {
            // A client that advertises roots and then fails to list them tells us nothing about
            // what is safe to read, so fall back to the directories that need no client.
            return [];
        }
    }

    private static string? LocalPathOf(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            return null;

        try
        {
            return Path.GetFullPath(parsed.LocalPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string EnsureOutputDirectory()
    {
        Directory.CreateDirectory(OutputDirectory);
        return OutputDirectory;
    }

    private string OutsideOutputDirectory()
        => $"Paths must stay inside the output directory {OutputDirectory}. Start the server with "
            + UnrestrictedFileAccessOption + " to lift this.";

    private static bool IsFileUrl(string? url)
        => url is not null && url.AsSpan().TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string candidate, string root)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var normalized = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return candidate.Equals(root.TrimEnd(Path.DirectorySeparatorChar), comparison)
            || candidate.StartsWith(normalized, comparison);
    }

    /// <summary>
    /// Follows symbolic links along the whole path, so a link planted inside the output directory
    /// cannot be used to step outside it. Segments that do not exist yet are kept as written, which
    /// is what a file about to be created looks like.
    /// </summary>
    private static string RealPath(string path) => RealPath(path, depth: 0);

    private static string RealPath(string path, int depth)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);

        // A link whose target is itself reached through links is followed too, up to a depth that
        // no real tree reaches and a crafted one cannot spin on.
        if (string.IsNullOrEmpty(root) || depth > 16)
            return full;

        var current = root;
        foreach (var segment in full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (ResolveLink(current) is { } target)
                current = RealPath(Path.GetFullPath(target, Path.GetDirectoryName(current) ?? root), depth + 1);
        }

        return current;
    }

    private static string? ResolveLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists)
                return null;

            try
            {
                return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
            catch (IOException)
            {
                // A broken or circular chain: the first hop is still the truth about where this
                // segment points.
                return info.ResolveLinkTarget(returnFinalTarget: false)?.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
