using System.Data;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework.Start;

/// <summary>A live record as the start reads it (brain.vw_ModuleState / vw_OpenIssues). Body is read only for the
/// records rendered whole: <c>_five-questions</c> and a lane's <c>_index</c>.</summary>
public sealed record StartRecord(int Id, string Lane, string Name, string Kind, string Description, string? Body, string Status,
                                 string? SourceKind, string? SourceRepo, string? SourceCommit, string? SourcePath, string? SourceRef,
                                 string? CheckKind, string? CheckRef, int? RegressionOf)
{
    /// <summary>Imported before 168 (no source): file memory moved into the DB. Rendered last, cut first.</summary>
    public bool Legacy => SourceKind is null;

    /// <summary>"Repo:path@commit7" for a git source, "kind: ref" otherwise.</summary>
    public string Where => SourceKind == "git"
        ? $"{SourceRepo}:{SourcePath}@{(SourceCommit is { Length: >= 7 } c ? c[..7] : SourceCommit)}"
        : SourceKind is null ? "imported, no source" : $"{SourceKind}: {SourceRef}";
}

/// <summary>A brain.LaneState row offered as a resume. <see cref="Session"/> is the column, or - for a compaction written
/// before 171 filled it - the id in "compaction &lt;id&gt;". <see cref="DidWork"/>: that session has a commit, record or push
/// event in this module, or the row is a <c>state</c> write (the lead's state writes count as work).</summary>
public sealed record ResumeRow(int Id, string ResumeBlock, string WrittenBy, DateTimeOffset WrittenAtUtc, string? Session,
                               bool IsCompaction, bool SessionDidWork)
{
    public bool Eligible => !IsCompaction || SessionDidWork;
}

/// <summary>THE resume and why, plus the other recent ones (listed one line each, never shown as the resume).</summary>
public sealed record ResumeChoice(ResumeRow? Chosen, string Why, IReadOnlyList<ResumeRow> Others);

