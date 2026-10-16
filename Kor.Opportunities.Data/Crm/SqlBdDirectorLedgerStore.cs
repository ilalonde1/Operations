#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>The three Director ledger tables. Original text is retained; only
/// retirement, ask closure and decision scoring have mutation paths.</summary>
public sealed class SqlBdDirectorLedgerStore : IBdDirectorLedgerStore
{
    private readonly string _connectionString;
    public SqlBdDirectorLedgerStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    private const string TargetColumns = "Id, Metric, Scope, ScopeKey, PeriodStartUtc, PeriodEndUtc, TargetValue, Note, SetBy, SetAtUtc";
    private const string AskColumns = "Id, ToStaffKey, EngagementId, CanonicalOrgId, Ask, WhyNow, Minutes, DueUtc, AskedBy, AskedAtUtc, Status, ClosedAtUtc, ClosedBy, ClosedNote";
    private const string DecisionColumns = "Id, Kind, EngagementId, CanonicalOrgId, Decision, Rationale, ExpectedOutcome, ReviewAtUtc, DecidedBy, DecidedAtUtc, Outcome, OutcomeNote, ScoredAtUtc, ScoredBy";

    public Task<IReadOnlyList<BdTarget>> ListTargetsAsync(CancellationToken ct) => ReadAsync(
        "SELECT " + TargetColumns + " FROM opportunities.BdTarget WHERE RetiredAtUtc IS NULL ORDER BY PeriodStartUtc DESC, Id DESC;",
        _ => { }, r => new BdTarget(r.GetInt64(0), r.GetString(1), r.GetString(2), Text(r, 3), r.GetDateTimeOffset(4),
            r.GetDateTimeOffset(5), r.GetDecimal(6), Text(r, 7), r.GetString(8), r.GetDateTimeOffset(9)), ct);

    public Task<long> SetTargetAsync(BdTarget target, CancellationToken ct)
    {
        if (!BdTargetMetrics.All.Contains(target.Metric) || target.PeriodEndUtc <= target.PeriodStartUtc
            || target.TargetValue < 0 || !new[] { "Firm", "Region", "Person" }.Contains(target.Scope)
            || (target.Scope != "Firm" && string.IsNullOrWhiteSpace(target.ScopeKey)))
            throw new ArgumentException("The target is not valid.", nameof(target));
        return InsertAsync(@"INSERT INTO opportunities.BdTarget
(Metric, Scope, ScopeKey, PeriodStartUtc, PeriodEndUtc, TargetValue, Note, SetBy)
OUTPUT INSERTED.Id VALUES (@metric, @scope, @key, @start, @end, @value, @note, @by);", cmd =>
        {
            AddText(cmd, "@metric", 40, target.Metric); AddText(cmd, "@scope", 20, target.Scope);
            AddText(cmd, "@key", 150, target.Scope == "Firm" ? null : target.ScopeKey);
            cmd.Parameters.Add("@start", SqlDbType.DateTimeOffset).Value = target.PeriodStartUtc;
            cmd.Parameters.Add("@end", SqlDbType.DateTimeOffset).Value = target.PeriodEndUtc;
            var value = cmd.Parameters.Add("@value", SqlDbType.Decimal);
            value.Precision = 18; value.Scale = 2; value.Value = target.TargetValue;
            AddText(cmd, "@note", 500, target.Note); AddText(cmd, "@by", 150, target.SetBy);
        }, ct);
    }

