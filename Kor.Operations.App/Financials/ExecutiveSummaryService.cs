#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Serilog;
namespace Kor.Operations.Financials
{
    public sealed class ExecutiveSummaryService
    {
        private ExecutiveSummaryResult? _cache;
        private DateTimeOffset _cacheAt;
        private readonly object _cacheLock = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly FinancialsService _financials;
        private readonly SqlFinancialPortfolioSnapshotStore _portfolioStore;
        private readonly ExecutiveSummaryDeltekLoader _deltek;

        public ExecutiveSummaryService(
            FinancialsService financials,
            SqlFinancialPortfolioSnapshotStore portfolioStore,
            ExecutiveSummaryDeltekLoader deltek)
        {
            _financials = financials ?? throw new ArgumentNullException(nameof(financials));
            _portfolioStore = portfolioStore ?? throw new ArgumentNullException(nameof(portfolioStore));
            _deltek = deltek ?? throw new ArgumentNullException(nameof(deltek));
        }

        public async Task<ExecutiveSummaryResult> GetExecutiveSummaryAsync(
            bool forceRefresh,
            FinancialsSnapshot? existingSnapshot,
            PortfolioTrendPoint[]? existingTrend,
            UtilizationRow[]? existingUtilRows,
            CancellationToken ct)
        {
            lock (_cacheLock)
            {
                if (!forceRefresh && _cache != null && (DateTimeOffset.Now - _cacheAt) <= CacheTtl)
                    return _cache;
            }

            FinancialsSnapshot? snap = existingSnapshot;
            PortfolioTrendPoint[]? trend = existingTrend;
            UtilizationRow[]? util = existingUtilRows;

            // Fetch what's missing (best-effort; each group is isolated).
            var tasks = new List<Task>();

            if (snap == null)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var s = await _financials.GetSnapshotAsync(forceRefresh, ct).ConfigureAwait(false);
                        snap = s;
                        util = s?.Rows?.Select(UtilizationRow.FromProject).ToArray();
                    }
                    catch (Exception ex)
                    {
                        Log.ForContext<ExecutiveSummaryService>().Warning(ex, "{Context} failed. {ErrorType}: {ErrorMessage}", "Snapshot load failed", ex.GetType().Name, ex.Message);
                    }
                }, ct));
            }

            if (trend == null)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Best-effort: if the portfolio store is unavailable, we still render everything else.
                        var start = DateTime.Now.Date.AddDays(-7 * 12);
                        var rows = await _portfolioStore.LoadSnapshotsAsync(start, ct).ConfigureAwait(false);
                        trend = rows
                            .Select(r => new PortfolioTrendPoint(r.SnapshotDate, r.HealthyCount, r.WatchCount, r.CriticalCount, r.TotalProjects))
                            .ToArray();
                    }
                    catch (Exception ex)
                    {
                        Log.ForContext<ExecutiveSummaryService>().Warning(ex, "{Context} failed. {ErrorType}: {ErrorMessage}", "Portfolio trend load failed", ex.GetType().Name, ex.Message);
                    }
                }, ct));
            }
            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);

            ExecutiveSummaryDeltekData? deltek = null;
            try
            {
                if (snap != null)
                    deltek = await _deltek.TryLoadAsync(snap.Rows.Select(r => r.Wbs1), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.ForContext<ExecutiveSummaryService>().Warning(ex, "{Context} failed. {ErrorType}: {ErrorMessage}", "Deltek supplement load failed", ex.GetType().Name, ex.Message);
            }

            var result = Build(snap, trend, util, deltek);

            lock (_cacheLock)
            {
                _cache = result;
                _cacheAt = DateTimeOffset.Now;
            }

            return result;
        }

        private static ExecutiveSummaryResult Build(
            FinancialsSnapshot? snap,
            PortfolioTrendPoint[]? trend,
            UtilizationRow[]? util,
            ExecutiveSummaryDeltekData? deltek)
        {
            var now = DateTimeOffset.Now;
            var kpis = new List<ExecutiveKpi>();
            var trends = new List<ExecutiveTrend>();
            var alerts = new List<ExecutiveAlert>();

            // Helper: safe KPI creation with per-card isolation.
            ExecutiveKpi SafeKpi(string title, Func<ExecutiveKpi> compute, string source)
            {
                try { return compute(); }
                catch (Exception ex)
                {
                    Log.ForContext<ExecutiveSummaryService>().Warning(ex, "{Context} failed. {ErrorType}: {ErrorMessage}", "KPI computation", ex.GetType().Name, ex.Message);
                    return ExecutiveKpi.DataUnavailable(title, source);
                }
            }

            // Helper: safe Trend creation.
            ExecutiveTrend SafeTrend(string title, Func<ExecutiveTrend> compute, string source)
            {
                try { return compute(); }
                catch (Exception ex)
                {
                    Log.ForContext<ExecutiveSummaryService>().Warning(ex, "{Context} failed. {ErrorType}: {ErrorMessage}", "Trend computation", ex.GetType().Name, ex.Message);
                    return ExecutiveTrend.DataUnavailable(title, source);
                }
            }

            var headline = snap?.Headline;
            var rows = snap?.Rows ?? new List<FinancialsProjectRow>();
            util ??= rows.Select(UtilizationRow.FromProject).ToArray();
            var rowByWbs = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
                .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // KPIs (use only what we already compute today; placeholders for not-sourced items).
            kpis.Add(SafeKpi("Cash Position", () =>
{
    if (deltek == null) return ExecutiveKpi.DataUnavailable("Cash Position", "Deltek cash/GL dataset unavailable.");

    var breakdown = "CAD " + deltek.CashCad.ToString("C0") + " | " +
                    "USA " + deltek.CashUsa.ToString("C0") + " | " +
                    "BCC " + deltek.CashBcc.ToString("C0");

    return new ExecutiveKpi(
        "Cash Position",
        deltek.CashTotal.ToString("C0"),
        "Bank GL balances as of period " + (deltek.CashPeriod ?? "") + ": " + breakdown + ".",
        "",
        null,
        deltek.CashHistory
            .Select(h => new KpiCashHistoryRow(
                Period: h.Period,
                Total: h.Total,
                Cad: h.Cad,
                Usa: h.Usa,
                Bcc: h.Bcc))
            .ToList());
}, "Deltek/ODBC CFGBanks+GLSummary"));

            kpis.Add(SafeKpi("AR Outstanding", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("AR Outstanding", "Deltek AR dataset unavailable.");
                var rowByWbs = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
                    .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var arRows = deltek.ArProjectRows
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArOutstandingRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            Total: a.Total,
                            Current: a.Current,
                            Aged31To60: a.Aged31To60,
                            Aged61To90: a.Aged61To90,
                            Aged90Plus: a.Aged90Plus,
                            OldestInvoiceDate: a.OldestInvoiceDate);
                    })
                    .ToList();
                var arInvoiceRows = deltek.ArInvoiceRows
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArInvoiceRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            InvoiceDate: a.InvoiceDate,
                            DueDate: a.DueDate,
                            DaysPastDue: a.DaysPastDue,
                            Balance: a.Balance);
                    })
                    .ToList();

                return new ExecutiveKpi(
                    "AR Outstanding",
                    deltek.ArOutstanding.ToString("C0"),
                    "Sum of open invoice balances (AR.InvBalanceSourceCurrency) across watchlist projects.",
                    "",
                    null,
                    null,
                    arRows,
                    arInvoiceRows);
            }, "Deltek/ODBC AR"));
            kpis.Add(SafeKpi("AR > 60 Days", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("AR > 60 Days", "Deltek AR dataset unavailable.");
                var rowByWbs = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
                    .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var ar60Rows = deltek.ArProjectRows
                    .Where(a => (a.Aged61To90 + a.Aged90Plus) > 0.004)
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArOutstandingRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            Total: a.Aged61To90 + a.Aged90Plus,
                            Current: 0.0,
                            Aged31To60: 0.0,
                            Aged61To90: a.Aged61To90,
                            Aged90Plus: a.Aged90Plus,
                            OldestInvoiceDate: a.OldestInvoiceDate);
                    })
                    .ToList();

                var ar60InvoiceRows = deltek.ArInvoiceRows
                    .Where(a => a.DaysPastDue > 60 && a.Balance > 0.004)
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArInvoiceRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            InvoiceDate: a.InvoiceDate,
                            DueDate: a.DueDate,
                            DaysPastDue: a.DaysPastDue,
                            Balance: a.Balance);
                    })
                    .ToList();

                return new ExecutiveKpi(
                    "AR > 60 Days",
                    deltek.ArOver60.ToString("C0"),
                    "Open AR past-due > 60 days (DueDate; falls back to InvoiceDate).",
                    "",
                    null,
                    null,
                    ar60Rows,
                    ar60InvoiceRows);
            }, "Deltek/ODBC AR"));

            // Hide the "WIP (Unbilled Earned)" card entirely when Deltek's Revenue Generation
            // feature is confirmed off — under that config WIP = Revenue - Billed is structurally
            // meaningless, and a permanently-DataUnavailable card is clutter. Loader failures
            // (WipDataLoaded == false) and missing dataset (deltek == null) still surface the
            // card so the user can see actionable error states. Drift back to "on" automatically
            // re-shows the card.
            var hideUnbilledEarned = deltek != null && deltek.WipDataLoaded && !deltek.RevenueGenerationDetected;
            if (!hideUnbilledEarned)
            {
                                    kpis.Add(SafeKpi("WIP (Unbilled Earned)", () =>
            {
                if (deltek == null)
                    return ExecutiveKpi.DataUnavailable("WIP (Unbilled Earned)", "Deltek PRSummaryMain dataset unavailable.");
                if (!deltek.WipDataLoaded)
                    return ExecutiveKpi.DataUnavailable(
                        "WIP (Unbilled Earned)",
                        "WIP data unavailable — check Deltek connection.");
                if (!deltek.RevenueGenerationDetected)
                    return ExecutiveKpi.DataUnavailable(
                        "WIP (Unbilled Earned)",
                        "Revenue Generation disabled in Deltek — WIP cannot be computed (KOR config).");
                var rowByWbs = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
                    .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var period = string.IsNullOrWhiteSpace(deltek.WipUnbilledPeriod) ? "n/a" : deltek.WipUnbilledPeriod;
                var wipRows = deltek.WipProjectRows
                    .Select(w =>
                    {
                        rowByWbs.TryGetValue((w.Wbs1 ?? string.Empty).Trim(), out var proj);
                        var fee = proj?.TotalFee ?? 0.0;
                        var pctFee = fee > 0.0 ? (w.Net / fee) : 0.0;
                        return new KpiWipUnbilledRow(
                            Wbs1: w.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            Earned: w.Earned,
                            Overbilled: w.Overbilled,
                            Net: w.Net,
                            NetAsPercentOfFee: pctFee,
                            Period: w.Period);
                    })
                    .ToList();
                if (wipRows.Count == 0)
                {
                    wipRows.Add(new KpiWipUnbilledRow(
                        Wbs1: "Portfolio",
                        ProjectName: "No project-level WIP rows returned; showing portfolio totals",
                        Pm: "",
                        Earned: deltek.WipUnbilled,
                        Overbilled: deltek.WipOverbilled,
                        Net: deltek.WipUnbilledNet,
                        NetAsPercentOfFee: 0.0,
                        Period: period));
                }

                var sub =
                    "As of period " + period + " (latest closed)." + "\n" +
                    "Watchlist: earned " + deltek.WipUnbilled.ToString("C0") +
                    " | overbilled " + deltek.WipOverbilled.ToString("C0") +
                    " | net " + deltek.WipUnbilledNet.ToString("C0") + "." + "\n" +
                    "Firmwide: earned " + deltek.FirmWipUnbilled.ToString("C0") +
                    " | overbilled " + deltek.FirmWipOverbilled.ToString("C0") +
                    " | net " + deltek.FirmWipNet.ToString("C0") + "." + "\n" +
                    "Source: PRSummaryMain. Uses Unbilled column when populated, else proxy = cumulative (BilledFee else Revenue) — Billed.";

                return new ExecutiveKpi(
                    "WIP (Unbilled Earned)",
                    deltek.WipUnbilled.ToString("C0"),
                    sub,
                    "",
                    null,
                    null,
                    null,
                    null,
                    wipRows);
            }, "Deltek/ODBC PRSummaryMain"));
            }
