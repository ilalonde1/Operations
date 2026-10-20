#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Plan;

/// <summary>One stage from signal to cash (1-8), or IT underneath (9). Today is a template: {Metric} fills from the readings.</summary>
public sealed record PlanStageRow(int No, string Pillar, string Name, string Owner, string What, string Today, string SystemText,
    string WeakPoint, string Health);

/// <summary>A goal to a date. Status is Recommended until Ian decides it, then Decided, or Retired.</summary>
public sealed record PlanGoalRow(int Id, string Pillar, int StageNo, string Title, string? Metric, double? BaselineValue,
    double? TargetValue, string TargetText, DateOnly DueDate, string Status, DateTimeOffset? DecidedAtUtc, string? DecidedBy);

public sealed record PlanPointRow(int Id, string Pillar, string Section, string Title, string Detail);

public sealed record PlanRhythmRow(int Id, string Name, string WhoWhen, string WhatHappens, string Moves, int StageFrom, int StageTo);

public sealed record PlanPillarRow(string Key, string Title, string Fortune500, string KorHas, string Found, string NextText);

/// <summary>The plan as stored (migration 372).</summary>
public sealed record OperatingPlanModel(IReadOnlyList<PlanStageRow> Stages, IReadOnlyList<PlanGoalRow> Goals,
    IReadOnlyList<PlanPointRow> Points, IReadOnlyList<PlanRhythmRow> Rhythm, IReadOnlyList<PlanPillarRow> Pillars);

/// <summary>
/// One real pursuit shown end to end as the plan's worked example (the plan point in section "Example" names it). Read live
/// from the CRM, so the example moves on by itself when the pursuit does: the posting, the owner, the files stored, the
/// checklist result, the submission and the outcome.
/// </summary>
public sealed record PlanExample(long EngagementId, string Title, string Buyer, string? Portal, DateTimeOffset? Found,
    DateTimeOffset? Deadline, string Owner, int Stage, decimal? Fee, DateTimeOffset? SubmittedAt, string? SubmittedFile,
    int Files, int Met, int NotMet, int NotApplicable, int Open);

/// <summary>Radar counts: signals first seen in the last 30 days, and enabled of all sources.</summary>
public sealed record PlanRadar(int Signals30d, int SourcesLive, int SourcesTotal);

public enum PlanUnit { Count, Money, Days, Percent, Hours, Ratio }

/// <summary>One metric the plan can read: how it prints, and which way is better.</summary>
public sealed record PlanMetric(string Key, string Label, PlanUnit Unit, bool LowerIsBetter);

/// <summary>A reading: today's value and, for the scoreboard's daily metrics, the baseline day's.</summary>
public sealed record PlanReading(double Value, double? Baseline, DateOnly AsOf, string Source);

