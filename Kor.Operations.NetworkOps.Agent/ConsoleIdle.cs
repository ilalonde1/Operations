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
/// itself, with --idle, in the console user's session, reads the one number it prints, and it exits. Nothing
/// stays running in the user's session, and it happens only when the health probe asks for it.
/// </summary>
internal static class ConsoleIdle
{
    /// <summary>Seconds since the last keyboard or mouse input in the session this process runs in.</summary>
    public static int ForThisSession()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return -1;
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
            catch (Exception ex) when (ex is Win32Exception or IOException or FormatException)
            {
                log.Warn("could not read the console user's idle time: " + ex.Message);
                return null;
            }
        }
    }

    private static int? Run(SafeAccessTokenHandle token)
    {
        var sa = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = true };
        if (!CreatePipe(out var read, out var write, ref sa, 0)) throw new Win32Exception();
        using (read)
        {
            // Only the write end is inherited by the child.
            SetHandleInformation(read, HandleFlagInherit, 0);
            var exe = typeof(ConsoleIdle).Assembly.Location;
            var si = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = @"winsta0\default",
                dwFlags = StartfUseStdHandles | StartfUseShowWindow,
                wShowWindow = 0,   // SW_HIDE
                hStdOutput = write.DangerousGetHandle(),
                hStdError = write.DangerousGetHandle(),
            };
            var cmd = new StringBuilder($"\"{exe}\" --idle");
            bool ok;
            ProcessInformation pi;
            using (write)
            {
                ok = CreateProcessAsUser(token, null, cmd, IntPtr.Zero, IntPtr.Zero, true, CreateNoWindow, IntPtr.Zero,
                    Path.GetDirectoryName(exe), ref si, out pi);
                if (!ok) throw new Win32Exception();
            }   // the parent's copy of the write end closes here, so the read below ends when the child exits
            try
            {
                if (WaitForSingleObject(pi.hProcess, 10000) != 0) { TerminateProcess(pi.hProcess, 1); return null; }
                using var fs = new FileStream(read, FileAccess.Read);
                using var sr = new StreamReader(fs);
                var text = sr.ReadToEnd().Trim();
                return int.TryParse(text, out var seconds) && seconds >= 0 ? seconds : null;
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
        }
    }

    private const uint HandleFlagInherit = 0x1;
    private const int StartfUseStdHandles = 0x100;
    private const int StartfUseShowWindow = 0x1;
    private const uint CreateNoWindow = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes sa, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string? application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
