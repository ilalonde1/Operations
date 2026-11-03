using System.Data;
using Kor.Operations.Framework.Capture;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework.Records;

/// <summary>A git source, verified: <c>git rev-parse Commit:Path</c> in <c>RepoDir</c> is <c>Blob</c> (all lowercase).</summary>
/// <param name="Repo">the repo's leaf name (brain.Memory.SourceRepo).</param>
/// <param name="RepoDir">the repo's top-level folder on this machine (never stored).</param>
/// <param name="Path">relative to the repo's top level, forward slashes.</param>
public sealed record GitSource(string Repo, string RepoDir, string Commit, string Path, string Blob);

/// <summary>
/// A record to write (brain.Memory, migrations 162 + 168 + 171). The source is either <see cref="Git"/> or a non-git
/// <see cref="SourceKind"/> with <see cref="SourceRef"/> (CK_Memory_OtherSource). The 171 columns: an issue's check
/// (<see cref="CheckKind"/> test|gate|query|source + <see cref="CheckRef"/>), a resolution's <see cref="ResolvesId"/> and
/// <see cref="EvidenceEventId"/>, a regression's <see cref="RegressionOf"/>, a restatement's <see cref="RestatesId"/>.
/// <see cref="SourceCommit"/> is the commit of a non-git source (a resolution proved by a check run at that commit).
/// </summary>
public sealed record NewRecord(string Lane, string Name, string Kind, string Description, string Body, string CreatedBy,
                               GitSource? Git = null, string? SourceKind = null, string? SourceRef = null,
                               string? SourceHash = null, string? Source = null, string? CheckKind = null,
                               string? CheckRef = null, int? ResolvesId = null, int? RegressionOf = null,
                               int? EvidenceEventId = null, int? RestatesId = null, string? SourceCommit = null);

/// <summary>A brain.Memory row as K5 / K5b read it.</summary>
public sealed record StoredRecord(int Id, string Lane, string Name, string Kind, string Description, string Body,
                                  string Status, string? SourceKind, string? SourceRepo, string? SourceCommit,
                                  string? SourcePath, string? SourceBlob, string? SourceRef, string? SourceHash,
                                  DateTimeOffset? RetiredAtUtc, int? SupersededBy, DateTimeOffset? CheckedAtUtc,
                                  string? CheckKind = null, string? CheckRef = null, int? ResolvesId = null,
                                  int? RegressionOf = null, int? EvidenceEventId = null, int? RestatesId = null,
                                  DateTimeOffset? CreatedAtUtc = null)
{
    public bool Live => RetiredAtUtc is null && Status != "superseded";

    /// <summary>An issue still open (current|stale, not retired).</summary>
    public bool OpenIssue => Kind == "issue" && Live;
}

/// <summary>A write RecordStore refuses before anything is sent.</summary>
public sealed class RecordRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// THE record writer (KNOWLEDGE-IDE-SPEC-K5-K6.md, shared plumbing; <c>kor record</c>, <c>kor recheck</c>, <c>kor issue</c>,
/// <c>kor check</c>, <c>kor resolve</c> and capture-tool go through it). Every write runs in the caller's
/// <see cref="BrainDb"/> - the caller commits, or (a test) never does.
/// Non-issues: <see cref="Insert"/> (a git source verified against git), <see cref="Supersede"/>, <see cref="Withdraw"/>.
/// Issues (171, K5b): <see cref="InsertIssue"/>, <see cref="RestateIssue"/> (ONE insert with RestatesId - the trigger
/// TR_Memory_Restate supersedes the old one in the same statement), <see cref="Resolve"/> / <see cref="ResolveBySource"/>
/// (a resolution row, then the issue superseded by it), <see cref="OpenRegression"/>. Each takes the app lock
/// 'brain.issue:&lt;Lane&gt;:&lt;Name&gt;' for the transaction. Every issue and resolution write is ONE ROW, and
/// brain.Event is never updated (migration 173 refuses multi-row issue writes and any brain.Event UPDATE by brain_rw).
/// Same-class miss: a writer that bypasses RecordStore (kor_brain.py note) is checked only by the table's CHECKs and
/// triggers, and is not protected from the same-name race the app lock closes.
/// </summary>
public static class RecordStore
{
    public static readonly string[] WritableKinds = { "finding", "decision", "dead-end", "lesson", "user", "feedback", "project", "reference" };

