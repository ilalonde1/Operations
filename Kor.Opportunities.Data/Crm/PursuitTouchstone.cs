#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One touchstone rule (migration 371). Applies: Tender (the pursuit carries an opportunity), Relationship (BD tracking) or
/// Any. The touchstone comes due OffsetDays after its anchor date and stays due for WindowDays (0 = until drafted).
/// Situations are the doctrine card tags it matches.
/// </summary>
public sealed record PursuitTouchstoneRow(int Id, string Applies, string Anchor, int OffsetDays, int WindowDays, string Name,
    string Carries, IReadOnlyList<string> Situations);

/// <summary>A live or recently closed pursuit, with the facts its owner's nudge draws on.</summary>
public sealed record PursuitState(long EngagementId, string OwnerKey, string OwnerName, int Stage, string Title, string Buyer,
    long? BuyerOrgId, long? OpportunityId, decimal? ProposedFee, DateTimeOffset OpenedUtc, DateTimeOffset LastTouchUtc)
{
    public DateTimeOffset? DeadlineUtc { get; init; }
    public DateTimeOffset? SubmittedUtc { get; init; }
    public DateTimeOffset? ClosedUtc { get; init; }
    public DateTimeOffset? NextActionDueUtc { get; init; }
    public string? NextActionNote { get; init; }
    public string? OutcomeReason { get; init; }
    public string? WonProjectWbs1 { get; init; }
    public int? KorProjects { get; init; }
    public DateTime? LastKorProject { get; init; }
    public int OpenRequirements { get; init; }
    public int Requirements { get; init; }
    public string? LatestNews { get; init; }
    public string? Region { get; init; }

    public bool IsTender => OpportunityId is not null;
    public bool IsLive => Stage is 1 or 3;
}

/// <summary>Which touchstone was drafted for which pursuit, and from which anchor date. Text form: "312:8:2026-09-06".</summary>
public sealed record TouchstoneKey(long EngagementId, int TouchstoneId, DateOnly AnchorDate)
{
    public override string ToString() => FormattableString.Invariant($"{EngagementId}:{TouchstoneId}:{AnchorDate:yyyy-MM-dd}");

    public static bool TryParse(string? text, out TouchstoneKey key)
    {
        key = null!;
        var parts = (text ?? "").Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var engagement) || engagement <= 0
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var touchstone) || touchstone <= 0
            || !DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var anchor))
            return false;
        key = new TouchstoneKey(engagement, touchstone, anchor);
        return true;
    }
}

public sealed record TouchstoneDraftRow(TouchstoneKey Key, DateTimeOffset DraftedAtUtc);

/// <summary>A touchstone due today on one pursuit. Urgent ones (dates set by a buyer or a stage) do not wait for the weekly roll-up.</summary>
public sealed record TouchstoneDue(PursuitState Pursuit, PursuitTouchstoneRow Touchstone, DateOnly AnchorDate, DateOnly DueFrom)
{
    public TouchstoneKey Key => new(Pursuit.EngagementId, Touchstone.Id, AnchorDate);
    public bool IsUrgent => Touchstone.Anchor is not ("LastTouch" or "Opened") || Pursuit.IsTender;
}

/// <summary>One owner's roll-up: everything due on their pursuits, and whether the weekly draft is owed today.</summary>
public sealed record OwnerRollup(string OwnerKey, string OwnerName, IReadOnlyList<TouchstoneDue> Due, int Pursuits,
    DateTimeOffset? LastDraftedUtc, bool IsOwed, DateOnly NextOn);

public static class PursuitTouchstones
{
    /// <summary>An owner hears at most once a week, unless something urgent is due.</summary>
    public const int RollupEveryDays = 7;