    public Task RetireTargetAsync(long id, string by, CancellationToken ct) => ExecuteAsync(@"
UPDATE opportunities.BdTarget SET RetiredAtUtc = sysdatetimeoffset(), RetiredBy = @by
WHERE Id = @id AND RetiredAtUtc IS NULL;", cmd => Identity(cmd, id, by), ct);

    public Task<IReadOnlyList<BdAsk>> ListAsksAsync(string? status, CancellationToken ct) => ReadAsync(
        "SELECT " + AskColumns + " FROM opportunities.BdAsk WHERE (@status IS NULL OR Status = @status) ORDER BY DueUtc, Id;",
        cmd => AddText(cmd, "@status", 20, status), ReadAsk, ct);

    public Task<IReadOnlyList<BdAsk>> ListOpenAsksForAsync(string toStaffKey, CancellationToken ct) => ReadAsync(
        "SELECT " + AskColumns + " FROM opportunities.BdAsk WHERE Status = N'Open' AND ToStaffKey = @key ORDER BY DueUtc, Id;",
        cmd => AddText(cmd, "@key", 150, toStaffKey), ReadAsk, ct);

    public Task<long> CreateAskAsync(BdAsk ask, CancellationToken ct)
    {
        if (ask.Minutes is < 5 or > 180 || string.IsNullOrWhiteSpace(ask.Ask) || string.IsNullOrWhiteSpace(ask.ToStaffKey))
            throw new ArgumentException("The ask is not valid.", nameof(ask));
        return InsertAsync(@"INSERT INTO opportunities.BdAsk
(ToStaffKey, EngagementId, CanonicalOrgId, Ask, WhyNow, Minutes, DueUtc, AskedBy)
OUTPUT INSERTED.Id VALUES (@to, @pursuit, @account, @ask, @why, @minutes, @due, @by);", cmd =>
        {
            AddText(cmd, "@to", 150, ask.ToStaffKey); Context(cmd, ask.EngagementId, ask.CanonicalOrgId);
            AddText(cmd, "@ask", 500, ask.Ask); AddText(cmd, "@why", 1000, ask.WhyNow);
            cmd.Parameters.Add("@minutes", SqlDbType.Int).Value = ask.Minutes;
            cmd.Parameters.Add("@due", SqlDbType.DateTimeOffset).Value = ask.DueUtc;
            AddText(cmd, "@by", 150, ask.AskedBy);
        }, ct);
    }

    public Task CloseAskAsync(long id, string status, string? note, string by, CancellationToken ct)
    {
        if (!new[] { "Done", "Declined", "Withdrawn" }.Contains(status)) throw new ArgumentException("Choose a closing status.", nameof(status));
        return ExecuteAsync(@"UPDATE opportunities.BdAsk
SET Status = @status, ClosedAtUtc = sysdatetimeoffset(), ClosedBy = @by, ClosedNote = @note
WHERE Id = @id AND Status = N'Open';", cmd =>
        {
            Identity(cmd, id, by); AddText(cmd, "@status", 20, status); AddText(cmd, "@note", 500, note);
        }, ct);
    }

    public Task<IReadOnlyList<BdDecision>> ListDecisionsAsync(CancellationToken ct) => ReadAsync(
        "SELECT " + DecisionColumns + " FROM opportunities.BdDecision ORDER BY DecidedAtUtc DESC, Id DESC;",
        _ => { }, r => new BdDecision(r.GetInt64(0), r.GetString(1), Id(r, 2), Id(r, 3), r.GetString(4),
            r.GetString(5), r.GetString(6), r.GetDateTimeOffset(7), r.GetString(8), r.GetDateTimeOffset(9),
            r.GetString(10), Text(r, 11), Date(r, 12), Text(r, 13)), ct);

    public Task<long> RecordDecisionAsync(BdDecision decision, CancellationToken ct)
    {
        if (!new[] { "Go", "NoGo", "Drop", "Reassign", "Price", "Target", "Other" }.Contains(decision.Kind)
            || string.IsNullOrWhiteSpace(decision.Decision) || string.IsNullOrWhiteSpace(decision.Rationale)
            || string.IsNullOrWhiteSpace(decision.ExpectedOutcome)) throw new ArgumentException("The decision is not valid.", nameof(decision));
        return InsertAsync(@"INSERT INTO opportunities.BdDecision
(Kind, EngagementId, CanonicalOrgId, Decision, Rationale, ExpectedOutcome, ReviewAtUtc, DecidedBy)
OUTPUT INSERTED.Id VALUES (@kind, @pursuit, @account, @decision, @rationale, @expected, @review, @by);", cmd =>
        {
            AddText(cmd, "@kind", 20, decision.Kind); Context(cmd, decision.EngagementId, decision.CanonicalOrgId);
            AddText(cmd, "@decision", 500, decision.Decision); AddText(cmd, "@rationale", 2000, decision.Rationale);
            AddText(cmd, "@expected", 500, decision.ExpectedOutcome);
            cmd.Parameters.Add("@review", SqlDbType.DateTimeOffset).Value = decision.ReviewAtUtc;
            AddText(cmd, "@by", 150, decision.DecidedBy);
        }, ct);
    }

    public async Task<BriefTrackRecord> GetBriefTrackRecordAsync(CancellationToken ct = default)
    {
        var rows = await ReadAsync("""
            WITH Calls AS (
                SELECT d.Id, d.Outcome, d.ReviewAtUtc, b.Section, b.Citations
                FROM opportunities.BdDecision d JOIN opportunities.DirectorBrief b ON b.Id = d.BriefRowId
                WHERE d.Kind = N'Brief' AND d.BriefRowId IS NOT NULL
            ), Entries AS (
                SELECT N'Total' AS Category, N'' AS Label, Id, Outcome, ReviewAtUtc FROM Calls
                UNION ALL
                SELECT N'Section', Section, Id, Outcome, ReviewAtUtc FROM Calls
                UNION ALL
                SELECT DISTINCT N'Book', c.Source, d.Id, d.Outcome, d.ReviewAtUtc
                FROM Calls d CROSS APPLY OPENJSON(d.Citations) WITH (Source NVARCHAR(512) '$.source') c
                WHERE c.Source IS NOT NULL
            )
            SELECT Category, Label,
                SUM(CASE WHEN Outcome <> N'Pending' THEN 1 ELSE 0 END) AS Scored,
                SUM(CASE WHEN Outcome = N'Right' THEN 1 ELSE 0 END) AS [Right],
                SUM(CASE WHEN Outcome = N'Wrong' THEN 1 ELSE 0 END) AS Wrong,
                SUM(CASE WHEN Outcome = N'Unclear' THEN 1 ELSE 0 END) AS Unclear,
                SUM(CASE WHEN Outcome = N'Pending' AND ReviewAtUtc < sysdatetimeoffset() THEN 1 ELSE 0 END) AS PendingPastReview
            FROM Entries GROUP BY Category, Label ORDER BY Category, Label;
            """, _ => { }, r => (Category: r.GetString(0), Label: r.GetString(1), Scored: r.GetInt32(2),
                Right: r.GetInt32(3), Wrong: r.GetInt32(4), Unclear: r.GetInt32(5), PendingPastReview: r.GetInt32(6)), ct).ConfigureAwait(false);
        var total = rows.SingleOrDefault(r => r.Category == "Total");
        return new(total.Scored, total.Right, total.Wrong, total.Unclear, total.PendingPastReview,
            rows.Where(r => r.Category == "Section").Select(r => (r.Label, r.Right, r.Wrong)).ToArray(),
            rows.Where(r => r.Category == "Book").Select(r => (r.Label, r.Right, r.Wrong)).ToArray());
    }

    public Task ScoreDecisionAsync(long id, string outcome, string? note, string by, CancellationToken ct)
    {
        if (!new[] { "Right", "Wrong", "Unclear" }.Contains(outcome)) throw new ArgumentException("Choose an outcome.", nameof(outcome));
        return ExecuteAsync(@"UPDATE opportunities.BdDecision
SET Outcome = @outcome, OutcomeNote = @note, ScoredAtUtc = sysdatetimeoffset(), ScoredBy = @by
WHERE Id = @id;", cmd =>
        {
            Identity(cmd, id, by); AddText(cmd, "@outcome", 20, outcome); AddText(cmd, "@note", 1000, note);
        }, ct);
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Action<SqlCommand> parameters, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add(map(reader));
        return rows;
    }

    private async Task<long> InsertAsync(string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    private async Task ExecuteAsync(string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddText(SqlCommand cmd, string name, int size, string? value)
    {
        if (value?.Length > size) throw new ArgumentException("Text is too long.", name);
        cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;
    }
    private static void Identity(SqlCommand cmd, long id, string by)
    {
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        AddText(cmd, "@by", 150, by);
    }
    private static void Context(SqlCommand cmd, long? pursuit, long? account)
    {
        cmd.Parameters.Add("@pursuit", SqlDbType.BigInt).Value = (object?)pursuit ?? DBNull.Value;
        cmd.Parameters.Add("@account", SqlDbType.BigInt).Value = (object?)account ?? DBNull.Value;
    }
    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? Id(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static DateTimeOffset? Date(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
    private static BdAsk ReadAsk(SqlDataReader r) => new(r.GetInt64(0), r.GetString(1), Id(r, 2), Id(r, 3),
        r.GetString(4), Text(r, 5), r.GetInt32(6), r.GetDateTimeOffset(7), r.GetString(8), r.GetDateTimeOffset(9),
        r.GetString(10), Date(r, 11), Text(r, 12), Text(r, 13));
}
