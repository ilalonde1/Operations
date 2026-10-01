using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// A Windows job object with kill-on-close: a job's PowerShell and everything it starts (sfc, DISM, a child script)
/// are in it, so ending the job ends all of them -- including when the agent itself dies, because the OS closes the
/// handle. Killing powershell.exe alone would leave its children running as SYSTEM.
/// </summary>
internal sealed class ProcessTree : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectLimitBreakawayOk = 0x00000800;
    private readonly IntPtr _job;

    /// <summary>The agent's own job (see <see cref="ContainSelf"/>); held for the life of the process, never closed by us.</summary>
    private static IntPtr _self;

    public ProcessTree() => _job = Create(JobObjectLimitKillOnJobClose);

    /// <summary>
    /// Puts the agent process itself in a kill-on-close job. Every process it starts from then on is born inside it --
    /// no window between start and containment (Codex re-check 2026-09-30) -- and when the agent exits for any reason
    /// the OS closes the handle and everything still running dies with it. Breakaway is allowed only for the idle helper,
    /// which runs in the user's session and is bounded by its own 10-second wait.
    /// </summary>
    public static void ContainSelf()
    {
        if (_self != IntPtr.Zero) return;
        var job = Create(JobObjectLimitKillOnJobClose | JobObjectLimitBreakawayOk);
        if (!AssignProcessToJobObject(job, Process.GetCurrentProcess().Handle))
        {
            var err = Marshal.GetLastWin32Error();
            CloseHandle(job);
            throw new Win32Exception(err, "could not put the agent in its job object");
        }
        _self = job;
    }

    /// <summary>
    /// Adds a just-started process (and so its future children) to this job, for per-job timeouts. If that fails the
    /// process is killed, not left running uncounted. A process that has already exited is fine as it is.
    /// </summary>
    public void Add(Process p)
    {
        if (AssignProcessToJobObject(_job, p.Handle) || p.HasExited) return;
        var err = Marshal.GetLastWin32Error();
        try { p.Kill(); } catch (InvalidOperationException) { } catch (Win32Exception) { }
        throw new Win32Exception(err, "could not contain the job's PowerShell; it was stopped");
    }

    private static IntPtr Create(uint limits)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        var info = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = limits } };
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                var err = Marshal.GetLastWin32Error();
                CloseHandle(job);   // never leak the handle of a job that could not be set up
                throw new Win32Exception(err);
            }
            return job;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public void Kill() => TerminateJobObject(_job, 1);

    /// <summary>Closing the handle kills whatever is still in the job (kill-on-close).</summary>
    public void Dispose() => CloseHandle(_job);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