    /// <summary>
    /// The touchstones due today. Anchors: Opened (live; the relationship one only while never touched), Deadline (pending
    /// tenders), Submitted (submitted pursuits), LastTouch (live relationship pursuits; the latest of the last touch and the
    /// last roll-up that covered the pursuit, so a draft restarts the clock), NextAction (live), Won (no Deltek project linked) and Lost (no
    /// reason recorded). A touchstone already drafted from the same anchor date is not due again.
    /// </summary>
    public static IReadOnlyList<TouchstoneDue> Compute(IEnumerable<PursuitTouchstoneRow> rules, IEnumerable<PursuitState> pursuits,
        IEnumerable<TouchstoneDraftRow> drafts, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var drafted = drafts.ToList();
        var done = drafted.Select(d => d.Key).ToHashSet();
        var today = Day(nowUtc, zone);
        var result = new List<TouchstoneDue>();
        var ruleList = rules.ToList();
        foreach (var p in pursuits)
        {
            foreach (var rule in ruleList)
            {
                if (rule.Applies == "Tender" && !p.IsTender || rule.Applies == "Relationship" && p.IsTender) continue;
                DateTimeOffset? anchor = rule.Anchor switch
                {
                    "Opened" when p.IsLive && (p.IsTender || p.LastTouchUtc <= p.OpenedUtc) => p.OpenedUtc,
                    "Deadline" when p.Stage == 1 => p.DeadlineUtc,
                    "Submitted" when p.Stage == 3 => p.SubmittedUtc,
                    "LastTouch" when p.IsLive => Latest(p.LastTouchUtc, drafted
                        .Where(d => d.Key.EngagementId == p.EngagementId)
                        .Select(d => (DateTimeOffset?)d.DraftedAtUtc).Max()),
                    "NextAction" when p.IsLive => p.NextActionDueUtc,
                    "Won" when p.Stage == 6 && string.IsNullOrWhiteSpace(p.WonProjectWbs1) => p.ClosedUtc,
                    "Lost" when p.Stage == 7 && string.IsNullOrWhiteSpace(p.OutcomeReason) => p.ClosedUtc,
                    _ => null,
                };
                if (anchor is not { } at) continue;
                var anchorDate = Day(at, zone);
                var dueFrom = anchorDate.AddDays(rule.OffsetDays);
                if (today < dueFrom || rule.WindowDays > 0 && today > dueFrom.AddDays(rule.WindowDays)) continue;
                var due = new TouchstoneDue(p, rule, anchorDate, dueFrom);
                if (!done.Contains(due.Key)) result.Add(due);
            }
        }
        return result;
    }

