using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Enroll;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// <c>kor enroll-check [--all] [--dir D] [--days N] [--hook]</c> (Framework board row 1209, mistake 139). This repo (the
/// git top of --dir, or of the current folder), or with --all every repo of <see cref="EnrollCheck.Repos"/> found beside
/// it. ONE verdict line always comes first ("kor enroll-check: ok - ..." or "kor enroll-check: FAIL - <repo>: <what is
/// missing>; ..."); without --hook each finding follows on its own line and a failure exits 1. In --hook mode (the
/// SessionStart entry) the one line is all it prints and it exits 0, so a lapsed enrolment is said at the start of every
/// session without ever failing one. The rules are <see cref="EnrollCheck"/>'s; none are kept here.
/// When KOR.Drafter is a target, a SECOND line follows the verdict: its copies rule (<see cref="CopiesCheck"/>, row 2206),
/// "kor copies-check: ok - ..." or "kor copies-check: FAIL - N fault(s): ..." (one line in --hook, capped near
/// <see cref="CopiesCheck.HookLineCap"/> characters; without --hook each fault follows on its own line, after the enrolment's).
/// Copies faults never change the exit code: it stays the enrolment's (the .py's exit 1 was for a manual run; at session
/// start the line is what is read). An error in the copies rule prints "kor copies-check: ERROR - ..." and nothing else changes.
/// </summary>
public static partial class Program
{
    private static int EnrollCheckVerb(Options o, TextWriter output, bool hook, BrainDb? db, string hookErrorLog)
    {
        var here = Git.TopLevel(o.Get("--dir") ?? Environment.CurrentDirectory)
                   ?? throw new ArgumentException($"enroll-check: {o.Get("--dir") ?? Environment.CurrentDirectory} is not in a git repo");
        var days = o.Get("--days") is null ? EnrollCheck.DefaultDays
            : o.Int("--days") is int n && n > 0 ? n : throw new ArgumentException($"--days takes a whole number above 0, not '{o.Get("--days")}'");
        var all = o.Positional.Contains("--all") || o.Get("--all") is not null;
        var targets = new List<(string Dir, KorRepo Repo)>();
        var absent = new List<string>();
        if (all)
        {
            var parent = Path.GetDirectoryName(here.TrimEnd('/', '\\'))!;
            foreach (var r in EnrollCheck.Repos)
            {
                var d = Path.Combine(parent, r.Folder);
                if (Directory.Exists(d)) targets.Add((d, r)); else absent.Add(r.Folder);
            }
        }
        else
        {
            var repo = EnrollCheck.RepoOf(here)
                       ?? throw new ArgumentException($"enroll-check: {Git.Leaf(here)} is not one of the KOR repos ({string.Join(", ", EnrollCheck.Repos.Select(r => r.Folder))})");
            targets.Add((here, repo));
        }

        // the copies rule (row 2206) runs beside the enrolment checks, not after them: the hook's 30 s are shared, and a DB that
        // cannot be reached took 15 s to fail on 19 Nov; its line still follows the enrolment line, whatever the enrolment did
        var drafter = targets.Where(t => t.Repo.Folder == CopiesCheck.Main).Select(t => t.Dir).FirstOrDefault();
        var copiesRun = drafter is null ? null : Task.Run(() => CopiesLines(drafter, hook));
        List<(KorRepo Repo, List<string> Findings)> results;
        try
        {
            results = targets.Select(t => (t.Repo, Findings: EnrollCheck.Check(t.Dir, t.Repo,
                                                     m => EnrollCheck.SessionCounts(m, days, db), days))).ToList();
        }
        catch (Exception e) when (copiesRun is not null)
        {
            // the enrolment's own failure is said exactly as before (the hook's COULD NOT CHECK line, or the error and exit 1),
            // and the copies line follows it instead of being lost with it
            if (!hook) { foreach (var l in copiesRun.Result) output.WriteLine(l); throw; }
            HookFailure(o, e, output, hookErrorLog);
            output.WriteLine(copiesRun.Result[0]);
            return 0;
        }
        var failed = results.Where(r => r.Findings.Count > 0).ToList();
        var line = failed.Count == 0 && absent.Count == 0
            ? $"kor enroll-check: ok - {results.Count} of {results.Count} enrolled ({string.Join(", ", results.Select(r => r.Repo.Folder))}; settings hooks, git hooks, session-start within {days} days)"
            : "kor enroll-check: FAIL - " + string.Join("; ",
                failed.Select(r => $"{r.Repo.Folder}: {string.Join(", ", r.Findings)}")
                      .Concat(absent.Select(a => $"{a}: no checkout beside {Git.Leaf(here)}")));
        output.WriteLine(line);
        var copies = copiesRun?.Result ?? new List<string>();
        if (copies.Count > 0) output.WriteLine(copies[0]);
        if (hook) return 0;
        foreach (var (repo, findings) in results)
            foreach (var f in findings) output.WriteLine($"  {repo.Folder}: {f}");
        foreach (var f in copies.Skip(1)) output.WriteLine(f);
        // copies faults are reported, never part of the exit (row 2206): the exit is the enrolment's alone
        return failed.Count == 0 && absent.Count == 0 ? 0 : 1;
    }

    /// <summary>The copies rule's lines for KOR.Drafter (row 2206): its verdict line first, then (not in --hook) each fault on its
    /// own line. Its own try/catch: whatever goes wrong here is one ERROR line and can never touch the enrolment line or exit.</summary>
    internal static List<string> CopiesLines(string repoDir, bool hook, Func<string, CopiesVerdict>? check = null)
    {
        try
        {
            var v = (check ?? (d => CopiesCheck.Check(d)))(repoDir);
            var lines = new List<string> { CopiesCheck.Line(v, hook ? CopiesCheck.HookLineCap : null) };
            if (!hook) lines.AddRange(v.Faults.Select(f => $"  {CopiesCheck.Main} copies: {f}"));
            return lines;
        }
        catch (Exception e)
        {
            return new List<string> { $"kor copies-check: ERROR - {OneLine(e.Message)}" };
        }
    }
}