/// <summary>
/// Every metric the plan's text and goals may name. Scoreboard metrics are recorded once a day by the morning composer
/// (opportunities.ImpactSnapshot, migration 370); the rest are counted live when the page is drawn. A key listed here
/// with no reading prints as a dash.
/// </summary>
public static class PlanMetrics
{
    public static readonly IReadOnlyList<PlanMetric> All = new[]
    {
        new PlanMetric("ArTotal", "Owed to KOR", PlanUnit.Money, true),
        new PlanMetric("ArOver60", "Owed over 60 days", PlanUnit.Money, true),
        new PlanMetric("ArOver90", "Owed over 90 days", PlanUnit.Money, true),
        new PlanMetric("ArInactive", "Owed on inactive projects", PlanUnit.Money, true),
        new PlanMetric("CriticalProjects", "Critical projects", PlanUnit.Count, true),
        new PlanMetric("OverBudgetProjects", "Projects over hours budget", PlanUnit.Count, true),
        new PlanMetric("ActiveProjects", "Active projects", PlanUnit.Count, false),
        new PlanMetric("CrmWonCount", "Pursuits won", PlanUnit.Count, false),
        new PlanMetric("CrmLostCount", "Pursuits lost", PlanUnit.Count, true),
        new PlanMetric("CrmWonFee", "Fees won", PlanUnit.Money, false),
        new PlanMetric("ActionsOpen", "Open actions", PlanUnit.Count, true),
        new PlanMetric("ActionsClosedTotal", "Actions closed", PlanUnit.Count, false),
        new PlanMetric("MeetingsHeldTotal", "Meetings held", PlanUnit.Count, false),
        // The Financials engine's own Executive Summary KPIs, recorded as-is by the composer (MorningImpact.ExecKey).
        new PlanMetric("Exec.CashPosition", "Cash position", PlanUnit.Money, false),
        new PlanMetric("Exec.LiquidityCashAR", "Liquidity (cash + AR)", PlanUnit.Money, false),
        new PlanMetric("Exec.AROutstanding", "AR outstanding", PlanUnit.Money, true),
        new PlanMetric("Exec.AR60Days", "AR over 60 days", PlanUnit.Money, true),
        new PlanMetric("Exec.Backlog", "Backlog", PlanUnit.Money, false),
        new PlanMetric("Exec.BillingsToDate", "Billings to date", PlanUnit.Money, false),
        new PlanMetric("Exec.NetMultiplier", "Net multiplier", PlanUnit.Ratio, false),
        new PlanMetric("Exec.LaborMarginT12mo", "Labour margin, 12 months", PlanUnit.Money, false),
        new PlanMetric("Exec.NetIncomeT12mo", "Net income, 12 months", PlanUnit.Money, false),
        new PlanMetric("Exec.DaysSalesOutstanding", "Days to collect (DSO)", PlanUnit.Days, true),
        new PlanMetric("Exec.Utilization", "Utilization", PlanUnit.Percent, false),
        // The engine's per-project ProfitDollars and multiplier, summed over active projects with booked labour.
        new PlanMetric("LosingProjects", "Active projects losing money", PlanUnit.Count, true),
        new PlanMetric("LosingProjectsLoss", "Lost on losing projects, after overhead", PlanUnit.Money, true),
        new PlanMetric("ProjectsBelowMultiplier2", "Projects below a 2.0 multiplier", PlanUnit.Count, true),
        new PlanMetric("LosingDoneProjects", "Losing projects 90%+ billed", PlanUnit.Count, true),
        new PlanMetric("LosingDoneProjectsLoss", "Lost on losing projects 90%+ billed, after overhead", PlanUnit.Money, true),
        new PlanMetric("LosingDoneFixedFee", "Of those, fixed fee only", PlanUnit.Count, true),
        new PlanMetric("LosingDoneFixedFeeLoss", "Lost on them: fixed fee only", PlanUnit.Money, true),
        new PlanMetric("LosingDoneHourly", "Of those, hourly only", PlanUnit.Count, true),
        new PlanMetric("LosingDoneHourlyLoss", "Lost on them: hourly only", PlanUnit.Money, true),
        new PlanMetric("LosingDoneMixed", "Of those, fixed fee with hourly extras", PlanUnit.Count, true),
        new PlanMetric("LosingDoneMixedLoss", "Lost on them: fixed fee with hourly extras", PlanUnit.Money, true),
        new PlanMetric("InvoicedMonthAvg6", "Invoiced a month, last six months", PlanUnit.Money, false),
        new PlanMetric("InvoicedMonthAvgPrior6", "Invoiced a month, the six before", PlanUnit.Money, false),
        new PlanMetric("BillingLagProjects", "Projects billing slower than they burn", PlanUnit.Count, true),
        new PlanMetric("OverBudgetHours", "Hours past budget on over-budget projects", PlanUnit.Hours, true),
        new PlanMetric("ItCriticalGroups", "Critical IT issue groups", PlanUnit.Count, true),
        new PlanMetric("ItWarningGroups", "IT warning groups", PlanUnit.Count, true),
        new PlanMetric("ItUpdatesDue", "Machines with security updates due", PlanUnit.Count, true),
        new PlanMetric("PursuitsLive", "Live pursuits", PlanUnit.Count, false),
        new PlanMetric("PursuitsWithFee", "Live pursuits with a fee", PlanUnit.Count, false),
        new PlanMetric("PursuitsUntouched14", "Pursuits untouched 14+ days", PlanUnit.Count, true),
        new PlanMetric("PursuitsTender", "Live tender pursuits", PlanUnit.Count, false),
        new PlanMetric("SubmittedOver60", "Submitted over 60 days, no outcome", PlanUnit.Count, true),
        new PlanMetric("Signals30d", "Signals in 30 days", PlanUnit.Count, false),
        new PlanMetric("SourcesLive", "Sources live", PlanUnit.Count, false),
        new PlanMetric("SourcesTotal", "Sources", PlanUnit.Count, false),
        new PlanMetric("RollupAsks", "Roll-up asks due", PlanUnit.Count, true),
    };

