using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// One row of brain.Event (KorStandards 168 + 171). Column limits are the table's. <see cref="Session"/> is the Claude Code
/// session that did it (171, K4); <see cref="IssueId"/> ties a 'check' event to its issue (171, K5b). brain.Event is
/// append-only from kor: it is never UPDATEd (migration 173 refuses any UPDATE of it by brain_rw).
/// </summary>
public sealed record BrainEvent(string Module, string Kind, string? Repo, string? Commit, string? Ref, string? Outcome,
                                string? Detail, string Actor, string? Session = null, int? IssueId = null);

/// <summary>A row read back from brain.Event.</summary>
public sealed record StoredEvent(int Id, DateTimeOffset AtUtc, string Module, string Kind, string? Repo, string? Commit,
                                 string? Ref, string? Outcome, string? Detail, string Actor, string? Session = null,
                                 int? IssueId = null);

/// <summary>A row read back from brain.LaneState (162).</summary>
public sealed record StoredLaneState(int Id, string Lane, string ResumeBlock, string WrittenBy, DateTimeOffset WrittenAtUtc,
                                     string? Session = null);

/// <summary>
/// Writes and reads the brain.Event / brain.LaneState records as korbrain_app (brain_rw: SELECT/INSERT/UPDATE, DELETE
/// denied - 162). Every text value is fitted to its column and stripped of secret-shaped lines BEFORE it is sent: the table
/// refuses Password= / Pwd= / sk-ant- / AccountKey= (CK_Event_NoSecret, CK_LaneState_NoSecret), and a refused row in hook
/// mode would be a silently lost event.
/// </summary>
public static class BrainStore
{
    /// <summary>The end of a TEST resume's WrittenBy (<c>kor state --test</c>; kor_handover hand --test). brain.LaneState has no
    /// flag column, so the mark is in WrittenBy, and kor start never offers such a row as a resume (Framework row 2059,
    /// HANDOVER-3: test LaneState #28209 was the session-start RESUME until the next real kor state).</summary>
    public const string TestStateWrittenBy = " kor state TEST";

    /// <summary>What <c>kor state --test</c> prints; kor_handover refuses a test state whose output lacks it.</summary>
    public const string TestStateSaid = "TEST (never a session-start resume)";

    public static bool IsTestState(string? writtenBy) => writtenBy?.EndsWith(TestStateWrittenBy, StringComparison.Ordinal) == true;

    /// <summary>THE 16 kinds of 171's CK_Event_Kind (168's 12 + K4's 3 + K5b's check).</summary>
    public static readonly string[] Kinds =
    {
        "commit", "merge", "checkout", "push", "test", "build", "migration", "review", "board", "gate", "compact", "session-end",
        "session-start", "capture-ask", "record", "check",
    };

    private static readonly Regex Hex40 = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    /// <summary>A lowercase 40-hex sha, or null (CK_Event_Commit refuses anything else).</summary>
    public static string? Sha(string? value)
    {
        var v = value?.Trim();
        return v is not null && Hex40.IsMatch(v) ? v : null;
    }