    /// <summary>Kinds written only through the issue path (never by Insert, Supersede or Withdraw).</summary>
    public static readonly string[] IssueKinds = { "issue", "resolution" };

    public static readonly string[] CheckKinds = { "test", "gate", "query", "source" };

    private const string Columns =
        "Id, Lane, Name, Kind, Description, Body, Status, SourceKind, SourceRepo, SourceCommit, SourcePath, SourceBlob, " +
        "SourceRef, SourceHash, RetiredAtUtc, SupersededBy, CheckedAtUtc, CheckKind, CheckRef, ResolvesId, RegressionOf, " +
        "EvidenceEventId, RestatesId, CreatedAtUtc";

    // ---- git sources ------------------------------------------------------------------------------------------------
    /// <summary>The source as it is at HEAD of the repo holding <paramref name="dirOrRepo"/>.</summary>
    public static GitSource AtHead(string dirOrRepo, string path) => At(dirOrRepo, path, "HEAD");

    /// <summary>The source at a revision. Throws <see cref="RecordRefusedException"/> when the path is not in that tree.</summary>
    public static GitSource At(string dirOrRepo, string path, string rev)
    {
        var top = Git.TopLevel(dirOrRepo) ?? throw new RecordRefusedException($"{dirOrRepo} is not inside a git work tree");
        var rel = Relative(top, path);
        var lines = Git.Run(top, "rev-parse", "--verify", "--quiet", rev + "^{commit}")?.Trim();
        var commit = Lower(lines) ?? throw new RecordRefusedException($"'{rev}' is not a commit in {Git.Leaf(top)}");
        var blob = BlobAt(top, commit, rel)
                   ?? throw new RecordRefusedException($"{rel} is not tracked at {commit[..10]} in {Git.Leaf(top)} - commit it, then cite the commit");
        return new GitSource(Git.Leaf(top)!, top, commit, rel, blob);
    }

    /// <summary>The path relative to the repo's top level with forward slashes; an absolute path outside it is refused.</summary>
    public static string Relative(string top, string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(p))
        {
            var t = top.Replace('\\', '/').TrimEnd('/') + "/";
            if (!p.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                throw new RecordRefusedException($"{path} is not inside {top}");
            p = p[t.Length..];
        }
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        p = p.TrimStart('/');
        if (p.Length == 0 || p.Split('/').Contains("..")) throw new RecordRefusedException($"'{path}' is not a file path in the repo");
        return p;
    }

    /// <summary>
    /// The lowercase blob of <c>rev:path</c>, or null when the path is not a FILE at that revision. <c>rev-parse
    /// rev:path</c> alone also resolves a directory (a tree - review row 150), so the object's type is read too.
    /// </summary>
    public static string? BlobAt(string top, string rev, string relPath)
    {
        var sha = Lower(Git.Run(top, "rev-parse", "--verify", "--quiet", $"{rev}:{relPath}")?.Trim());
        if (sha is null) return null;
        return Git.Run(top, "cat-file", "-t", sha)?.Trim() == "blob" ? sha : null;
    }

    /// <summary>Lowercased 40-hex, or null (git output is lowercased before any compare or write - review row 102).</summary>
    public static string? Lower(string? sha) => BrainStore.Sha(sha?.Trim().ToLowerInvariant());