    private static readonly Dictionary<string, PlanMetric> ByKey = All.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public static PlanMetric? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var m) ? m : null;

    public static string Format(PlanUnit unit, double v) => unit switch
    {
        PlanUnit.Money => Math.Abs(v) >= 999_500 ? "$" + (v / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M"
            : Math.Abs(v) >= 1_000 ? "$" + Math.Round(v / 1_000).ToString("N0", CultureInfo.InvariantCulture) + "k"
            : "$" + Math.Round(v).ToString("N0", CultureInfo.InvariantCulture),
        PlanUnit.Percent => v.ToString("0.0", CultureInfo.InvariantCulture) + "%",
        PlanUnit.Hours => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture) + " hours",
        PlanUnit.Ratio => v.ToString("0.00", CultureInfo.InvariantCulture),
        _ => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture),
    };
}

/// <summary>The day's readings, keyed by metric, and the text filler that puts them into the plan's prose.</summary>
public sealed class PlanReadings
{
    public const string Dash = "—";
    private static readonly Regex Token = new(@"\{([A-Za-z][A-Za-z0-9.]*)\}", RegexOptions.Compiled);
    private readonly Dictionary<string, PlanReading> _values;

    public PlanReadings(IReadOnlyDictionary<string, PlanReading> values) =>
        _values = new Dictionary<string, PlanReading>(values, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, PlanReading> Values => _values;

    public PlanReading? this[string? key] => key is not null && _values.TryGetValue(key, out var r) ? r : null;

    /// <summary>The latest scoreboard date the readings carry, or null when none.</summary>
    public DateOnly? ScoreboardAsOf => _values.Values.Where(r => r.Source == "scoreboard").Select(r => (DateOnly?)r.AsOf).Max();

    /// <summary>A metric printed in its unit, or a dash when there is no reading.</summary>
    public string Show(string key)
    {
        var m = PlanMetrics.Find(key);
        var r = this[key];
        return m is null || r is null ? Dash : PlanMetrics.Format(m.Unit, r.Value);
    }

    /// <summary>Change since the baseline day, signed, with whether it is better; null without a baseline or with no change.</summary>
    public (string Text, bool Better)? Change(string key)
    {
        var m = PlanMetrics.Find(key);
        var r = this[key];
        if (m is null || r?.Baseline is not { } b) return null;
        var d = r.Value - b;
        if (Math.Abs(d) < 0.5) return null;
        return ((d > 0 ? "+" : "−") + PlanMetrics.Format(m.Unit, Math.Abs(d)), (d < 0) == m.LowerIsBetter);
    }

    /// <summary>Replaces each {Metric} with its reading; unknown names and missing readings print as a dash.</summary>
    public string Fill(string template) => Token.Replace(template ?? "", t => Show(t.Groups[1].Value));

    /// <summary>The metric names a template asks for.</summary>
    public static IEnumerable<string> TokensIn(string template) =>
        Token.Matches(template ?? "").Select(t => t.Groups[1].Value);

    /// <summary>
    /// Builds the readings: the scoreboard's latest value per metric with its first day as the baseline, then the live pursuit
    /// counts (from the same pursuit states the touchstones use), the radar and this week's roll-up asks.
    /// </summary>
    public static PlanReadings From(IReadOnlyList<ImpactReading> scoreboard, IReadOnlyList<PursuitState> pursuits, PlanRadar? radar,
        int rollupAsks, DateTimeOffset now)
    {
        var values = new Dictionary<string, PlanReading>(StringComparer.Ordinal);
        foreach (var g in scoreboard.GroupBy(r => r.Metric, StringComparer.Ordinal))
        {
            var ordered = g.OrderBy(r => r.Date).ToList();
            values[g.Key] = new PlanReading(ordered[^1].Value, ordered[0].Value, ordered[^1].Date, "scoreboard");
        }

        var today = DateOnly.FromDateTime(now.Date);
        void Live(string key, double v) => values[key] = new PlanReading(v, null, today, "live");
        var live = pursuits.Where(p => p.IsLive).ToList();
        Live("PursuitsLive", live.Count);
        Live("PursuitsWithFee", live.Count(p => p.ProposedFee is > 0));
        Live("PursuitsUntouched14", live.Count(p => now - p.LastTouchUtc >= TimeSpan.FromDays(14)));
        Live("PursuitsTender", live.Count(p => p.IsTender));
        Live("SubmittedOver60", live.Count(p => p.Stage == 3 && p.SubmittedUtc is { } s && now - s >= TimeSpan.FromDays(60)));
        Live("RollupAsks", rollupAsks);
        if (radar is not null)
        {
            Live("Signals30d", radar.Signals30d);
            Live("SourcesLive", radar.SourcesLive);
            Live("SourcesTotal", radar.SourcesTotal);
        }

        return new PlanReadings(values);
    }
}

/// <summary>One concrete thing under a stage today: a roll-up ask or a morning move.</summary>
public sealed record PlanItem(int StageNo, string Title, string Detail, bool Urgent, string? Link);

/// <summary>Maps today's detail onto the stages: touchstone asks onto 2-5 by their anchor, each morning move onto the stage of every goal it names (an hour written before goals: COO moves onto 6, IT moves onto 9).</summary>
public static class PlanItems
{
    /// <summary>The stage a touchstone serves, from its anchor.</summary>
    public static int StageOf(PursuitTouchstoneRow t) => t.Anchor switch
    {
        "Deadline" => 3,
        "Opened" when t.Applies == "Tender" => 3,
        "Submitted" => 4,
        "Won" or "Lost" => 5,
        _ => 2,
    };

