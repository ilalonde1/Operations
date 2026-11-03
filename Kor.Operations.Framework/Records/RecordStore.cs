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
/// A record to write (brain.Memory, migrations 162 + 168). The source is either <see cref="Git"/> or a non-git
/// <see cref="SourceKind"/> with <see cref="SourceRef"/> (CK_Memory_OtherSource).
/// TODO(migration 171, not applied on 2 Nov): CheckKind, CheckRef, ResolvesId, RegressionOf, EvidenceEventId and
/// RestatesId join this record when 171 has run - they are NOT faked here, so issues and resolutions are refused until then.
/// </summary>
public sealed record NewRecord(string Lane, string Name, string Kind, string Description, string Body, string CreatedBy,
                               GitSource? Git = null, string? SourceKind = null, string? SourceRef = null,
                               string? SourceHash = null, string? Source = null);

/// <summary>A brain.Memory row as K5 reads it.</summary>
public sealed record StoredRecord(int Id, string Lane, string Name, string Kind, string Description, string Body,
                                  string Status, string? SourceKind, string? SourceRepo, string? SourceCommit,
                                  string? SourcePath, string? SourceBlob, string? SourceRef, string? SourceHash,
                                  DateTimeOffset? RetiredAtUtc, int? SupersededBy, DateTimeOffset? CheckedAtUtc)
{
    public bool Live => RetiredAtUtc is null && Status != "superseded";
}

/// <summary>A write RecordStore refuses before anything is sent.</summary>
public sealed class RecordRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// THE record writer (KNOWLEDGE-IDE-SPEC-K5-K6.md, shared plumbing; K4's <c>kor record</c> and K5's <c>kor recheck</c>
/// go through it). Every write runs in the caller's <see cref="BrainDb"/> - the caller commits, or (a test) never does.
/// Covers on the CURRENT schema (168): Insert with a git source verified against git; AtHead / At; Supersede and
/// Withdraw for non-issues. Does NOT cover until migration 171: issues and resolutions (they need CheckKind/CheckRef and
/// RestatesId - an issue is restated by ONE insert naming the old one, the trigger TR_Memory_Restate supersedes it; the
/// app lock 'brain.issue:&lt;Lane&gt;:&lt;Name&gt;' around it). Same-class miss: a writer that bypasses RecordStore
/// (kor_brain.py note) is not verified by it - only by the table's CHECKs.
/// </summary>
public static class RecordStore
{
    public static readonly string[] WritableKinds = { "finding", "decision", "dead-end", "lesson", "user", "feedback", "project", "reference" };

    /// <summary>Kinds that need migration 171's columns. TODO(171): route them through the issue path.</summary>
    public static readonly string[] KindsAwaiting171 = { "issue", "resolution" };

