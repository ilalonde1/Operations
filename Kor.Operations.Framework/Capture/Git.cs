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

    /// <summary>
    /// The repo's top-level folder without starting git (~120 ms a process on this machine, 3 Nov - K6's start budget): the
    /// nearest folder at or above <paramref name="dir"/> holding a <c>.git</c> DIRECTORY, with forward slashes as git
    /// prints it. A <c>.git</c> FILE (a worktree or submodule) or no <c>.git</c> at all asks git (<see cref="TopLevel"/>).
    /// </summary>
    public static string? TopLevelFast(string dir)
    {
        try
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d is not null; d = d.Parent)
            {
                var git = Path.Combine(d.FullName, ".git");
                if (Directory.Exists(git)) return d.FullName.TrimEnd('\\', '/').Replace('\\', '/');
                if (File.Exists(git)) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return TopLevel(dir);
    }

    /// <summary>
    /// HEAD's commit (lowercase 40-hex) read from the files git keeps it in - <c>.git/HEAD</c>, then the loose ref or
    /// <c>packed-refs</c> - without starting git. Anything else (a .git file, an unreadable or odd ref) asks git.
    /// Order, as git resolves a ref: a detached HEAD is its sha; a LOOSE ref file wins over packed-refs, and when it exists
    /// but does not hold a sha, git is asked (packed-refs is NOT consulted then); only a ref with no loose file is looked
    /// up in packed-refs. Tested on a throwaway repo: loose, packed-only, detached, and a .git file (review row 194).
    /// </summary>
    public static string? HeadFast(string top)
    {
        try
        {
            var gitDir = Path.Combine(top, ".git");
            if (Directory.Exists(gitDir))
            {
                var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (IsSha(head)) return head.ToLowerInvariant();
                if (head.StartsWith("ref: ", StringComparison.Ordinal))
                {
                    var name = head[5..].Trim();
                    if (name.StartsWith("refs/", StringComparison.Ordinal) && !name.Contains(".."))
                    {
                        var loose = Path.Combine(gitDir, name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(loose))
                        {
                            var sha = File.ReadAllText(loose).Trim();
                            if (IsSha(sha)) return sha.ToLowerInvariant();
                        }
                        else if (File.Exists(Path.Combine(gitDir, "packed-refs")))
                        {
                            foreach (var line in File.ReadLines(Path.Combine(gitDir, "packed-refs")))
                            {
                                var sp = line.IndexOf(' ');
                                if (sp == 40 && line[(sp + 1)..].Trim() == name && IsSha(line[..40])) return line[..40].ToLowerInvariant();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return Run(top, TimeSpan.FromSeconds(3), "rev-parse", "HEAD")?.Trim().ToLowerInvariant();
    }

    private static bool IsSha(string s) => s.Length == 40 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>The leaf name of a path ("C:/VIsual Studio Projects/Framework" -> "Framework").</summary>
    public static string? Leaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var leaf = Path.GetFileName(path.Trim().TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }
}