    public static IReadOnlyList<PlanItem> From(IReadOnlyList<TouchstoneDue> due, DirectorHourContent? hour,
        IReadOnlyDictionary<int, int>? goalStages = null)
    {
        var items = new List<PlanItem>();
        foreach (var d in due.OrderByDescending(d => d.IsUrgent).ThenBy(d => d.DueFrom))
        {
            var p = d.Pursuit;
            var who = string.IsNullOrWhiteSpace(p.OwnerName) ? p.OwnerKey : p.OwnerName;
            var title = string.IsNullOrWhiteSpace(p.Title) ? p.Buyer
                : string.IsNullOrWhiteSpace(p.Buyer) || string.Equals(p.Buyer, p.Title, StringComparison.OrdinalIgnoreCase)
                ? p.Title : $"{p.Title} ({p.Buyer})";
            var detail = d.Touchstone.Name + (string.IsNullOrWhiteSpace(p.NextActionNote) || d.Touchstone.Anchor != "NextAction"
                ? "" : ": " + p.NextActionNote.Trim());
            items.Add(new PlanItem(StageOf(d.Touchstone), $"{title} · {FirstName(who)}", detail, d.IsUrgent,
                Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.Pursuit(p.EngagementId)));
        }

        foreach (var s in hour?.Sections ?? Array.Empty<HourSection>())
        {
            var roleStage = s.Role.ToUpperInvariant() switch { "COO" => 6, "IT" => 9, _ => 0 };
            foreach (var m in s.Moves)
            {
                // A move sits on the stage of each goal it names; an hour written before goals falls back to its role.
                var stages = goalStages is null ? new List<int>()
                    : m.Goals.Where(goalStages.ContainsKey).Select(id => goalStages[id]).Distinct().ToList();
                if (stages.Count == 0 && roleStage != 0) stages.Add(roleStage);
                foreach (var stage in stages)
                    items.Add(new PlanItem(stage, m.Title, m.WhyNow.FirstOrDefault() ?? "", false, null));
            }
        }

        return items;
    }

