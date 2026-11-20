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

    /// <summary>One git run as <see cref="Exec"/> saw it. <paramref name="ExitCode"/> null = it never reached an exit
    /// (could not start, threw, or ran out of time); <paramref name="Expired"/> = its time ran out, process or output.</summary>
    public sealed record Result(int? ExitCode, string? Stdout, bool Expired)
    {
        public bool Ok => ExitCode == 0 && !Expired && Stdout is not null;
    }

    public static string? Run(string dir, TimeSpan timeout, params string[] args) =>
        Exec(dir, timeout, args) is { Ok: true } r ? r.Stdout : null;

    /// <summary>
    /// The one git runner (<see cref="Run"/> calls it): git's exit code, its stdout and whether the time ran out, so a
    /// verb that answers with its exit code (<c>merge-base --is-ancestor</c>: 1 = no) is told apart from a failure and from an
    /// overrun. The whole call - process AND its two output streams - is held to <paramref name="timeout"/> (board row 2206:
    /// the streams were read unbounded once the process exited, so a child holding the pipe open could hang a hook); the
    /// streams get at least <see cref="StreamGrace"/> once the process has exited inside its time.
    /// </summary>
    public static Result Exec(string dir, TimeSpan timeout, params string[] args)
    {
        var sw = Stopwatch.StartNew();
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
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p is null) return new Result(null, null, false);
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            // the time runs from git's start, as it always has for Run's callers (the capture hooks): starting git took 0.03-0.12 s
            // idle and more when loaded, and counting it would have tightened every existing timeout
            sw.Restart();
            if (!p.WaitForExit(Ms(timeout)))
            {
                KillAside(p);
                p = null;
                return new Result(null, null, true);
            }
            var left = timeout - sw.Elapsed;
            if (!Task.WaitAll(new Task[] { stdout, stderr }, Ms(left < StreamGrace ? StreamGrace : left)))
                return new Result(null, null, true);
            return new Result(p.ExitCode, stdout.Result, false);
        }
        catch
        {
            return new Result(null, null, false);
        }
        finally
        {
            p?.Dispose();
        }
    }

    /// <summary>
    /// Kills an overrun git and its tree off the caller's thread, waiting at most <see cref="KillGrace"/> for it: the tree
    /// kill (`git` is a wrapper that starts the real git.exe) walks every process: kill alone measured 0.09-0.11 s idle, and an
    /// expired call with the kill inline came back up to 2.7 s late on a loaded machine (row 2206, 19 Nov: a 270 ms slice
    /// returned after 2956 ms; kill and dispose were not timed apart), which broke the caller's deadline.
    /// If the calling process exits first, the kill is not finished and the overrun git runs on to its own end.
    /// </summary>
    private static void KillAside(Process p)
    {
        var kill = Task.Run(() =>
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            finally { p.Dispose(); }
        });
        kill.Wait(KillGrace);
    }

    /// <summary>The longest an overrun call waits for its git to be killed before returning.</summary>
    public static readonly TimeSpan KillGrace = TimeSpan.FromMilliseconds(150);

    /// <summary>The least time the output streams are given once git has exited inside its time. Generous on purpose: the bound
    /// is there for a pipe that never closes, not to hurry a loaded machine. At 250 ms, three git-reading tests went red in a
    /// loaded full-suite run (row 2206, 20 Nov 00:0x); a slow stream read was the suspected cause, not proven.</summary>
    public static readonly TimeSpan StreamGrace = TimeSpan.FromSeconds(5);

    // a negative TimeSpan would be -1 ms = wait forever to WaitForExit; clamp it, and cap at int.MaxValue
    private static int Ms(TimeSpan t) => t <= TimeSpan.Zero ? 0 : (int)Math.Min(t.TotalMilliseconds, int.MaxValue);

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
