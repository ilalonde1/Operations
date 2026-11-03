using System.Data;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K5 - nothing stale (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). LIVE on KorStandards as korbrain_app, every test in
/// its own <see cref="BrainDb.OpenRolledBack"/> (never committed), lane zz-test-k5-&lt;time&gt;. Fixture: Framework's real
/// history of docs/KNOWLEDGE-IDE.md (3b2ce76c, then 3caceb0f) and a real deletion in this repo's history.
///
/// Covers: the verdicts (old blob stale, HEAD blob current, reverted current, missing path stale, unknown repo
/// unverifiable + unwritten), superseded never reopened, recheck confirm supersedes, insert refuses a wrong blob, git
/// output lowercased before compare and write, FileChanged exits 0 on garbage and writes nothing, capture-git re-checks.
/// Does NOT cover: the hook wiring (settings.json FileChanged/SessionStart - not wired until K6); a file-sourced record
/// (FileMemoryLiveTests: an imported memory file that changes reads stale). Same-class miss: a record citing a repo that sits anywhere but beside this checkout reads
/// unverifiable here though it may be current.
/// </summary>
[Collection(LiveDb.Name)]
public class FreshnessTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private readonly Xunit.Abstractions.ITestOutputHelper _log = log;

    private static readonly string Lane = $"zz-test-k5-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Doc = "docs/KNOWLEDGE-IDE.md";
    private const string OldCommit = "3b2ce76c";

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static string FrameworkDir() =>
        RepoLocator.Find("Framework", RepoDir()) ?? throw new InvalidOperationException("Framework is not beside this checkout");

    private static NewRecord Finding(string name, GitSource source) =>
        new(Lane, name, "finding", $"k5 test {name}", $"k5 test body {name}", "k5-test", source);

    private static int Insert(BrainDb db, string name, GitSource source) => RecordStore.Insert(db, Finding(name, source));

    private static StoredRecord Row(BrainDb db, int id) => RecordStore.Get(db, id) ?? throw new InvalidOperationException($"no #{id}");

    private static Checked Only(FreshnessReport r, int id) => Assert.Single(r.Rows, x => x.Id == id);

    /// <summary>A raw row, for states RecordStore will not write (an unknown repo, a superseded row).</summary>
    private static int RawInsert(BrainDb db, string name, string repo, string commit, string path, string blob, string status,
                                 bool retired)
    {
        using var c = db.Command(
            "DECLARE @id TABLE (Id int); " +
            "INSERT brain.Memory (Lane, Name, Kind, Description, Body, CreatedBy, SourceKind, SourceRepo, SourceCommit, SourcePath, " +
            "SourceBlob, Status, RetiredAtUtc, RetiredBy, RetiredBecause) OUTPUT INSERTED.Id INTO @id VALUES (@l, @n, N'finding', N'd', N'b', " +
            "N'k5-test', N'git', @r, @c, @p, @b, @s, " + (retired ? "SYSDATETIMEOFFSET(), N'k5-test', N'test'" : "NULL, NULL, NULL") +
            "); SELECT Id FROM @id;");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = Lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = name;
        c.Parameters.Add("@r", SqlDbType.NVarChar, 80).Value = repo;
        c.Parameters.Add("@c", SqlDbType.Char, 40).Value = commit;
        c.Parameters.Add("@p", SqlDbType.NVarChar, 400).Value = path;
        c.Parameters.Add("@b", SqlDbType.Char, 40).Value = blob;
        c.Parameters.Add("@s", SqlDbType.NVarChar, 12).Value = status;
        return Convert.ToInt32(c.ExecuteScalar());
    }

    // 1
    [Fact]
    public void LiveAnOldBlobIsStaleAndWrittenStale()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Insert(db, "old-blob", RecordStore.At(FrameworkDir(), Doc, OldCommit));
        var c = Only(Freshness.CheckModule(db, Lane, RepoDir()), id);
        Assert.Equal(Verdict.Stale, c.Verdict);
        Assert.Equal(Git.Run(FrameworkDir(), "rev-parse", $"HEAD:{Doc}")!.Trim(), c.HeadBlob);
        var row = Row(db, id);
        Assert.Equal("stale", row.Status);
        Assert.NotNull(row.CheckedAtUtc);
    }

    // 2
    [Fact]
    public void LiveTheHeadBlobIsCurrent()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Insert(db, "head-blob", RecordStore.AtHead(FrameworkDir(), Doc));
        Assert.Equal(Verdict.Current, Only(Freshness.CheckModule(db, Lane, RepoDir()), id).Verdict);
        Assert.Equal("current", Row(db, id).Status);
        Assert.NotNull(Row(db, id).CheckedAtUtc);
    }

    // 3
    [Fact]
    public void LiveARevertedFileReturnsToCurrent()
    {
        using var db = BrainDb.OpenRolledBack();
        var source = RecordStore.AtHead(FrameworkDir(), Doc);
        var id = Insert(db, "reverted", source);
        // HEAD moves to a different blob (the file was edited and committed) ...
        Freshness.HeadReader edited = (_, paths) => new HeadTree(new string('1', 40), paths.ToDictionary(p => p, _ => new string('2', 40)));
        Assert.Equal(Verdict.Stale, Only(Freshness.CheckModule(db, Lane, RepoDir(), edited), id).Verdict);
        Assert.Equal("stale", Row(db, id).Status);
        // ... and back to the cited blob (the edit was reverted): the record reads current again.
        Assert.Equal(Verdict.Current, Only(Freshness.CheckModule(db, Lane, RepoDir()), id).Verdict);
        Assert.Equal("current", Row(db, id).Status);
    }

    // 4
    [Fact]
    public void LiveAPathMissingAtHeadIsStale()
    {
        using var db = BrainDb.OpenRolledBack();
        var ops = RepoDir();
        // A real deletion from this repo's history: cite the file as it was in the deleting commit's parent.
        var log = Git.Run(ops, "log", "--diff-filter=D", "-1", "--format=%H", "--name-only", "--no-renames")!
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (commit, path) = (log[0], log[1]);
        Assert.Null(Git.Run(ops, "rev-parse", "--verify", "--quiet", $"HEAD:{path}"));   // it is gone at HEAD
        var id = Insert(db, "missing-path", RecordStore.At(ops, path, commit + "^"));
        var c = Only(Freshness.CheckModule(db, Lane, ops), id);
        Assert.Equal(Verdict.Stale, c.Verdict);
        Assert.Null(c.HeadBlob);
        Assert.Contains("is gone", c.Why);
        Assert.Equal("stale", Row(db, id).Status);
    }

    // 5
    [Fact]
    public void LiveAnUnknownRepoIsUnverifiableAndUnwritten()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = RawInsert(db, "unknown-repo", "zz-no-such-repo-k5", new string('a', 40), Doc, new string('b', 40), "current", false);
        var c = Only(Freshness.CheckModule(db, Lane, RepoDir()), id);
        Assert.Equal(Verdict.Unverifiable, c.Verdict);
        var row = Row(db, id);
        Assert.Equal("current", row.Status);    // never "current" BY the check, and never written
        Assert.Null(row.CheckedAtUtc);
    }

    // 6
    [Fact]
    public void LiveASupersededRecordIsNeverReopened()
    {
        using var db = BrainDb.OpenRolledBack();
        var head = RecordStore.AtHead(FrameworkDir(), Doc);
        // Superseded but not retired, and retired-stale: both carry the HEAD blob, so a careless write would revive them.
        var superseded = RawInsert(db, "superseded", head.Repo, head.Commit, head.Path, head.Blob, "superseded", false);
        var retired = RawInsert(db, "retired", head.Repo, head.Commit, head.Path, head.Blob, "stale", true);
        var report = Freshness.CheckModule(db, Lane, RepoDir());
        Assert.DoesNotContain(report.Rows, r => r.Id == superseded || r.Id == retired);
        Freshness.CheckRepo(db, head.Repo, RepoDir());
        // The write itself, handed these rows directly (review row 139: the read filter alone must not be what holds it).
        Freshness.Write(db, new[] { superseded, retired }.Select(id => new Checked(id, Lane, "x", "finding", "git", head.Repo,
            head.Path, head.Blob, head.Blob, Verdict.Current, "test")).ToList());
        Assert.Equal(("superseded", (DateTimeOffset?)null), (Row(db, superseded).Status, Row(db, superseded).CheckedAtUtc));
        Assert.Equal(("stale", (DateTimeOffset?)null), (Row(db, retired).Status, Row(db, retired).CheckedAtUtc));
    }

    // 7
    [Fact]
    public void LiveRecheckConfirmSupersedesWithTheHeadBlob()
    {
        using var db = BrainDb.OpenRolledBack();
        var old = Insert(db, "confirm-me", RecordStore.At(FrameworkDir(), Doc, OldCommit));
        Assert.Equal(Verdict.Stale, Only(Freshness.CheckModule(db, Lane, RepoDir()), old).Verdict);
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(new[] { "recheck", old.ToString(), "confirm", "--because", "k5 test: still true", "--dir", RepoDir() },
                               () => null, output, error, Path.Combine(Path.GetTempPath(), $"kor-k5-{Guid.NewGuid():N}.log"), db);
        Assert.True(code == 0, error.ToString());
        var m = System.Text.RegularExpressions.Regex.Match(output.ToString(), @"superseded by #(\d+)");
        Assert.True(m.Success, output.ToString());
        var newId = int.Parse(m.Groups[1].Value);
        var o = Row(db, old);
        Assert.Equal(("superseded", (int?)newId), (o.Status, o.SupersededBy));
        Assert.NotNull(o.RetiredAtUtc);
        var n = Row(db, newId);
        Assert.Equal((Lane, "confirm-me", "k5 test body confirm-me"), (n.Lane, n.Name, n.Body));
        Assert.Equal(Git.Run(FrameworkDir(), "rev-parse", $"HEAD:{Doc}")!.Trim(), n.SourceBlob);
        var after = Freshness.CheckModule(db, Lane, RepoDir());
        Assert.Equal(Verdict.Current, Assert.Single(after.Rows).Verdict);   // only the new record is live, and current
        Assert.Equal(newId, after.Rows[0].Id);
    }

    // 8
    [Fact]
    public void LiveInsertRefusesABlobThatIsNotTheCommitsBlob()
    {
        using var db = BrainDb.OpenRolledBack();
        var head = RecordStore.AtHead(FrameworkDir(), Doc);
        var oldBlob = RecordStore.At(FrameworkDir(), Doc, OldCommit).Blob;
        Assert.NotEqual(head.Blob, oldBlob);
        var e = Assert.Throws<RecordRefusedException>(() => Insert(db, "wrong-blob", head with { Blob = oldBlob }));
        Assert.Contains("is not git rev-parse", e.Message);
        Assert.Throws<RecordRefusedException>(() => Insert(db, "upper-blob", head with { Blob = head.Blob.ToUpperInvariant() }));
        Assert.Null(RecordStore.LiveByName(db, Lane, "wrong-blob"));
        Assert.Null(RecordStore.LiveByName(db, Lane, "upper-blob"));
    }

    // 9
    [Fact]
    public void LiveGitOutputIsLowercasedBeforeCompareAndWrite()
    {
        using var db = BrainDb.OpenRolledBack();
        var head = RecordStore.AtHead(FrameworkDir(), Doc);
        var id = Insert(db, "lowercase", head);
        // git printing uppercase (it does not today; review row 102) must still read current - compared and written lowercased.
        Freshness.HeadReader upper = (_, paths) =>
            new HeadTree(head.Commit.ToUpperInvariant(), paths.ToDictionary(p => p, _ => head.Blob.ToUpperInvariant()));
        var c = Only(Freshness.CheckModule(db, Lane, RepoDir(), upper), id);
        Assert.Equal((Verdict.Current, head.Blob), (c.Verdict, c.HeadBlob));
        Assert.Equal("current", Row(db, id).Status);   // the DB's binary compare agreed: the value sent was lowercase
        Assert.Equal(head.Blob, Freshness.ParseLsTree($"100644 blob {head.Blob.ToUpperInvariant()}\t{Doc}\0").Single().Blob);
    }

    // 10
    [Fact]
    public void FileChangedExitsZeroSilentlyOnGarbage()
    {
        var log = Path.Combine(Path.GetTempPath(), $"kor-k5-{Guid.NewGuid():N}.log");
        foreach (var input in new[] { "{not json", "[1,2]", "", null, "{\"file_path\": 7}", "{\"event\":\"change\"}" })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "fresh", "--file-changed", "--hook" }, () => input, output, error, log));
            Assert.Equal("", output.ToString() + error.ToString());
        }
        if (File.Exists(log)) File.Delete(log);
    }

    // 11 (beyond the spec's 10): FileChanged names a changed source and writes nothing
    [Fact]
    public void LiveFileChangedNamesTheRecordAndWritesNothing()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Insert(db, "file-changed", RecordStore.At(FrameworkDir(), Doc, OldCommit));
        var json = System.Text.Json.JsonSerializer.Serialize(new { file_path = Path.Combine(FrameworkDir(), Doc), @event = "change" });
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "fresh", "--file-changed", "--hook" }, () => json, output, new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-k5-{Guid.NewGuid():N}.log"), db));
        Assert.Contains("systemMessage", output.ToString());
        Assert.Contains($"#{id} finding file-changed", output.ToString());
        var row = Row(db, id);
        Assert.Equal(("current", (DateTimeOffset?)null), (row.Status, row.CheckedAtUtc));   // nothing written
    }

    // 12: capture-git re-checks the repo after its event
    [Fact]
    public void LiveCaptureGitRechecksTheRepoAfterItsEvent()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Insert(db, "capture-git", RecordStore.At(FrameworkDir(), Doc, OldCommit));
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(new[] { "capture-git", "post-checkout", "--dir", FrameworkDir(), "--module", Lane }, () => null,
                               output, error, Path.Combine(Path.GetTempPath(), $"kor-k5-{Guid.NewGuid():N}.log"), db);
        Assert.True(code == 0, error.ToString());
        Assert.Contains("freshness of repo Framework:", output.ToString());
        Assert.Equal("stale", Row(db, id).Status);
    }

    // --source git:path[@rev] is split where git finds the path at the rev (review rows 139, 140). The fake "git" tracks
    // docs/a.md and node_modules/@scope/x.js at the revs a real repo would have.
    private static bool FakeTracked(string rev, string path) =>
        path is "docs/a.md" or "node_modules/@scope/x.js" && rev is "HEAD" or "3b2ce76c" or "HEAD@{1}" or "main@{yesterday}";

    [Theory]
    [InlineData("docs/a.md", "docs/a.md", "HEAD")]
    [InlineData("docs/a.md@3b2ce76c", "docs/a.md", "3b2ce76c")]
    [InlineData("docs/a.md@HEAD@{1}", "docs/a.md", "HEAD@{1}")]
    [InlineData("docs/a.md@main@{yesterday}", "docs/a.md", "main@{yesterday}")]
    [InlineData("node_modules/@scope/x.js", "node_modules/@scope/x.js", "HEAD")]
    [InlineData("node_modules/@scope/x.js@3b2ce76c", "node_modules/@scope/x.js", "3b2ce76c")]
    [InlineData("node_modules/@scope/x.js@HEAD@{1}", "node_modules/@scope/x.js", "HEAD@{1}")]
    public void AGitSourceSplitsWhereGitFindsThePathAtTheRev(string spec, string path, string rev)
    {
        Assert.Equal((path, rev), Program.SplitGitSource(spec, FakeTracked));
    }

    [Fact]
    public void AGitSourceSplitIsDecidedByTheRealRepo()
    {
        // Real git: Framework's docs/KNOWLEDGE-IDE.md at its first commit, and at HEAD; a path git does not know stays whole.
        var tracked = Program.TrackedIn(FrameworkDir());
        Assert.Equal((Doc, OldCommit), Program.SplitGitSource($"{Doc}@{OldCommit}", tracked));
        Assert.Equal((Doc, "HEAD"), Program.SplitGitSource(Doc, tracked));
        Assert.Equal(("node_modules/@scope/x.js", "HEAD"), Program.SplitGitSource("node_modules/@scope/x.js", tracked));
        // a DIRECTORY at a revision is not a source (review row 150: rev-parse rev:dir resolves a tree)
        var docsTree = Git.Run(FrameworkDir(), "rev-parse", "--verify", "--quiet", "HEAD:docs")?.Trim();   // precondition (row 154)
        Assert.Equal("tree", Git.Run(FrameworkDir(), "cat-file", "-t", docsTree ?? "missing")?.Trim());
        Assert.False(tracked("HEAD", "docs"));
        Assert.Null(RecordStore.BlobAt(FrameworkDir(), "HEAD", "docs"));
        Assert.Throws<RecordRefusedException>(() => RecordStore.AtHead(FrameworkDir(), "docs"));
        Assert.Equal(Git.Run(FrameworkDir(), "rev-parse", $"HEAD:{Doc}")!.Trim(), RecordStore.BlobAt(FrameworkDir(), "HEAD", Doc));
    }

    // capture-git's 3 s cap (review row 140): the installed kor.exe passes no unit of work and waits at most the cap.
    [Fact]
    public void TheRecheckCapStopsWaitingAndSaysSo()
    {
        var release = new ManualResetEventSlim();
        var logged = new List<Exception>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var line = Program.RecheckWithin("Framework", () => { release.Wait(TimeSpan.FromSeconds(30)); return Empty; },
                                         TimeSpan.FromMilliseconds(200), logged.Add);
        sw.Stop();
        release.Set();
        Assert.Equal("freshness of repo Framework: over 0.2 s - not waited for", line);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"waited {sw.Elapsed}");
        Assert.Empty(logged);
        Assert.Equal(TimeSpan.FromSeconds(3), Program.FreshnessCap);
    }

    [Fact]
    public void TheRecheckFailureIsLoggedAndNamedNeverThrown()
    {
        var logged = new List<Exception>();
        var line = Program.RecheckWithin("Framework", () => throw new InvalidOperationException("db down"),
                                         TimeSpan.FromSeconds(3), logged.Add);
        Assert.Equal("freshness of repo Framework failed: db down", line);
        Assert.Equal("db down", Assert.Single(logged).Message);
        Assert.Equal("freshness of repo Framework: 0 checked, 0 current, 0 stale, 0 unverifiable",
                     Program.RecheckWithin("Framework", () => Empty, TimeSpan.FromSeconds(3), logged.Add));
    }

    private static readonly FreshnessReport Empty = new(Array.Empty<Checked>(), new Dictionary<string, string?>(), true);

    // ---- review row 146: the fresh hook always says one verdict line, and is capped -------------------------------------
    private static string[] Lines(StringWriter w) =>
        w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private (int Code, string[] Out, string Log) RunFresh(BrainDb? db, params string[] args)
    {
        var log = Path.Combine(Path.GetTempPath(), $"kor-k5-{Guid.NewGuid():N}.log");
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, () => null, output, error, log, db);
        Assert.Equal("", error.ToString());
        var logged = File.Exists(log) ? File.ReadAllText(log) : "";
        if (File.Exists(log)) File.Delete(log);
        return (code, Lines(output), logged);
    }

    [Fact]
    public void LiveTheFreshHookPrintsExactlyOneVerdictLineWhenAllIsCurrent()
    {
        using var db = BrainDb.OpenRolledBack();
        Insert(db, "hook-current", RecordStore.AtHead(FrameworkDir(), Doc));
        var head = Git.Run(FrameworkDir(), "rev-parse", "HEAD")!.Trim()[..7];
        var (code, lines, _) = RunFresh(db, "fresh", "--module", Lane, "--dir", RepoDir(), "--hook");
        Assert.Equal(0, code);
        Assert.Equal($"kor fresh: {Lane} - 1 current, 0 stale, 0 unverifiable at HEAD Framework {head}", Assert.Single(lines));
    }

    [Fact]
    public void LiveTheFreshHookPutsTheVerdictLineFirstThenEachStaleRecord()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = Insert(db, "hook-stale", RecordStore.At(FrameworkDir(), Doc, OldCommit));
        var (code, lines, _) = RunFresh(db, "fresh", "--module", Lane, "--dir", RepoDir(), "--hook");
        Assert.Equal(0, code);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith($"kor fresh: {Lane} - 0 current, 1 stale, 0 unverifiable at HEAD Framework ", lines[0]);
        Assert.StartsWith($"STALE #{id} finding hook-stale", lines[1]);
        Assert.StartsWith($"recheck: kor recheck {id} confirm", lines[2]);
    }

    [Fact]
    public void LiveTheFreshHookSaysCouldNotCheckOnAnyErrorAndLogsIt()
    {
        // usage error (no --module)
        var (code, lines, log) = RunFresh(null, "fresh", "--hook");
        Assert.Equal(0, code);
        Assert.Equal("kor fresh: COULD NOT CHECK - fresh needs --module <lane> (or --file-changed) (logged)", Assert.Single(lines));
        Assert.Contains("fresh needs --module", log);
        // the database gone from under it (a disposed unit of work: the connection is closed)
        var dead = BrainDb.OpenRolledBack();
        dead.Dispose();
        (code, lines, log) = RunFresh(dead, "fresh", "--module", Lane, "--dir", RepoDir(), "--hook");
        Assert.Equal(0, code);
        var line = Assert.Single(lines);
        Assert.StartsWith("kor fresh: COULD NOT CHECK - ", line);
        Assert.EndsWith(" (logged)", line);
        Assert.Contains("fresh module=" + Lane, log);
    }

    [Fact]
    public void LiveACappedCheckWritesWhatItCheckedAndReportsTheRestUnchecked()
    {
        using var db = BrainDb.OpenRolledBack();
        // Two repos, read in Id order: Framework first, then Operations. Each git read takes 2.5 s; the budget is 2 s, which the DB read alone cannot use up (review row 150).
        var fw = Insert(db, "cap-framework", RecordStore.AtHead(FrameworkDir(), Doc));
        var ops = Insert(db, "cap-operations", RecordStore.AtHead(RepoDir(), "Kor.Operations.Framework/BrainDb.cs"));
        Freshness.HeadReader slow = (dir, paths) => { Thread.Sleep(2500); return Freshness.GitHead(dir, paths); };
        var r = Freshness.CheckModule(db, Lane, RepoDir(), slow, TimeSpan.FromSeconds(2));
        Assert.True(r.Partial);
        Assert.Equal(Verdict.Current, Only(r, fw).Verdict);
        Assert.Equal(Verdict.Unverifiable, Only(r, ops).Verdict);
        Assert.StartsWith("cap hit after", Only(r, ops).Why);
        Assert.NotNull(Row(db, fw).CheckedAtUtc);    // what it checked is written
        Assert.Null(Row(db, ops).CheckedAtUtc);      // what it did not, is not
        Assert.StartsWith($"kor fresh: PARTIAL - cap hit after ", Program.VerdictLine(Lane, r));
        Assert.Contains("1 current, 0 stale, 1 unverifiable at HEAD Framework ", Program.VerdictLine(Lane, r));
        // and through the verb: --cap 0 checks nothing, still exits 0 with one PARTIAL verdict line
        var (code, lines, _) = RunFresh(db, "fresh", "--module", Lane, "--dir", RepoDir(), "--cap", "0", "--hook");
        Assert.Equal(0, code);
        // the WHOLE line (review row 150): it must not claim there were no records
        Assert.Matches($@"^kor fresh: PARTIAL - cap hit after \d+(\.\d)? s - {System.Text.RegularExpressions.Regex.Escape(Lane)}: " +
                       @"0 current, 0 stale, 2 unverifiable - no repo was read before the cap$", Assert.Single(lines.Take(1)));
        Assert.Equal(3, lines.Length);   // the verdict, then one line per unchecked record (no recheck line: not stale)
        Assert.Equal(10, Program.FreshCap.TotalSeconds);
    }

    [Fact]
    public void SecondsArePrintedWithADotOnACommaDecimalMachine()
    {
        // review rows 154, 157: a de-DE / fr-FR culture must not turn "10.5" into "10,5" (CurrentCulture is per thread).
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
                Assert.Equal("10,5", (10.5).ToString("0.#"));   // the culture really is comma-decimal here
                Assert.Equal("10.5", Freshness.Secs(TimeSpan.FromSeconds(10.5)));
                Assert.Equal("10.5", Program.Secs(TimeSpan.FromSeconds(10.5)));
                Assert.Equal("freshness of repo R: over 0.2 s - not waited for",
                             Program.RecheckWithin("R", () => { Thread.Sleep(2000); return Empty; }, TimeSpan.FromMilliseconds(200), _ => { }));
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void ACappedCheckThatHangsIsNotWaitedForAndAFailureIsItself()
    {
        var release = new ManualResetEventSlim();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(Program.CappedCheck(() => { release.Wait(TimeSpan.FromSeconds(30)); return Empty; }, TimeSpan.FromMilliseconds(200)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"waited {sw.Elapsed}");
        release.Set();
        var e = Assert.Throws<InvalidOperationException>(() => Program.CappedCheck(() => throw new InvalidOperationException("db down"), TimeSpan.FromSeconds(3)));
        Assert.Equal("db down", e.Message);
    }

    // ---- 171 readiness: a plain OUTPUT clause is refused (error 334) on a table with an enabled trigger -------------------
    // The pattern is built so this file's own text never matches it; it is proved on a known-bad and a known-good sample.
    // THE PROOF USED for 171 readiness is this source-shape test: korbrain_app may not create a trigger (SQL 2104, the
    // probe below), so no live trigger test is possible as that login.
    // Covers: a plain OUTPUT of INSERTED./DELETED. columns or * (bracketed or not, one or a list) in any .cs file of
    // Kor.Operations.Framework, .Cli and .Tests (obj/bin excluded).
    // Does NOT cover (review row 152): an OUTPUT of an EXPRESSION (CAST(INSERTED.Id AS ...), $action); OUTPUT text split
    // across two C# string literals; SQL outside these three projects (db/*.sql, Python tools, other apps writing
    // brain.Memory); a MERGE without OUTPUT that 171's triggers would still fire on (that is fine - only OUTPUT breaks).
    // Same-class miss: a writer in another repo (Framework tools/kor_brain.py) with a plain OUTPUT on brain.Memory.
    private static readonly System.Text.RegularExpressions.Regex PlainOutput = new(
        @"OUTPUT\s+(?>(?:INSERTED|DELETED)\.(?:\[?\w+\]?|\*)(?:\s*,\s*(?:INSERTED|DELETED)\.(?:\[?\w+\]?|\*))*)(?!\s+INTO\b)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Fact]
    public void NoPlainOutputColumnListInTheKorSources()
    {
        const string o = "OUT" + "PUT";
        Assert.Matches(PlainOutput, $"INSERT brain.Memory (a) {o} INSERTED.Id VALUES (1)");
        Assert.Matches(PlainOutput, $"UPDATE brain.Memory SET a = 1 {o} DELETED.Id, INSERTED.Id WHERE Id = 1");
        Assert.Matches(PlainOutput, $"INSERT brain.Memory (a) {o} INSERTED.* VALUES (1)");   // review row 152
        Assert.DoesNotMatch(PlainOutput, $"INSERT brain.Memory (a) {o} INSERTED.* INTO @t VALUES (1)");
        Assert.DoesNotMatch(PlainOutput, $"INSERT brain.Memory (a) {o} INSERTED.Id INTO @id VALUES (1)");
        Assert.DoesNotMatch(PlainOutput, $"UPDATE x SET a = 1 {o} DELETED.Id, INSERTED.Id INTO @t WHERE Id = 1");
        var root = RepoDir();
        var files = new[] { "Kor.Operations.Framework", "Kor.Operations.Framework.Cli", "Kor.Operations.Framework.Tests" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count >= 10, $"only {files.Count} source files found under {root}");
        var plain = files.SelectMany(f => PlainOutput.Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}")).ToList();
        Assert.True(plain.Count == 0, "OUTPUT without INTO (refused once a trigger exists): " + string.Join("; ", plain));
        // and the writers this rests on still use the safe shape (4 = RecordStore, Event, LaneState, this file's fixture)
        var into = files.Sum(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"INSERTED\.Id INTO @id").Count);
        Assert.True(into >= 4, $"{into} OUTPUT ... INTO @id writers found");
    }

    /// <summary>
    /// The direct proof, if korbrain_app may create a trigger: a trigger on brain.Memory inside this rolled-back
    /// transaction, then RecordStore.Insert. If the login may not (brain_rw has SELECT/INSERT/UPDATE only - 162), the
    /// refusal is asserted to be a PERMISSION refusal, and <see cref="NoPlainOutputColumnListInTheKorSources"/> is the proof.
    /// </summary>
    [Fact]
    public void LiveRecordStoreInsertSurvivesATriggerOrTheLoginMayNotCreateOne()
    {
        using var db = BrainDb.OpenRolledBack();
        try
        {
            using (var c = db.Command("CREATE TRIGGER brain.TR_zz_test_k5_probe ON brain.Memory AFTER INSERT AS BEGIN SET NOCOUNT ON; END"))
                c.ExecuteNonQuery();
        }
        catch (SqlException e)
        {
            Assert.True(e.Number == 2104,   // seen live 2 Nov: "Cannot create the trigger ... because you do not have permission"
                        $"CREATE TRIGGER failed for a reason other than permission: {e.Number} {e.Message}");
            _log.WriteLine($"TRIGGER PROBE: korbrain_app may not create a trigger (SQL error {e.Number}: {e.Message})");
            return;
        }
        var id = Insert(db, "under-trigger", RecordStore.AtHead(FrameworkDir(), Doc));
        Assert.NotNull(RecordStore.Get(db, id));
        _log.WriteLine("TRIGGER PROBE: trigger created in the rolled-back transaction; RecordStore.Insert succeeded under it");
    }


    // 13: the guarantee every live test rests on
    [Fact]
    public void LiveATestUnitOfWorkCannotCommitAndLeavesNothing()
    {
        int id;
        using (var db = BrainDb.OpenRolledBack())
        {
            id = Insert(db, "rollback", RecordStore.AtHead(FrameworkDir(), Doc));
            Assert.Throws<InvalidOperationException>(() => db.Commit());
            Assert.NotNull(RecordStore.Get(db, id));
        }
        using var fresh = BrainDb.OpenRolledBack();
        Assert.Null(RecordStore.Get(fresh, id));
    }

    // 14: an issue never goes through the plain Insert (it needs its check - the issue path), and withdraw retires
    [Fact]
    public void LiveAnIssueNeverGoesThroughThePlainInsertAndWithdrawRetires()
    {
        using var db = BrainDb.OpenRolledBack();
        var head = RecordStore.AtHead(FrameworkDir(), Doc);
        var e = Assert.Throws<RecordRefusedException>(() => RecordStore.Insert(db, Finding("issue", head) with { Kind = "issue" }));
        Assert.Contains("written only through the issue path", e.Message);
        var id = Insert(db, "withdraw-me", head);
        RecordStore.Withdraw(db, id, "k5-test", "k5 test: wrong");
        Assert.NotNull(Row(db, id).RetiredAtUtc);
        Assert.Throws<RecordRefusedException>(() => RecordStore.Supersede(db, id, Finding("withdraw-me", head), "k5-test", "again"));
        Assert.Empty(Freshness.CheckModule(db, Lane, RepoDir()).Rows);
    }
}