    private static string FirstName(string? name)
    {
        var n = (name ?? "").Trim();
        var at = n.IndexOf('@');
        if (at > 0) n = n[..at];
        var space = n.IndexOf(' ');
        return space > 0 ? n[..space] : n;
    }
}

public interface IOperatingPlanStore
{
    Task<OperatingPlanModel> LoadAsync(CancellationToken ct);

    Task<PlanRadar> ReadRadarAsync(CancellationToken ct);

    /// <summary>Marks a goal Decided by this person now, optionally with a new target. Returns false when the goal is missing.</summary>
    Task<bool> DecideGoalAsync(int goalId, double? target, string decidedBy, CancellationToken ct);

    /// <summary>The plan's worked example, read live; null when the pursuit is gone.</summary>
    Task<PlanExample?> ReadExampleAsync(long engagementId, CancellationToken ct);
}

public sealed class SqlOperatingPlanStore : IOperatingPlanStore
{
    private readonly string _connectionString;

    public SqlOperatingPlanStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<OperatingPlanModel> LoadAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        var stages = await ReadAsync(con, "SELECT No, Pillar, Name, Owner, What, Today, SystemText, WeakPoint, Health FROM opportunities.PlanStage ORDER BY No;",
            r => new PlanStageRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetString(6), r.GetString(7), r.GetString(8)), ct).ConfigureAwait(false);
        var goals = await ReadAsync(con, @"SELECT Id, Pillar, StageNo, Title, Metric, BaselineValue, TargetValue, TargetText, DueDate, Status,
    DecidedAtUtc, DecidedBy FROM opportunities.PlanGoal WHERE Status <> N'Retired' ORDER BY SortOrder, Id;",
            r => new PlanGoalRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDouble(5), r.IsDBNull(6) ? null : r.GetDouble(6), r.GetString(7),
                DateOnly.FromDateTime(r.GetDateTime(8)), r.GetString(9), r.IsDBNull(10) ? null : r.GetDateTimeOffset(10),
                r.IsDBNull(11) ? null : r.GetString(11)), ct).ConfigureAwait(false);
        var points = await ReadAsync(con, "SELECT Id, Pillar, Section, Title, Detail FROM opportunities.PlanPoint ORDER BY SortOrder, Id;",
            r => new PlanPointRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)), ct).ConfigureAwait(false);
        var rhythm = await ReadAsync(con, "SELECT Id, Name, WhoWhen, WhatHappens, Moves, StageFrom, StageTo FROM opportunities.PlanRhythm ORDER BY SortOrder, Id;",
            r => new PlanRhythmRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6)), ct).ConfigureAwait(false);
        var pillars = await ReadAsync(con, "SELECT PillarKey, Title, Fortune500, KorHas, Found, NextText FROM opportunities.PlanPillar ORDER BY SortOrder;",
            r => new PlanPillarRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)), ct).ConfigureAwait(false);
        return new OperatingPlanModel(stages, goals, points, rhythm, pillars);
    }

    public async Task<PlanRadar> ReadRadarAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT (SELECT COUNT(*) FROM opportunities.Opportunities WHERE CreatedAtUtc >= DATEADD(day, -30, SYSDATETIMEOFFSET())),
       (SELECT COUNT(*) FROM opportunities.OpportunitySources WHERE IsEnabled = 1),
       (SELECT COUNT(*) FROM opportunities.OpportunitySources);", con) { CommandTimeout = 60 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await r.ReadAsync(ct).ConfigureAwait(false);
        return new PlanRadar(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
    }

    public async Task<bool> DecideGoalAsync(int goalId, double? target, string decidedBy, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE opportunities.PlanGoal SET Status = N'Decided', DecidedAtUtc = SYSDATETIMEOFFSET(), DecidedBy = @by,
    TargetValue = COALESCE(@target, TargetValue)
WHERE Id = @id AND Status <> N'Retired';", con) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@id", goalId);
        cmd.Parameters.AddWithValue("@by", decidedBy);
        cmd.Parameters.AddWithValue("@target", (object?)target ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<PlanExample?> ReadExampleAsync(long engagementId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        // The portal is the source of the opportunity's first observation; the submission time is the pursuit's entry into
        // Submitted (stage 3), the same anchor the roll-ups use. BdStaff.StaffKey carries an explicit collation.
        await using var cmd = new SqlCommand(@"
SELECT e.Id, COALESCE(NULLIF(LTRIM(RTRIM(o.Name)), N''), e.PotentialProjects, N''), COALESCE(co.DisplayName, o.BuyerName, N''),
       src.Name, o.CreatedAtUtc, o.SubmissionDeadlineUtc, COALESCE(s.DisplayName, e.OwnerStaffId, N''), e.Stage, e.ProposedFee,
       sh.SubmittedUtc, e.SubmittedFileName,
       (SELECT COUNT(*) FROM opportunities.OpportunityFiles f WHERE f.EngagementId = e.Id),
       (SELECT COUNT(*) FROM opportunities.PursuitRequirement r WHERE r.EngagementId = e.Id AND r.Status = N'Met'),
       (SELECT COUNT(*) FROM opportunities.PursuitRequirement r WHERE r.EngagementId = e.Id AND r.Status = N'NotMet'),
       (SELECT COUNT(*) FROM opportunities.PursuitRequirement r WHERE r.EngagementId = e.Id AND r.Status = N'NotApplicable'),
       (SELECT COUNT(*) FROM opportunities.PursuitRequirement r WHERE r.EngagementId = e.Id AND r.Status = N'Open')
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = COALESCE(e.BuyerCanonicalOrgId, o.BuyerCanonicalOrgId)
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId COLLATE SQL_Latin1_General_CP1_CI_AS
OUTER APPLY (SELECT TOP (1) os.Name FROM opportunities.OpportunityObservations ob
             JOIN opportunities.OpportunitySources os ON os.Id = ob.OpportunitySourceId
             WHERE ob.OpportunityId = o.Id ORDER BY ob.IngestedAtUtc, ob.Id) src
OUTER APPLY (SELECT MAX(h.EnteredAtUtc) AS SubmittedUtc FROM opportunities.CrmEngagementStageHistory h
             WHERE h.EngagementId = e.Id AND h.Stage = 3) sh
WHERE e.Id = @id;", con) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@id", engagementId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        DateTimeOffset? At(int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
        return new PlanExample(r.GetInt64(0), r.GetString(1), r.GetString(2), Portal(r.IsDBNull(3) ? null : r.GetString(3)), At(4), At(5),
            r.GetString(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetDecimal(8), At(9), r.IsDBNull(10) ? null : r.GetString(10),
            r.GetInt32(11), r.GetInt32(12), r.GetInt32(13), r.GetInt32(14), r.GetInt32(15));
    }

    /// <summary>The portal a source name belongs to, in words a reader knows.</summary>
    public static string? Portal(string? sourceName) => sourceName switch
    {
        null => null,
        _ when sourceName.StartsWith("BidsTenders", StringComparison.OrdinalIgnoreCase) => "Bids&Tenders",
        _ when sourceName.StartsWith("APC", StringComparison.OrdinalIgnoreCase) => "Alberta Purchasing Connection",
        _ when sourceName.StartsWith("BcBid", StringComparison.OrdinalIgnoreCase) => "BC Bid",
        _ when sourceName.StartsWith("Bonfire", StringComparison.OrdinalIgnoreCase) => "Bonfire",
        _ => sourceName,
    };

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(SqlConnection con, string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<T>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(map(r));
        return list;
    }
}