    /// <summary>Re-reads the source from git and refuses it unless the commit, path and blob agree, all lowercase.</summary>
    public static void Verify(GitSource g)
    {
        if (BrainStore.Sha(g.Commit) is null) throw new RecordRefusedException($"commit '{g.Commit}' is not 40 lowercase hex");
        if (BrainStore.Sha(g.Blob) is null) throw new RecordRefusedException($"blob '{g.Blob}' is not 40 lowercase hex");
        var top = Git.TopLevel(g.RepoDir) ?? throw new RecordRefusedException($"{g.RepoDir} is not inside a git work tree");
        if (!string.Equals(Git.Leaf(top), g.Repo, StringComparison.Ordinal))
            throw new RecordRefusedException($"{g.RepoDir} is repo {Git.Leaf(top)}, not {g.Repo}");
        var commit = Lower(Git.Run(top, "rev-parse", "--verify", "--quiet", g.Commit + "^{commit}")?.Trim());
        if (commit != g.Commit) throw new RecordRefusedException($"{g.Commit} is not a commit in {g.Repo}");
        var blob = BlobAt(top, g.Commit, g.Path);
        if (blob is null) throw new RecordRefusedException($"{g.Path} is not tracked at {g.Commit[..10]} in {g.Repo}");
        if (blob != g.Blob)
            throw new RecordRefusedException($"blob {g.Blob} is not git rev-parse {g.Commit[..10]}:{g.Path} ({blob}) in {g.Repo}");
    }

    // ---- writes: non-issues ------------------------------------------------------------------------------------------
    /// <summary>A finding, decision, dead-end, lesson (or a 162 kind). Issues and resolutions go through the issue path.</summary>
    public static int Insert(BrainDb db, NewRecord r)
    {
        if (IssueKinds.Contains(r.Kind))
            throw new RecordRefusedException($"kind '{r.Kind}' is written only through the issue path (`kor issue`, `kor check`, `kor resolve`)");
        Validate(r);
        return InsertRow(db, r);
    }

    /// <summary>The ONE insert of brain.Memory: a single row, its Id read through OUTPUT ... INTO.</summary>
    private static int InsertRow(BrainDb db, NewRecord r)
    {
        using var c = db.Command(
            // OUTPUT ... INTO a table variable: a plain OUTPUT is refused (error 334) on a table with an enabled trigger,
            // and migration 171 put triggers on brain.Memory (TR_Memory_Restate, TR_Memory_EvidenceAppendOnly).
            "DECLARE @id TABLE (Id int); " +
            "INSERT brain.Memory (Lane, Name, Kind, Description, Body, Source, CreatedBy, SourceKind, SourceRepo, SourceCommit, " +
            "SourcePath, SourceBlob, SourceRef, SourceHash, CheckKind, CheckRef, ResolvesId, RegressionOf, EvidenceEventId, RestatesId) " +
            "OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@l, @n, @k, @d, @b, @s, @cb, @sk, @sr, @sc, @sp, @sb, @sref, @sh, @ck, @cr, @res, @reg, @ev, @rst); SELECT Id FROM @id;");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = r.Lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = r.Name;
        c.Parameters.Add("@k", SqlDbType.NVarChar, 10).Value = r.Kind;
        c.Parameters.Add("@d", SqlDbType.NVarChar, 400).Value = r.Description;
        c.Parameters.Add("@b", SqlDbType.NVarChar, -1).Value = r.Body;
        c.Parameters.Add("@s", SqlDbType.NVarChar, 400).Value = (object?)BrainStore.Clip(r.Source, 400) ?? DBNull.Value;
        c.Parameters.Add("@cb", SqlDbType.NVarChar, 100).Value = BrainStore.Clip(r.CreatedBy, 100) ?? "unknown";
        c.Parameters.Add("@sk", SqlDbType.NVarChar, 10).Value = r.Git is not null ? "git" : r.SourceKind!;
        c.Parameters.Add("@sr", SqlDbType.NVarChar, 80).Value = (object?)r.Git?.Repo ?? DBNull.Value;
        c.Parameters.Add("@sc", SqlDbType.Char, 40).Value = (object?)(r.Git?.Commit ?? r.SourceCommit) ?? DBNull.Value;
        c.Parameters.Add("@sp", SqlDbType.NVarChar, 400).Value = (object?)r.Git?.Path ?? DBNull.Value;
        c.Parameters.Add("@sb", SqlDbType.Char, 40).Value = (object?)r.Git?.Blob ?? DBNull.Value;
        c.Parameters.Add("@sref", SqlDbType.NVarChar, 400).Value = (object?)r.SourceRef ?? DBNull.Value;
        c.Parameters.Add("@sh", SqlDbType.NVarChar, 64).Value = (object?)r.SourceHash ?? DBNull.Value;
        c.Parameters.Add("@ck", SqlDbType.NVarChar, 10).Value = (object?)r.CheckKind ?? DBNull.Value;
        c.Parameters.Add("@cr", SqlDbType.NVarChar, 2000).Value = (object?)r.CheckRef ?? DBNull.Value;
        c.Parameters.Add("@res", SqlDbType.Int).Value = (object?)r.ResolvesId ?? DBNull.Value;
        c.Parameters.Add("@reg", SqlDbType.Int).Value = (object?)r.RegressionOf ?? DBNull.Value;
        c.Parameters.Add("@ev", SqlDbType.Int).Value = (object?)r.EvidenceEventId ?? DBNull.Value;
        c.Parameters.Add("@rst", SqlDbType.Int).Value = (object?)r.RestatesId ?? DBNull.Value;
        return Convert.ToInt32(c.ExecuteScalar());
    }

