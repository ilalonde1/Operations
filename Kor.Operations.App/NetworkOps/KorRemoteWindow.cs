using System.Diagnostics;
using System.IO;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// Opens a KOR Remote page as an Edge app window: the page only, no tabs, toolbar or bookmarks -- the same window as the
/// "KOR Remote" shortcut (infra/vms/kor-mesh01/README.md). Same Edge profile, so the sign-in and saved Web-RDP credentials
/// carry over. A PC without Edge gets the default browser instead.
/// </summary>
public static class KorRemoteWindow
{
    /// <summary>The command line for <paramref name="url"/>: Edge in app mode when Edge is installed, else the URL itself (shell-opened).</summary>
    public static ProcessStartInfo StartInfo(string url, string? edgePath)
        => edgePath is { Length: > 0 }
            ? new ProcessStartInfo(edgePath) { UseShellExecute = false, ArgumentList = { "--app=" + url, "--window-size=1600,1000" } }
            : new ProcessStartInfo(url) { UseShellExecute = true };

    /// <summary>Opens <paramref name="url"/>. Throws <see cref="System.ComponentModel.Win32Exception"/> if nothing can open it.</summary>
    public static void Open(string url) => Process.Start(StartInfo(url, FindEdge()));

    /// <summary>Where msedge.exe is on this PC, or null.</summary>
    public static string? FindEdge()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