kpis.Add(SafeKpi("WIP (Draft Invoices)", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("WIP (Draft Invoices)", "Deltek pre-invoice dataset unavailable.");
                return new ExecutiveKpi(
                    "WIP (Draft Invoices)",
                    deltek.WipPreInvoice.ToString("C0"),
                    "Draft invoices in pipeline (ARPreInvoice). If there are no open pre-invoices, this shows $0 (current state in this dataset).",
                    "");
            }, "Deltek/ODBC ARPreInvoice"));

            kpis.Add(SafeKpi("Backlog", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Backlog", "Deltek/ODBC snapshot unavailable.");
                var backlogRows = rows
                    .Select(r =>
                    {
                        var fee = r?.TotalFee ?? 0.0;
                        var billed = r?.FeeBilled ?? 0.0;
                        var backlog = fee - billed;
                        return new KpiBacklogRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            Fee: fee,
                            FeeBilled: billed,
                            Backlog: backlog,
                            PercentBilled: r?.PercentBilled ?? 0.0);
                    })
                    .Where(x => x.Backlog > 0.004)
                    .OrderByDescending(x => x.Backlog)
                    .ToList();

                return new ExecutiveKpi(
                    "Backlog",
                    $"{headline.TotalUnbilled:C0}",
                    "Fee remaining not yet billed (Total Fees - Total Fee Billed).",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    backlogRows);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Billings To Date", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Billings To Date", "Deltek/ODBC snapshot unavailable.");
                var billingsRows = rows
                    .Select(r =>
                    {
                        var billed = r?.FeeBilled ?? 0.0;
                        var fee = r?.TotalFee ?? 0.0;
                        var contribution = headline.TotalFeeBilled <= 0.0 ? 0.0 : (billed / headline.TotalFeeBilled);
                        return new KpiBillingsRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            FeeBilled: billed,
                            Fee: fee,
                            PercentBilled: r?.PercentBilled ?? 0.0,
                            ContributionPercent: contribution);
                    })
                    .Where(x => x.FeeBilled > 0.004)
                    .OrderByDescending(x => x.FeeBilled)
                    .ToList();

                return new ExecutiveKpi(
                    "Billings To Date",
                    $"{headline.TotalFeeBilled:C0}",
                    "Sum of Fee Billed across watchlist projects (no period filter).",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    billingsRows);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Budget Burn", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Budget Burn", "Deltek/ODBC snapshot unavailable.");
                var burnRows = rows
                    .Select(r =>
                    {
                        var engBudget = r?.EngBudget ?? 0.0;
                        var engHours = r?.EngHrs ?? 0.0;
                        var percentUsed = engBudget <= 0.0 ? 0.0 : (engHours / engBudget);
                        var remaining = engBudget - engHours;
                        return new KpiBudgetBurnRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            EngHours: engHours,
                            EngBudget: engBudget,
                            PercentUsed: percentUsed,
                            RemainingHours: remaining);
                    })
                    .Where(x => x.EngBudget > 0.004)
                    .OrderByDescending(x => x.PercentUsed)
                    .ToList();
                return new ExecutiveKpi(
                    "Budget Burn",
                    $"{headline.PercentHoursSpent:P1}",
                    "% Hours Spent across the portfolio vs calculated hours budget.",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    burnRows);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Portfolio Delivery Risk", () =>
            {
                if (util == null || util.Length == 0)
                    return ExecutiveKpi.DataUnavailable("Portfolio Delivery Risk", "Portfolio is empty or snapshot unavailable.");

                var critical = util.Count(u => u.ConfidenceLevel == DeliveryConfidenceLevel.Critical);
                var atRisk = util.Count(u => u.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk);
                var riskRows = util
                    .Where(u => u.ConfidenceLevel == DeliveryConfidenceLevel.Critical || u.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk)
                    .OrderByDescending(u => u.ConfidenceLevel == DeliveryConfidenceLevel.Critical ? 1 : 0)
                    .ThenBy(u => u.RemainingEngHours)
                    .Select(u => new KpiDeliveryRiskRow(
                        Wbs1: u.Wbs1,
                        ProjectName: u.ProjectName,
                        Pm: u.Pm,
                        DeliveryRisk: u.ConfidenceDisplay,
                        BudgetStatus: u.RiskStatus,
                        PercentEngUsed: u.PercentEngUsed,
                        RemainingHours: u.RemainingEngHours))
                    .ToList();
                return new ExecutiveKpi(
                    Title: "Portfolio Delivery Risk",
                    ValueText: $"{critical + atRisk:N0} projects",
                    SubText: "Count of projects rated Critical or At Risk by Delivery Risk.",
                    StatusMessage: "",
                    DeliveryRiskRows: riskRows);
            }, "local compute"));

            kpis.Add(SafeKpi("Projects Over Budget", () =>
            {
                if (util == null || util.Length == 0)
                    return ExecutiveKpi.DataUnavailable("Projects Over Budget", "Portfolio is empty or snapshot unavailable.");

                var over = util.Where(u => string.Equals(u.RiskStatus, "Over budget", StringComparison.OrdinalIgnoreCase)).ToList();
                var top3 = over
                    .OrderBy(u => u.RemainingEngHours)
                    .Take(3)
                    .Select(u => $"{u.Wbs1} ({u.RemainingEngHours:N1} hrs)")
                    .ToList();
                var projectRows = over
                    .OrderBy(u => u.RemainingEngHours)
                    .Select(u => new KpiProjectDrilldownRow(
                        Wbs1: u.Wbs1,
                        ProjectName: u.ProjectName,
                        Pm: u.Pm,
                        OverByHours: Math.Max(0.0, -u.RemainingEngHours),
                        PercentEngUsed: u.PercentEngUsed,
                        PercentBilled: u.PercentBilled))
                    .ToList();

                return new ExecutiveKpi(
                    "Projects Over Budget",
                    $"{over.Count:N0}",
                    over.Count > 0 ? $"Top: {string.Join(", ", top3)}" : "No projects currently over engineering budget.",
                    "",
                    projectRows);
            }, "local compute"));

            kpis.Add(SafeKpi("Utilization", () =>
{
    if (deltek == null) return ExecutiveKpi.DataUnavailable("Utilization", "Deltek timesheet dataset unavailable.");
    if (deltek.UtilizationTotalHours30 <= 0.0) return ExecutiveKpi.DataUnavailable("Utilization", "No timesheet hours found in the last 30 days.");

    var pct = deltek.UtilizationPct30;
    var sub = string.Format(CultureInfo.CurrentCulture, "Last 30 days (watchlist): {0:N1} billable hrs of {1:N1} total charged hrs.", deltek.UtilizationBillableHours30, deltek.UtilizationTotalHours30);
    var rowByWbs = rows
        .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
        .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    var utilRows = deltek.UtilizationProjectRows
        .Select(u =>
        {
            rowByWbs.TryGetValue((u.Wbs1 ?? string.Empty).Trim(), out var proj);
            var nonBillable = Math.Max(0.0, u.TotalHours - u.BillableHours);
            return new KpiUtilizationRow(
                Wbs1: u.Wbs1 ?? string.Empty,
                ProjectName: proj?.Name ?? string.Empty,
                Pm: proj?.Pm ?? string.Empty,
                BillableHours: u.BillableHours,
                NonBillableHours: nonBillable,
                TotalHours: u.TotalHours,
                UtilizationPct: u.UtilizationPct);
        })
        .OrderByDescending(u => u.UtilizationPct)
        .ThenByDescending(u => u.TotalHours)
        .ToList();

    return new ExecutiveKpi(
        Title: "Utilization",
        ValueText: pct.ToString("P1"),
        SubText: sub,
        StatusMessage: "",
        UtilizationRows: utilRows);
}, "Deltek/ODBC tkDetail"));

            // Trends (best-effort; placeholders for not-sourced items)
            trends.Add(SafeTrend("Revenue (Earned) (30/90 day)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("Revenue (Earned) (30/90 day)", "Deltek PRSummaryMain dataset unavailable.");
                var gap30 = deltek.Revenue30 - deltek.Billed30;
                var gap90 = deltek.Revenue90 - deltek.Billed90;
                var v = string.Format(
                    CultureInfo.CurrentCulture,
                    "30d Earned {0:C0} / Invoiced {1:C0} | 90d Earned {2:C0} / Invoiced {3:C0}",
                    deltek.Revenue30,
                    deltek.Billed30,
                    deltek.Revenue90,
                    deltek.Billed90);
                var billedByWbs = deltek.BilledPayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var arByWbs = deltek.ArPayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var payerRows = deltek.RevenuePayerRows
                    .Select(r =>
                    {
                        var key = (r.Wbs1 ?? string.Empty).Trim();
                        rowByWbs.TryGetValue(key, out var proj);
                        billedByWbs.TryGetValue(key, out var billed);
                        arByWbs.TryGetValue(key, out var ar);
                        return new TrendPayerRow(
                            Wbs1: r.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            PayerName: r.PayerName,
                            Amount: r.Amount,
                            RevenueAmount: r.Amount,
                            BilledAmount: billed,
                            ArOutstandingAmount: ar);
                    })
                    .OrderByDescending(r => r.Amount)
                    .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var topGap = payerRows
                    .Select(r => new { r.PayerName, Gap = r.RevenueAmount - r.BilledAmount })
                    .OrderByDescending(x => x.Gap)
                    .FirstOrDefault();
                var status = (Math.Abs(gap30) <= 0.004 && Math.Abs(gap90) <= 0.004)
                    ? "Unbilled gap is ~0 in both windows (earned and invoiced are aligned)."
                    : (topGap != null && Math.Abs(topGap.Gap) > 0.004)
                        ? string.Format(
                            CultureInfo.CurrentCulture,
                            "Unbilled gap: 30d {0:C0} | 90d {1:C0}. Top unbilled payer: {2} ({3:C0})",
                            gap30,
                            gap90,
                            topGap.PayerName,
                            topGap.Gap)
                        : string.Format(CultureInfo.CurrentCulture, "Unbilled gap: 30d {0:C0} | 90d {1:C0}", gap30, gap90);
                return new ExecutiveTrend("Revenue (Earned) (30/90 day)", v, status, deltek.RevenueSeries, payerRows);
            }, "Deltek/ODBC PRSummaryMain"));

            trends.Add(SafeTrend("Billings (Invoiced) (30/90 day)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("Billings (Invoiced) (30/90 day)", "Deltek PRSummaryMain dataset unavailable.");
                var arToBilled90 = deltek.Billed90 <= 0.004 ? 0.0 : (deltek.ArOutstanding / deltek.Billed90);
                var v = string.Format(
                    CultureInfo.CurrentCulture,
                    "30d Invoiced {0:C0} | 90d Invoiced {1:C0}",
                    deltek.Billed30,
                    deltek.Billed90);
                var revenueByWbs = deltek.RevenuePayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var arByWbs = deltek.ArPayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var payerRows = deltek.BilledPayerRows
                    .Select(r =>
                    {
                        var key = (r.Wbs1 ?? string.Empty).Trim();
                        rowByWbs.TryGetValue(key, out var proj);
                        revenueByWbs.TryGetValue(key, out var revenue);
                        arByWbs.TryGetValue(key, out var ar);
                        return new TrendPayerRow(
                            Wbs1: r.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            PayerName: r.PayerName,
                            Amount: r.Amount,
                            RevenueAmount: revenue,
                            BilledAmount: r.Amount,
                            ArOutstandingAmount: ar);
                    })
                    .OrderByDescending(r => r.Amount)
                    .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var topExposure = payerRows
                    .Where(r => r.BilledAmount > 0.004)
                    .Select(r => new { r.PayerName, Ratio = r.ArOutstandingAmount / r.BilledAmount, r.ArOutstandingAmount })
                    .OrderByDescending(x => x.Ratio)
                    .ThenByDescending(x => x.ArOutstandingAmount)
                    .FirstOrDefault();
                var status = topExposure == null
                    ? string.Format(CultureInfo.CurrentCulture, "Collection exposure: AR/90d billed {0:P1}", arToBilled90)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "Collection exposure: AR/90d billed {0:P1}. Top collection risk: {1} ({2:P1}, AR {3:C0})",
                        arToBilled90,
                        topExposure.PayerName,
                        topExposure.Ratio,
                        topExposure.ArOutstandingAmount);
                return new ExecutiveTrend("Billings (Invoiced) (30/90 day)", v, status, deltek.BilledSeries, payerRows);
            }, "Deltek/ODBC PRSummaryMain"));
            trends.Add(SafeTrend("AR Outstanding (Recent Months)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("AR Outstanding (Recent Months)", "Deltek PRSummaryMain dataset unavailable.");
                var latest = (deltek.ArSeries == null || deltek.ArSeries.Length == 0) ? 0.0 : deltek.ArSeries[deltek.ArSeries.Length - 1];
                var revenueByWbs = deltek.RevenuePayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var billedByWbs = deltek.BilledPayerRows.ToDictionary(
                    x => (x.Wbs1 ?? string.Empty).Trim(),
                    x => x.Amount,
                    StringComparer.OrdinalIgnoreCase);
                var payerRows = deltek.ArPayerRows
                    .Select(r =>
                    {
                        var key = (r.Wbs1 ?? string.Empty).Trim();
                        rowByWbs.TryGetValue(key, out var proj);
                        revenueByWbs.TryGetValue(key, out var revenue);
                        billedByWbs.TryGetValue(key, out var billed);
                        return new TrendPayerRow(
                            Wbs1: r.Wbs1 ?? string.Empty,
                            ProjectName: proj?.Name ?? string.Empty,
                            Pm: proj?.Pm ?? string.Empty,
                            PayerName: r.PayerName,
                            Amount: r.Amount,
                            RevenueAmount: revenue,
                            BilledAmount: billed,
                            ArOutstandingAmount: r.Amount);
                    })
                    .OrderByDescending(r => r.Amount)
                    .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var topAr = payerRows.Take(3).Select(r => $"{r.PayerName} ({r.ArOutstandingAmount:C0})").ToList();
                var status = topAr.Count == 0
                    ? "Period-end AR from PRSummaryMain."
                    : "Top AR payers: " + string.Join("; ", topAr);
                return new ExecutiveTrend("AR Outstanding (Recent Months)", latest.ToString("C0"), status, deltek.ArSeries, payerRows);
            }, "Deltek/ODBC PRSummaryMain"));

            trends.Add(SafeTrend("Delivery Risk (Critical Count)", () =>
            {
                if (trend == null || trend.Length < 2)
                    return ExecutiveTrend.DataUnavailable("Delivery Risk (Critical Count)", "Portfolio trend unavailable.");

                var vals = trend.Select(p => (double)p.CriticalCount).ToArray();
                var latest = trend[^1].CriticalCount;
                var criticalRows = (util ?? Array.Empty<UtilizationRow>())
                    .Where(u => u.ConfidenceLevel == DeliveryConfidenceLevel.Critical)
                    .OrderBy(u => u.RemainingEngHours)
                    .Select(u => new KpiDeliveryRiskRow(
                        Wbs1: u.Wbs1,
                        ProjectName: u.ProjectName,
                        Pm: u.Pm,
                        DeliveryRisk: u.ConfidenceDisplay,
                        BudgetStatus: u.RiskStatus,
                        PercentEngUsed: u.PercentEngUsed,
                        RemainingHours: u.RemainingEngHours))
                    .ToList();

                return new ExecutiveTrend(
                    "Delivery Risk (Critical Count)",
                    $"{latest:N0}",
                    "",
                    vals,
                    null,
                    criticalRows);
            }, "local SQL trend"));

            // Alerts (deterministic local rules)
            alerts.AddRange(BuildAlerts(snap, util, headline, deltek));

            return new ExecutiveSummaryResult(now, kpis, trends, alerts, snap?.MaxPostedPeriod);
        }

        private static IEnumerable<ExecutiveAlert> BuildAlerts(FinancialsSnapshot? snap, UtilizationRow[]? util, FinancialsHeadlineKpis? headline, ExecutiveSummaryDeltekData? deltek)
        {
            var list = new List<ExecutiveAlert>();
            var rows = snap?.Rows ?? new List<FinancialsProjectRow>();
            var rowByWbs = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Wbs1))
                .GroupBy(r => (r.Wbs1 ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var arRowsAll = deltek?.ArProjectRows
                .Select(a =>
                {
                    rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                    return new KpiArOutstandingRow(
                        Wbs1: a.Wbs1 ?? string.Empty,
                        ProjectName: proj?.Name ?? string.Empty,
                        Pm: proj?.Pm ?? string.Empty,
                        Total: a.Total,
                        Current: a.Current,
                        Aged31To60: a.Aged31To60,
                        Aged61To90: a.Aged61To90,
                        Aged90Plus: a.Aged90Plus,
                        OldestInvoiceDate: a.OldestInvoiceDate);
                })
                .OrderByDescending(r => r.Aged90Plus)
                .ThenByDescending(r => r.Total)
                .ToList() ?? new List<KpiArOutstandingRow>();

            var arRowsOver60 = arRowsAll
                .Where(r => (r.Aged61To90 + r.Aged90Plus) > 0.004)
                .ToList();

            var arInvoiceRowsOver60 = deltek?.ArInvoiceRows
                .Where(a => a.DaysPastDue > 60 && Math.Abs(a.Balance) > 0.004)
                .Select(a =>
                {
                    rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                    return new KpiArInvoiceRow(
                        Wbs1: a.Wbs1 ?? string.Empty,
                        ProjectName: proj?.Name ?? string.Empty,
                        Pm: proj?.Pm ?? string.Empty,
                        InvoiceDate: a.InvoiceDate,
                        DueDate: a.DueDate,
                        DaysPastDue: a.DaysPastDue,
                        Balance: a.Balance);
                })
                .OrderByDescending(r => r.DaysPastDue)
                .ThenByDescending(r => r.Balance)
                .ToList() ?? new List<KpiArInvoiceRow>();

            var overBudgetProjects = (util ?? Array.Empty<UtilizationRow>())
                .Where(u => string.Equals(u.RiskStatus, "Over budget", StringComparison.OrdinalIgnoreCase))
                .OrderBy(u => u.RemainingEngHours)
                .Select(u => new KpiProjectDrilldownRow(
                    Wbs1: u.Wbs1,
                    ProjectName: u.ProjectName,
                    Pm: u.Pm,
                    OverByHours: Math.Abs(Math.Min(0.0, u.RemainingEngHours)),
                    PercentEngUsed: u.PercentEngUsed,
                    PercentBilled: u.PercentBilled))
                .ToList();

            var backlogRows = rows
                .Select(r =>
                {
                    var fee = r?.TotalFee ?? 0.0;
                    var billed = r?.FeeBilled ?? 0.0;
                    var backlog = fee - billed;
                    return new KpiBacklogRow(
                        Wbs1: r?.Wbs1 ?? string.Empty,
                        ProjectName: r?.Name ?? string.Empty,
                        Pm: r?.Pm ?? string.Empty,
                        Fee: fee,
                        FeeBilled: billed,
                        Backlog: backlog,
                        PercentBilled: r?.PercentBilled ?? 0.0);
                })
                .Where(x => x.Backlog > 0.004)
                .OrderByDescending(x => x.Backlog)
                .ToList();

            var laggingBurnRows = rows
                .Select(r =>
                {
                    var engBudget = r?.EngBudget ?? 0.0;
                    var engHours = r?.EngHrs ?? 0.0;
                    var pctUsed = engBudget <= 0.004 ? 0.0 : engHours / engBudget;
                    return new
                    {
                        Row = new KpiBudgetBurnRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            EngHours: engHours,
                            EngBudget: engBudget,
                            PercentUsed: pctUsed,
                            RemainingHours: engBudget - engHours),
                        PercentBilled = r?.PercentBilled ?? 0.0
                    };
                })
                .Where(x => (x.Row.PercentUsed - x.PercentBilled) >= 0.15 && x.Row.PercentUsed >= 0.60)
                .OrderByDescending(x => (x.Row.PercentUsed - x.PercentBilled))
                .Select(x => x.Row)
                .ToList();

            ExecutiveAlert CreateAlert(string title, string message)
            {
                var t = (title ?? string.Empty).Trim();
                if (t.IndexOf("AR", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    t.IndexOf("60", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ExecutiveAlert(
                        t,
                        message,
                        ArOutstandingRows: arRowsOver60,
                        ArInvoiceRows: arInvoiceRowsOver60);
                }

                if (t.IndexOf("AR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ExecutiveAlert(
                        t,
                        message,
                        ArOutstandingRows: arRowsAll);
                }

                if (t.IndexOf("Over Budget", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ExecutiveAlert(
                        t,
                        message,
                        ProjectDrilldownRows: overBudgetProjects);
                }

                if (t.IndexOf("Backlog", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ExecutiveAlert(
                        t,
                        message,
                        BacklogRows: backlogRows);
                }

                if (t.IndexOf("Burn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("Billing Lagging", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ExecutiveAlert(
                        t,
                        message,
                        BudgetBurnRows: laggingBurnRows);
                }

                return new ExecutiveAlert(t, message);
            }

            if (deltek == null)
            {
                list.Add(CreateAlert(
                    "AR > 60 Days",
                    "Data unavailable (Deltek AR aging not loaded)."));
            }
            else if (deltek.ArOver60 > 0.0)
            {
                list.Add(CreateAlert(
                    "AR > 60 Days",
                    "Over 60 days: " + deltek.ArOver60.ToString("C0") + "."));
            }
            else
            {
                list.Add(CreateAlert(
                    "AR > 60 Days",
                    "No open AR over 60 days."));
            }

            // Projects over budget
            if (util == null || util.Length == 0)
            {
                list.Add(CreateAlert("Projects Over Budget", "Data unavailable (portfolio snapshot not loaded)."));
            }
            else
            {
                var over = util.Where(u => string.Equals(u.RiskStatus, "Over budget", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(u => u.RemainingEngHours)
                    .ToList();

                if (over.Count == 0)
                {
                    list.Add(CreateAlert("Projects Over Budget", "No projects currently over engineering budget."));
                }
                else
                {
                    var top = over.Take(3).Select(u => $"{u.Wbs1} {u.ProjectName} ({u.RemainingEngHours:N1} hrs)").ToList();
                    list.Add(CreateAlert(
                        "Projects Over Budget",
                        $"{over.Count:N0} projects over engineering budget. Top: {string.Join("; ", top)}"));
                }
            }

            // Backlog declining - requires history not currently available.
            list.Add(CreateAlert(
                "Backlog Declining",
                "Not currently sourced (no historical backlog series is loaded today)."));

            // Optional: billing lag vs burn (local)
            if (headline != null)
            {
                var pctBilled = headline.TotalFees <= 0 ? 0.0 : (headline.TotalFeeBilled / headline.TotalFees);
                var burn = headline.PercentHoursSpent;
                if ((burn - pctBilled) >= 0.15 && burn >= 0.60)
                {
                    list.Add(CreateAlert(
                        "Billing Lagging Burn",
                        $"Burn is {burn:P0} vs billed {pctBilled:P0}. Consider reviewing billing cadence on high-burn projects."));
                }
            }

            return list;
        }
    }

    public sealed record ExecutiveSummaryResult(
        DateTimeOffset GeneratedAt,
        List<ExecutiveKpi> Kpis,
        List<ExecutiveTrend> Trends,
        List<ExecutiveAlert> Alerts,
        DateTime? MaxPostedPeriod = null);

    public sealed record ExecutiveKpi(
        string Title,
        string ValueText,
        string SubText,
        string StatusMessage,
        IReadOnlyList<KpiProjectDrilldownRow>? ProjectDrilldownRows = null,
        IReadOnlyList<KpiCashHistoryRow>? CashHistoryRows = null,
        IReadOnlyList<KpiArOutstandingRow>? ArOutstandingRows = null,
        IReadOnlyList<KpiArInvoiceRow>? ArInvoiceRows = null,
        IReadOnlyList<KpiWipUnbilledRow>? WipUnbilledRows = null,
        IReadOnlyList<KpiBacklogRow>? BacklogRows = null,
        IReadOnlyList<KpiBillingsRow>? BillingsRows = null,
        IReadOnlyList<KpiBudgetBurnRow>? BudgetBurnRows = null,
        IReadOnlyList<KpiDeliveryRiskRow>? DeliveryRiskRows = null,
        IReadOnlyList<KpiUtilizationRow>? UtilizationRows = null)
    {
        public static ExecutiveKpi NotSourced(string title, string message)
            => new(title, "N/A", "", message);

        public static ExecutiveKpi DataUnavailable(string title, string reason)
            => new(title, "Data unavailable", "", reason);
    }

    public sealed record ExecutiveTrend(
        string Title,
        string ValueText,
        string StatusMessage,
        double[]? Values,
        IReadOnlyList<TrendPayerRow>? TrendPayerRows = null,
        IReadOnlyList<KpiDeliveryRiskRow>? DeliveryRiskRows = null)
    {
        public static ExecutiveTrend NotSourced(string title, string message)
            => new(title, "N/A", message, null);

        public static ExecutiveTrend DataUnavailable(string title, string reason)
            => new(title, "Data unavailable", reason, null);
    }

    public sealed record ExecutiveAlert(
        string Title,
        string Message,
        IReadOnlyList<KpiProjectDrilldownRow>? ProjectDrilldownRows = null,
        IReadOnlyList<KpiArOutstandingRow>? ArOutstandingRows = null,
        IReadOnlyList<KpiArInvoiceRow>? ArInvoiceRows = null,
        IReadOnlyList<KpiBacklogRow>? BacklogRows = null,
        IReadOnlyList<KpiBudgetBurnRow>? BudgetBurnRows = null);

    public sealed record KpiProjectDrilldownRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double OverByHours,
        double PercentEngUsed,
        double PercentBilled);

    public sealed record KpiCashHistoryRow(
        string Period,
        double Total,
        double Cad,
        double Usa,
        double Bcc);

    public sealed record KpiArOutstandingRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double Total,
        double Current,
        double Aged31To60,
        double Aged61To90,
        double Aged90Plus,
        DateTime? OldestInvoiceDate);

    public sealed record KpiArInvoiceRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        DateTime? InvoiceDate,
        DateTime? DueDate,
        int DaysPastDue,
        double Balance);

    public sealed record KpiWipUnbilledRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double Earned,
        double Overbilled,
        double Net,
        double NetAsPercentOfFee,
        string Period);

    public sealed record KpiBacklogRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double Fee,
        double FeeBilled,
        double Backlog,
        double PercentBilled);

    public sealed record KpiBillingsRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double FeeBilled,
        double Fee,
        double PercentBilled,
        double ContributionPercent);

    public sealed record KpiBudgetBurnRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double EngHours,
        double EngBudget,
        double PercentUsed,
        double RemainingHours);

    public sealed record KpiDeliveryRiskRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        string DeliveryRisk,
        string BudgetStatus,
        double PercentEngUsed,
        double RemainingHours);

    public sealed record KpiUtilizationRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double BillableHours,
        double NonBillableHours,
        double TotalHours,
        double UtilizationPct);

    public sealed record TrendPayerRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        string PayerName,
        double Amount,
        double RevenueAmount,
        double BilledAmount,
        double ArOutstandingAmount);
}
