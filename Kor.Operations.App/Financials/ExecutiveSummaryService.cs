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
    public enum ScopeKind
    {
        None,
        Firmwide,
        Scoped
    }

    public sealed class ExecutiveSummaryService
    {
        private ExecutiveSummaryResult? _cache;
        private DateTimeOffset _cacheAt;
        private readonly object _cacheLock = new();
        // 5 min: long enough to make tab-switching instant (the dominant access
        // pattern for executives glancing at the dashboard) without serving
        // stale numbers during a working session. Click Refresh to force-bypass.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

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

        internal static ExecutiveSummaryResult Build(
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
    if (deltek == null) return ExecutiveKpi.DataUnavailable("Cash Position", "Deltek cash/GL dataset unavailable.", ScopeKind.Firmwide);

    var usdCadEquivalent = deltek.CashUsa * deltek.CashUsdToCadRate;
    var lines = new List<string>
    {
        string.Format(CultureInfo.CurrentCulture, "CAD bank balances: {0:C0}", deltek.CashCad),
        string.Format(CultureInfo.CurrentCulture, "USD bank balances: {0:C0} (= {1:C0} CAD @ {2:0.00})",
            deltek.CashUsa, usdCadEquivalent, deltek.CashUsdToCadRate)
    };
    if (deltek.CashBcc > AnalyticsThresholds.RoundingDollarFloor)
        lines.Add(string.Format(CultureInfo.CurrentCulture, "BCC bank balances: {0:C0}", deltek.CashBcc));
    lines.Add(string.Format(CultureInfo.CurrentCulture,
        "Total: {0:C0} CAD-equivalent",
        deltek.CashCombinedCadEquivalent));
    var breakdown = string.Join("  •  ", lines);

    return new ExecutiveKpi(
        "Cash Position",
        deltek.CashCombinedCadEquivalent.ToString("C0"),
        "Bank GL balances as of period " + (deltek.CashPeriod ?? "") + ".  " + breakdown + ".",
        "",
        null,
        deltek.CashHistory
            .Select(h => new KpiCashHistoryRow(
                Period: h.Period,
                Total: h.Total,
                Cad: h.Cad,
                Usa: h.Usa,
                Bcc: h.Bcc))
            .ToList(),
        CashAccountRows: deltek.CashPerAccount
            .Select(a => new KpiCashAccountRow(a.Company, a.Account, a.Org, a.Currency, a.Balance))
            .ToList(),
        Scope: ScopeKind.Firmwide);
}, "Deltek/ODBC CFGBanks+GLSummary"));

            kpis.Add(SafeKpi("Liquidity (Cash + AR)", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("Liquidity (Cash + AR)", "Deltek cash/AR dataset unavailable.", ScopeKind.Firmwide);

                var cashCadEquiv = deltek.CashCombinedCadEquivalent;
                var arFirmwideCadEquiv = deltek.ArFirmwideOutstanding;
                var liquidity = cashCadEquiv + arFirmwideCadEquiv;

                var arBreakdown = deltek.ArFirmwideOutstandingUsa > AnalyticsThresholds.UsaArBreakdownDisplayThreshold
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        " (CAD AR {0:C0} + USA AR {1:C0} → {2:C0} CAD-equiv @ {3:0.00})",
                        deltek.ArFirmwideOutstandingCad,
                        deltek.ArFirmwideOutstandingUsa,
                        deltek.ArFirmwideOutstandingUsa * deltek.ArFirmwideUsdToCadRate,
                        deltek.ArFirmwideUsdToCadRate)
                    : string.Empty;

                var subText = string.Format(
                    CultureInfo.CurrentCulture,
                    "Cash on hand {0:C0} + firmwide AR {1:C0}{2} = {3:C0} of near-cash assets. AR is what clients have been billed but haven't paid yet — typically settles within 30-60 days. All values CAD-equivalent.",
                    cashCadEquiv,
                    arFirmwideCadEquiv,
                    arBreakdown,
                    liquidity);

                return new ExecutiveKpi(
                    "Liquidity (Cash + AR)",
                    liquidity.ToString("C0"),
                    subText,
                    "",
                    Scope: ScopeKind.Firmwide);
            }, "Deltek/ODBC CFGBanks+GLSummary+AR"));

            kpis.Add(SafeKpi("AR Outstanding", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("AR Outstanding", "Deltek AR dataset unavailable.", ScopeKind.Firmwide);

                var arRows = deltek.ArProjectRows
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArOutstandingRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                            Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
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
                            ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                            Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
                            InvoiceDate: a.InvoiceDate,
                            DueDate: a.DueDate,
                            DaysPastDue: a.DaysPastDue,
                            Balance: a.Balance);
                    })
                    .ToList();

                return new ExecutiveKpi(
                    "AR Outstanding",
                    deltek.ArFirmwideOutstanding.ToString("C0"),
                    "Sum of open invoice balances (AR.InvBalanceSourceCurrency) firmwide. Drilldown rows show firmwide AR aging and are not filtered by the Scope toggle.",
                    "",
                    null,
                    null,
                    arRows,
                    arInvoiceRows,
                    Scope: ScopeKind.Firmwide);
            }, "Deltek/ODBC AR"));
            kpis.Add(SafeKpi("AR > 60 Days", () =>
            {
                if (deltek == null) return ExecutiveKpi.DataUnavailable("AR > 60 Days", "Deltek AR dataset unavailable.", ScopeKind.Firmwide);

                var ar60Rows = deltek.ArProjectRows
                    .Where(a => Math.Abs(a.Aged61To90 + a.Aged90Plus) > AnalyticsThresholds.RoundingDollarFloor)
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArOutstandingRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                            Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
                            Total: a.Aged61To90 + a.Aged90Plus,
                            Current: 0.0,
                            Aged31To60: 0.0,
                            Aged61To90: a.Aged61To90,
                            Aged90Plus: a.Aged90Plus,
                            OldestInvoiceDate: a.OldestInvoiceDate);
                    })
                    .ToList();

                var ar60InvoiceRows = deltek.ArInvoiceRows
                    .Where(a => a.DaysPastDue > 60 && Math.Abs(a.Balance) > AnalyticsThresholds.RoundingDollarFloor)
                    .Select(a =>
                    {
                        rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                        return new KpiArInvoiceRow(
                            Wbs1: a.Wbs1 ?? string.Empty,
                            ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                            Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
                            InvoiceDate: a.InvoiceDate,
                            DueDate: a.DueDate,
                            DaysPastDue: a.DaysPastDue,
                            Balance: a.Balance);
                    })
                    .ToList();

                return new ExecutiveKpi(
                    "AR > 60 Days",
                    deltek.ArFirmwideOver60.ToString("C0"),
                    "Firmwide AR past-due > 60 days (DueDate; falls back to InvoiceDate). Drilldown rows show firmwide AR aging and are not filtered by the Scope toggle.",
                    "",
                    null,
                    null,
                    ar60Rows,
                    ar60InvoiceRows,
                    Scope: ScopeKind.Firmwide);
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
                    return ExecutiveKpi.DataUnavailable("WIP (Unbilled Earned)", "Deltek PRSummaryMain dataset unavailable.", ScopeKind.Scoped);
                if (!deltek.WipDataLoaded)
                    return ExecutiveKpi.DataUnavailable(
                        "WIP (Unbilled Earned)",
                        "WIP data unavailable — check Deltek connection.",
                        ScopeKind.Scoped);
                if (!deltek.RevenueGenerationDetected)
                    return ExecutiveKpi.DataUnavailable(
                        "WIP (Unbilled Earned)",
                        "Revenue Generation disabled in Deltek — WIP cannot be computed (KOR config).",
                        ScopeKind.Scoped);

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
                    "Firmwide as of period " + period + ": earned " + deltek.FirmWipUnbilled.ToString("C0") +
                    " | overbilled " + deltek.FirmWipOverbilled.ToString("C0") +
                    " | net " + deltek.FirmWipNet.ToString("C0") + "." + "\n" +
                    "Current scope (watchlist or all-active): earned " + deltek.WipUnbilled.ToString("C0") +
                    " | overbilled " + deltek.WipOverbilled.ToString("C0") +
                    " | net " + deltek.WipUnbilledNet.ToString("C0") + "." + "\n" +
                    "Source: PRSummaryMain. Uses Unbilled column when populated, else proxy = cumulative (BilledFee else Revenue) − Billed.";

                return new ExecutiveKpi(
                    "WIP (Unbilled Earned)",
                    deltek.FirmWipUnbilled.ToString("C0"),
                    sub,
                    "",
                    null,
                    null,
                    null,
                    null,
                    wipRows,
                    Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC PRSummaryMain"));
            }
            // Per-project FX so breakdown totals match the CAD-equivalent headline tile.
            var fxRate = snap?.UsdToCadRate ?? 1.36;

            kpis.Add(SafeKpi("Backlog", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Backlog", "Deltek/ODBC snapshot unavailable.", ScopeKind.Scoped);
                // Empty scope (e.g., watchlist toggled on with zero hotlisted projects) makes
                // headline a populated-but-zero record, which would render "$0 backlog" — that's
                // not "you have no backlog", it's "no projects in scope". Surface that explicitly.
                if (rows.Count == 0) return ExecutiveKpi.DataUnavailable("Backlog", "No projects in current scope.", ScopeKind.Scoped);
                var backlogRows = rows
                    .Select(r =>
                    {
                        var fx = OrgFx.IsUsaOrg(r?.Org) ? fxRate : 1.0;
                        var fee = (r?.TotalFee ?? 0.0) * fx;
                        var billedPosted = (r?.FeeBilled ?? 0.0) * fx;
                        var unposted = (r?.UnpostedFeeBilled ?? 0.0) * fx;
                        // Backlog uses billed-with-unposted (real-time invoicing state)
                        // so the drilldown reconciles to the headline TotalUnbilled,
                        // which now also includes the LedgerAR overlay.
                        var backlog = fee - (billedPosted + unposted);
                        return new KpiBacklogRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            Fee: fee,
                            FeeBilled: billedPosted,
                            UnpostedFeeBilled: unposted,
                            Backlog: backlog,
                            PercentBilled: r?.PercentBilled ?? 0.0);
                    })
                    // Math.Abs threshold so overbilled/credit-balance projects (negative
                    // backlog) stay in the list — the headline sums them, the drilldown
                    // must too, otherwise Σ rows ≠ tile.
                    .Where(x => Math.Abs(x.Backlog) > AnalyticsThresholds.RoundingDollarFloor)
                    .OrderByDescending(x => x.Backlog)
                    .ToList();

                return new ExecutiveKpi(
                    "Backlog",
                    $"{headline.TotalUnbilled:C0}",
                    "Fee remaining not yet billed across the current scope (watchlist or all-active per Scope toggle): Σ TotalFee − Σ (FeeBilled + UnpostedFeeBilled). Includes the real-time LedgerAR overlay so PRSummaryMain's ~3-month posting lag does not inflate Backlog. Lifetime per project — not period-filtered. USA-org rows are FX-converted to CAD-equivalent at " + fxRate.ToString("0.00", CultureInfo.InvariantCulture) + ".",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    backlogRows,
                    Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Billings To Date", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Billings To Date", "Deltek/ODBC snapshot unavailable.", ScopeKind.Scoped);
                if (rows.Count == 0) return ExecutiveKpi.DataUnavailable("Billings To Date", "No projects in current scope.", ScopeKind.Scoped);
                var billingsRows = rows
                    .Select(r =>
                    {
                        var fx = OrgFx.IsUsaOrg(r?.Org) ? fxRate : 1.0;
                        var billedPosted = (r?.FeeBilled ?? 0.0) * fx;
                        var unposted = (r?.UnpostedFeeBilled ?? 0.0) * fx;
                        var billedAll = billedPosted + unposted;
                        var fee = (r?.TotalFee ?? 0.0) * fx;
                        var denominator = headline.TotalFeeBilledWithUnposted;
                        var contribution = denominator <= 0.0 ? 0.0 : (billedAll / denominator);
                        return new KpiBillingsRow(
                            Wbs1: r?.Wbs1 ?? string.Empty,
                            ProjectName: r?.Name ?? string.Empty,
                            Pm: r?.Pm ?? string.Empty,
                            FeeBilled: billedPosted,
                            UnpostedFeeBilled: unposted,
                            Fee: fee,
                            PercentBilled: r?.PercentBilled ?? 0.0,
                            ContributionPercent: contribution);
                    })
                    // Math.Abs threshold so credit memos / refunds (negative FeeBilled)
                    // stay in the list — headline sums them, drilldown must match.
                    // Includes rows with only unposted activity (no posted yet) so
                    // current-month invoicing is visible.
                    .Where(x => Math.Abs(x.FeeBilledWithUnposted) > AnalyticsThresholds.RoundingDollarFloor)
                    .OrderByDescending(x => x.FeeBilledWithUnposted)
                    .ToList();

                return new ExecutiveKpi(
                    "Billings To Date",
                    $"{headline.TotalFeeBilledWithUnposted:C0}",
                    "Lifetime fee billed across the current scope (watchlist or all-active per Scope toggle). Includes posted FeeBilled plus the real-time LedgerAR overlay (UnpostedFeeBilled), so PRSummaryMain's ~3-month posting lag does not understate billings. Not period-filtered. For period-specific billings see the P&L Report tab. USA-org rows are FX-converted to CAD-equivalent at " + fxRate.ToString("0.00", CultureInfo.InvariantCulture) + ".",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    billingsRows,
                    Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Budget Burn", () =>
            {
                if (headline == null) return ExecutiveKpi.DataUnavailable("Budget Burn", "Deltek/ODBC snapshot unavailable.", ScopeKind.Scoped);
                if (rows.Count == 0) return ExecutiveKpi.DataUnavailable("Budget Burn", "No projects in current scope.", ScopeKind.Scoped);
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
                    .Where(x => x.EngBudget > AnalyticsThresholds.RoundingDollarFloor)
                    .OrderByDescending(x => x.PercentUsed)
                    .ToList();
                // Compute headline % from the same Eng-only inputs as the drilldown rows
                // (the previous headline used Eng+Draft from FinancialsHeadlineKpis, which
                // didn't reconcile against the drilldown's Eng-only PercentUsed).
                var engBudgetTotal = rows.Sum(r => r?.EngBudget ?? 0.0);
                var engHoursTotal = rows.Sum(r => r?.EngHrs ?? 0.0);
                var headlinePct = engBudgetTotal > AnalyticsThresholds.RoundingDollarFloor ? engHoursTotal / engBudgetTotal : 0.0;
                return new ExecutiveKpi(
                    "Budget Burn",
                    $"{headlinePct:P1}",
                    "Engineering hours burn (watchlist): Σ EngHrs / Σ EngBudget across active projects. Matches the % Used column in the drilldown.",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    burnRows,
                    Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC snapshot"));

            kpis.Add(SafeKpi("Portfolio Delivery Risk", () =>
            {
                if (util == null || util.Length == 0)
                    return ExecutiveKpi.DataUnavailable("Portfolio Delivery Risk", "Portfolio is empty or snapshot unavailable.", ScopeKind.Scoped);

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
                    SubText: "Count of projects rated Critical or At Risk by Delivery Risk (current scope: watchlist or all-active per Scope toggle).",
                    StatusMessage: "",
                    DeliveryRiskRows: riskRows,
                    Scope: ScopeKind.Scoped);
            }, "local compute"));

            kpis.Add(SafeKpi("Projects Over Budget", () =>
            {
                if (util == null || util.Length == 0)
                    return ExecutiveKpi.DataUnavailable("Projects Over Budget", "Portfolio is empty or snapshot unavailable.", ScopeKind.Scoped);

                var over = util.Where(u => string.Equals(u.RiskStatus, "Over budget", StringComparison.OrdinalIgnoreCase)).ToList();
                var top3 = over
                    .OrderBy(u => u.RemainingEngHours)
                    .Take(3)
                    .Select(u => $"{u.Wbs1} ({u.RemainingEngHours:N1} hrs)")
                    .ToList();
                // OverByHours = signed hours past budget. Positive = project has used more
                // hours than budgeted (true overage). Zero or negative = project is flagged
                // "Over budget" by a percentage/burn-rate criterion but hasn't yet exceeded
                // budgeted hours. The drilldown's PercentEngUsed column shows why it's flagged
                // in those cases.
                var projectRows = over
                    .OrderBy(u => u.RemainingEngHours)
                    .Select(u => new KpiProjectDrilldownRow(
                        Wbs1: u.Wbs1,
                        ProjectName: u.ProjectName,
                        Pm: u.Pm,
                        OverByHours: -u.RemainingEngHours,
                        PercentEngUsed: u.PercentEngUsed,
                        PercentBilled: u.PercentBilled,
                        PercentBilledWithUnposted: u.PercentBilledWithUnposted,
                        HasUnpostedBilling: u.HasUnpostedBilling))
                    .ToList();

                return new ExecutiveKpi(
                    "Projects Over Budget",
                    $"{over.Count:N0}",
                    over.Count > 0
                        ? $"Top: {string.Join(", ", top3)} (current scope: watchlist or all-active per Scope toggle)."
                        : "No projects currently flagged Over Budget by the engineering-hours risk rule (current scope).",
                    "",
                    projectRows,
                    Scope: ScopeKind.Scoped);
            }, "local compute"));

            kpis.Add(SafeKpi("Utilization", () =>
{
    if (deltek == null) return ExecutiveKpi.DataUnavailable("Utilization", "Deltek timesheet dataset unavailable.", ScopeKind.Firmwide);
    if (deltek.UtilizationTotalHours30 <= 0.0) return ExecutiveKpi.DataUnavailable("Utilization", "No timesheet hours found in the last 30 days.", ScopeKind.Firmwide);

    var pct = deltek.UtilizationPct30;
    var sub = string.Format(CultureInfo.CurrentCulture, "Last 30 days, firmwide active labor: {0:N1} billable hrs of {1:N1} total charged hrs. Billable = LaborCode NOT IN Admin/NonBillable AND WBS1 not in overhead prefixes. Drilldown rows mirror this firmwide scope.", deltek.UtilizationBillableHours30, deltek.UtilizationTotalHours30);
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
        UtilizationRows: utilRows,
        Scope: ScopeKind.Firmwide);
}, "Deltek/ODBC tkDetail"));

            // Trends (best-effort; placeholders for not-sourced items)
            // "1mo / 3mo" labels use the latest closed PRSummaryMain period (and the
            // last 3) rather than calendar windows, since PRSummaryMain posts ~3
            // months behind real-time at KOR. Calendar windows produce misleading
            // $0 headlines in the steady state.
            trends.Add(SafeTrend("Revenue (Earned) (latest 1 / 3 periods)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("Revenue (Earned) (latest 1 / 3 periods)", "Deltek PRSummaryMain dataset unavailable.", ScopeKind.Scoped);
                if (rows.Count == 0) return ExecutiveTrend.DataUnavailable("Revenue (Earned) (latest 1 / 3 periods)", "No projects in current scope.", ScopeKind.Scoped);
                var gap30 = deltek.Revenue30 - deltek.Billed30;
                var gap90 = deltek.Revenue90 - deltek.Billed90;
                var isAligned = Math.Abs(gap30) <= AnalyticsThresholds.RoundingDollarFloor && Math.Abs(gap90) <= AnalyticsThresholds.RoundingDollarFloor;
                var realtimeRevSegment = deltek.LedgerArInvoicedSincePeriod > 0
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        " | Real-time invoiced since {0}-{1:00}: {2:C0} (LedgerAR)",
                        deltek.LedgerArInvoicedSincePeriod / 100,
                        deltek.LedgerArInvoicedSincePeriod % 100,
                        deltek.LedgerArInvoicedSinceLatestPosted)
                    : string.Empty;
                var v = string.Format(
                    CultureInfo.CurrentCulture,
                    "Latest period: Earned {0:C0} / Invoiced {1:C0} | Last 3 periods: Earned {2:C0} / Invoiced {3:C0}",
                    deltek.Revenue30,
                    deltek.Billed30,
                    deltek.Revenue90,
                    deltek.Billed90) + realtimeRevSegment;
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
                var status = isAligned
                    ? "Unbilled gap is ~0 in both windows (earned and invoiced are aligned)."
                    : (topGap != null && Math.Abs(topGap.Gap) > AnalyticsThresholds.RoundingDollarFloor)
                        ? string.Format(
                            CultureInfo.CurrentCulture,
                            "Unbilled gap: 30d {0:C0} | 90d {1:C0}  •  Top unbilled payer: {2} ({3:C0})",
                            gap30,
                            gap90,
                            topGap.PayerName,
                            topGap.Gap)
                        : string.Format(CultureInfo.CurrentCulture, "Unbilled gap: 30d {0:C0} | 90d {1:C0}", gap30, gap90);
                return new ExecutiveTrend("Revenue (Earned) (latest 1 / 3 periods)", v, status, deltek.RevenueSeries, payerRows, Scope: ScopeKind.Scoped, IsAligned: isAligned);
            }, "Deltek/ODBC PRSummaryMain"));

            trends.Add(SafeTrend("Billings (Invoiced) (latest 1 / 3 periods)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("Billings (Invoiced) (latest 1 / 3 periods)", "Deltek PRSummaryMain dataset unavailable.", ScopeKind.Scoped);
                if (rows.Count == 0) return ExecutiveTrend.DataUnavailable("Billings (Invoiced) (latest 1 / 3 periods)", "No projects in current scope.", ScopeKind.Scoped);
                var arToBilled90 = deltek.Billed90 <= AnalyticsThresholds.RoundingDollarFloor ? 0.0 : (deltek.ArScopedOutstanding / deltek.Billed90);
                // Append a real-time LedgerAR figure for periods after the latest
                // closed PRSummaryMain period, so the user sees what's been invoiced
                // since the posting cutoff. Skip when the gap is empty (no LedgerAR
                // hits or the period math couldn't resolve).
                var realtimeSegment = deltek.LedgerArInvoicedSincePeriod > 0
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        " | Real-time since {0}-{1:00}: {2:C0} (LedgerAR)",
                        deltek.LedgerArInvoicedSincePeriod / 100,
                        deltek.LedgerArInvoicedSincePeriod % 100,
                        deltek.LedgerArInvoicedSinceLatestPosted)
                    : string.Empty;
                var v = string.Format(
                    CultureInfo.CurrentCulture,
                    "Latest period: Invoiced {0:C0} | Last 3 periods: Invoiced {1:C0}",
                    deltek.Billed30,
                    deltek.Billed90) + realtimeSegment;
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
                    .Where(r => r.BilledAmount > AnalyticsThresholds.RoundingDollarFloor)
                    .Select(r => new { r.PayerName, Ratio = r.ArOutstandingAmount / r.BilledAmount, r.ArOutstandingAmount })
                    .OrderByDescending(x => x.Ratio)
                    .ThenByDescending(x => x.ArOutstandingAmount)
                    .FirstOrDefault();
                var status = topExposure == null
                    ? string.Format(CultureInfo.CurrentCulture, "Collection exposure: AR/90d billed {0:P1}", arToBilled90)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "Collection exposure: AR/90d billed {0:P1}  •  Top collection risk: {1} ({2:P1}, AR {3:C0})",
                        arToBilled90,
                        topExposure.PayerName,
                        topExposure.Ratio,
                        topExposure.ArOutstandingAmount);
                return new ExecutiveTrend("Billings (Invoiced) (latest 1 / 3 periods)", v, status, deltek.BilledSeries, payerRows, Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC PRSummaryMain"));
            trends.Add(SafeTrend("AR Outstanding (Recent Months)", () =>
            {
                if (deltek == null) return ExecutiveTrend.DataUnavailable("AR Outstanding (Recent Months)", "Deltek PRSummaryMain dataset unavailable.", ScopeKind.Scoped);
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
                return new ExecutiveTrend("AR Outstanding (Recent Months)", latest.ToString("C0"), status, deltek.ArSeries, payerRows, Scope: ScopeKind.Scoped);
            }, "Deltek/ODBC PRSummaryMain"));

            trends.Add(SafeTrend("Delivery Risk (Critical Count)", () =>
            {
                if (trend == null || trend.Length < 2)
                    return ExecutiveTrend.DataUnavailable("Delivery Risk (Critical Count)", "Portfolio trend unavailable.", ScopeKind.Scoped);

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
                    criticalRows,
                    Scope: ScopeKind.Scoped);
            }, "local SQL trend"));

            // Alerts (deterministic local rules)
            alerts.AddRange(BuildAlerts(snap, util, headline, deltek));

            return new ExecutiveSummaryResult(
                now,
                kpis,
                trends,
                alerts,
                snap?.MaxPostedPeriod,
                SnapshotLoaded: snap != null,
                DeltekLoaded: deltek != null,
                TrendLoaded: trend != null && trend.Length > 0,
                SchemaDriftMessages: deltek?.SchemaDriftMessages);
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
                        ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                        Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
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
                .Where(r => Math.Abs(r.Aged61To90 + r.Aged90Plus) > AnalyticsThresholds.RoundingDollarFloor)
                .ToList();

            var arInvoiceRowsOver60 = deltek?.ArInvoiceRows
                .Where(a => a.DaysPastDue > 60 && Math.Abs(a.Balance) > AnalyticsThresholds.RoundingDollarFloor)
                .Select(a =>
                {
                    rowByWbs.TryGetValue((a.Wbs1 ?? string.Empty).Trim(), out var proj);
                    return new KpiArInvoiceRow(
                        Wbs1: a.Wbs1 ?? string.Empty,
                        ProjectName: !string.IsNullOrWhiteSpace(a.ProjectName) ? a.ProjectName : proj?.Name ?? string.Empty,
                        Pm: !string.IsNullOrWhiteSpace(a.Pm) ? a.Pm : proj?.Pm ?? string.Empty,
                        InvoiceDate: a.InvoiceDate,
                        DueDate: a.DueDate,
                        DaysPastDue: a.DaysPastDue,
                        Balance: a.Balance);
                })
                .OrderByDescending(r => r.DaysPastDue)
                .ThenByDescending(r => Math.Abs(r.Balance))
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
                    PercentBilled: u.PercentBilled,
                    PercentBilledWithUnposted: u.PercentBilledWithUnposted,
                    HasUnpostedBilling: u.HasUnpostedBilling))
                .ToList();

            var alertFxRate = snap?.UsdToCadRate ?? 1.36;
            var backlogRows = rows
                .Select(r =>
                {
                    var fx = OrgFx.IsUsaOrg(r?.Org) ? alertFxRate : 1.0;
                    var fee = (r?.TotalFee ?? 0.0) * fx;
                    var billed = (r?.FeeBilled ?? 0.0) * fx;
                    var backlog = fee - billed;
                    return new KpiBacklogRow(
                        Wbs1: r?.Wbs1 ?? string.Empty,
                        ProjectName: r?.Name ?? string.Empty,
                        Pm: r?.Pm ?? string.Empty,
                        Fee: fee,
                        FeeBilled: billed,
                        UnpostedFeeBilled: (r?.UnpostedFeeBilled ?? 0.0) * fx,
                        Backlog: backlog,
                        PercentBilled: r?.PercentBilled ?? 0.0);
                })
                .Where(x => Math.Abs(x.Backlog) > AnalyticsThresholds.RoundingDollarFloor)
                .OrderByDescending(x => x.Backlog)
                .ToList();

            var laggingBurnRows = rows
                .Select(r =>
                {
                    var engBudget = r?.EngBudget ?? 0.0;
                    var engHours = r?.EngHrs ?? 0.0;
                    var pctUsed = engBudget <= AnalyticsThresholds.RoundingDollarFloor ? 0.0 : engHours / engBudget;
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
                .Where(x => (x.Row.PercentUsed - x.PercentBilled) >= AnalyticsThresholds.BillingLaggingBurnDeltaThreshold && x.Row.PercentUsed >= AnalyticsThresholds.BillingLaggingBurnPercentFloor)
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
                // Use TotalFeeBilledWithUnposted so the "billing lagging burn" alert
                // does not false-fire purely because PRSummaryMain hasn't been posted
                // yet for the current period — UnpostedFeeBilled is the LedgerAR
                // overlay capturing real-time invoicing.
                var pctBilled = headline.TotalFees <= 0 ? 0.0 : (headline.TotalFeeBilledWithUnposted / headline.TotalFees);
                var burn = headline.PercentHoursSpent;
                if ((burn - pctBilled) >= AnalyticsThresholds.BillingLaggingBurnDeltaThreshold && burn >= AnalyticsThresholds.BillingLaggingBurnPercentFloor)
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
        DateTime? MaxPostedPeriod = null,
        bool SnapshotLoaded = true,
        bool DeltekLoaded = true,
        bool TrendLoaded = true,
        IReadOnlyList<string>? SchemaDriftMessages = null);

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
        IReadOnlyList<KpiUtilizationRow>? UtilizationRows = null,
        IReadOnlyList<KpiCashAccountRow>? CashAccountRows = null,
        ScopeKind Scope = ScopeKind.None)
    {
        public static ExecutiveKpi NotSourced(string title, string message, ScopeKind scope = ScopeKind.None)
            => new(title, "N/A", "", message, Scope: scope);

        public static ExecutiveKpi DataUnavailable(string title, string reason, ScopeKind scope = ScopeKind.None)
            => new(title, "Data unavailable", "", reason, Scope: scope);
    }

    public sealed record ExecutiveTrend(
        string Title,
        string ValueText,
        string StatusMessage,
        double[]? Values,
        IReadOnlyList<TrendPayerRow>? TrendPayerRows = null,
        IReadOnlyList<KpiDeliveryRiskRow>? DeliveryRiskRows = null,
        ScopeKind Scope = ScopeKind.None,
        bool IsAligned = false)
    {
        public static ExecutiveTrend NotSourced(string title, string message, ScopeKind scope = ScopeKind.None)
            => new(title, "N/A", message, null, Scope: scope);

        public static ExecutiveTrend DataUnavailable(string title, string reason, ScopeKind scope = ScopeKind.None)
            => new(title, "Data unavailable", reason, null, Scope: scope);
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
        double PercentBilled,
        double PercentBilledWithUnposted,
        bool HasUnpostedBilling);

    public sealed record KpiCashHistoryRow(
        string Period,
        double Total,
        double Cad,
        double Usa,
        double Bcc);

    public sealed record KpiCashAccountRow(
        string Company,
        string Account,
        string Org,
        string Currency,
        double Balance);

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
        double UnpostedFeeBilled,
        double Backlog,
        double PercentBilled)
    {
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double PercentBilledWithUnposted => Fee > 0 ? FeeBilledWithUnposted / Fee : 0;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > AnalyticsThresholds.RoundingDollarFloor;
    }

    public sealed record KpiBillingsRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double FeeBilled,
        double UnpostedFeeBilled,
        double Fee,
        double PercentBilled,
        double ContributionPercent)
    {
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double PercentBilledWithUnposted => Fee > 0 ? FeeBilledWithUnposted / Fee : 0;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > AnalyticsThresholds.RoundingDollarFloor;
    }

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
