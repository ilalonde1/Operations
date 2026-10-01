using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// How long the person at the keyboard has been idle. Windows only tells a process in that person's own session
/// (GetLastInputInfo); the agent runs as SYSTEM in session 0, which sees nothing. So the agent starts a copy of
/// itself, with --idle, in the console user's session; that copy's EXIT CODE is the number of idle seconds.
///
/// The exit code, not a pipe, on purpose (Codex audit 2026-09-30, finding 8): nothing is inherited from the SYSTEM
/// process (bInheritHandles = false), and there is no read that a user holding the child could stall. The user owns
/// that child and could make it lie about their own idle time -- which they could do anyway by moving the mouse.
/// </summary>
internal static class ConsoleIdle
{
    /// <summary>The exit code meaning "could not read it".</summary>
    public const int Unknown = -1;

    /// <summary>Seconds since the last keyboard or mouse input in the session this process runs in; -1 if unreadable.</summary>
    public static int ForThisSession()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return Unknown;
        // Both are milliseconds since boot, wrapping at 49.7 days: unsigned subtraction is right across the wrap.
        return (int)(unchecked((uint)Environment.TickCount - info.dwTime) / 1000);
    }

    /// <summary>The console user's idle seconds; null when nobody is signed in at the console or it cannot be read.</summary>
    public static int? ForConsoleUser(AgentLog log)
    {
        // A test in a console window is already in the user's session.
        if (Environment.UserInteractive) return ForThisSession() is var s && s >= 0 ? s : null;

        var session = WTSGetActiveConsoleSessionId();
        if (session == 0xFFFFFFFF) return null;
        if (!WTSQueryUserToken(session, out var token)) return null;   // nobody signed in at the console
        using (token)
        {
            try { return Run(token); }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                log.Warn("could not read the console user's idle time: " + ex.Message);
                return null;
            }
        }
    }

    private static int? Run(SafeAccessTokenHandle token)
    {
        var exe = typeof(ConsoleIdle).Assembly.Location;
        var si = new StartupInfo
        {
            cb = Marshal.SizeOf<StartupInfo>(),
            lpDesktop = @"winsta0\default",
            dwFlags = StartfUseShowWindow,
            wShowWindow = 0,   // SW_HIDE
        };
        var cmd = new StringBuilder($"\"{exe}\" --idle");
        // Out of the agent's job (ProcessTree.ContainSelf): it runs in another session, and its life is bounded here.
        if (!CreateProcessAsUser(token, null, cmd, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow | CreateBreakawayFromJob, IntPtr.Zero,
                Path.GetDirectoryName(exe), ref si, out var pi))
            throw new Win32Exception();
        try
        {
            if (WaitForSingleObject(pi.hProcess, 10000) != 0) { TerminateProcess(pi.hProcess, 1); return null; }
            if (!GetExitCodeProcess(pi.hProcess, out var code)) throw new Win32Exception();
            var seconds = unchecked((int)code);
            return seconds >= 0 ? seconds : null;
        }
        finally
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }

    private const int StartfUseShowWindow = 0x1;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateBreakawayFromJob = 0x01000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string? application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