    private static void Validate(NewRecord r)
    {
        if (!WritableKinds.Contains(r.Kind) && !IssueKinds.Contains(r.Kind))
            throw new RecordRefusedException($"kind '{r.Kind}' is not a brain.Memory kind");
        Need(r.Lane, 60, "lane");
        Need(r.Name, 120, "name");
        Need(r.Description, 400, "description");
        if (string.IsNullOrWhiteSpace(r.Body)) throw new RecordRefusedException("the body is empty");
        if (Redact.HasSecretMarker(r.Body) || Redact.HasSecretMarker(r.Description))
            throw new RecordRefusedException("the record carries a secret marker (Password=, Pwd=, sk-ant-, AccountKey=)");
        if (r.Git is not null)
        {
            if (r.SourceKind is not null and not "git") throw new RecordRefusedException("a git source with another SourceKind");
            if (r.Git.Path.Length > 400) throw new RecordRefusedException("the source path is longer than 400");
            Verify(r.Git);
        }
        else
        {
            if (r.SourceKind is not ("file" or "query" or "run" or "session"))
                throw new RecordRefusedException("every record carries a source: git, or file/query/run/session with a ref (CK_Memory_HasSource)");
            Need(r.SourceRef, 400, "source ref");
            if (r.SourceCommit is not null && BrainStore.Sha(r.SourceCommit) is null)
                throw new RecordRefusedException($"source commit '{r.SourceCommit}' is not 40 lowercase hex");
        }
        if (r.CheckKind is not null && !CheckKinds.Contains(r.CheckKind))
            throw new RecordRefusedException($"check kind '{r.CheckKind}' is not test|gate|query|source");
        if (r.CheckRef is { Length: > 2000 }) throw new RecordRefusedException("the check ref is longer than 2000 characters");
    }

    private static void Need(string? value, int max, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new RecordRefusedException($"the {what} is empty");
        if (value.Length > max) throw new RecordRefusedException($"the {what} is longer than {max} characters");
    }

    /// <summary>
    /// Non-issue supersede, in the caller's transaction: the old record is retired (who, why) with Status superseded,
    /// the replacement is inserted (the live-name index allows the same name once the old one is retired), and the old
    /// one points at it. Returns the new Id. An issue is restated with <see cref="RestateIssue"/>, never this.
    /// </summary>
    public static int Supersede(BrainDb db, int oldId, NewRecord replacement, string by, string because)
    {
        var old = Get(db, oldId) ?? throw new RecordRefusedException($"no record #{oldId}");
        RefuseIssueKinds(old, "superseded");
        if (!old.Live) throw new RecordRefusedException($"#{oldId} is not live (status {old.Status}{(old.RetiredAtUtc is null ? "" : ", retired")}) - nothing to supersede");
        if (replacement.Lane != old.Lane) throw new RecordRefusedException($"the replacement is in lane {replacement.Lane}, #{oldId} in {old.Lane}");
        Insertable(replacement);
        Need(because, 400, "reason (--because)");
        Retire(db, oldId, by, because, superseded: true);
        var newId = InsertRow(db, replacement);
        using var c = db.Command("UPDATE brain.Memory SET SupersededBy = @n, UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @o");
        c.Parameters.Add("@n", SqlDbType.Int).Value = newId;
        c.Parameters.Add("@o", SqlDbType.Int).Value = oldId;
        c.ExecuteNonQuery();
        return newId;
    }

