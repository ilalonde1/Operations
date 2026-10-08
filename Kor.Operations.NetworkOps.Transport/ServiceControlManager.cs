#nullable enable
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kor.Operations.NetworkOps.Transport;

// Typed access to a remote machine's Service Control Manager over the svcctl named pipe --
// the one control channel that works on every workstation here (WinRM and the RPC dynamic
// range are blocked). This replaces screen-scraping sc.exe output with regexes, which the
// PowerShell prototype did and which the 2026-09-07 port decision named as the reason to port.
internal static class ServiceControlManager
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint Delete = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceDemandStart = 0x00000003;
    private const uint ServiceErrorIgnore = 0x00000000;

    public const int ErrorServiceRequestTimeout = 1053;   // "did not respond": expected, cmd is not a service
    public const int ErrorServiceDoesNotExist = 1060;
    public const int ErrorServiceMarkedForDelete = 1072;

    // ---- connection reuse
    //
    // Opening a workstation's SCM costs ~21 s here, every time: since Windows 10 1709 the client tries
    // RPC over TCP first -- port 135 answers, the dynamic port it is sent to is blocked, and only after
    // TCP gives up does it fall back to named pipes over SMB (measured 2026-09-28: KOR-216 and KOR-305
    // 21,471 ms per call; KOR-APP01, where the dynamic range is open, 315 ms). The cost is paid when the
    // handle is opened, not per call, so one handle per machine is kept and reused: a long-running
    // service pays it once per PC, and "check this PC now" answers in seconds after that.
    // A handle that has gone stale (the PC rebooted, the pipe dropped) is reopened once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<ScmHandle>> Pool = new(StringComparer.OrdinalIgnoreCase);

    private static readonly int[] StaleHandleErrors = [6 /* invalid handle */, 1722 /* RPC server unavailable */, 1726 /* RPC call failed */, 1727 /* RPC call failed, did not execute */];

    /// <summary>
    /// Runs <paramref name="use"/> against a pooled manager handle for <paramref name="machine"/> (opened with
    /// create rights), reopening once if the pooled handle has gone stale.
    /// </summary>
    public static T WithPooledManager<T>(string machine, Func<ScmHandle, T> use)
    {
        for (var attempt = 0; ; attempt++)
        {
            var lazy = Pool.GetOrAdd(machine, m => new Lazy<ScmHandle>(() => OpenManager(m, forCreate: true)));
            ScmHandle handle;
            try { handle = lazy.Value; }
            catch { Pool.TryRemove(new KeyValuePair<string, Lazy<ScmHandle>>(machine, lazy)); throw; }
            try { return use(handle); }
            catch (Win32Exception ex) when (attempt == 0 && StaleHandleErrors.Contains(ex.NativeErrorCode))
            {
                if (Pool.TryRemove(new KeyValuePair<string, Lazy<ScmHandle>>(machine, lazy))) handle.Dispose();
                LocalAuth.Drop(machine);   // a workgroup box's IPC$ session may have dropped with the handle; re-open it next attempt
            }
        }
    }

    public static ScmHandle OpenManager(string machine, bool forCreate)
    {
        // For a workgroup box with a registered local credential, open the IPC$ session first so this SCM connect
        // (OpenSCManager uses the caller's identity) authenticates as that local admin. A no-op for a domain machine.
        LocalAuth.Ensure(machine);
        var access = ScManagerConnect | (forCreate ? ScManagerCreateService : 0);
        var h = OpenSCManagerW(@"\\" + machine, null, access);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenSCManager on {machine} failed");
        return h;
    }

    public static ScmHandle Create(ScmHandle manager, string name, string binaryPath)
    {
        var h = CreateServiceW(manager, name, name, Delete | ServiceStart | ServiceQueryStatus,
            ServiceWin32OwnProcess, ServiceDemandStart, ServiceErrorIgnore, binaryPath,
            null, IntPtr.Zero, null, null /* LocalSystem */, null);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateService '{name}' failed");
        return h;
    }

    /// <summary>Starts the service; returns the Win32 error (0 on success). 1053 is the normal outcome for a one-shot.</summary>
    public static int Start(ScmHandle service)
        => StartServiceW(service, 0, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();

    /// <summary>Marks the service for deletion; returns the Win32 error (0 on success).</summary>
    public static int MarkForDelete(ScmHandle service)
        => DeleteService(service) ? 0 : Marshal.GetLastWin32Error();

    /// <summary>The current state of a named service, or null when it does not exist.</summary>
    public static ServiceState? QueryState(ScmHandle manager, string name)
    {
        using var h = OpenServiceW(manager, name, ServiceQueryStatus);
        if (h.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorServiceDoesNotExist) return null;
            throw new Win32Exception(err, $"OpenService '{name}' failed");
        }
        if (!QueryServiceStatus(h, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"QueryServiceStatus '{name}' failed");
        return (ServiceState)status.dwCurrentState;
    }

    // ---- a long-lived service (the endpoint agent), as opposed to the one-shots above

    private const uint ServiceAllAccess = 0x000F01FF;
    private const uint ServiceAutoStart = 0x00000002;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceConfigDescription = 1;
    private const uint ServiceConfigFailureActions = 2;
    public const int ErrorServiceNotActive = 1062;
    public const int ErrorServiceAlreadyRunning = 1056;
    public const int ErrorServiceExists = 1073;

    /// <summary>Opens a named service for full control, or null when it does not exist.</summary>
    public static ScmHandle? OpenForControl(ScmHandle manager, string name)
    {
        var h = OpenServiceW(manager, name, ServiceAllAccess);
        if (!h.IsInvalid) return h;
        var err = Marshal.GetLastWin32Error();
        h.Dispose();
        if (err == ErrorServiceDoesNotExist) return null;
        throw new Win32Exception(err, $"OpenService '{name}' failed");
    }

    /// <summary>Creates an automatic-start service running as LocalSystem.</summary>
    public static ScmHandle CreateAutoStart(ScmHandle manager, string name, string displayName, string binaryPath)
    {
        var h = CreateServiceW(manager, name, displayName, ServiceAllAccess, ServiceWin32OwnProcess, ServiceAutoStart, ServiceErrorNormal,
            binaryPath, null, IntPtr.Zero, null, null /* LocalSystem */, null);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateService '{name}' failed");
        return h;
    }

    /// <summary>Points an existing service at <paramref name="binaryPath"/> and makes it start automatically.</summary>
    public static void Reconfigure(ScmHandle service, string displayName, string binaryPath)
    {
        if (!ChangeServiceConfigW(service, ServiceNoChange, ServiceAutoStart, ServiceErrorNormal, binaryPath, null, IntPtr.Zero, null, null, null, displayName))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig failed");
    }

    /// <summary>The description shown in services.msc, and: restart after a crash, a minute later, every time.</summary>
    public static void Describe(ScmHandle service, string description)
    {
        var text = Marshal.StringToHGlobalUni(description);
        var actions = Marshal.AllocHGlobal(Marshal.SizeOf<ScAction>() * 3);
        try
        {
            if (!ChangeServiceConfig2W(service, ServiceConfigDescription, ref text))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "setting the service description failed");
            for (var i = 0; i < 3; i++)
                Marshal.StructureToPtr(new ScAction { Type = 1 /* SC_ACTION_RESTART */, Delay = 60_000 }, actions + i * Marshal.SizeOf<ScAction>(), false);
            var failure = new ServiceFailureActions { dwResetPeriod = 86_400, cActions = 3, lpsaActions = actions };
            if (!ChangeServiceConfig2W(service, ServiceConfigFailureActions, ref failure))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "setting the service's restart-on-failure failed");
        }
        finally
        {
            Marshal.FreeHGlobal(text);
            Marshal.FreeHGlobal(actions);
        }
    }

    /// <summary>Asks a service to stop; returns the Win32 error (0 on success, 1062 if it was not running).</summary>
    public static int Stop(ScmHandle service)
        => ControlService(service, ServiceControlStop, out _) ? 0 : Marshal.GetLastWin32Error();

    public static ServiceState State(ScmHandle service)
        => QueryServiceStatus(service, out var s) ? (ServiceState)s.dwCurrentState : throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatus failed");

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction { public int Type; public uint Delay; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceFailureActions
    {
        public uint dwResetPeriod;
        public IntPtr lpRebootMsg, lpCommand;
        public uint cActions;
        public IntPtr lpsaActions;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(ScmHandle service, uint serviceType, uint startType, uint errorControl, string? binaryPathName,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2W(ScmHandle service, uint infoLevel, ref IntPtr info);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2W(ScmHandle service, uint infoLevel, ref ServiceFailureActions info);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(ScmHandle service, uint control, out ServiceStatus status);

    public sealed class ScmHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScmHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
                    dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ScmHandle OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ScmHandle CreateServiceW(ScmHandle manager, string serviceName, string displayName,
        uint desiredAccess, uint serviceType, uint startType, uint errorControl, string binaryPathName,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ScmHandle OpenServiceW(ScmHandle manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartServiceW(ScmHandle service, uint argCount, IntPtr args);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(ScmHandle service);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(ScmHandle service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}

public enum ServiceState
{
    Stopped = 1, StartPending = 2, StopPending = 3, Running = 4,
    ContinuePending = 5, PausePending = 6, Paused = 7,
}
