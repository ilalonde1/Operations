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
/// (no writer makes one yet). Same-class miss: a record citing a repo that sits anywhere but beside this checkout reads
/// unverifiable here though it may be current.
/// </summary>
public class FreshnessTests
{
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
            "INSERT brain.Memory (Lane, Name, Kind, Description, Body, CreatedBy, SourceKind, SourceRepo, SourceCommit, SourcePath, " +
            "SourceBlob, Status, RetiredAtUtc, RetiredBy, RetiredBecause) OUTPUT INSERTED.Id VALUES (@l, @n, N'finding', N'd', N'b', " +
            "N'k5-test', N'git', @r, @c, @p, @b, @s, " + (retired ? "SYSDATETIMEOFFSET(), N'k5-test', N'test'" : "NULL, NULL, NULL") + ")");
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

    // 14: issues wait for migration 171, and recheck never withdraws or supersedes something that is not live
    [Fact]
    public void LiveIssuesAreRefusedUntil171AndWithdrawRetires()
    {
        using var db = BrainDb.OpenRolledBack();
        var head = RecordStore.AtHead(FrameworkDir(), Doc);
        var e = Assert.Throws<RecordRefusedException>(() => RecordStore.Insert(db, Finding("issue", head) with { Kind = "issue" }));
        Assert.Contains("171", e.Message);
        var id = Insert(db, "withdraw-me", head);
        RecordStore.Withdraw(db, id, "k5-test", "k5 test: wrong");
        Assert.NotNull(Row(db, id).RetiredAtUtc);
        Assert.Throws<RecordRefusedException>(() => RecordStore.Supersede(db, id, Finding("withdraw-me", head), "k5-test", "again"));
        Assert.Empty(Freshness.CheckModule(db, Lane, RepoDir()).Rows);
    }
}