    /// <summary>
    /// The ONE refusal of the retire/supersede paths for issues and resolutions, up front, before any write (migration 173
    /// refuses retiring a resolution with 51700; kor says why first). A wrongly closed issue is fixed by a REGRESSION - its
    /// check failing again (RecordStore.OpenRegression, via `kor check --resolved` or a failing test run) - never by
    /// retiring its resolution; an issue is closed only by evidence.
    /// </summary>
    public static void RefuseIssueKinds(StoredRecord r, string verb)
    {
        if (r.Kind == "resolution")
            throw new RecordRefusedException(
                $"#{r.Id} is a resolution (of #{r.ResolvesId}): a resolution is never {verb} or retired. If #{r.ResolvesId} was closed " +
                $"wrongly, its check failing again opens a regression (OpenRegression): `kor check --issue {r.ResolvesId} --resolved`, " +
                "or a failing run of its test");
        if (r.Kind == "issue")
            throw new RecordRefusedException(
                $"#{r.Id} is an issue: it is never {verb} or retired - it is restated (`kor issue --restates {r.Id}`) or closed " +
                $"only by evidence (`kor check --issue {r.Id}`, `kor resolve --issue {r.Id}`)");
    }

    private static void Insertable(NewRecord r)
    {
        if (IssueKinds.Contains(r.Kind))
            throw new RecordRefusedException($"kind '{r.Kind}' is written only through the issue path");
        Validate(r);
    }

    /// <summary>Retires a non-issue with no replacement (it was wrong, or no longer applies).</summary>
    public static void Withdraw(BrainDb db, int id, string by, string because)
    {
        var old = Get(db, id) ?? throw new RecordRefusedException($"no record #{id}");
        if (old.RetiredAtUtc is not null) throw new RecordRefusedException($"#{id} is already retired");
        RefuseIssueKinds(old, "withdrawn");
        Need(because, 400, "reason (--because)");
        Retire(db, id, by, because, superseded: false);
    }

    private static void Retire(BrainDb db, int id, string by, string because, bool superseded)
    {
        using var c = db.Command(
            "UPDATE brain.Memory SET RetiredAtUtc = SYSDATETIMEOFFSET(), RetiredBy = @by, RetiredBecause = @why, " +
            "UpdatedAtUtc = SYSDATETIMEOFFSET()" + (superseded ? ", Status = N'superseded'" : "") +
            " WHERE Id = @id AND RetiredAtUtc IS NULL AND Status <> N'superseded'");
        c.Parameters.Add("@by", SqlDbType.NVarChar, 100).Value = BrainStore.Clip(by, 100) ?? "unknown";
        c.Parameters.Add("@why", SqlDbType.NVarChar, 400).Value = because.Trim();
        c.Parameters.Add("@id", SqlDbType.Int).Value = id;
        if (c.ExecuteNonQuery() != 1) throw new RecordRefusedException($"#{id} changed under us - it is no longer live");
    }

    // ---- writes: issues (171, K5b) ----------------------------------------------------------------------------------
    /// <summary>
    /// Holds 'brain.issue:&lt;Lane&gt;:&lt;Name&gt;' exclusively until the caller's transaction ends: CK_Memory_OneOpenIssue is a
    /// function CHECK, not race-safe - two writers of the same issue name are serialised here (spec K5b, section 4).
    /// </summary>
    public static void LockIssue(BrainDb db, string lane, string name)
    {
        using var c = db.Command(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', " +
            "@LockTimeout = 10000; SELECT @r;");
        c.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value = BrainStore.Clip($"brain.issue:{lane}:{name}", 255);
        var r = Convert.ToInt32(c.ExecuteScalar());
        if (r < 0) throw new RecordRefusedException($"the issue lock for {lane}:{name} was not granted (sp_getapplock {r})");
    }

