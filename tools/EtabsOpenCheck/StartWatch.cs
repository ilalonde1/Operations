// CSI START WATCH (board row 2104, framework-13 GO 12 Nov 13:5x). A CSI program's cOAPI.ApplicationStart() is a blocking COM
// call: on 12 Nov SAFE L2 sat 37.6 min in it behind a licence box on KOR-210 (Ian closed it by hand) and only then returned
// -1 (brain #133398). Run() calls the start on the caller's thread and, if it has not returned within the cap, calls
// onTimeout from a watcher thread - the caller names the windows, pictures the screen, ends its own program and exits
// the process there, because the COM call itself never returns.
// THIS IS A COPY, for EtabsOpenCheck (KOR.Drafter row 2104, 12 Nov): Operations has no reference to KOR.Drafter's Core.
// The TESTED original is KOR.Drafter Kor.Operations.EngineeringTools.Core/Csi/CsiStartWatch.cs (CsiStartWatchTests, 5
// tests, at KOR.Drafter 3423bbfe); only the namespace differs. Change it there first and copy it here.
// ChildrenNamed is what a tool ends on a timeout: only the program whose PARENT is the tool, so a sibling run's program
// started meanwhile on the same machine is never ended (review 1845). Whether cHelper.CreateObject starts the program as
// our child is NOT known (no CSI program on the dev PC): if it does not, nothing is ended and the tool says so.
// COVERS: a start that does not return within the cap fires onTimeout once, within the cap plus scheduling; a start that
//   returns in time never fires it, also after the cap has passed; the start's own return value and exceptions pass through.
//   WindowTitlesOfProcessesNotIn names the visible top-level windows of every process started since a snapshot.
// DOES NOT: prove live that a licence box is what holds the start (no box can be planted: UNPROVEN LIVE until the first
//   staged start after JM ships); dismiss or click a dialog; tell a licence box from any other window (it names them all).
// SAME-CLASS FAULT IT WOULD MISS: a start that returns 0 and then blocks in the NEXT call (OpenFile, a dialog after
//   start) - that is the open's own 60 s watch in each tool; and a dialog owned by a process older than the snapshot.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kor.EtabsOpenCheck;

public static class CsiStartWatch
{
    /// <summary>Calls <paramref name="start"/> on this thread; if it has not returned within <paramref name="cap"/>, a
    /// watcher thread calls <paramref name="onTimeout"/> once (the start keeps running - the caller ends the process).</summary>
    public static int Run(Func<int> start, TimeSpan cap, Action onTimeout)
    {
        using var returned = new ManualResetEventSlim();
        var watcher = new Thread(() => { if (!returned.Wait(cap)) onTimeout(); }) { IsBackground = true, Name = "csi-start-watch" };
        watcher.Start();
        try { return start(); }
        finally { returned.Set(); watcher.Join(); }
    }

    /// <summary>Ids of every process running now (the snapshot taken just before the start).</summary>
    public static HashSet<int> Snapshot() => Process.GetProcesses().Select(p => { using (p) return p.Id; }).ToHashSet();

    /// <summary>"process (pid): 'title'" for each visible titled top-level window whose process is not in
    /// <paramref name="before"/>, ordered by process then title.</summary>
    public static List<string> WindowTitlesOfProcessesNotIn(IReadOnlySet<int> before)
    {
        var found = new List<(int Pid, string Title)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (before.Contains((int)pid)) return true;
            int n = GetWindowTextLength(h);
            if (n <= 0) return true;
            var sb = new StringBuilder(n + 1);
            GetWindowText(h, sb, sb.Capacity);
            found.Add(((int)pid, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return found.OrderBy(f => f.Pid).ThenBy(f => f.Title, StringComparer.Ordinal)
            .Select(f => $"{ProcessName(f.Pid)} ({f.Pid}): '{f.Title}'").ToList();
    }

    /// <summary>The running processes named <paramref name="processName"/> whose parent is <paramref name="parentPid"/> - the
    /// only ones a tool may end as "ours" (review 1845: "every SAFE not running before" also takes a sibling batch run's
    /// SAFE started meanwhile). Empty when the program was not started as our child (e.g. by COM activation): end nothing.</summary>
    public static List<Process> ChildrenNamed(string processName, int parentPid)
    {
        var mine = new List<Process>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (ParentPid(p) == parentPid) mine.Add(p);
            else p.Dispose();
        }
        return mine;
    }

    /// <summary>The parent process id of <paramref name="p"/>, or null when it cannot be read (gone, or no access).</summary>
    public static int? ParentPid(Process p)
    {
        try
        {
            var pbi = new ProcessBasicInformation();
            return NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0
                ? (int)pbi.InheritedFromUniqueProcessId : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    private static string ProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (ArgumentException) { return "gone"; }
        catch (InvalidOperationException) { return "gone"; }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
}
