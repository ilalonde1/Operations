using System.Data;
using System.Globalization;
using Kor.Operations.App.Options;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Opportunities.Data.Crm;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kor.MorningBrief;

public sealed record MorningFact(string Name, IReadOnlyList<string> Lines, int Shown, int? Total, string? CouldNotRead = null);

/// <summary>Read-only snapshots. A failed source stays visible, and does not suppress another source.</summary>
public sealed record MorningFacts(DateTimeOffset CapturedAtUtc, IReadOnlyList<MorningFact> Bd,
    IReadOnlyList<MorningFact> Coo, IReadOnlyList<MorningFact> It)
{
    private const string BdConnection = "KOR_OPPORTUNITIES_OPPORTUNITIESDB";
    private const string NetworkConnection = "KOR_NETWORKOPS_WATCHDB";

    public static async Task<MorningFacts> GatherAsync(IConfiguration configuration, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        var bd = new List<MorningFact>();
        var coo = new List<MorningFact>();
        var it = new List<MorningFact>();
        var (odbc, financials) = DeltekOptions(configuration);
        await GatherBdAsync(bd, date, now, ct);
        await GatherCooAsync(coo, odbc, financials, date, ct);
        await GatherItAsync(it, now, ct);
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
                       COALESCE(o.Name, e.PotentialProjects, N'(unnamed)'), e.ProposedFee, e.OpenedAtUtc,
                       la.LastActivityUtc, w.LastTouchUtc
                FROM opportunities.CrmEngagements e
                LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
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
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4), touches.Max()));
            }
            return PursuitFacts(rows, now);
        }, ct);
        await ReadAsync(facts, "Open and late actions by owner", async () =>
        {
            var rows = (await new SqlBdRhythmStore(Connection(BdConnection)).ListActionsAsync(ct)).Where(a => a.IsOpen).ToArray();
            var groups = rows.GroupBy(a => a.OwnerKey).OrderBy(g => g.Key, StringComparer.Ordinal);
            var lines = groups.Select(g => $"{g.First().OwnerName}: {g.Count()} of {rows.Length} open; {g.Count(a => a.DueUtc < now)} of {g.Count()} late. "
                + string.Join("; ", g.OrderBy(a => a.DueUtc).Select(a => $"#{a.Id} {a.Action} (due {a.DueUtc:O})"))).ToArray();
            return new[] { new MorningFact("Open actions by owner (late means due before snapshot)", lines, rows.Length, rows.Length) };
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

    public sealed record Pursuit(long Id, string Owner, string Region, string Title, decimal? Value, DateTimeOffset LastTouch);

    public static IReadOnlyList<MorningFact> PursuitFacts(IReadOnlyList<Pursuit> rows, DateTimeOffset now)
    {
        var groups = rows.GroupBy(r => (r.Owner, r.Region)).OrderBy(g => g.Key.Owner, StringComparer.Ordinal).ThenBy(g => g.Key.Region, StringComparer.Ordinal);
        var lines = groups.Select(g => $"{g.Key.Owner} / {g.Key.Region}: {g.Count()} of {rows.Count} live pursuits; "
            + $"{g.Count(r => r.Value.HasValue)} of {g.Count()} have proposed fees. "
            + string.Join("; ", g.Select(Describe))).ToArray();
        var stale = rows.Where(r => r.LastTouch <= now.AddDays(-14)).OrderByDescending(r => r.Value.HasValue)
            .ThenByDescending(r => r.Value).ThenBy(r => r.Id).ToArray();
        return new[]
        {
            new MorningFact("Live pursuits by owner and region (value is proposed fee; currency not supplied)", lines, rows.Count, rows.Count),
            new MorningFact("No touch in 14+ days, top 10 by proposed fee (unknown fees last)", stale.Take(10).Select(Describe).ToArray(), Math.Min(10, stale.Length), stale.Length),
        };
    }

    private static string Describe(Pursuit p) => $"Pursuit #{p.Id}: {p.Title}; {p.Owner} / {p.Region}; proposed fee "
        + (p.Value?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown") + $"; last touch/opened floor {p.LastTouch:O}";

    private static async Task GatherCooAsync(List<MorningFact> facts, DeltekOdbcOptions odbc, FinancialsOptions financials, DateOnly date, CancellationToken ct)
    {
        await ReadAsync(facts, "Deltek AR aging and overdue invoices", async () =>
        {
            var ar = await new ArFinancialsService(odbc, financials).LoadAsync(ct);
            var overdue = ar.InvoiceRows.Where(r => r.DaysPastDue > 0 && r.Balance > 0).OrderByDescending(r => r.Balance).ToArray();
            return new[]
            {
                new MorningFact("AR aging, CAD equivalent (all project rows)", new[] {
                    $"Outstanding {N(ar.FirmwideOutstandingCadEquiv)}; current {N(ar.ProjectRows.Sum(r => r.Current))}; 31–60 {N(ar.ProjectRows.Sum(r => r.Aged31To60))}; "
                    + $"61–90 {N(ar.ProjectRows.Sum(r => r.Aged61To90))}; 90+ {N(ar.ProjectRows.Sum(r => r.Aged90Plus))}; USD/CAD {N(ar.UsdToCadRate)}."
                }, ar.ProjectRows.Count, ar.ProjectRows.Count),
                new MorningFact("Largest overdue invoices, CAD equivalent", overdue.Take(10).Select(r =>
                    $"{r.Wbs1} {r.ProjectName}, invoice {r.Invoice}, client {r.ClientName}, PM {r.Pm}: {N(r.Balance)}, {r.DaysPastDue} days past due (due {r.DueDate:yyyy-MM-dd}).").ToArray(), Math.Min(10, overdue.Length), overdue.Length),
            };
        }, ct);
        await ReadAsync(facts, "Deltek unbilled WIP", async () =>
        {
            var wip = await new WipFinancialsService(odbc, financials).LoadAsync(ct, failOnReadError: true);
            if (!wip.DataLoaded || !wip.RevenueGenerationDetected || wip.WipUnbilledPeriod == "n/a")
                return new[] { new MorningFact("Unbilled WIP", Array.Empty<string>(), 0, null, "No usable revenue-generation/WIP signal; service zeros are not a measured balance.") };
            var rows = wip.WipProjectRows.Where(r => r.Earned > 0).OrderByDescending(r => r.Earned).ToArray();
            return new[]
            {
                new MorningFact("Firm unbilled WIP, CAD equivalent", new[] { $"Period {wip.WipUnbilledPeriod}: unbilled {N(wip.FirmWipUnbilled)}, overbilled {N(wip.FirmWipOverbilled)}, net {N(wip.FirmWipNet)}." }, 1, 1),
                new MorningFact("Top unbilled WIP projects, CAD equivalent", rows.Take(10).Select(r => $"{r.Wbs1} {r.ProjectName}, client {r.ClientName}, PM {r.Pm}: {N(r.Earned)}.").ToArray(), Math.Min(10, rows.Length), rows.Length),
            };
        }, ct);
        // EmployeeAnalyticsService uses Sunday-start weeks. Use the last four completed matching weeks.
        var thisWeek = date.ToDateTime(TimeOnly.MinValue).AddDays(-(int)date.DayOfWeek);
        for (var back = 4; back >= 1; back--)
        {
            var from = thisWeek.AddDays(-7 * back);
            await ReadAsync(facts, $"Firm utilization week {from:yyyy-MM-dd}", async () =>
            {
                var stats = await Task.Run(() => new FirmAnalyticsService(odbc).LoadFirmUtilizationSync(from, from.AddDays(7), ct), ct);
                return new[] { stats.TotalHrs == 0
                    ? new MorningFact($"Firm utilization week {from:yyyy-MM-dd}", Array.Empty<string>(), 0, 1, "No recorded hours for this week.")
                    : new MorningFact($"Firm utilization week {from:yyyy-MM-dd}", new[] { $"{N(stats.BillablePct * 100)}% = {N(stats.BillableHrs)} billable / {N(stats.TotalHrs)} recorded hours (all staff)." }, 1, 1) };
            }, ct);
        }
        await ReadAsync(facts, "People under 50% or over 110% last week", async () =>
        {
            var rows = await Task.Run(() => new EmployeeAnalyticsService(odbc).LoadEmployeeWeeklyUtilizationSync(ct), ct);
            return EmployeeFacts(rows, thisWeek.AddDays(-7));
        }, ct);
        await ReadAsync(facts, "Projects most over budget", async () =>
        {
            var rows = await Task.Run(() => new ProjectAnalyticsService(odbc, financials).LoadProjectRowsSync(ct), ct);
            var threshold = AnalyticsThresholds.OverBudgetFactor;
            var atRisk = rows.Where(r => r.EstEngBudget > 0 && r.EngHrs > r.EstEngBudget * threshold).OrderByDescending(r => r.EngHrs / r.EstEngBudget).ToArray();
            return new[] { new MorningFact($"Most over budget, engineering-hours ratio > {N(threshold)} ({atRisk.Length} of {rows.Count} projects qualify)",
                atRisk.Take(10).Select(r => $"{r.Wbs1} {r.Name}, PM {r.Pm}, DM {r.DraftingManager}: {N(r.EngHrs)} hours / {N(r.EstEngBudget)} estimated budget = {N(r.EngHrs / r.EstEngBudget)}; overage {N(r.EngHrs - r.EstEngBudget)} hours.").ToArray(),
                Math.Min(10, atRisk.Length), atRisk.Length) };
        }, ct);
        await CardsAsync(facts, "Operations", ct);
    }

    public static IReadOnlyList<MorningFact> EmployeeFacts(IReadOnlyList<EmployeeWeeklyHours> rows, DateTime week)
    {
        var last = rows.Where(r => r.WeekStart.Date == week.Date).ToArray();
        if (last.Length == 0) return new[] { new MorningFact($"People week {week:yyyy-MM-dd}", Array.Empty<string>(), 0, null, "No employee hours returned for last week; no older week was substituted.") };
        var measured = last.Where(r => r.TotalHrs > 0).ToArray();
        var outside = measured.Where(r => r.BillableHrs / r.TotalHrs < .5 || r.BillableHrs / r.TotalHrs > 1.1).ToArray();
        return new[] { new MorningFact($"People under 50% or over 110%, week {week:yyyy-MM-dd} ({outside.Length} of {measured.Length} measured people; {measured.Length} of {last.Length} have positive hours)",
            new[] { "Utilization means billable / recorded hours, as in the existing employee tool; it is not workload / contracted capacity. Missing timesheets and contracted hours are not supplied." }
                .Concat(outside.Select(r => $"{r.EmployeeName} ({r.EmployeeId}): {N(r.BillableHrs / r.TotalHrs * 100)}% = {N(r.BillableHrs)} / {N(r.TotalHrs)} hours.")).ToArray(), outside.Length, outside.Length,
            measured.Length == last.Length ? null : "Some people have no positive recorded hours; their utilization is unknown.") };
    }

    private static async Task CardsAsync(List<MorningFact> facts, string role, CancellationToken ct) =>
        await ReadAsync(facts, "Accepted " + role + " cards", async () =>
        {
            var cards = (await new SqlBdDoctrineStore(Connection(BdConnection)).ListCardsAsync(role, ct)).Where(c => c.Role == role && c.Status == "accepted").ToArray();
            return new[] { new MorningFact("Accepted " + role + " cards", cards.Select(c => $"#{c.Id} {c.Title}").ToArray(), cards.Length, cards.Length) };
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
                """, now, r => $"{Severity(r.GetByte(1))} / {r.GetString(0)}: {r.GetInt32(2)} open findings: {r.GetString(3)}", ct);
            return new[] { new MorningFact("Open findings grouped by severity and device (groups)", rows.Lines, rows.Lines.Count, rows.Total) };
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
            return new[] { new MorningFact($"Finding changes [{now.AddHours(-24):O}, {now:O}] (a row may have opened AND cleared)", rows.Lines, rows.Lines.Count, rows.Total) };
        }, ct);
        await ReadAsync(facts, "NetworkOps actions in the last 24 hours", async () =>
        {
            var rows = await NetworkRowsAsync("""
                SELECT TOP (1000) d.Name, a.ActionId, a.Kind, a.RequestedBy, a.RequestedUtc, a.Status, a.Detail, COUNT(*) OVER() AS TotalRows
                FROM NetworkOps.Actions a JOIN NetworkOps.Devices d ON d.DeviceId = a.DeviceId
                WHERE d.RetiredUtc IS NULL AND a.RequestedUtc >= @since AND a.RequestedUtc <= @until ORDER BY a.ActionId;
                """, now, r => $"{r.GetString(0)} action #{r.GetInt64(1)} {r.GetString(2)}, requested by {r.GetString(3)} at {r.GetDateTime(4):O} UTC: {r.GetString(5)}; {(r.IsDBNull(6) ? "" : r.GetString(6))}", ct, counted: true);
            return new[] { new MorningFact($"Actions requested [{now.AddHours(-24):O}, {now:O}]", rows.Lines, rows.Lines.Count, rows.Total) };
        }, ct);
    }

    private static async Task<(IReadOnlyList<string> Lines, int Total)> NetworkRowsAsync(string sql, DateTimeOffset now,
        Func<SqlDataReader, string> format, CancellationToken ct, bool counted = false)
    {
        await using var con = await OpenAsync(NetworkConnection, ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = now.AddHours(-24).UtcDateTime;
        cmd.Parameters.Add("@until", SqlDbType.DateTime2).Value = now.UtcDateTime;
        var lines = new List<string>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lines.Add(format(reader));
            if (counted) total = reader.GetInt32(reader.FieldCount - 1);
        }
        return (lines, counted ? total : lines.Count);
    }

    private static string Severity(byte value) => value switch { 2 => "Critical", 1 => "Warning", 0 => "Info", _ => "Unknown " + value };
    private static string N(double value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string Connection(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
        ?? Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Source environment variable is unset.");

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