    /// <summary>A new open issue: it must carry its check (CK_Memory_IssueHasCheck); a source-kind check needs a git source.</summary>
    public static int InsertIssue(BrainDb db, NewRecord issue)
    {
        IssueShape(issue);
        if (issue.RestatesId is not null) throw new RecordRefusedException("a restatement goes through RestateIssue");
        LockIssue(db, issue.Lane, issue.Name);
        if (OpenIssueByName(db, issue.Lane, issue.Name) is { } open)
            throw new RecordRefusedException($"issue '{issue.Name}' is already open in {issue.Lane} as #{open.Id} - restate it (--restates {open.Id}) or resolve it");
        return InsertRow(db, issue);
    }

    /// <summary>
    /// Restates an open issue: ONE insert of the new issue with RestatesId = old; TR_Memory_Restate supersedes the old one
    /// in the same statement (51701 if the old one is not an open issue with the same Lane and Name).
    /// </summary>
    public static int RestateIssue(BrainDb db, int oldId, NewRecord issue)
    {
        IssueShape(issue);
        var old = Get(db, oldId) ?? throw new RecordRefusedException($"no record #{oldId}");
        if (!old.OpenIssue) throw new RecordRefusedException($"#{oldId} is not an open issue");
        if (old.Lane != issue.Lane || old.Name != issue.Name)
            throw new RecordRefusedException($"a restatement keeps the lane and name of #{oldId} ({old.Lane}:{old.Name})");
        LockIssue(db, issue.Lane, issue.Name);
        return InsertRow(db, issue with { RestatesId = oldId });
    }

    private static void IssueShape(NewRecord issue)
    {
        if (issue.Kind != "issue") throw new RecordRefusedException($"kind '{issue.Kind}' is not an issue");
        if (issue.CheckKind is null || string.IsNullOrWhiteSpace(issue.CheckRef))
            throw new RecordRefusedException("an issue carries its check: --check test|gate|query|source and --check-ref");
        if (issue.CheckKind == "source" && issue.Git is null)
            throw new RecordRefusedException("a source-checked issue cites a git file (--source-path): it is resolved when that file changes");
        Validate(issue);
    }

    /// <summary>
    /// Closes an issue whose check PASSED: a resolution row citing the pass event (SourceKind run, SourceCommit = the
    /// event's commit), then the issue - one row - superseded by it. The database refuses a resolution without evidence,
    /// citing a fail, or a pass at another commit (CK_Memory_ResolutionShape / CK_Memory_ResolutionEvidence).
    /// </summary>
    public static int Resolve(BrainDb db, int issueId, int passEventId, string by, string because)
    {
        var issue = Get(db, issueId) ?? throw new RecordRefusedException($"no record #{issueId}");
        if (!issue.OpenIssue) throw new RecordRefusedException($"#{issueId} is not an open issue");
        var ev = BrainStore.EventById(passEventId, db) ?? throw new RecordRefusedException($"no brain.Event #{passEventId}");
        LockIssue(db, issue.Lane, issue.Name);
        var res = InsertRow(db, new NewRecord(issue.Lane, $"resolution of #{issueId}", "resolution",
            BrainStore.Clip($"#{issueId} {issue.Name} resolved: {because}", 400)!, $"{because}\n\nevidence: brain.Event #{passEventId} " +
            $"({ev.Kind} {ev.Outcome} at {ev.Commit}) - {ev.Ref}", by, SourceKind: "run",
            SourceRef: $"brain.Event #{passEventId}", SourceCommit: ev.Commit, ResolvesId: issueId, EvidenceEventId: passEventId));
        CloseIssue(db, issueId, res);
        return res;
    }

    /// <summary>
    /// Closes a source-checked issue: a resolution citing the issue's file at HEAD, which must be a different blob (the
    /// database refuses an unchanged one - CK_Memory_ResolutionEvidence).
    /// </summary>
    public static int ResolveBySource(BrainDb db, int issueId, string by, string because, string? fromDir = null)
    {
        var issue = Get(db, issueId) ?? throw new RecordRefusedException($"no record #{issueId}");
        if (!issue.OpenIssue) throw new RecordRefusedException($"#{issueId} is not an open issue");
        if (issue.CheckKind != "source" || issue.SourceRepo is null || issue.SourcePath is null)
            throw new RecordRefusedException($"#{issueId} has a {issue.CheckKind ?? "no"} check: only a source-checked issue is resolved by its file - run `kor check --issue {issueId}`");
        var repo = RepoLocator.Find(issue.SourceRepo, fromDir)
                   ?? throw new RecordRefusedException($"repo {issue.SourceRepo} is not on this machine beside the current one");
        var head = AtHead(repo, issue.SourcePath);
        LockIssue(db, issue.Lane, issue.Name);
        var res = InsertRow(db, new NewRecord(issue.Lane, $"resolution of #{issueId}", "resolution",
            BrainStore.Clip($"#{issueId} {issue.Name} resolved: {because}", 400)!,
            $"{because}\n\nevidence: {head.Repo}:{head.Path} is blob {head.Blob} at {head.Commit} (the issue cited {issue.SourceBlob})",
            by, head, ResolvesId: issueId));
        CloseIssue(db, issueId, res);
        return res;
    }

