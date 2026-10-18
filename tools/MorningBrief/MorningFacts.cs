using System.Data;
using System.Globalization;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Store;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kor.MorningBrief;

public sealed record MorningFact(string Name, IReadOnlyList<string> Lines, int Shown, int? Total,
    string? CouldNotRead = null, string? SectionScope = null)
{
    // Retain row text separately from meaning and cards already read; matching adds no situation/card queries.
    public IReadOnlyList<MorningSituation> Situations { get; init; } = Array.Empty<MorningSituation>();
    public IReadOnlyList<DoctrineCardRow> AcceptedCards { get; init; } = Array.Empty<DoctrineCardRow>();
}

/// <summary>Read-only snapshots. A failed source stays visible, and does not suppress another source.</summary>
public sealed record MorningFacts(DateTimeOffset CapturedAtUtc, IReadOnlyList<MorningFact> Bd,
    IReadOnlyList<MorningFact> Coo, IReadOnlyList<MorningFact> It)
{
    private const string BdConnection = "KOR_OPPORTUNITIES_OPPORTUNITIESDB";

    public static async Task<MorningFacts> GatherAsync(IConfiguration configuration, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        var bd = new List<MorningFact>();
        var coo = new List<MorningFact>();
        var it = new List<MorningFact>();
        var (odbc, financials) = DeltekOptions(configuration);
        await GatherBdAsync(bd, date, now, ct);
        await GatherCooAsync(coo, odbc, financials, date, ct);
        await GatherItAsync(it, now, ct);
        using var embedder = new KnowledgeEmbedder();
        using var knowledge = new MorningKnowledgeReader(() => new SqlKnowledgeStore(Connection(BdConnection)), embedder);
        bd.Add(await knowledge.MatchAsync(bd, "BD", ct));
        coo.Add(await knowledge.MatchAsync(coo, "Operations", ct));
        it.Add(await knowledge.MatchAsync(it, "IT", ct));
        return new MorningFacts(now, bd, coo, it);
    }

    private static async Task GatherBdAsync(List<MorningFact> facts, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        await ReadAsync(facts, "Live pursuits and 14-day no-touch pursuits", async () =>
        {
            var rows = new List<Pursuit>();
            await using var con = await OpenAsync(BdConnection, ct);
            // Same activity/email fusion as SqlPursuitOverwatchStore; include unassigned live pursuits too.
            await using var cmd = new SqlCommand("""
                SELECT e.Id, COALESCE(e.OwnerStaffId, N'(unassigned)'), COALESCE(e.Region, o.ProjectProvince, N'(unknown)'),
                       COALESCE(NULLIF(LTRIM(RTRIM(o.Name)), N''), NULLIF(LTRIM(RTRIM(e.PotentialProjects)), N''), N''),
                       e.ProposedFee, e.OpenedAtUtc, la.LastActivityUtc, w.LastTouchUtc,
                       COALESCE(NULLIF(LTRIM(RTRIM(co.DisplayName)), N''), o.BuyerName, N'')
                FROM opportunities.CrmEngagements e
                LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
                LEFT JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId
                LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId
                OUTER APPLY (SELECT MAX(a.OccurredAtUtc) AS LastActivityUtc
                             FROM opportunities.CrmActivities a WHERE a.EngagementId = e.Id) la
                WHERE e.Stage IN (1, 3) ORDER BY e.Id;
                """, con) { CommandTimeout = 30 };
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var opened = reader.GetDateTimeOffset(5);
                var touches = new[] { opened, reader.IsDBNull(6) ? opened : reader.GetDateTimeOffset(6),
                    reader.IsDBNull(7) ? opened : reader.GetDateTimeOffset(7) };
                rows.Add(new Pursuit(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4), touches.Max(), reader.GetString(8), opened));
            }
            return PursuitFacts(rows, now);
        }, ct);
        await ReadAsync(facts, "Open and late actions by owner", async () =>
        {
            var rows = await new SqlBdRhythmStore(Connection(BdConnection)).ListActionsAsync(ct);
            return ActionFacts(rows, now);
        }, ct);
        await ReadAsync(facts, "Last hour's move results", async () =>
        {
            await using var con = await OpenAsync(BdConnection, ct);
            await using var cmd = new SqlCommand("""
                SELECT TOP (1) Id, ForDate, Kind, ContentJson FROM opportunities.DirectorHour
                WHERE ForDate < @date AND Kind IN (N'Morning', N'BD', N'Operations')
                ORDER BY ForDate DESC, CASE WHEN Kind = N'Morning' THEN 0 ELSE 1 END, ImportedAtUtc DESC, Id DESC;
                """, con) { CommandTimeout = 30 };
            cmd.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
            long id;
            DirectorHourContent hour;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) return new[] { new MorningFact("Last hour's move results", new[] { "No earlier hour exists." }, 0, 0) };
                id = reader.GetInt64(0);
                hour = DirectorHourFile.Parse(reader.GetString(3)).Content ?? throw new InvalidOperationException("Stored hour is invalid.");
            }
            var results = (await new SqlBdRhythmStore(Connection(BdConnection)).ListMoveResultsAsync(id, ct)).ToDictionary(r => r.Rank);
            var moves = DirectorHourFile.AllMoves(hour).ToArray();
            var lines = moves.Select(m => $"{hour.ForDate} {hour.Kind} hour #{id}, move {m.Rank} {m.Title}: "
                + (results.TryGetValue(m.Rank, out var r) ? $"{r.Result}; {r.Note}" : "no result recorded")).ToArray();
            return new[] { new MorningFact($"Last hour's moves ({results.Count} of {moves.Length} have a recorded result)", lines, moves.Length, moves.Length) };
        }, ct);
        await ReadAsync(facts, "Flagged BD doctrine cards", async () =>
        {
            var records = await new SqlBdDoctrineStore(Connection(BdConnection)).ListRecordsAsync("BD", ct);
            var flagged = records.Where(r => r.Flags.Count > 0).ToArray();
            return new[] { new MorningFact($"Flagged BD doctrine ({flagged.Length} of {records.Count} card records flagged)",
                flagged.Select(r => $"#{r.CardId}: {r.FlagText}; {r.Summary}").ToArray(), flagged.Length, flagged.Length) };
        }, ct);
        await CardsAsync(facts, "BD", ct);
    }

    public sealed record Pursuit(long Id, string Owner, string Region, string Title, decimal? Value, DateTimeOffset LastTouch,
        string? OrganizationName = null, DateTimeOffset? OpenedAtUtc = null);

    public static IReadOnlyList<MorningFact> PursuitFacts(IReadOnlyList<Pursuit> rows, DateTimeOffset now)
    {
        var groups = rows.GroupBy(r => (r.Owner, r.Region)).OrderBy(g => g.Key.Owner, StringComparer.Ordinal).ThenBy(g => g.Key.Region, StringComparer.Ordinal);
        var lines = groups.Select(g => $"{g.Key.Owner} / {g.Key.Region}: {g.Count()} of {rows.Count} live pursuits; "
            + $"{g.Count(r => r.Value.HasValue)} of {g.Count()} have proposed fees. "
            + string.Join("; ", g.Select(r => Describe(r, now)))).ToArray();
        var stale = rows.Where(r => r.LastTouch <= now.AddDays(-14)).OrderByDescending(r => QuietDays(r, now))
            .ThenByDescending(r => r.Value.HasValue)
            .ThenByDescending(r => r.Value).ThenBy(r => r.Id).ToArray();
        return new[]
        {
            new MorningFact("Live pursuits by owner and region (value is proposed fee; currency not supplied)", lines, rows.Count, rows.Count),
            new MorningFact("No touch in 14+ days, top 10 by days quiet (proposed fee breaks ties)", stale.Take(10).Select(r => Describe(r, now)).ToArray(), Math.Min(10, stale.Length), stale.Length)
            { Situations = stale.Take(10).Select(r => new MorningSituation("stale pursuit",
                FormattableString.Invariant($"A client pursuit has gone quiet: no contact for {QuietDays(r, now)} days{(r.Value.HasValue ? "" : ", no proposed fee")}. Revive a quiet relationship, or decide to stop pursuing."),
                Describe(r, now), r.OpenedAtUtc is { } opened && r.LastTouch <= opened
                    ? new[] { "going-cold", "never-touched" } : new[] { "going-cold" })).ToArray() },
        };
    }

    private static int QuietDays(Pursuit p, DateTimeOffset now) => Math.Max(0, (int)(now - p.LastTouch).TotalDays);

    private static string Describe(Pursuit p, DateTimeOffset now)
    {
        var title = !string.IsNullOrWhiteSpace(p.Title) ? p.Title
            : !string.IsNullOrWhiteSpace(p.OrganizationName) ? p.OrganizationName : "(untitled)";
        return $"Pursuit #{p.Id}: {title}; {p.Owner} / {p.Region}; proposed fee "
            + (p.Value?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown")
            + $"; {QuietDays(p, now)} days quiet; last touch/opened floor {p.LastTouch:yyyy-MM-dd}";
    }

    public static IReadOnlyList<MorningFact> ActionFacts(IReadOnlyList<BdActionRow> rows, DateTimeOffset now)
    {
        var open = rows.Where(a => a.IsOpen).ToArray();
        var groups = open.GroupBy(a => a.OwnerKey).OrderBy(g => g.Key, StringComparer.Ordinal);
        var lines = groups.Select(g => $"{g.First().OwnerName}: {g.Count()} of {open.Length} open; {g.Count(a => a.DueUtc < now)} of {g.Count()} late. "
            + string.Join("; ", g.OrderBy(a => a.DueUtc).Select(a => $"#{a.Id} {a.Action} (due {a.DueUtc:yyyy-MM-dd})"))).ToArray();
        var late = open.Where(a => a.DueUtc < now).OrderByDescending(IsIan).ThenBy(a => a.DueUtc).ThenBy(a => a.Id).ToArray();
        var lateLines = late.Take(10).Select(a => $"{a.OwnerName}: #{a.Id} {a.Action} (due {a.DueUtc:yyyy-MM-dd})").ToArray();
        return new[]
        {
            new MorningFact("Open actions by owner (late means due before snapshot)", lines, open.Length, open.Length),
            new MorningFact("Late actions, Ian first, top 10", lateLines, lateLines.Length, late.Length)
            { Situations = late.Take(10).Select((a, i) => new MorningSituation("late action",
                FormattableString.Invariant($"A promised follow-up is overdue by {(int)(now - a.DueUtc).TotalDays} days: {MorningKnowledge.WithoutIdentifiers(a.Action, a.OwnerName, a.OwnerKey)}."),
                lateLines[i], new[] { "late-action" })).ToArray() },
        };
    }

    private static bool IsIan(BdActionRow action) =>
        action.OwnerKey.Split('@')[0].Equals("ilalonde", StringComparison.OrdinalIgnoreCase)
        || action.OwnerKey.Equals("ian", StringComparison.OrdinalIgnoreCase)
        || action.OwnerName.Equals("Ian Lalonde", StringComparison.OrdinalIgnoreCase);

    private static async Task GatherCooAsync(List<MorningFact> facts, DeltekOdbcOptions odbc, FinancialsOptions financialsOptions, DateOnly date, CancellationToken ct)
    {
        await ReadAsync(facts, "Executive Summary", async () =>
        {
            var financials = new FinancialsService(odbc, financialsOptions);
            var snap = await financials.GetSnapshotAsync(forceRefresh: true, ct, watchlistOnly: false);
            var trendConnection = OptionalConnection("KOR_FILESYNC_KORTRANSMITTALSDB");
            var trendNote = trendConnection is null
                ? "Portfolio trend not read: KOR_FILESYNC_KORTRANSMITTALSDB unset."
                : null;
            var store = new SqlFinancialPortfolioSnapshotStore(trendConnection ?? "unused: trend supplied by caller");
            var exec = new ExecutiveSummaryService(financials, store, new ExecutiveSummaryDeltekLoader(odbc, financialsOptions));
            var utilRows = snap.Rows.Select(UtilizationRow.FromProject).ToArray();
            if (trendConnection is not null)
            {
                // The composer is the daily writer of the all-active series, the scope it reads. This is its only write.
                try
                {
                    var counts = PortfolioHealthCounts.From(utilRows);
                    await store.TryInsertSnapshotAsync(date.ToDateTime(TimeOnly.MinValue), PortfolioSnapshotScope.AllActive,
                        counts.Healthy, counts.Watch, counts.Critical, counts.Total, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    trendNote = $"Today's all-active portfolio point not recorded ({ex.GetType().Name}).";
                }
            }
            var result = trendConnection is not null
                ? await exec.GetExecutiveSummaryAsync(forceRefresh: true, existingSnapshot: snap,
                    existingTrend: null, existingUtilRows: utilRows, ct: ct, trendScope: PortfolioSnapshotScope.AllActive)
                : await exec.GetExecutiveSummaryAsync(forceRefresh: true, existingSnapshot: snap,
                    existingTrend: Array.Empty<PortfolioTrendPoint>(), existingUtilRows: utilRows, ct: ct);
            return ExecutiveFacts(result, snap.Rows.Count, trendNote);
        }, ct);
        // Weekly completeness only selects the employee outlier week; the engine's
        // Utilization KPI above is printed unchanged.
        var thisWeek = date.ToDateTime(TimeOnly.MinValue).AddDays(-(int)date.DayOfWeek);
        await ReadAsync(facts, "Timesheet completeness and people utilization", async () =>
        {
            var rows = await Task.Run(() => new EmployeeAnalyticsService(odbc).LoadEmployeeWeeklyUtilizationSync(ct), ct);
            return EmployeeWeekFacts(rows, thisWeek);
        }, ct);
        await CardsAsync(facts, "Operations", ct);
    }

    public static IReadOnlyList<MorningFact> ExecutiveFacts(ExecutiveSummaryResult r, int activeProjects, string? trendNote = null)
    {
        var gaps = new List<string>();
        if (!r.SnapshotLoaded) gaps.Add("Active-project snapshot unavailable.");
        if (!r.DeltekLoaded) gaps.Add("Deltek supplement unavailable.");
        if (!r.TrendLoaded) gaps.Add(trendNote ?? "Portfolio trend unavailable.");
        gaps.AddRange((r.LoaderFailureMessages ?? Array.Empty<string>()).Select(message => "Loader: " + message));
        gaps.AddRange((r.SchemaDriftMessages ?? Array.Empty<string>()).Select(message => "Schema drift: " + message));
        var facts = new List<MorningFact>
        {
            new("Executive Summary coverage",
                new[]
                {
                    $"SnapshotLoaded: {r.SnapshotLoaded}; DeltekLoaded: {r.DeltekLoaded}; TrendLoaded: {r.TrendLoaded}.",
                    $"MaxPostedPeriod: {r.MaxPostedPeriod?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "unknown"}.",
                }.Concat(gaps).ToArray(),
                (r.SnapshotLoaded ? 1 : 0) + (r.DeltekLoaded ? 1 : 0) + (r.TrendLoaded ? 1 : 0), 3,
                gaps.Count == 0 ? null : string.Join("; ", gaps),
                SectionScope: $"Executive Summary engine; project-scoped figures cover all active projects ({activeProjects})"),
        };
        foreach (var kpi in r.Kpis)
        {
            var rows = KpiRows(kpi);
            var lines = new[] { kpi.ValueText, kpi.InlineSubText, kpi.StatusMessage }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Concat(rows.Take(10).Select(ExecutiveRow)).ToArray();
            facts.Add(new MorningFact($"{kpi.Title} · {ScopeLabel(kpi.Scope, activeProjects)}", lines, Math.Min(10, rows.Count), rows.Count,
                kpi.ValueText == "Data unavailable" ? kpi.StatusMessage : null)
            {
                Situations = kpi.Title switch
                {
                    "AR > 60 Days" => rows.OfType<KpiArInvoiceRow>().Take(10).Select(p => new MorningSituation("AR invoice",
                        FormattableString.Invariant($"An invoice to a developer client is {p.DaysPastDue} days unpaid ({(p.Balance < 5000 ? "under 5k" : p.Balance <= 25000 ? "5–25k" : "over 25k")}). Collect old receivables, or decide on a write-off."),
                        ExecutiveRow(p), Array.Empty<string>())).ToArray(),
                    "Projects Over Budget" => rows.OfType<KpiProjectDrilldownRow>().Take(5).Select(p => new MorningSituation("over budget",
                        FormattableString.Invariant($"A project is {(p.PercentEngUsed - 1) * 100:0.#}% over its engineering-hours budget with {p.PercentBilled * 100:0.#}% of the fee billed. Diagnose the overrun and recover fee through change orders."),
                        ExecutiveRow(p), Array.Empty<string>())).ToArray(),
                    _ => Array.Empty<MorningSituation>(),
                },
            });
        }
        foreach (var trend in r.Trends)
        {
            var rows = TrendRows(trend);
            var lines = new[] { trend.ValueText, trend.StatusMessage }.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            if (trend.Values is { } values)
                lines.Add($"Series, {values.Length} periods, oldest first (period labels are not carried by the engine): {string.Join("; ", values.Select(Money))}");
            lines.AddRange(rows.Take(10).Select(ExecutiveRow));
            facts.Add(new MorningFact($"{trend.Title} · {ScopeLabel(trend.Scope, activeProjects)}", lines, Math.Min(10, rows.Count), rows.Count,
                trend.ValueText == "Data unavailable" ? trend.StatusMessage : null));
        }
        foreach (var alert in r.Alerts)
        {
            var rows = AlertRows(alert);
            facts.Add(new MorningFact($"{alert.Title} · {ScopeLabel(ScopeKind.None, activeProjects)}", new[] { alert.Message }
                .Concat(rows.Take(10).Select(ExecutiveRow)).ToArray(), Math.Min(10, rows.Count), rows.Count));
        }
        return facts;
    }

    private static string ScopeLabel(ScopeKind scope, int activeProjects) => scope switch
    {
        ScopeKind.Firmwide => "firmwide",
        ScopeKind.Scoped => $"all active projects ({activeProjects})",
        _ => "scope not stated by the engine",
    };

    // AR and cash KPIs can carry two lists. Prefer invoice detail and cash accounts;
    // otherwise use the supplied list, including an explicitly empty list.
    private static IReadOnlyList<object> KpiRows(ExecutiveKpi kpi) => kpi switch
    {
        { ArInvoiceRows: { } rows } => rows,
        { CashAccountRows: { } rows } => rows,
        { ProjectDrilldownRows: { } rows } => rows,
        { CashHistoryRows: { } rows } => rows,
        { ArOutstandingRows: { } rows } => rows,
        { WipUnbilledRows: { } rows } => rows,
        { BacklogRows: { } rows } => rows,
        { BillingsRows: { } rows } => rows,
        { BudgetBurnRows: { } rows } => rows,
        { DeliveryRiskRows: { } rows } => rows,
        { UtilizationRows: { } rows } => rows,
        _ => Array.Empty<object>(),
    };

    private static IReadOnlyList<object> TrendRows(ExecutiveTrend trend) => trend switch
    {
        { TrendPayerRows: { } rows } => rows,
        { DeliveryRiskRows: { } rows } => rows,
        _ => Array.Empty<object>(),
    };

    private static IReadOnlyList<object> AlertRows(ExecutiveAlert alert) => alert switch
    {
        { ArInvoiceRows: { } rows } => rows,
        { ProjectDrilldownRows: { } rows } => rows,
        { ArOutstandingRows: { } rows } => rows,
        { BacklogRows: { } rows } => rows,
        { BudgetBurnRows: { } rows } => rows,
        _ => Array.Empty<object>(),
    };

    private static string ExecutiveRow(object row) => row switch
    {
        KpiProjectDrilldownRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: over by {N(p.OverByHours)} hours; engineering used {P(p.PercentEngUsed)}; "
            + $"billed {P(p.PercentBilled)}, with unposted {P(p.PercentBilledWithUnposted)}; unposted billing {(p.HasUnpostedBilling ? "yes" : "no")}.",
        KpiCashHistoryRow p => $"Period {p.Period}: total {Money(p.Total)}; CAD {Money(p.Cad)}; USA {Money(p.Usa)}; BCC {Money(p.Bcc)}.",
        KpiCashAccountRow p => $"{p.Company}, account {p.Account}, org {p.Org}: {p.Currency} {Money(p.Balance)}.",
        KpiArOutstandingRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: total {Money(p.Total)}; current {Money(p.Current)}; "
            + $"31–60 {Money(p.Aged31To60)}; 61–90 {Money(p.Aged61To90)}; 90+ {Money(p.Aged90Plus)}; oldest invoice {D(p.OldestInvoiceDate)}.",
        KpiArInvoiceRow p => $"{p.Wbs1} {p.ProjectName}, invoice {p.Invoice}, client {p.ClientName} ({p.ClientId}), PM {p.Pm}: "
            + $"balance {Money(p.Balance)}; invoice date {D(p.InvoiceDate)}; {p.DaysPastDue} {(p.DueDate.HasValue ? "days past due" : "days since invoice")}.",
        KpiWipUnbilledRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}, period {p.Period}: earned {Money(p.Earned)}; "
            + $"overbilled {Money(p.Overbilled)}; net {Money(p.Net)}; net/fee {P(p.NetAsPercentOfFee)}.",
        KpiBacklogRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: fee {Money(p.Fee)}; billed {Money(p.FeeBilled)}; "
            + $"unposted {Money(p.UnpostedFeeBilled)}; backlog {Money(p.Backlog)}; billed {P(p.PercentBilled)}.",
        KpiBillingsRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: billed {Money(p.FeeBilled)}; unposted {Money(p.UnpostedFeeBilled)}; "
            + $"fee {Money(p.Fee)}; billed {P(p.PercentBilled)}; contribution {P(p.ContributionPercent)}.",
        KpiBudgetBurnRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: {N(p.EngHours)} engineering hours / {N(p.EngBudget)} budget; "
            + $"used {P(p.PercentUsed)}; remaining {N(p.RemainingHours)} hours.",
        KpiDeliveryRiskRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: delivery risk {p.DeliveryRisk}; budget {p.BudgetStatus}; "
            + $"engineering used {P(p.PercentEngUsed)}; remaining {N(p.RemainingHours)} hours.",
        KpiUtilizationRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}: {N(p.BillableHours)} billable / {N(p.TotalHours)} recorded hours; "
            + $"nonbillable {N(p.NonBillableHours)} hours; utilization {P(p.UtilizationPct)}.",
        TrendPayerRow p => $"{p.Wbs1} {p.ProjectName}, PM {p.Pm}, payer {p.PayerName}: amount {Money(p.Amount)}; "
            + $"earned {Money(p.RevenueAmount)}; invoiced {Money(p.BilledAmount)}; AR {Money(p.ArOutstandingAmount)}.",
        _ => throw new ArgumentException("Unsupported Executive Summary drilldown row.", nameof(row)),
    };

    private static string Money(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string P(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
    private static string D(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";

    public static IReadOnlyList<MorningFact> EmployeeWeekFacts(IReadOnlyList<EmployeeWeeklyHours> rows, DateTime throughWeek)
    {
        // Include the current Sunday week: it can have records and still be partial.
        var counts = rows.Where(row => row.WeekStart.Date <= throughWeek.Date).GroupBy(row => row.WeekStart.Date)
            .OrderBy(group => group.Key)
            .Select(group => (Week: group.Key, People: group.Select(row => row.EmployeeId).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .ToArray();
        var weeks = counts.Select((week, index) =>
        {
            var prior = counts.Take(index).TakeLast(8).Select(p => p.People).OrderBy(n => n).ToArray();
            double? median = prior.Length == 0 ? null : (prior[(prior.Length - 1) / 2] + prior[prior.Length / 2]) / 2.0;
            return (week.Week, week.People, Median: median, Complete: median.HasValue && week.People >= median.Value);
        }).ToArray();
        var partial = weeks.Where(week => week.Median.HasValue && !week.Complete).ToArray();
        var partialLine = string.Join("; ", partial.Select(week => $"partial: week {week.Week:yyyy-MM-dd}, {week.People} of {week.Median!.Value.ToString("0.#", CultureInfo.InvariantCulture)} people"));
        var notes = partial.Length == 0 ? new List<string>() : new List<string> { partialLine };
        notes.AddRange(weeks.Where(week => !week.Median.HasValue)
            .Select(week => $"Week {week.Week:yyyy-MM-dd}: {week.People} people; completeness unknown without a prior-week baseline."));
        var lastComplete = weeks.Where(week => week.Complete).Select(week => (DateTime?)week.Week).LastOrDefault();
        if (lastComplete is { } selected)
            notes.Add($"People outliers use last complete week {selected:yyyy-MM-dd}.");
        var facts = new List<MorningFact>
        {
            new("Partial timesheet weeks (distinct people; median of up to eight prior weeks; ties complete)",
                notes, partial.Length, weeks.Length,
                lastComplete.HasValue ? null : "No complete week with a prior-week baseline is available.")
            { Situations = partial.Length == 0 ? Array.Empty<MorningSituation>() : new[]
                {
                    new MorningSituation("partial timesheets", "Timesheets are incomplete for recent weeks; utilization and billing are understated.",
                        partialLine, Array.Empty<string>()),
                } },
        };
        if (lastComplete is { } last)
            facts.AddRange(EmployeeFacts(rows, last));
        else
            facts.Add(new MorningFact("People, last complete week", Array.Empty<string>(), 0, null,
                "No complete week with a prior-week baseline is available."));
        return facts;
    }

    public static IReadOnlyList<MorningFact> EmployeeFacts(IReadOnlyList<EmployeeWeeklyHours> rows, DateTime week)
    {
        var last = rows.Where(r => r.WeekStart.Date == week.Date).ToArray();
        if (last.Length == 0) return new[] { new MorningFact($"People week {week:yyyy-MM-dd}", Array.Empty<string>(), 0, null, "No employee hours returned for the selected week.") };
        var measured = last.Where(r => r.TotalHrs > 0).ToArray();
        var outside = measured.Where(r => r.BillableHrs / r.TotalHrs < .5 || r.BillableHrs / r.TotalHrs > 1.1).ToArray();
        return new[] { new MorningFact($"People under 50% or over 110%, last complete week {week:yyyy-MM-dd} ({outside.Length} of {measured.Length} measured people; {measured.Length} of {last.Length} have positive hours)",
            new[] { "Utilization means billable / recorded hours, as in the existing employee tool; it is not workload / contracted capacity. Missing timesheets and contracted hours are not supplied." }
                .Concat(outside.Select(r => $"{r.EmployeeName} ({r.EmployeeId}): {N(r.BillableHrs / r.TotalHrs * 100)}% = {N(r.BillableHrs)} / {N(r.TotalHrs)} hours.")).ToArray(), outside.Length, outside.Length,
            measured.Length == last.Length ? null : "Some people have no positive recorded hours; their utilization is unknown.") };
    }

    private static async Task CardsAsync(List<MorningFact> facts, string role, CancellationToken ct) =>
        await ReadAsync(facts, "Accepted " + role + " cards", async () =>
        {
            var cards = (await new SqlBdDoctrineStore(Connection(BdConnection)).ListCardsAsync(role, ct)).Where(c => c.Role == role && c.Status == "accepted").ToArray();
            return new[] { new MorningFact("Accepted " + role + " cards", cards.Select(c => $"#{c.Id} {c.Title}").ToArray(), cards.Length, cards.Length)
            { AcceptedCards = cards } };
        }, ct);

    private static async Task GatherItAsync(List<MorningFact> facts, DateTimeOffset now, CancellationToken ct)
    {
        // SQL adapted from NetworkOpsStore.OpenFindingsOfRulesAsync and ChangesSinceAsync.
        // All active devices and all rules; no token, service reference or service endpoint.
        await ReadAsync(facts, "NetworkOps open findings", async () =>
        {
            var rows = await NetworkRowsAsync("""
                SELECT d.Name, f.Severity, COUNT(*) AS Findings,
                       STRING_AGG(CONVERT(nvarchar(max), CONCAT(f.RuleKey, N': ', f.Title)), N'; ')
                FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
                WHERE f.ClearedUtc IS NULL AND d.RetiredUtc IS NULL
                GROUP BY d.DeviceId, d.Name, f.Severity ORDER BY f.Severity DESC, d.Name;
                """, now, r => FindingSituation(r.GetString(0), r.GetByte(1), r.GetInt32(2), r.GetString(3)), ct);
            return new[] { new MorningFact($"Open findings grouped by severity and device (groups); connection variable {rows.ConnectionVariable}", rows.Lines.Select(s => s.Printed).ToArray(), rows.Lines.Count, rows.Total,
                SectionScope: "NetworkOps SELECT snapshots; connection variable " + rows.ConnectionVariable)
            { Situations = rows.Lines.Take(10).ToArray() } };
        }, ct);
        await ReadAsync(facts, "NetworkOps finding changes in the last 24 hours", async () =>
        {
            var rows = await NetworkRowsAsync("""
                SELECT TOP (1000) d.Name, f.RuleKey, f.Severity, f.Title, f.FirstSeenUtc, f.ClearedUtc, COUNT(*) OVER() AS TotalRows
                FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
                WHERE d.RetiredUtc IS NULL
                  AND ((f.FirstSeenUtc >= @since AND f.FirstSeenUtc <= @until) OR (f.ClearedUtc >= @since AND f.ClearedUtc <= @until))
                ORDER BY f.Severity DESC, d.Name, f.RuleKey, f.FindingId;
                """, now, r => $"{r.GetString(0)} / {Severity(r.GetByte(2))} / {r.GetString(1)}: {r.GetString(3)}; first seen {r.GetDateTime(4):O} UTC; cleared {(r.IsDBNull(5) ? "no" : r.GetDateTime(5).ToString("O", CultureInfo.InvariantCulture) + " UTC")}", ct, counted: true);
            return new[] { new MorningFact($"Finding changes [{now.AddHours(-24):O}, {now:O}] (a row may have opened AND cleared); connection variable {rows.ConnectionVariable}", rows.Lines, rows.Lines.Count, rows.Total) };
        }, ct);
        await ReadAsync(facts, "NetworkOps actions in the last 24 hours", async () =>
        {
            var rows = await NetworkRowsAsync("""
                SELECT TOP (1000) d.Name, a.ActionId, a.Kind, a.RequestedBy, a.RequestedUtc, a.Status, a.Detail, COUNT(*) OVER() AS TotalRows
                FROM NetworkOps.Actions a JOIN NetworkOps.Devices d ON d.DeviceId = a.DeviceId
                WHERE d.RetiredUtc IS NULL AND a.RequestedUtc >= @since AND a.RequestedUtc <= @until ORDER BY a.ActionId;
                """, now, r => $"{r.GetString(0)} action #{r.GetInt64(1)} {r.GetString(2)}, requested by {r.GetString(3)} at {r.GetDateTime(4):O} UTC: {r.GetString(5)}; {(r.IsDBNull(6) ? "" : r.GetString(6))}", ct, counted: true);
            return new[] { new MorningFact($"Actions requested [{now.AddHours(-24):O}, {now:O}]; connection variable {rows.ConnectionVariable}", rows.Lines, rows.Lines.Count, rows.Total) };
        }, ct);
        await CardsAsync(facts, "IT", ct);
    }

    public static MorningSituation FindingSituation(string device, byte severity, int count, string titles) => new("IT finding",
        MorningKnowledge.WithoutIdentifiers(titles, device),
        $"{Severity(severity)} / {device}: {count} open findings: {titles}", Array.Empty<string>());

    private static async Task<(IReadOnlyList<T> Lines, int Total, string ConnectionVariable)> NetworkRowsAsync<T>(string sql, DateTimeOffset now,
        Func<SqlDataReader, T> format, CancellationToken ct, bool counted = false)
    {
        var watchConnection = OptionalConnection("KOR_NETWORKOPS_WATCHDB");
        var connection = watchConnection ?? Connection("KOR_NETWORKOPS_DB");
        var connectionVariable = watchConnection is not null ? "KOR_NETWORKOPS_WATCHDB" : "KOR_NETWORKOPS_DB";
        await using var con = new SqlConnection(new SqlConnectionStringBuilder(connection) { ConnectTimeout = 20 }.ConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = now.AddHours(-24).UtcDateTime;
        cmd.Parameters.Add("@until", SqlDbType.DateTime2).Value = now.UtcDateTime;
        var lines = new List<T>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lines.Add(format(reader));
            if (counted) total = reader.GetInt32(reader.FieldCount - 1);
        }
        return (lines, counted ? total : lines.Count, connectionVariable);
    }

    private static string Severity(byte value) => value switch { 2 => "Critical", 1 => "Warning", 0 => "Info", _ => "Unknown " + value };
    private static string N(double value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string? OptionalConnection(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
        ?? Environment.GetEnvironmentVariable(name);
    private static string Connection(string name) => OptionalConnection(name)
        ?? throw new InvalidOperationException("Source environment variable is unset.");

    private static async Task<SqlConnection> OpenAsync(string name, CancellationToken ct)
    {
        var con = new SqlConnection(new SqlConnectionStringBuilder(Connection(name)) { ConnectTimeout = 20 }.ConnectionString);
        try { await con.OpenAsync(ct); return con; }
        catch { await con.DisposeAsync(); throw; }
    }

    private static async Task ReadAsync(List<MorningFact> facts, string source, Func<Task<IReadOnlyList<MorningFact>>> read, CancellationToken ct)
    {
        try { facts.AddRange(await read()); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            facts.Add(new MorningFact(source, Array.Empty<string>(), 0, null,
                $"Read failed ({ex.GetType().Name}); check the source and its configuration. No figures inferred."));
        }
    }

    // Keep the option defaults identical to Kor.Operations.Mcp/Program.cs; no credentials are persisted.
    private static (DeltekOdbcOptions, FinancialsOptions) DeltekOptions(IConfiguration configuration)
    {
        var d = configuration.GetSection("DeltekOdbc");
        var f = configuration.GetSection("Financials");
        return (new DeltekOdbcOptions { Dsn = d["Dsn"] ?? "Deltek", User = d["User"] ?? "", Password = d["Password"] ?? "", Catalog = d["Catalog"] ?? "" },
            new FinancialsOptions
            {
                BilledRevenueAccounts = f["BilledRevenueAccounts"] ?? "",
                BilledExpenseAccountRanges = f["BilledExpenseAccountRanges"] ?? "",
                BilledOtherIncomeAccountRanges = f["BilledOtherIncomeAccountRanges"] ?? "",
                BilledExpenseAccountExcludes = f["BilledExpenseAccountExcludes"] ?? "",
                BilledExpenseAccountIncludes = f["BilledExpenseAccountIncludes"] ?? "",
                BilledOtherIncomeAccountExcludes = f["BilledOtherIncomeAccountExcludes"] ?? "",
                BilledOtherIncomeAccountIncludes = f["BilledOtherIncomeAccountIncludes"] ?? "",
                BilledUsdToCadRate = f["BilledUsdToCadRate"] ?? "",
                UsdToCadRateByYear = f["UsdToCadRateByYear"] ?? "",
                BilledDefaultOrg = f["BilledDefaultOrg"] ?? "",
                PnLGlFlipSign = f["PnLGlFlipSign"] ?? "",
                PnLGlTableNameLike = f["PnLGlTableNameLike"] ?? "",
                PnLIncomeGroupTypes = f["PnLIncomeGroupTypes"] ?? "",
                PnLExpenseGroupTypes = f["PnLExpenseGroupTypes"] ?? "",
                CashAccountWhitelist = f["CashAccountWhitelist"] ?? "",
                CashUsdAccounts = f["CashUsdAccounts"] ?? "",
                CashUsdToCadRate = f["CashUsdToCadRate"] ?? "",
                PnLOverheadRate = f["PnLOverheadRate"] ?? "",
                FiscalYearStartMonth = f["FiscalYearStartMonth"] ?? "",
            });
    }
}