    private const string Columns =
        "Id, Lane, Name, Kind, Description, Body, Status, SourceKind, SourceRepo, SourceCommit, SourcePath, SourceBlob, " +
        "SourceRef, SourceHash, RetiredAtUtc, SupersededBy, CheckedAtUtc";

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
        var blob = Lower(Git.Run(top, "rev-parse", "--verify", "--quiet", $"{commit}:{rel}")?.Trim())
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
        var blob = Lower(Git.Run(top, "rev-parse", "--verify", "--quiet", $"{g.Commit}:{g.Path}")?.Trim());
        if (blob is null) throw new RecordRefusedException($"{g.Path} is not tracked at {g.Commit[..10]} in {g.Repo}");
        if (blob != g.Blob)
            throw new RecordRefusedException($"blob {g.Blob} is not git rev-parse {g.Commit[..10]}:{g.Path} ({blob}) in {g.Repo}");
    }

    // ---- writes -----------------------------------------------------------------------------------------------------
    public static int Insert(BrainDb db, NewRecord r)
    {
        Validate(r);
        using var c = db.Command(
            // OUTPUT ... INTO a table variable: a plain OUTPUT is refused (error 334) on a table with an enabled trigger,
            // and migration 171 adds triggers to brain.Memory (TR_Memory_Restate, TR_Memory_EvidenceAppendOnly).
            "DECLARE @id TABLE (Id int); " +
            "INSERT brain.Memory (Lane, Name, Kind, Description, Body, Source, CreatedBy, SourceKind, SourceRepo, SourceCommit, " +
            "SourcePath, SourceBlob, SourceRef, SourceHash) OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@l, @n, @k, @d, @b, @s, @cb, @sk, @sr, @sc, @sp, @sb, @sref, @sh); SELECT Id FROM @id;");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = r.Lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = r.Name;
        c.Parameters.Add("@k", SqlDbType.NVarChar, 10).Value = r.Kind;
        c.Parameters.Add("@d", SqlDbType.NVarChar, 400).Value = r.Description;
        c.Parameters.Add("@b", SqlDbType.NVarChar, -1).Value = r.Body;
        c.Parameters.Add("@s", SqlDbType.NVarChar, 400).Value = (object?)BrainStore.Clip(r.Source, 400) ?? DBNull.Value;
        c.Parameters.Add("@cb", SqlDbType.NVarChar, 100).Value = BrainStore.Clip(r.CreatedBy, 100) ?? "unknown";
        c.Parameters.Add("@sk", SqlDbType.NVarChar, 10).Value = r.Git is not null ? "git" : r.SourceKind!;
        c.Parameters.Add("@sr", SqlDbType.NVarChar, 80).Value = (object?)r.Git?.Repo ?? DBNull.Value;
        c.Parameters.Add("@sc", SqlDbType.Char, 40).Value = (object?)r.Git?.Commit ?? DBNull.Value;
        c.Parameters.Add("@sp", SqlDbType.NVarChar, 400).Value = (object?)r.Git?.Path ?? DBNull.Value;
        c.Parameters.Add("@sb", SqlDbType.Char, 40).Value = (object?)r.Git?.Blob ?? DBNull.Value;
        c.Parameters.Add("@sref", SqlDbType.NVarChar, 400).Value = (object?)r.SourceRef ?? DBNull.Value;
        c.Parameters.Add("@sh", SqlDbType.NVarChar, 64).Value = (object?)r.SourceHash ?? DBNull.Value;
        return Convert.ToInt32(c.ExecuteScalar());
    }

    private static void Validate(NewRecord r)
    {
        if (KindsAwaiting171.Contains(r.Kind))
            throw new RecordRefusedException($"kind '{r.Kind}' needs migration 171 (CheckKind/CheckRef, RestatesId) - it is written by `kor issue` once 171 has run");
        if (!WritableKinds.Contains(r.Kind)) throw new RecordRefusedException($"kind '{r.Kind}' is not a brain.Memory kind");
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
        }
    }

    private static void Need(string? value, int max, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new RecordRefusedException($"the {what} is empty");
        if (value.Length > max) throw new RecordRefusedException($"the {what} is longer than {max} characters");
    }

    /// <summary>
    /// Non-issue supersede, in the caller's transaction: the old record is retired (who, why) with Status superseded,
    /// the replacement is inserted (the live-name index allows the same name once the old one is retired), and the old
    /// one points at it. Returns the new Id. TODO(171): an issue is restated by ONE insert with RestatesId = old.
    /// </summary>
    public static int Supersede(BrainDb db, int oldId, NewRecord replacement, string by, string because)
    {
        var old = Get(db, oldId) ?? throw new RecordRefusedException($"no record #{oldId}");
        if (!old.Live) throw new RecordRefusedException($"#{oldId} is not live (status {old.Status}{(old.RetiredAtUtc is null ? "" : ", retired")}) - nothing to supersede");
        if (KindsAwaiting171.Contains(old.Kind))
            throw new RecordRefusedException($"#{oldId} is an {old.Kind}: it is restated or closed only through the issue path, which needs migration 171");
        if (replacement.Lane != old.Lane) throw new RecordRefusedException($"the replacement is in lane {replacement.Lane}, #{oldId} in {old.Lane}");
        Validate(replacement);   // before anything changes
        Need(because, 400, "reason (--because)");
        Retire(db, oldId, by, because, superseded: true);
        var newId = Insert(db, replacement);
        using var c = db.Command("UPDATE brain.Memory SET SupersededBy = @n, UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @o");
        c.Parameters.Add("@n", SqlDbType.Int).Value = newId;
        c.Parameters.Add("@o", SqlDbType.Int).Value = oldId;
        c.ExecuteNonQuery();
        return newId;
    }

    /// <summary>Retires a non-issue with no replacement (it was wrong, or no longer applies).</summary>
    public static void Withdraw(BrainDb db, int id, string by, string because)
    {
        var old = Get(db, id) ?? throw new RecordRefusedException($"no record #{id}");
        if (old.RetiredAtUtc is not null) throw new RecordRefusedException($"#{id} is already retired");
        if (KindsAwaiting171.Contains(old.Kind))
            throw new RecordRefusedException($"#{id} is an {old.Kind}: an issue is closed only by evidence, never withdrawn");
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

    // ---- reads ------------------------------------------------------------------------------------------------------
    public static StoredRecord? Get(BrainDb db, int id)
    {
        using var c = db.Command($"SELECT {Columns} FROM brain.Memory WHERE Id = @id");
        c.Parameters.Add("@id", SqlDbType.Int).Value = id;
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>The live (not retired) record with this name in the lane, if any (UX_Memory_LiveName allows one).</summary>
    public static StoredRecord? LiveByName(BrainDb db, string lane, string name)
    {
        using var c = db.Command($"SELECT {Columns} FROM brain.Memory WHERE Lane = @l AND Name = @n AND RetiredAtUtc IS NULL");
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = lane;
        c.Parameters.Add("@n", SqlDbType.NVarChar, 120).Value = name;
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    internal static StoredRecord Read(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        S(r, 7), S(r, 8), S(r, 9)?.Trim(), S(r, 10), S(r, 11)?.Trim(), S(r, 12), S(r, 13),
        r.IsDBNull(14) ? null : r.GetDateTimeOffset(14), r.IsDBNull(15) ? null : r.GetInt32(15),
        r.IsDBNull(16) ? null : r.GetDateTimeOffset(16));

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Who is writing: the Windows user, plus the Claude session when one is running kor.</summary>
    public static string Actor()
    {
        var session = Environment.GetEnvironmentVariable("CLAUDE_CODE_BRIDGE_SESSION_ID");
        var who = $"kor ({Environment.UserName})";
        return string.IsNullOrWhiteSpace(session) ? who : $"{who} (claude session {session.Trim()})";
    }
}