    /// <summary>The issue - ONE row - superseded by its resolution (CK_Memory_IssueClosedByEvidence checks the pointer).</summary>
    private static void CloseIssue(BrainDb db, int issueId, int resolutionId)
    {
        using var c = db.Command(
            "UPDATE brain.Memory SET Status = N'superseded', SupersededBy = @r, UpdatedAtUtc = SYSDATETIMEOFFSET() " +
            "WHERE Id = @i AND Kind = N'issue' AND Status IN (N'current', N'stale') AND RetiredAtUtc IS NULL");
        c.Parameters.Add("@r", SqlDbType.Int).Value = resolutionId;
        c.Parameters.Add("@i", SqlDbType.Int).Value = issueId;
        if (c.ExecuteNonQuery() != 1) throw new RecordRefusedException($"#{issueId} changed under us - it is no longer open");
    }

    /// <summary>
    /// A resolved issue failed its check again: a NEW open issue "&lt;name&gt; (regression of #id)" carrying the same check,
    /// RegressionOf = the old issue, evidence = the fail event (CK_Memory_RegressionEvidence: a fail check of the old
    /// issue after its resolution's evidence). Returns the open regression already there instead of a second one.
    /// </summary>
    public static int OpenRegression(BrainDb db, int resolvedIssueId, int failEventId, string by)
    {
        var old = Get(db, resolvedIssueId) ?? throw new RecordRefusedException($"no record #{resolvedIssueId}");
        if (ResolutionOf(db, old) is null)
            throw new RecordRefusedException($"#{resolvedIssueId} is not a resolved issue (superseded by a resolution)" +
                                             (old.SupersededBy is { } next && old.Kind == "issue" ? $" - it was restated as #{next}" : ""));
        var name = BrainStore.Clip($"{old.Name} (regression of #{resolvedIssueId})", 120)!;
        LockIssue(db, old.Lane, name);
        if (OpenIssueByName(db, old.Lane, name) is { } open) return open.Id;
        var ev = BrainStore.EventById(failEventId, db) ?? throw new RecordRefusedException($"no brain.Event #{failEventId}");
        return InsertRow(db, new NewRecord(old.Lane, name, "issue",
            BrainStore.Clip($"REGRESSION of #{resolvedIssueId}: {old.Description}", 400)!,
            $"{old.Body}\n\nregressed: brain.Event #{failEventId} ({ev.Outcome} at {ev.Commit}) - {ev.Ref}", by,
            SourceKind: "run", SourceRef: $"brain.Event #{failEventId}", SourceCommit: ev.Commit,
            CheckKind: old.CheckKind, CheckRef: old.CheckRef, RegressionOf: resolvedIssueId, EvidenceEventId: failEventId));
    }

    /// <summary>
    /// The resolution that closed this issue, or null. RESOLVED = superseded by a row of Kind resolution; an issue
    /// superseded by its restatement (another issue) is not resolved (review row 165).
    /// </summary>
    public static int? ResolutionOf(BrainDb db, StoredRecord issue) =>
        issue.Kind == "issue" && issue.Status == "superseded" && issue.SupersededBy is { } by && Get(db, by)?.Kind == "resolution"
            ? by : null;