    /// <summary>
    /// The Claude Code session running this process, from CLAUDE_CODE_SESSION_ID - the same id the hooks receive as
    /// session_id (checked 3 Nov: both d0313f99-...). A git hook run from a session's shell inherits it, so a commit is
    /// tied to the session that made it in ANY repo (closes K4's same-class miss). Null outside a session (a human).
    /// </summary>
    public static string? CurrentSession() =>
        Clip(Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID"), 64);

    public static BrainEvent Fit(BrainEvent e) => e with
    {
        Module = Clip(e.Module, 60) ?? "unknown",
        Kind = e.Kind,
        Repo = Clip(e.Repo, 80),
        Commit = Sha(e.Commit),
        // mistake 138: an event is a hook's record of what happened - it is never refused (a lost event), its credential
        // values are MASKED ([REDACTED len=N]) before the key-marker pass
        Ref = Clip(Redact.Text(SecretGuard.Mask(e.Ref)), 400),
        Outcome = Clip(e.Outcome, 20),
        Detail = Redact.Text(SecretGuard.MaskJsonOrText(e.Detail)),   // a JSON Detail is masked value by value
        Actor = Clip(Redact.Text(SecretGuard.Mask(e.Actor)), 100) ?? "unknown",
        Session = Clip(e.Session, 64),
    };

    /// <summary>
    /// Runs one command on the caller's unit of work (<see cref="BrainDb"/>), or on one of its own that it commits (the
    /// hooks: the old signatures wrap Open + Commit).
    /// </summary>
    private static T Exec<T>(BrainDb? db, string sql, Action<SqlCommand> bind, Func<SqlCommand, T> run) =>
        BrainDb.Use(db, d =>
        {
            using var c = d.Command(sql);
            bind(c);
            return run(c);
        });

    public static int Insert(BrainEvent e, BrainDb? db = null)
    {
        if (!Kinds.Contains(e.Kind)) throw new ArgumentException($"kind '{e.Kind}' is not one brain.Event accepts");
        var f = Fit(e);
        return Exec(db,
            // OUTPUT ... INTO: a plain OUTPUT is refused (error 334) once the table has a trigger - the same shape on
            // every brain table, so none breaks when a migration adds one (FreshnessTests.NoPlainOutputColumnListInTheKorSources).
            "DECLARE @id TABLE (Id int); " +
            "INSERT brain.Event (Module, Kind, Repo, [Commit], Ref, Outcome, Detail, Actor, Session, IssueId) OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@m, @k, @r, @c, @ref, @o, @d, @a, @s, @i); SELECT Id FROM @id;",
            cmd =>
            {
                cmd.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = f.Module;
                cmd.Parameters.Add("@k", System.Data.SqlDbType.NVarChar, 16).Value = f.Kind;
                cmd.Parameters.Add("@r", System.Data.SqlDbType.NVarChar, 80).Value = (object?)f.Repo ?? DBNull.Value;
                cmd.Parameters.Add("@c", System.Data.SqlDbType.Char, 40).Value = (object?)f.Commit ?? DBNull.Value;
                cmd.Parameters.Add("@ref", System.Data.SqlDbType.NVarChar, 400).Value = (object?)f.Ref ?? DBNull.Value;
                cmd.Parameters.Add("@o", System.Data.SqlDbType.NVarChar, 20).Value = (object?)f.Outcome ?? DBNull.Value;
                cmd.Parameters.Add("@d", System.Data.SqlDbType.NVarChar, -1).Value = (object?)f.Detail ?? DBNull.Value;
                cmd.Parameters.Add("@a", System.Data.SqlDbType.NVarChar, 100).Value = f.Actor;
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = (object?)f.Session ?? DBNull.Value;
                cmd.Parameters.Add("@i", System.Data.SqlDbType.Int).Value = (object?)f.IssueId ?? DBNull.Value;
            },
            cmd => Convert.ToInt32(cmd.ExecuteScalar()));
    }

    /// <summary>A resume record, refused whole, nothing written, when it holds a credential value (mistake 138;
    /// SecretGuard.Refuse) - checked before any connection is opened. capture-compact and kor state do NOT use this: they
    /// mask and store (<see cref="InsertLaneStateMasked"/>).</summary>
    public static int InsertLaneState(string lane, string resumeBlock, string writtenBy, BrainDb? db = null, string? session = null)
    {
        SecretGuard.Refuse(resumeBlock, "resume summary");
        SecretGuard.Refuse(writtenBy, "writer");
        return InsertLaneStateChecked(lane, resumeBlock, writtenBy, db, session);
    }

    /// <summary>
    /// A resume record that is never lost to a credential value: each value in the block (and the writer) is MASKED
    /// (<c>[REDACTED len=N]</c>, SecretGuard.Mask) and the rest stored - losing a resume costs more than a masked token. Used
    /// by <c>kor state</c> and capture-compact. Returns the row id and the hits masked in the block (positions in the
    /// ORIGINAL text; never the value), so the caller can say so in one line.
    /// </summary>
    public static (int Id, IReadOnlyList<SecretGuard.Hit> Masked) InsertLaneStateMasked(string lane, string resumeBlock, string writtenBy,
                                                                                       BrainDb? db = null, string? session = null)
    {
        var hits = SecretGuard.Find(resumeBlock);
        var id = InsertLaneStateChecked(lane, SecretGuard.Mask(resumeBlock) ?? "", SecretGuard.Mask(writtenBy) ?? "unknown", db, session);
        return (id, hits);
    }

    private static int InsertLaneStateChecked(string lane, string resumeBlock, string writtenBy, BrainDb? db, string? session) =>
        Exec(db,"DECLARE @id TABLE (Id int); " +
                 "INSERT brain.LaneState (Lane, ResumeBlock, WrittenBy, Session) OUTPUT INSERTED.Id INTO @id VALUES (@l, @b, @w, @s); " +
                 "SELECT Id FROM @id;",
            cmd =>
            {
                cmd.Parameters.Add("@l", System.Data.SqlDbType.NVarChar, 60).Value = Clip(lane, 60) ?? "unknown";
                cmd.Parameters.Add("@b", System.Data.SqlDbType.NVarChar, -1).Value = Redact.Text(resumeBlock) ?? "";
                cmd.Parameters.Add("@w", System.Data.SqlDbType.NVarChar, 100).Value = Clip(Redact.Text(writtenBy), 100) ?? "unknown";
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = (object?)Clip(session, 64) ?? DBNull.Value;
            },
            cmd => Convert.ToInt32(cmd.ExecuteScalar()));

    private const string EventColumns = "Id, AtUtc, Module, Kind, Repo, [Commit], Ref, Outcome, Detail, Actor, Session, IssueId";

    public static IReadOnlyList<StoredEvent> Last(string module, int count, string? kind = null, BrainDb? db = null) =>
        Exec(db,
            $"SELECT TOP (@n) {EventColumns} FROM brain.Event WHERE Module = @m AND (@k IS NULL OR Kind = @k) ORDER BY Id DESC",
            cmd =>
            {
                cmd.Parameters.Add("@n", System.Data.SqlDbType.Int).Value = Math.Clamp(count, 1, 1000);
                cmd.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = module;
                cmd.Parameters.Add("@k", System.Data.SqlDbType.NVarChar, 16).Value = (object?)kind ?? DBNull.Value;
            },
            ReadEvents);

    public static StoredEvent? EventById(int id, BrainDb? db = null) =>
        Exec(db, $"SELECT {EventColumns} FROM brain.Event WHERE Id = @id",
            cmd => cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = id,
            ReadEvents).SingleOrDefault();

    /// <summary>The newest event of this session with one of the kinds (any module), or null.</summary>
    public static StoredEvent? NewestOfSession(string session, string[] kinds, BrainDb? db = null) =>
        Exec(db,
            $"SELECT TOP (1) {EventColumns} FROM brain.Event WHERE Session = @s AND Kind IN (SELECT value FROM OPENJSON(@k)) ORDER BY Id DESC",
            cmd =>
            {
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = session;
                cmd.Parameters.Add("@k", System.Data.SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(kinds);
            },
            ReadEvents).SingleOrDefault();

    /// <summary>
    /// K4: the commit events after <paramref name="afterId"/> that this session must be asked about - made by this
    /// session in ANY module (Session = it), or made in its module with no session at all (a human at a terminal). A
    /// commit by ANOTHER Claude session is that session's to record.
    /// </summary>
    public static IReadOnlyList<StoredEvent> CommitsToAsk(string session, string module, int afterId, BrainDb? db = null) =>
        Exec(db,
            $"SELECT TOP (50) {EventColumns} FROM brain.Event WHERE Kind = N'commit' AND Id > @after " +
            "AND (Session = @s OR (Module = @m AND Session IS NULL)) ORDER BY Id",
            cmd =>
            {
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = session;
                cmd.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = module;
                cmd.Parameters.Add("@after", System.Data.SqlDbType.Int).Value = afterId;
            },
            ReadEvents);

    /// <summary>Row 1393: this session's events of these kinds written at or after <paramref name="sinceUtc"/> (the turn's
    /// start, read off the transcript), oldest first, at most 50.</summary>
    public static IReadOnlyList<StoredEvent> SessionEventsSince(string session, string[] kinds, DateTimeOffset sinceUtc, BrainDb? db = null) =>
        Exec(db,
            $"SELECT TOP (50) {EventColumns} FROM brain.Event WHERE Session = @s AND Kind IN (SELECT value FROM OPENJSON(@k)) " +
            "AND AtUtc >= @since ORDER BY Id",
            cmd =>
            {
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = session;
                cmd.Parameters.Add("@k", System.Data.SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(kinds);
                cmd.Parameters.Add("@since", System.Data.SqlDbType.DateTimeOffset).Value = sinceUtc;
            },
            ReadEvents);

    /// <summary>Row 1393: has this session already recorded the background task <paramref name="taskId"/>'s run (its
    /// Detail carries "task":"&lt;id&gt;")?</summary>
    public static bool BackgroundRunRecorded(string session, string taskId, BrainDb? db = null) =>
        Exec(db,
            "SELECT COUNT(*) FROM brain.Event WHERE Session = @s AND Kind IN (N'test', N'build', N'migration', N'gate') AND Detail LIKE @t",
            cmd =>
            {
                cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 64).Value = session;
                // the id is [A-Za-z0-9]: no LIKE wildcard can come from it (checked by the caller)
                cmd.Parameters.Add("@t", System.Data.SqlDbType.NVarChar, 200).Value = "%\"task\":\"" + taskId + "\"%";
            },
            cmd => Convert.ToInt32(cmd.ExecuteScalar()) > 0);

    /// <summary>Check events of an issue, newest first.</summary>
    public static IReadOnlyList<StoredEvent> ChecksOf(int issueId, BrainDb? db = null) =>
        Exec(db, $"SELECT TOP (100) {EventColumns} FROM brain.Event WHERE Kind = N'check' AND IssueId = @i ORDER BY Id DESC",
            cmd => cmd.Parameters.Add("@i", System.Data.SqlDbType.Int).Value = issueId,
            ReadEvents);

    private static IReadOnlyList<StoredEvent> ReadEvents(SqlCommand cmd)
    {
        var rows = new List<StoredEvent>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new StoredEvent(r.GetInt32(0), r.GetDateTimeOffset(1), r.GetString(2), r.GetString(3), Str(r, 4),
                                     Str(r, 5), Str(r, 6), Str(r, 7), Str(r, 8), r.GetString(9), Str(r, 10),
                                     r.IsDBNull(11) ? null : r.GetInt32(11)));
        return rows;
    }

    public static StoredLaneState? LaneStateById(int id, BrainDb? db = null) =>
        Exec(db, "SELECT Id, Lane, ResumeBlock, WrittenBy, WrittenAtUtc, Session FROM brain.LaneState WHERE Id = @id",
            cmd => cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = id,
            cmd =>
            {
                using var r = cmd.ExecuteReader();
                return r.Read() ? new StoredLaneState(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDateTimeOffset(4), Str(r, 5)) : null;
            });

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static string? Clip(string? s, int max)
    {
        if (s is null) return null;
        s = s.Trim();
        if (s.Length == 0) return null;
        return s.Length <= max ? s : s[..(max - 3)] + "...";
    }
}

/// <summary>
/// Removes every line carrying a marker the brain tables refuse (CK_Event_NoSecret, CK_LaneState_NoSecret - LIKE under a
/// case-insensitive collation, so matched case-insensitively here). The line is replaced by a notice, never kept.
/// </summary>
public static class Redact
{
    private static readonly Regex Marker = new(@"password=|pwd=|sk-ant-|accountkey=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasSecretMarker(string? s) => s is not null && Marker.IsMatch(s);

    public static string? Text(string? s)
    {
        if (s is null || !Marker.IsMatch(s)) return s;
        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (Marker.IsMatch(lines[i])) lines[i] = "[line removed by kor: secret marker]";
        return string.Join('\n', lines);
    }
}
