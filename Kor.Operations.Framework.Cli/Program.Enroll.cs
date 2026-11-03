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
/// </summary>
public static partial class Program
{
    private static int EnrollCheckVerb(Options o, TextWriter output, bool hook, BrainDb? db)
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

        var results = targets.Select(t => (t.Repo, Findings: EnrollCheck.Check(t.Dir, t.Repo,
                                                     m => EnrollCheck.SessionCounts(m, days, db), days))).ToList();
        var failed = results.Where(r => r.Findings.Count > 0).ToList();
        var line = failed.Count == 0 && absent.Count == 0
            ? $"kor enroll-check: ok - {results.Count} of {results.Count} enrolled ({string.Join(", ", results.Select(r => r.Repo.Folder))}; settings hooks, git hooks, session-start within {days} days)"
            : "kor enroll-check: FAIL - " + string.Join("; ",
                failed.Select(r => $"{r.Repo.Folder}: {string.Join(", ", r.Findings)}")
                      .Concat(absent.Select(a => $"{a}: no checkout beside {Git.Leaf(here)}")));
        output.WriteLine(line);
        if (hook) return 0;
        foreach (var (repo, findings) in results)
            foreach (var f in findings) output.WriteLine($"  {repo.Folder}: {f}");
        return failed.Count == 0 && absent.Count == 0 ? 0 : 1;
    }
}