    // ---- reads ------------------------------------------------------------------------------------------------------
    public static StoredRecord? Get(BrainDb db, int id)
    {
        using var c = db.Command($"SELECT {Columns} FROM brain.Memory WHERE Id = @id");
        c.Parameters.Add("@id", SqlDbType.Int).Value = id;
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>The live (not retired) non-issue with this name in the lane, if any (UX_Memory_LiveName allows one).</summary>
    public static StoredRecord? LiveByName(BrainDb db, string lane, string name)
    {
        using var c = db.Command($"SELECT {Columns} FROM brain.Memory WHERE Lane = @l AND Name = @n AND RetiredAtUtc IS NULL AND Kind <> N'issue'");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = name;
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>The open issue with this name in the lane (CK_Memory_OneOpenIssue allows one), if any.</summary>
    public static StoredRecord? OpenIssueByName(BrainDb db, string lane, string name)
    {
        using var c = db.Command($"SELECT {Columns} FROM brain.Memory WHERE Lane = @l AND Name = @n AND Kind = N'issue' " +
                                 "AND Status IN (N'current', N'stale') AND RetiredAtUtc IS NULL");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = name;
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>
    /// A module's issues to check: the open ones, and with <paramref name="resolved"/> also each resolved one that has no
    /// open regression yet (so a fail can open one). Optionally only these check kinds.
    /// </summary>
    public static IReadOnlyList<StoredRecord> IssuesToCheck(BrainDb db, string lane, bool resolved, IReadOnlyCollection<string>? kinds)
    {
        using var c = db.Command(
            $"SELECT {Columns} FROM brain.Memory m WHERE m.Lane = @l AND m.Kind = N'issue' AND m.RetiredAtUtc IS NULL " +
            "AND (m.Status IN (N'current', N'stale') OR (@res = 1 AND m.Status = N'superseded' " +
            "     AND EXISTS (SELECT 1 FROM brain.Memory r WHERE r.Id = m.SupersededBy AND r.Kind = N'resolution') " +
            "     AND NOT EXISTS (SELECT 1 FROM brain.Memory g WHERE g.RegressionOf = m.Id AND g.Kind = N'issue'))) " +
            "AND (@k IS NULL OR m.CheckKind IN (SELECT value FROM OPENJSON(@k))) ORDER BY m.Id");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = lane;
        c.Parameters.Add("@res", SqlDbType.Bit).Value = resolved;
        c.Parameters.Add("@k", SqlDbType.NVarChar, -1).Value = kinds is null ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(kinds);
        var rows = new List<StoredRecord>();
        using var r = c.ExecuteReader();
        while (r.Read()) rows.Add(Read(r));
        return rows;
    }

    /// <summary>Live records of a lane, newest first (kor memory).</summary>
    public static IReadOnlyList<StoredRecord> Recent(BrainDb db, string lane, int count)
    {
        using var c = db.Command($"SELECT TOP (@n) {Columns} FROM brain.Memory WHERE Lane = @l AND RetiredAtUtc IS NULL " +
                                 "AND Status <> N'superseded' ORDER BY Id DESC");
        c.Parameters.Add("@n", SqlDbType.Int).Value = Math.Clamp(count, 1, 1000);
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = lane;
        var rows = new List<StoredRecord>();
        using var r = c.ExecuteReader();
        while (r.Read()) rows.Add(Read(r));
        return rows;
    }

    internal static StoredRecord Read(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        S(r, 7), S(r, 8), S(r, 9)?.Trim(), S(r, 10), S(r, 11)?.Trim(), S(r, 12), S(r, 13),
        r.IsDBNull(14) ? null : r.GetDateTimeOffset(14), r.IsDBNull(15) ? null : r.GetInt32(15),
        r.IsDBNull(16) ? null : r.GetDateTimeOffset(16), S(r, 17), S(r, 18), I(r, 19), I(r, 20), I(r, 21), I(r, 22),
        r.IsDBNull(23) ? null : r.GetDateTimeOffset(23));

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    /// <summary>Who is writing: the Windows user, plus the Claude session when one is running kor.</summary>
    public static string Actor()
    {
        var session = Environment.GetEnvironmentVariable("CLAUDE_CODE_BRIDGE_SESSION_ID");
        var who = $"kor ({Environment.UserName})";
        return string.IsNullOrWhiteSpace(session) ? who : $"{who} (claude session {session.Trim()})";
    }
}