    /// <summary>
    /// One roll-up per owner of a live pursuit, with one item per pursuit: its most pressing touchstone (urgent first, then the
    /// rule's order). The others wait, and a keep-warm restarts from the roll-up. It is owed when something urgent is due, or
    /// when anything is due and the owner has had no roll-up for <see cref="RollupEveryDays"/> days (or ever). Items run urgent
    /// first, then by when they came due, then by proposed fee.
    /// </summary>
    public static IReadOnlyList<OwnerRollup> Rollups(IReadOnlyList<TouchstoneDue> due, IEnumerable<PursuitState> pursuits,
        IEnumerable<TouchstoneDraftRow> drafts, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var all = pursuits.ToList();
        var byEngagement = all.ToDictionary(p => p.EngagementId);
        var draftList = drafts.ToList();
        var today = Day(nowUtc, zone);
        return all.Where(p => p.IsLive).Select(p => p.OwnerKey).Concat(due.Select(d => d.Pursuit.OwnerKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(owner =>
            {
                var mine = due.Where(d => string.Equals(d.Pursuit.OwnerKey, owner, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(d => d.Pursuit.EngagementId)
                    .Select(g => g.OrderByDescending(d => d.IsUrgent).ThenBy(d => d.Touchstone.Id).First())
                    .OrderByDescending(d => d.IsUrgent).ThenBy(d => d.DueFrom).ThenByDescending(d => d.Pursuit.ProposedFee ?? 0m)
                    .ThenBy(d => d.Pursuit.EngagementId).ThenBy(d => d.Touchstone.Id).ToList();
                var last = draftList.Where(d => byEngagement.TryGetValue(d.Key.EngagementId, out var p)
                        && string.Equals(p.OwnerKey, owner, StringComparison.OrdinalIgnoreCase))
                    .Select(d => (DateTimeOffset?)d.DraftedAtUtc).Max();
                var next = last is { } l ? Day(l, zone).AddDays(RollupEveryDays) : today;
                var owed = mine.Count > 0 && (mine.Any(d => d.IsUrgent) || next <= today);
                var name = all.FirstOrDefault(p => string.Equals(p.OwnerKey, owner, StringComparison.OrdinalIgnoreCase))?.OwnerName ?? owner;
                return new OwnerRollup(owner, name, mine, all.Count(p => p.IsLive && string.Equals(p.OwnerKey, owner, StringComparison.OrdinalIgnoreCase)),
                    last, owed, next < today ? today : next);
            })
            .OrderByDescending(r => r.IsOwed).ThenBy(r => r.OwnerName, StringComparer.Ordinal).ToList();
    }

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset? b) => b is { } x && x > a ? x : a;

    private static DateOnly Day(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
}

public interface IPursuitTouchstoneStore
{
    Task<IReadOnlyList<PursuitTouchstoneRow>> ListRulesAsync(CancellationToken ct);
    /// <summary>Live pursuits (pending or submitted) and those won or lost in the last <paramref name="closedWithinDays"/> days.</summary>
    Task<IReadOnlyList<PursuitState>> ListPursuitsAsync(int closedWithinDays, CancellationToken ct);
    Task<IReadOnlyList<TouchstoneDraftRow>> ListDraftsAsync(CancellationToken ct);
    /// <summary>Records the touchstones a roll-up draft covered; returns how many were new. Unknown pursuits or rules are skipped.</summary>
    Task<int> RecordDraftedAsync(IEnumerable<TouchstoneKey> keys, string by, CancellationToken ct);
}

public sealed class SqlPursuitTouchstoneStore : IPursuitTouchstoneStore
{
    private readonly string _connectionString;

    public SqlPursuitTouchstoneStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public Task<IReadOnlyList<PursuitTouchstoneRow>> ListRulesAsync(CancellationToken ct) => ReadAsync(@"
SELECT Id, Applies, Anchor, OffsetDays, WindowDays, Name, Carries, Situations
FROM opportunities.PursuitTouchstone WHERE IsActive = 1 ORDER BY SortOrder, Id;", _ => { },
        r => new PursuitTouchstoneRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5),
            r.GetString(6), r.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), ct);

    // Last touch fuses activity and filed email the same way as SqlPursuitOverwatchStore; the opened date is the floor.
    // BdStaff.StaffKey carries an explicit collation; the join states one.
    public Task<IReadOnlyList<PursuitState>> ListPursuitsAsync(int closedWithinDays, CancellationToken ct) => ReadAsync(@"
SELECT e.Id, e.OwnerStaffId, COALESCE(s.DisplayName, e.OwnerStaffId), e.Stage,
       COALESCE(NULLIF(LTRIM(RTRIM(o.Name)), N''), NULLIF(LTRIM(RTRIM(e.PotentialProjects)), N''), N''),
       COALESCE(NULLIF(LTRIM(RTRIM(co.DisplayName)), N''), o.BuyerName, N''), e.BuyerCanonicalOrgId, e.OpportunityId,
       e.ProposedFee, e.OpenedAtUtc, la.LastActivityUtc, w.LastTouchUtc,
       o.SubmissionDeadlineUtc, COALESCE(sh.SubmittedUtc, o.ProposalSubmittedAtUtc), e.ClosedAtUtc,
       e.NextActionDueUtc, e.NextActionNote, e.OutcomeReason, e.WonProjectWbs1,
       co.KorProjectsCount, co.LastKorProjectAtUtc, rq.OpenCount, rq.TotalCount, news.Line, e.Region
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId
LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId COLLATE SQL_Latin1_General_CP1_CI_AS
OUTER APPLY (SELECT MAX(a.OccurredAtUtc) AS LastActivityUtc FROM opportunities.CrmActivities a WHERE a.EngagementId = e.Id) la
OUTER APPLY (SELECT MAX(h.EnteredAtUtc) AS SubmittedUtc FROM opportunities.CrmEngagementStageHistory h
             WHERE h.EngagementId = e.Id AND h.Stage = 3) sh
OUTER APPLY (SELECT SUM(CASE WHEN r.Status = N'Open' THEN 1 ELSE 0 END) AS OpenCount, COUNT(*) AS TotalCount
             FROM opportunities.PursuitRequirement r WHERE r.EngagementId = e.Id) rq
OUTER APPLY (SELECT TOP (1) CONVERT(nvarchar(10), i.FirstSeenAtUtc, 23) + N' ' + i.SignalType + N': ' + LEFT(i.Subject, 200) AS Line
             FROM opportunities.IntelSignal i
             WHERE i.CanonicalOrgId = e.BuyerCanonicalOrgId AND i.RetiredAtUtc IS NULL
               AND i.FirstSeenAtUtc >= DATEADD(day, -180, SYSDATETIMEOFFSET())
             ORDER BY i.FirstSeenAtUtc DESC, i.Id DESC) news
WHERE e.OwnerStaffId IS NOT NULL
  AND (e.Stage IN (1, 3) OR (e.Stage IN (6, 7) AND e.ClosedAtUtc >= DATEADD(day, -@closed, SYSDATETIMEOFFSET())))
ORDER BY e.Id;", cmd => cmd.Parameters.Add("@closed", SqlDbType.Int).Value = closedWithinDays,
        r =>
        {
            var opened = r.GetDateTimeOffset(9);
            var touch = new[] { opened, When(r, 10) ?? opened, When(r, 11) ?? opened }.Max();
            return new PursuitState(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5),
                Id(r, 6), Id(r, 7), r.IsDBNull(8) ? null : r.GetDecimal(8), opened, touch)
            {
                DeadlineUtc = When(r, 12), SubmittedUtc = When(r, 13), ClosedUtc = When(r, 14), NextActionDueUtc = When(r, 15),
                NextActionNote = Text(r, 16), OutcomeReason = Text(r, 17), WonProjectWbs1 = Text(r, 18),
                KorProjects = r.IsDBNull(19) ? null : r.GetInt32(19), LastKorProject = r.IsDBNull(20) ? null : r.GetDateTime(20),
                OpenRequirements = r.IsDBNull(21) ? 0 : r.GetInt32(21), Requirements = r.IsDBNull(22) ? 0 : r.GetInt32(22),
                LatestNews = Text(r, 23), Region = Text(r, 24),
            };
        }, ct);

    public Task<IReadOnlyList<TouchstoneDraftRow>> ListDraftsAsync(CancellationToken ct) => ReadAsync(@"
SELECT EngagementId, TouchstoneId, AnchorDate, DraftedAtUtc FROM opportunities.PursuitTouchstoneDraft;", _ => { },
        r => new TouchstoneDraftRow(new TouchstoneKey(r.GetInt64(0), r.GetInt32(1), DateOnly.FromDateTime(r.GetDateTime(2))),
            r.GetDateTimeOffset(3)), ct);

    public async Task<int> RecordDraftedAsync(IEnumerable<TouchstoneKey> keys, string by, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(by)) throw new ArgumentException("Say who drafted it.", nameof(by));
        var list = keys.Distinct().ToList();
        if (list.Count == 0) return 0;
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        var added = 0;
        foreach (var key in list)
        {
            await using var cmd = new SqlCommand(@"
INSERT INTO opportunities.PursuitTouchstoneDraft (EngagementId, TouchstoneId, AnchorDate, DraftedBy)
SELECT e.Id, t.Id, @anchor, @by
FROM opportunities.CrmEngagements e CROSS JOIN opportunities.PursuitTouchstone t
WHERE e.Id = @engagement AND t.Id = @touchstone
  AND NOT EXISTS (SELECT 1 FROM opportunities.PursuitTouchstoneDraft d
                  WHERE d.EngagementId = e.Id AND d.TouchstoneId = t.Id AND d.AnchorDate = @anchor);", con, tx) { CommandTimeout = 30 };
            cmd.Parameters.Add("@engagement", SqlDbType.BigInt).Value = key.EngagementId;
            cmd.Parameters.Add("@touchstone", SqlDbType.Int).Value = key.TouchstoneId;
            cmd.Parameters.Add("@anchor", SqlDbType.Date).Value = key.AnchorDate.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = by.Trim();
            added += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return added;
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Action<SqlCommand> bind, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add(map(reader));
        return rows;
    }

    private static DateTimeOffset? When(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
    private static long? Id(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
