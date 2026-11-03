using System.Diagnostics;
using System.Text;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// Runs git as a child process (argv, no shell) with a hard timeout. A git that fails, exits non-zero or overruns gives
/// null - the caller records what it can; a hook must never hang or fail because git did.
/// </summary>
public static class Git
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static string? Run(string dir, params string[] args) => Run(dir, DefaultTimeout, args);

    public static string? Run(string dir, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }
            p.WaitForExit();
            _ = stderr.Result;
            return p.ExitCode == 0 ? stdout.Result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The repo's top-level folder, or null when dir is not inside a git work tree.</summary>
    public static string? TopLevel(string dir)
    {
        var t = Run(dir, "rev-parse", "--show-toplevel")?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>The leaf name of a path ("C:/VIsual Studio Projects/Framework" -> "Framework").</summary>
    public static string? Leaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var leaf = Path.GetFileName(path.Trim().TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }
}
