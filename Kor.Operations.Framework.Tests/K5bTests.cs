using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K5b - an issue is cleared only by evidence (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md, the spec's 12 tests and more).
/// LIVE on KorStandards (migration 171 applied) as korbrain_app, each test in its own <see cref="BrainDb.OpenRolledBack"/>;
/// a refusal by a constraint or trigger gets a unit of work of its own and asserts the constraint's NAME (or 51700).
/// Lane zz-test-k5b-&lt;time&gt;.
/// Covers: query pass resolves (gone from vw_OpenIssues), query fail stays open with a fail event; resolutions without
/// evidence / citing a fail / citing another commit are refused by name; a source issue resolves only on a changed blob;
/// resolved + fail -> linked regression; no reopen (51700); 0 tests = error; dirty = dirty; "#issue N" parsing;
/// capture-tool's failed-test sample -> regression and its exact-filter pass -> resolution (or dirty); restatement in one
/// insert; freshness skips check-decided issues; the gate allowlist.
/// Does NOT cover: a real `dotnet test` run of a test check (the live K5b proof in Framework does that); whether a check
/// really tests its issue (SELECT 1 AS Pass clears anything). Same-class miss: a query issue regressing in another
/// module's data between sessions is seen only when someone runs `kor check --resolved`.
/// </summary>
[Collection(LiveDb.Name)]
public class K5bTests
{
    private static readonly string Lane = $"zz-test-k5b-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Doc = "docs/KNOWLEDGE-IDE.md";
    private const string Fqn = "Kor.Operations.Framework.Tests.SkeletonTests.VersionPrintsKor";
    private const string PassSql = "brain:SELECT CAST(1 AS bit) AS Pass";
    private const string FailSql = "brain:SELECT CAST(0 AS bit) AS Pass";

    private const string TestFailOut = """
        Starting test execution, please wait...
          Failed Kor.Operations.Framework.Tests.SkeletonTests.VersionPrintsKor [4 ms]
          Error Message:
           Assert.StartsWith() Failure: String start does not match
        Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7, Duration: 2 s - Kor.Operations.Framework.Tests.dll (net8.0)
        """;

    private const string TestPassOut = """
        Starting test execution, please wait...
        A total of 1 test files matched the specified pattern.
        Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 40 ms - Kor.Operations.Framework.Tests.dll (net8.0)
        """;

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static string OpsLeaf() => Git.Leaf(Git.TopLevel(RepoDir()))!;
    private static string FrameworkDir() => RepoLocator.Find("Framework", RepoDir())!;
    private static string Head() => Git.Run(RepoDir(), "rev-parse", "HEAD")!.Trim();
    private static string TestRef() => $"{OpsLeaf()}/Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj|{Fqn}";

    private static (int Code, string Out, string Err) Run(BrainDb? db, string? stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, () => stdin, output, error, Path.Combine(Path.GetTempPath(), $"kor-k5b-{Guid.NewGuid():N}.log"), db);
        return (code, output.ToString(), error.ToString());
    }

    private static int Id(string output, string pattern) => int.Parse(Regex.Match(output, pattern).Groups[1].Value);

    private static int Issue(BrainDb db, string name, string check, string checkRef, GitSource? git = null) =>
        RecordStore.InsertIssue(db, new NewRecord(Lane, name, "issue", $"k5b {name}", $"k5b body {name}", "k5b-test", git,
            git is null ? "session" : null, git is null ? "k5b test" : null, CheckKind: check, CheckRef: checkRef));

    private static int CheckEvent(BrainDb db, int issue, string outcome, string commit) =>
        BrainStore.Insert(new BrainEvent(Lane, "check", OpsLeaf(), commit, "k5b", outcome, null, "k5b-test", null, issue), db);

    private static bool Open(BrainDb db, int id)
    {
        using var c = db.Command("SELECT COUNT(*) FROM brain.vw_OpenIssues WHERE Id = @i");
        c.Parameters.Add("@i", SqlDbType.Int).Value = id;
        return Convert.ToInt32(c.ExecuteScalar()) == 1;
    }

    /// <summary>A resolution written raw (RecordStore would never send these), to prove the DATABASE refuses it.</summary>
    private static void RawResolution(BrainDb db, int issue, int? ev, string? commit, string sourceKind = "run")
    {
        using var c = db.Command(
            "DECLARE @id TABLE (Id int); INSERT brain.Memory (Lane, Name, Kind, Description, Body, CreatedBy, SourceKind, SourceRef, " +
            "SourceCommit, ResolvesId, EvidenceEventId) OUTPUT INSERTED.Id INTO @id VALUES (@l, @n, N'resolution', N'd', N'b', N'k5b-test', " +
            "@sk, N'k5b raw', @c, @i, @e); SELECT Id FROM @id;");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = Lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = $"raw resolution of #{issue}";
        c.Parameters.Add("@sk", SqlDbType.NVarChar, 10).Value = sourceKind;
        c.Parameters.Add("@c", SqlDbType.Char, 40).Value = (object?)commit ?? DBNull.Value;
        c.Parameters.Add("@i", SqlDbType.Int).Value = issue;
        c.Parameters.Add("@e", SqlDbType.Int).Value = (object?)ev ?? DBNull.Value;
        c.ExecuteScalar();
    }

    // 1
    [Fact]
    public void LiveAQueryPassResolvesTheIssueAndItLeavesTheOpenList()
    {
        using var db = BrainDb.OpenRolledBack();
        var (c1, o1, e1) = Run(db, null, "issue", "--module", Lane, "--name", "q-pass", "--description", "k5b query pass",
                               "--check", "query", "--check-ref", PassSql, "--dir", RepoDir());
        Assert.True(c1 == 0, e1);
        var id = Id(o1, @"recorded issue #(\d+)");
        Assert.True(Open(db, id));
        var (c2, o2, e2) = Run(db, null, "check", "--issue", id.ToString(), "--dir", RepoDir());
        Assert.True(c2 == 0, e2 + o2);
        var res = Id(o2, @"RESOLVED by #(\d+)");
        Assert.False(Open(db, id));
        var issue = RecordStore.Get(db, id)!;
        Assert.Equal(("superseded", (int?)res), (issue.Status, issue.SupersededBy));
        var r = RecordStore.Get(db, res)!;
        var pass = Assert.Single(BrainStore.ChecksOf(id, db));
        Assert.Equal(("resolution", (int?)id, (int?)pass.Id, pass.Commit), (r.Kind, r.ResolvesId, r.EvidenceEventId, r.SourceCommit));
        Assert.Equal("pass", pass.Outcome);
    }

    // 2
    [Fact]
    public void LiveAQueryFailLeavesTheIssueOpenWithAFailEvent()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "q-fail", "query", FailSql);
        var (code, o, _) = Run(db, null, "check", "--issue", id.ToString(), "--dir", RepoDir());
        Assert.Equal(1, code);
        Assert.Contains("fail", o);
        Assert.True(Open(db, id));
        Assert.Equal("fail", Assert.Single(BrainStore.ChecksOf(id, db)).Outcome);
    }

    // 3, 4, 5: the database refuses a resolution that is not proved - each by its constraint's NAME
    [Fact]
    public void LiveAResolutionWithoutEvidenceIsRefusedByResolutionShape()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "no-evidence", "query", PassSql);
        var e = Assert.Throws<SqlException>(() => RawResolution(db, id, null, null));
        Assert.Contains("CK_Memory_ResolutionShape", e.Message);
    }

    [Fact]
    public void LiveAResolutionCitingAFailEventIsRefusedByResolutionEvidence()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "fail-evidence", "query", PassSql);
        var fail = CheckEvent(db, id, "fail", Head());
        var e = Assert.Throws<SqlException>(() => RawResolution(db, id, fail, Head()));
        Assert.Contains("CK_Memory_ResolutionEvidence", e.Message);
    }

    [Fact]
    public void LiveAResolutionCitingAPassAtAnotherCommitIsRefusedByResolutionEvidence()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "other-commit", "query", PassSql);
        var pass = CheckEvent(db, id, "pass", Head());
        var other = Git.Run(RepoDir(), "rev-parse", "HEAD~1")!.Trim();
        var e = Assert.Throws<SqlException>(() => RawResolution(db, id, pass, other));
        Assert.Contains("CK_Memory_ResolutionEvidence", e.Message);
    }

    // 6
    [Fact]
    public void LiveASourceIssueWithAnUnchangedBlobIsNotResolved()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "source-same", "source", "docs/KNOWLEDGE-IDE.md must change", RecordStore.AtHead(FrameworkDir(), Doc));
        var e = Assert.Throws<SqlException>(() => RecordStore.ResolveBySource(db, id, "k5b-test", "it changed?", RepoDir()));
        Assert.Contains("CK_Memory_ResolutionEvidence", e.Message);
    }

    [Fact]
    public void LiveASourceIssueWithAChangedBlobIsResolved()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "source-changed", "source", "docs/KNOWLEDGE-IDE.md must change", RecordStore.At(FrameworkDir(), Doc, "3b2ce76c"));
        var res = RecordStore.ResolveBySource(db, id, "k5b-test", "the definition was rewritten", RepoDir());
        var r = RecordStore.Get(db, res)!;
        Assert.Equal(("git", Doc, Git.Run(FrameworkDir(), "rev-parse", $"HEAD:{Doc}")!.Trim()), (r.SourceKind, r.SourcePath, r.SourceBlob));
        Assert.Equal(("superseded", (int?)res), (RecordStore.Get(db, id)!.Status, RecordStore.Get(db, id)!.SupersededBy));
    }

    // 7
    [Fact]
    public void LiveAFailAfterResolutionOpensALinkedRegression()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "regress", "query", PassSql);
        Run(db, null, "check", "--issue", id.ToString(), "--dir", RepoDir());
        var resolved = RecordStore.Get(db, id)!;
        Assert.Equal("superseded", resolved.Status);
        var o = IssueResolver.CheckAndRecord(db, resolved, new CheckResult("fail", OpsLeaf(), Head(), "broken again"), "k5b-test", null);
        Assert.NotNull(o.RegressionId);
        var g = RecordStore.Get(db, o.RegressionId!.Value)!;
        Assert.Equal(("issue", (int?)id, (int?)o.EventId, "query", PassSql), (g.Kind, g.RegressionOf, g.EvidenceEventId, g.CheckKind, g.CheckRef));
        Assert.Equal($"regress (regression of #{id})", g.Name);
        Assert.True(Open(db, g.Id));
        // a second fail does not open a second regression
        var again = IssueResolver.CheckAndRecord(db, RecordStore.Get(db, id)!, new CheckResult("fail", OpsLeaf(), Head(), "still"), "k5b-test", null);
        Assert.Equal(g.Id, again.RegressionId);
    }

    // 8
    [Fact]
    public void LiveAResolvedIssueDoesNotReopen()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "no-reopen", "query", PassSql);
        Run(db, null, "check", "--issue", id.ToString(), "--dir", RepoDir());
        using var c = db.Command("UPDATE brain.Memory SET Status = N'current', SupersededBy = NULL WHERE Id = @i");
        c.Parameters.Add("@i", SqlDbType.Int).Value = id;
        var e = Assert.Throws<SqlException>(() => c.ExecuteNonQuery());
        Assert.Equal(51700, e.Number);
    }

    // 9
    [Fact]
    public void ZeroTestsIsAnErrorNeverAPass()
    {
        using var repo = TempRepo.Create();
        var issue = TestIssue(repo.Dir);
        var r = IssueCheck.Run(issue, TimeSpan.FromSeconds(5), repo.Dir,
            (_, _, _, _) => (0, "Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0, Duration: 1 ms - x.dll (net8.0)"));
        Assert.Equal("error", r.Outcome);
        Assert.Contains("0 tests ran", r.Detail);
        var ok = IssueCheck.Run(issue, TimeSpan.FromSeconds(5), repo.Dir, (_, _, _, _) => (0, TestPassOut));
        Assert.Equal(("pass", (int?)1), (ok.Outcome, ok.Total));
        var noSummary = IssueCheck.Run(issue, TimeSpan.FromSeconds(5), repo.Dir, (_, _, _, _) => (0, "No test matches the given testcase filter"));
        Assert.Equal("error", noSummary.Outcome);
    }

    // 10
    [Fact]
    public void ADirtyTreeIsDirtyAndTheTestIsNeverRun()
    {
        using var repo = TempRepo.Create();
        File.AppendAllText(Path.Combine(repo.Dir, "a.md"), "\nuncommitted");
        var ran = false;
        var r = IssueCheck.Run(TestIssue(repo.Dir), TimeSpan.FromSeconds(5), repo.Dir, (_, _, _, _) => { ran = true; return (0, TestPassOut); });
        Assert.Equal("dirty", r.Outcome);
        Assert.False(ran);
        Assert.Equal(Git.Run(repo.Dir, "rev-parse", "HEAD")!.Trim(), r.Commit);
    }

    private static StoredRecord TestIssue(string repoDir) => new(1, Lane, "t", "issue", "d", "b", "current", "session", null, null,
        null, null, "k5b", null, null, null, null, CheckKind: "test", CheckRef: $"{Git.Leaf(repoDir)}/x/x.csproj|A.B.C");

    // 11
    [Fact]
    public void IssueTagsAreReadFromTheCommitMessage()
    {
        Assert.Equal(new[] { 12 }, Program.IssueIds("K5b proof #issue 12"));
        Assert.Equal(new[] { 12, 7 }, Program.IssueIds("fix #issue 12 and #Issue #7, again #issue 12\n\nbody"));
        Assert.Empty(Program.IssueIds("issue 12 without a hash, #12 alone, #issues 3"));
    }

    // 12
    [Fact]
    public void LiveCaptureToolsFailedTestOpensARegressionOfTheResolvedIssue()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "tool-regress", "test", TestRef());
        var resolved = IssueResolver.CheckAndRecord(db, RecordStore.Get(db, id)!, new CheckResult("pass", OpsLeaf(), Head(), "k5b pass", 1), "k5b-test", null);
        Assert.NotNull(resolved.ResolutionId);
        var json = JsonSerializer.Serialize(new
        {
            session_id = "zz-k5b-session", cwd = RepoDir(), hook_event_name = "PostToolUseFailure", tool_name = "Bash",
            tool_input = new { command = "dotnet test Kor.Operations.Framework.Tests" }, error = "Exit code 1\n" + TestFailOut,
        });
        var (code, o, e) = Run(db, json, "capture-tool", "--module", Lane);
        Assert.True(code == 0, e);
        // THIS issue's line (3 Nov: capture-tool also maps the REAL resolved issues with the same test - #110887 - and the
        // first "REGRESSION #" line was theirs: shared DB rows, failing every run after the live proof made them).
        var clean = IssueCheck.HeadAndDirty(RepoDir()).Dirty is null;
        var check = BrainStore.ChecksOf(id, db).First();
        Assert.Equal(clean ? "fail" : "dirty", check.Outcome);
        if (clean) Assert.Equal((int?)id, RecordStore.Get(db, Id(o, $@"#{id} [^\n]*REGRESSION #(\d+) open"))!.RegressionOf);
        else Assert.DoesNotMatch($@"#{id} [^\n]*REGRESSION", o);
    }

    // 12b/12c (OverLORD, 3 Nov): a test run in a tree with uncommitted tracked changes is 'dirty' - a fail never opens a
    // regression, a pass never resolves. Driven in a throwaway repo so BOTH states are always exercised.
    private static (StoredRecord Issue, BrainEvent Test) InTempRepo(BrainDb db, TempRepo repo, string name, bool resolve, string outcome)
    {
        var leaf = Git.Leaf(repo.Dir)!;
        var head = Git.Run(repo.Dir, "rev-parse", "HEAD")!.Trim();
        var id = Issue(db, name, "test", $"{leaf}/x/x.csproj|{Fqn}");
        if (resolve) IssueResolver.CheckAndRecord(db, RecordStore.Get(db, id)!, new CheckResult("pass", leaf, head, "pass", 1), "k5b-test", null);
        return (RecordStore.Get(db, id)!, new BrainEvent(Lane, "test", leaf, head, "dotnet test", outcome, null, "k5b-test", "zz-k5b-session"));
    }

    [Fact]
    public void LiveAFailInACleanTreeOpensARegressionAndInADirtyTreeNever()
    {
        using var db = BrainDb.OpenRolledBack();
        using var repo = TempRepo.Create();
        var (issue, test) = InTempRepo(db, repo, "fail-clean-dirty", resolve: true, "fail");
        File.AppendAllText(Path.Combine(repo.Dir, "a.md"), "\nuncommitted break");
        var dirtyLine = Assert.Single(TestRunIssues.Map(db, test, "dotnet test", TestFailOut, repo.Dir, "k5b-test"));
        Assert.Equal("dirty", BrainStore.ChecksOf(issue.Id, db).First().Outcome);
        Assert.DoesNotContain("REGRESSION", dirtyLine);
        Assert.Null(RecordStore.OpenIssueByName(db, Lane, $"fail-clean-dirty (regression of #{issue.Id})"));
        Git.Run(repo.Dir, "checkout", "--", "a.md");   // clean again
        var cleanLine = Assert.Single(TestRunIssues.Map(db, test, "dotnet test", TestFailOut, repo.Dir, "k5b-test"));
        Assert.Equal("fail", BrainStore.ChecksOf(issue.Id, db).First().Outcome);
        Assert.Equal((int?)issue.Id, RecordStore.Get(db, Id(cleanLine, @"REGRESSION #(\d+) open"))!.RegressionOf);
    }

    [Fact]
    public void LiveAPassInADirtyTreeNeverResolvesAndInACleanTreeDoes()
    {
        using var db = BrainDb.OpenRolledBack();
        using var repo = TempRepo.Create();
        var (issue, test) = InTempRepo(db, repo, "pass-clean-dirty", resolve: false, "pass");
        var command = $"dotnet test x --filter FullyQualifiedName={Fqn}";
        File.AppendAllText(Path.Combine(repo.Dir, "a.md"), "\nuncommitted fix");
        Assert.Contains("dirty", Assert.Single(TestRunIssues.Map(db, test, command, TestPassOut, repo.Dir, "k5b-test")));
        Assert.True(Open(db, issue.Id));
        Git.Run(repo.Dir, "checkout", "--", "a.md");
        Assert.Contains("RESOLVED", Assert.Single(TestRunIssues.Map(db, test, command, TestPassOut, repo.Dir, "k5b-test")));
        Assert.False(Open(db, issue.Id));
    }

    // 13: capture-tool's exact-filter pass resolves - or records 'dirty' when the tree is not clean
    [Fact]
    public void LiveCaptureToolsExactFilterPassResolvesTheOpenIssueOrIsDirty()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "tool-pass", "test", TestRef());
        var json = JsonSerializer.Serialize(new
        {
            session_id = "zz-k5b-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = $"dotnet test Kor.Operations.Framework.Tests --filter \"FullyQualifiedName={Fqn}\"" },
            tool_response = new { stdout = TestPassOut, stderr = "" },
        });
        var (code, o, e) = Run(db, json, "capture-tool", "--module", Lane);
        Assert.True(code == 0, e);
        var clean = IssueCheck.HeadAndDirty(RepoDir()).Dirty is null;
        var check = BrainStore.ChecksOf(id, db).First();
        Assert.Equal(clean ? "pass" : "dirty", check.Outcome);
        Assert.Equal(clean, !Open(db, id));
        Assert.Contains(clean ? "RESOLVED" : "dirty", o);
        // a wider filter is not the issue's own check: it records nothing for the issue
        Assert.Null(TestRunIssues.FilterFqn("dotnet test --filter \"FullyQualifiedName~Kor.Operations\""));
        Assert.Equal(Fqn, TestRunIssues.FilterFqn($"dotnet test X --filter FullyQualifiedName={Fqn} --no-build"));
    }

    // 14: a restatement is ONE insert, and the old issue is superseded in the same statement
    [Fact]
    public void LiveARestatementSupersedesTheOldIssueInOneInsert()
    {
        using var db = BrainDb.OpenRolledBack();
        var old = Issue(db, "restate", "query", FailSql);
        var (code, o, e) = Run(db, null, "issue", "--module", Lane, "--name", "restate", "--description", "k5b restated",
                               "--check", "query", "--check-ref", PassSql, "--restates", old.ToString());
        Assert.True(code == 0, e);
        var nu = Id(o, @"recorded issue #(\d+)");
        Assert.Equal(("superseded", (int?)nu), (RecordStore.Get(db, old)!.Status, RecordStore.Get(db, old)!.SupersededBy));
        Assert.True(Open(db, nu));
        Assert.Contains("already open", Assert.Throws<RecordRefusedException>(() => Issue(db, "restate", "query", PassSql)).Message);
    }

    // 15: freshness skips issues whose truth is their check; a source issue is compared
    [Fact]
    public void LiveFreshnessSkipsCheckDecidedIssues()
    {
        using var db = BrainDb.OpenRolledBack();
        var old = RecordStore.At(FrameworkDir(), Doc, "3b2ce76c");
        var test = Issue(db, "fresh-test", "test", TestRef(), old);
        var source = Issue(db, "fresh-source", "source", "the file changes", old);
        var r = Freshness.CheckModule(db, Lane, RepoDir());
        Assert.DoesNotContain(r.Rows, x => x.Id == test);
        Assert.Equal(Verdict.Stale, Assert.Single(r.Rows, x => x.Id == source).Verdict);
    }

    // 17: a resolution is never retired or superseded (173 refuses it with 51700; kor refuses first, naming the regression path)
    [Fact]
    public void LiveAResolutionIsNeverWithdrawnOrSupersededAndTheRefusalNamesTheRegressionPath()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Issue(db, "keep-resolution", "query", PassSql);
        var res = Id(Run(db, null, "check", "--issue", id.ToString(), "--dir", RepoDir()).Out, @"RESOLVED by #(\d+)");
        foreach (var action in new[] { "withdraw", "confirm", "supersede" })
        {
            var (code, _, e) = Run(db, null, "recheck", res.ToString(), action, "--because", "k5b: closed wrongly", "--body-file", "x.md");
            Assert.Equal(1, code);
            Assert.Contains($"#{res} is a resolution (of #{id})", e);
            Assert.Contains("OpenRegression", e);
            Assert.Contains($"kor check --issue {id} --resolved", e);
        }
        Assert.Contains("OpenRegression", Assert.Throws<RecordRefusedException>(() => RecordStore.Withdraw(db, res, "k5b", "x")).Message);
        var finding = new NewRecord(Lane, "x", "finding", "x", "x", "k5b", RecordStore.AtHead(FrameworkDir(), Doc));
        Assert.Contains("OpenRegression", Assert.Throws<RecordRefusedException>(() => RecordStore.Supersede(db, res, finding, "k5b", "x")).Message);
        var r = RecordStore.Get(db, res)!;
        Assert.Equal(("current", (DateTimeOffset?)null), (r.Status, r.RetiredAtUtc));   // nothing was written
        // and an issue: never withdrawn either
        Assert.Contains("closed only by evidence", Run(db, null, "recheck", id.ToString(), "withdraw", "--because", "x").Err);
    }

    // 18 (review row 165): a RESTATED issue is superseded by an issue, not a resolution - it is not resolved, its failing
    // check opens no regression, and the check event is kept
    [Fact]
    public void LiveARestatedIssueIsNotResolvedAndItsFailOpensNoRegression()
    {
        using var db = BrainDb.OpenRolledBack();
        var old = Issue(db, "restated-fail", "query", FailSql);
        var nu = RecordStore.RestateIssue(db, old, new NewRecord(Lane, "restated-fail", "issue", "k5b restated", "b", "k5b-test",
            SourceKind: "session", SourceRef: "k5b test", CheckKind: "query", CheckRef: FailSql));
        var restated = RecordStore.Get(db, old)!;
        Assert.Equal(("superseded", (int?)nu), (restated.Status, restated.SupersededBy));
        Assert.Null(RecordStore.ResolutionOf(db, restated));
        var (code, o, e) = Run(db, null, "check", "--issue", old.ToString(), "--dir", RepoDir());
        Assert.Equal(1, code);                                    // a fail - but no exception, no raw SQL error
        Assert.Equal("", e);
        Assert.Contains($"it was restated as #{nu}: check that one", o);
        Assert.Equal("fail", Assert.Single(BrainStore.ChecksOf(old, db)).Outcome);   // the evidence is kept
        Assert.Contains("restated as", Assert.Throws<RecordRefusedException>(() => RecordStore.OpenRegression(db, old, 1, "k5b")).Message);
        var pass = IssueResolver.Apply(db, restated, BrainStore.ChecksOf(old, db)[0].Id, "pass", "k5b", "x");
        Assert.DoesNotContain("already resolved", pass.Line);
        Assert.Contains($"restated as #{nu}", pass.Line);
    }

    [Theory]
    [InlineData("brain:WITH x AS (SELECT 1 AS a) UPDATE brain.Memory SET Body = Body WHERE 1 = 0")]
    [InlineData("brain:SELECT CAST(1 AS bit) AS Pass INTO #t")]
    [InlineData("brain:SELECT CAST(1 AS bit) AS Pass; EXEC sp_who")]
    public void AQueryCheckThatWritesIsRefusedBeforeItRuns(string checkRef)
    {
        Assert.Null(IssueCheck.ParseQueryRef(checkRef).Sql);
        Assert.NotNull(IssueCheck.ParseQueryRef(PassSql).Sql);
    }

    // 16: an issue without its check, and a gate outside the allowlist
    [Fact]
    public void AnIssueNeedsItsCheckAndAGateIsAllowlisted()
    {
        var (code, _, e) = Run(null, null, "issue", "--module", Lane, "--name", "x", "--description", "x");
        Assert.Equal(1, code);
        Assert.Contains("--check test|gate|query|source", e);
        var gate = TestIssue(RepoDir()) with { CheckKind = "gate", CheckRef = "powershell -c exit 0" };
        Assert.Contains("is not one of kor, netops, dotnet", IssueCheck.Run(gate, TimeSpan.FromSeconds(5), RepoDir()).Detail);
    }
}
