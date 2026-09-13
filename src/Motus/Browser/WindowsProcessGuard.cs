using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Motus;

/// <summary>
/// Ties a launched browser's lifetime to this process on Windows.
/// </summary>
/// <remarks>
/// Everywhere else a browser Motus starts is driven over a pipe, and a pipe is what tells the
/// browser its launcher has gone: the far end closes and the browser exits, however the launcher
/// died. Windows offers no way to hand a child the two descriptors Chromium reads and writes CDP
/// on, so the browser there keeps a debugging port, and a port says nothing about who is still
/// alive.
///
/// A job object says it instead. The browser is made a member of a job marked to end its members
/// when the last handle to the job closes, and every handle a process holds closes when that
/// process exits, killed or not. The handle is held here for as long as the browser is wanted.
///
/// Creating the job is best effort. Without it the browser is simply not tied to this process,
/// which is where things stood before, and that is not a reason to refuse to launch.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessGuard : IDisposable
{
    /// <summary>Ends every process in the job when the last handle to the job closes.</summary>
    private const int JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>The information class for <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.</summary>
    private const int JobObjectExtendedLimitInformation = 9;

    private IntPtr _job;
    private bool _disposed;

    private WindowsProcessGuard(IntPtr job) => _job = job;

    /// <summary>
    /// Puts the given process in a job that ends it when this process goes, or returns null when
    /// the operating system would not arrange that.
    /// </summary>
    internal static WindowsProcessGuard? TryAdopt(Process process)
    {
        var job = IntPtr.Zero;

        try
        {
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                return null;

            var limits = new JobObjectExtendedLimitInformationLayout();
            limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

            var size = Marshal.SizeOf<JobObjectExtendedLimitInformationLayout>();
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);

                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size)
                    || !AssignProcessToJobObject(job, process.Handle))
                {
                    CloseHandle(job);
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return new WindowsProcessGuard(job);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or InvalidOperationException or PlatformNotSupportedException)
        {
            if (job != IntPtr.Zero)
                CloseHandle(job);

            return null;
        }
    }

    /// <summary>
    /// Releases the job. Safe once the browser has already been ended, which is the only place
    /// this is called from, because closing the handle while the browser is still running would
    /// end it there and then.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformationLayout
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public int LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public int ActiveProcessLimit;
        public nuint Affinity;
        public int PriorityClass;
        public int SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCountersLayout
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationLayout
    {
        public JobObjectBasicLimitInformationLayout BasicLimitInformation;
        public IoCountersLayout IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
