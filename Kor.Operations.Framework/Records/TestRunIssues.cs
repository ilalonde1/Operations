using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Records;

/// <summary>
/// K5b through capture-tool: a test run the session made anyway is evidence for the issues it names.
/// <list type="bullet">
/// <item>A PASSING <c>dotnet test ... --filter FullyQualifiedName=&lt;fqn&gt;</c> that ran at least one test, in a clean
/// tree, is a 'pass' check event of every open test-kind issue whose check names that fqn in that repo -> resolved.</item>
/// <item>A FAILING run, in a clean tree, names its failed tests ("Failed &lt;fqn&gt; [..]"): each RESOLVED test-kind issue
/// whose fqn is among them gets a 'fail' check event -> a linked regression.</item>
/// <item>In a tree with uncommitted tracked changes either run is recorded as 'dirty' and changes nothing: the code that
/// ran is not the commit the event would cite (OverLORD, 3 Nov - fail event #27272).</item>
/// </list>
/// Not covered: a pass of a wider filter (or none) never resolves - only the exact fqn filter is the issue's own check; a
/// failing run whose output does not list the failed test by name (a crash, a build error) opens no regression; a pass or
/// fail in a dirty tree opens and resolves nothing (it is recorded as dirty).
/// </summary>
public static class TestRunIssues
{
    private static readonly Regex Filter = new(
        @"--filter\s+(?:""|')?\s*FullyQualifiedName\s*=\s*(?<fqn>[A-Za-z_][\w.`+]*)\s*(?:""|')?(?=\s|$)", RegexOptions.CultureInvariant);

    private static readonly Regex FailedLine = new(@"^\s*Failed\s+(?<fqn>[A-Za-z_][\w.`+]*)\s+\[", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex TotalLine = new(@"\bTotal(?: tests)?:\s*(\d+)", RegexOptions.CultureInvariant);

    /// <summary>The fqn of an exact <c>--filter FullyQualifiedName=X</c>, or null.</summary>
    public static string? FilterFqn(string? command) => command is null ? null : Filter.Match(command) is { Success: true } m ? m.Groups["fqn"].Value : null;

    public static IReadOnlyList<string> FailedTests(string? output) =>
        output is null ? Array.Empty<string>() : FailedLine.Matches(output).Select(m => m.Groups["fqn"].Value).Distinct().ToList();

    public static int? Total(string? output) =>
        output is null ? null : TotalLine.Matches(output).Select(m => (int?)int.Parse(m.Groups[1].Value)).LastOrDefault();

    /// <summary>
    /// Maps one recorded test event onto issues. <paramref name="repoDir"/> = the repo the run was proved to run in (for
    /// the dirty check). Returns one line per issue touched.
    /// </summary>
    public static IReadOnlyList<string> Map(BrainDb db, BrainEvent test, string? command, string? output, string? repoDir,
                                            string actor)
    {
        var lines = new List<string>();
        if (test.Kind != "test" || test.Repo is null || test.Commit is null || repoDir is null) return lines;
        var issues = TestIssuesIn(db, test.Repo);
        if (issues.Count == 0) return lines;
        if (test.Outcome == "pass" && FilterFqn(command) is { } fqn && Total(output) is >= 1)
        {
            foreach (var issue in issues.Where(i => i.OpenIssue && IssueCheck.ParseTestRef(i.CheckRef!)?.Fqn == fqn))
            {
                var (head, dirty) = IssueCheck.HeadAndDirty(repoDir);
                var r = new CheckResult(dirty is null ? "pass" : "dirty", test.Repo, head ?? test.Commit,
                    $"capture-tool: {command}" + (dirty is null ? "" : $" - uncommitted changes, not evidence: {dirty}"), Total(output));
                lines.Add(IssueResolver.CheckAndRecord(db, issue, r, actor, test.Session).Line);
            }
        }
        else if (test.Outcome == "fail")
        {
            var failed = FailedTests(output).ToHashSet(StringComparer.Ordinal);
            foreach (var issue in issues.Where(i => i.Status == "superseded" && failed.Contains(IssueCheck.ParseTestRef(i.CheckRef!)?.Fqn ?? "")))
            {
                // The same rule as a pass and as IssueCheck (3 Nov, OverLORD: fail event #27272 cited a7d73d98 while the
                // test had been broken in an UNCOMMITTED tree - a7d73d98 never held that code): a run in a tree with
                // uncommitted tracked changes is 'dirty', and a dirty event never opens a regression.
                var (head, dirty) = IssueCheck.HeadAndDirty(repoDir);
                var r = new CheckResult(dirty is null ? "fail" : "dirty", test.Repo, head ?? test.Commit,
                    $"capture-tool: {command}" + (dirty is null ? "" : $" - uncommitted changes, not evidence: {dirty}"), Total(output));
                lines.Add(IssueResolver.CheckAndRecord(db, issue, r, actor, test.Session).Line);
            }
        }
        return lines;
    }

    /// <summary>Test-kind issues checked in this repo: open ones, and resolved ones with no regression yet.</summary>
    private static List<StoredRecord> TestIssuesIn(BrainDb db, string repo)
    {
        using var c = db.Command(
            "SELECT m.Id FROM brain.Memory m WHERE m.Kind = N'issue' AND m.CheckKind = N'test' AND m.RetiredAtUtc IS NULL " +
            "AND m.CheckRef LIKE @r + N'/%' " +
            "AND (m.Status IN (N'current', N'stale') OR (m.Status = N'superseded' " +
            "     AND EXISTS (SELECT 1 FROM brain.Memory r WHERE r.Id = m.SupersededBy AND r.Kind = N'resolution') " +
            "     AND NOT EXISTS (SELECT 1 FROM brain.Memory g WHERE g.RegressionOf = m.Id AND g.Kind = N'issue'))) " +
            "ORDER BY m.Id");   // the lines come out in issue order, never the server's choice
        c.Parameters.Add("@r", System.Data.SqlDbType.NVarChar, 80).Value = repo.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        var ids = new List<int>();
        using (var r = c.ExecuteReader()) while (r.Read()) ids.Add(r.GetInt32(0));
        return ids.Select(id => RecordStore.Get(db, id)!).Where(i => IssueCheck.ParseTestRef(i.CheckRef ?? "")?.Repo == repo).ToList();
    }
}