/// <summary>Everything the record part renders, read in one korbrain_app unit of work.</summary>
public sealed record ModuleState(string Module, DateTimeOffset ReadAt, FreshnessReport Fresh, IReadOnlyList<StartRecord> Records,
                                 IReadOnlyList<StartRecord> OpenIssues, IReadOnlyList<StartRecord> Office, ResumeChoice Resume,
                                 StoredEvent? LastSessionEnd, IReadOnlyList<StoredEvent> Events, IReadOnlyList<string> CheckLines)
{
    public const string FiveQuestionsName = "_five-questions";
    public const string IndexName = "_index";
    public const string OfficeLane = "Office";

    /// <summary>The session-start hook's view: which session is starting, and how (startup|resume|clear|compact|fork).</summary>
    public sealed record Start(string? SessionId, string? Source);

    /// <summary>
    /// K6 - known at start. ONE korbrain_app unit of work (the caller's): the module's open query-kind issues are checked
    /// first (K5b, at most <paramref name="checkCap"/> in all; each query runs in its own rolled-back connection BEFORE this
    /// unit of work writes anything, so none can wait on it), then K5 freshness (one read, git, one idempotent write), then
    /// ONE batch of reads: live records, open issues (vw_OpenIssues), the Office lane, the resume candidates, the last
    /// session-end and the last 10 events. The caller commits (kor.exe) or never does (a test).
    /// </summary>
    public static ModuleState Load(BrainDb db, string module, Start start, string? fromDir = null, TimeSpan? freshnessCap = null,
                                   TimeSpan? checkCap = null, Freshness.HeadReader? reader = null)
    {
        StartTrace.Mark("connected");
        var checkLines = RunQueryChecks(db, module, fromDir, checkCap ?? TimeSpan.FromSeconds(2), start.SessionId);
        StartTrace.Mark("query checks");
        var fresh = Freshness.CheckModule(db, module, fromDir, reader, freshnessCap);
        StartTrace.Mark("freshness");
        using var c = db.Command(Batch, 30);
        c.Parameters.Add("@m", SqlDbType.NVarChar, 60).Value = module;
        c.Parameters.Add("@office", SqlDbType.NVarChar, 60).Value = OfficeLane;
        c.Parameters.Add("@me", SqlDbType.NVarChar, 64).Value = (object?)start.SessionId ?? DBNull.Value;
        using var r = c.ExecuteReader();
        var records = ReadRecords(r, issues: false);
        r.NextResult();
        var open = ReadRecords(r, issues: true);
        r.NextResult();
        var office = ReadRecords(r, issues: false);
        r.NextResult();
        var resumes = new List<ResumeRow>();
        while (r.Read())
            resumes.Add(new ResumeRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDateTimeOffset(3), S(r, 4),
                                      r.GetInt32(5) == 1, r.GetInt32(6) == 1));
        r.NextResult();
        var events = ReadEvents(r);
        r.NextResult();
        var end = ReadEvents(r).FirstOrDefault();
        StartTrace.Mark("batch read");
        return new ModuleState(module, DateTimeOffset.Now, fresh, records, open, office,
                               ChooseResume(resumes, start), end, events, checkLines);
    }

    /// <summary>
    /// WHICH RESUME (spec K6, found live 2 Nov 23:14): chosen by SESSION, never newest-in-lane. Source compact or resume
    /// -> the newest row written by THIS session (its compaction summary or a state it wrote). Otherwise (startup, clear,
    /// fork, or no row of its own) -> the newest row from a session that did work in this module. Rows newest first.
    /// </summary>
    public static ResumeChoice ChooseResume(IReadOnlyList<ResumeRow> rows, Start start)
    {
        var ordered = rows.GroupBy(x => x.Id).Select(g => g.First())
                          .OrderByDescending(x => x.WrittenAtUtc).ThenByDescending(x => x.Id).ToList();
        ResumeRow? chosen = null;
        string why;
        var own = start.SessionId is { Length: > 0 } me ? ordered.FirstOrDefault(x => x.Session == me) : null;
        if (start.Source is "compact" or "resume" && own is not null)
        {
            chosen = own;
            why = $"this session's own ({(own.IsCompaction ? "its compaction summary" : "a state it wrote")}; source {start.Source})";
        }
        else
        {
            chosen = ordered.FirstOrDefault(x => x.Eligible);
            var because = start.Source is "compact" or "resume"
                ? $"source {start.Source}, but this session wrote none - so"
                : $"source {start.Source ?? "not given"}:";
            why = chosen is null ? "no resume from a session that did work in this module"
                : $"{because} the newest from a session that did work in this module ({(chosen.IsCompaction ? "a compaction of a session with commit/record/push events here" : "a state write")})";
        }
        var others = ordered.Where(x => chosen is null || x.Id != chosen.Id).Take(5).ToList();
        return new ResumeChoice(chosen, why, others);
    }

    private const string RecordColumns =
        "Id, Module, Name, Kind, Description, CASE WHEN Name IN (N'_five-questions', N'_index') THEN Body END AS Body, Status, " +
        "SourceKind, SourceRepo, SourceCommit, SourcePath, SourceRef, CheckKind, CheckRef, RegressionOf";

    private const string EventColumns = "Id, AtUtc, Module, Kind, Repo, [Commit], Ref, Outcome, Detail, Actor, Session, IssueId";

    /// <summary>One round trip, six result sets (see <see cref="Load"/>).</summary>
    internal const string Batch =
        // 1. live records of the lane (not issues: those come from vw_OpenIssues), as kor_brain lists them: Kind, Name
        $"SELECT {RecordColumns} FROM brain.vw_ModuleState WHERE Module = @m AND Kind <> N'issue' ORDER BY Kind, Name; " +
        // 2. open issues
        "SELECT Id, Module, Name, N'issue' AS Kind, Description, CAST(NULL AS nvarchar(max)) AS Body, Status, SourceKind, SourceRepo, " +
        "SourceCommit, SourcePath, SourceRef, CheckKind, CheckRef, RegressionOf FROM brain.vw_OpenIssues WHERE Module = @m ORDER BY Id; " +
        // 3. the Office lane (every KOR lead gets it; never onto itself)
        $"SELECT {RecordColumns} FROM brain.vw_ModuleState WHERE Module = @office AND @m <> @office AND Kind <> N'issue' ORDER BY Kind, Name; " +
        // 4. resume candidates: the 12 newest of the lane, and this session's newest, each with its session and whether
        //    that session did work here (a commit, record or push event under it in this module)
        "WITH c AS (SELECT a.Id FROM (SELECT TOP (12) Id FROM brain.LaneState WHERE Lane = @m ORDER BY WrittenAtUtc DESC, Id DESC) a " +
        "           UNION SELECT b.Id FROM (SELECT TOP (1) Id FROM brain.LaneState WHERE Lane = @m AND @me IS NOT NULL " +
        "                 AND (Session = @me OR WrittenBy = N'compaction ' + @me) ORDER BY WrittenAtUtc DESC, Id DESC) b) " +
        "SELECT s.Id, s.ResumeBlock, s.WrittenBy, s.WrittenAtUtc, x.Sess, x.IsCompaction, " +
        "       CASE WHEN x.Sess IS NOT NULL AND EXISTS (SELECT 1 FROM brain.Event e WHERE e.Session = x.Sess AND e.Module = @m " +
        "            AND e.Kind IN (N'commit', N'record', N'push')) THEN 1 ELSE 0 END AS DidWork " +
        "FROM brain.LaneState s JOIN c ON c.Id = s.Id " +
        "CROSS APPLY (SELECT CASE WHEN s.WrittenBy LIKE N'compaction %' THEN 1 ELSE 0 END AS IsCompaction, " +
        "             COALESCE(s.Session, CASE WHEN s.WrittenBy LIKE N'compaction %' THEN LTRIM(RTRIM(SUBSTRING(s.WrittenBy, 12, 64))) END) AS Sess) x " +
        "ORDER BY s.WrittenAtUtc DESC, s.Id DESC; " +
        // 5. the last 10 events of the module
        $"SELECT TOP (10) {EventColumns} FROM brain.Event WHERE Module = @m ORDER BY Id DESC; " +
        // 6. the last session-end
        $"SELECT TOP (1) {EventColumns} FROM brain.Event WHERE Module = @m AND Kind = N'session-end' ORDER BY Id DESC;";

    private static List<StartRecord> ReadRecords(SqlDataReader r, bool issues)
    {
        var list = new List<StartRecord>();
        while (r.Read())
            list.Add(new StartRecord(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), S(r, 5), r.GetString(6),
                                     S(r, 7), S(r, 8), S(r, 9)?.Trim(), S(r, 10), S(r, 11), S(r, 12), S(r, 13),
                                     r.IsDBNull(14) ? null : r.GetInt32(14)));
        return list;
    }

    private static List<StoredEvent> ReadEvents(SqlDataReader r)
    {
        var list = new List<StoredEvent>();
        while (r.Read())
            list.Add(new StoredEvent(r.GetInt32(0), r.GetDateTimeOffset(1), r.GetString(2), r.GetString(3), S(r, 4), S(r, 5)?.Trim(),
                                     S(r, 6), S(r, 7), S(r, 8), r.GetString(9), S(r, 10), r.IsDBNull(11) ? null : r.GetInt32(11)));
        return list;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>
    /// K5b at start: the module's OPEN query-kind issues, each run (its own rolled-back connection - all of them before
    /// this unit of work writes, so none can wait on our locks), then each result recorded here (a pass resolves). Past
    /// the cap the rest are named, not run.
    /// </summary>
    private static List<string> RunQueryChecks(BrainDb db, string module, string? fromDir, TimeSpan cap, string? session)
    {
        var issues = RecordStore.IssuesToCheck(db, module, resolved: false, new[] { "query" });
        var lines = new List<string>();
        if (issues.Count == 0) return lines;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<(StoredRecord Issue, CheckResult Result)>();
        foreach (var i in issues)
        {
            var left = cap - clock.Elapsed;
            if (left <= TimeSpan.Zero) { lines.Add($"#{i.Id} {i.Name}: not checked - the {Freshness.Secs(cap)} s start cap was spent"); continue; }
            results.Add((i, IssueCheck.Run(i, left < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left, fromDir)));
        }
        var actor = RecordStore.Actor() + " (kor start)";
        foreach (var (issue, result) in results)
            lines.Add(IssueResolver.CheckAndRecord(db, issue, result, BrainStore.Clip(actor, 100)!, session).Line);
        return lines;
    }
}
