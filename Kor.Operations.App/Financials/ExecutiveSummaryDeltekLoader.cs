#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials.Loaders;
using Serilog;

namespace Kor.Operations.Financials;

public sealed record ExecutiveSummaryDeltekData(
    double CashTotal,
    double CashCombinedCadEquivalent,
    double CashCad,
    double CashUsa,
    double CashBcc,
    double CashUsdToCadRate,
    string CashPeriod,
    IReadOnlyList<CashHistoryPoint> CashHistory,
    IReadOnlyList<CashAccountBalanceRow> CashPerAccount,
    double UtilizationPct30,
    double UtilizationBillableHours30,
    double UtilizationTotalHours30,
    IReadOnlyList<UtilizationProjectRow> UtilizationProjectRows,
    double ArOutstanding,
    double ArScopedOutstanding,
    double ArOver60,
    double ArFirmwideOutstanding,
    double ArFirmwideOver60,
    double ArFirmwideOutstandingCad,
    double ArFirmwideOutstandingUsa,
    double ArFirmwideUsdToCadRate,
    IReadOnlyList<ArProjectOutstandingRow> ArProjectRows,
    IReadOnlyList<ArInvoiceOutstandingRow> ArInvoiceRows,
    double WipUnbilled,
    double WipOverbilled,
    double WipUnbilledNet,
    string WipUnbilledPeriod,
    IReadOnlyList<WipProjectBreakdownRow> WipProjectRows,
    double FirmWipUnbilled,
    double FirmWipOverbilled,
    double FirmWipNet,
    double Revenue30,
    double Revenue90,
    double Billed30,
    double Billed90,
    IReadOnlyList<TrendPayerAmountRow> RevenuePayerRows,
    IReadOnlyList<TrendPayerAmountRow> BilledPayerRows,
    IReadOnlyList<TrendPayerAmountRow> ArPayerRows,
    double[] RevenueSeries,
    double[] BilledSeries,
    double[] ArSeries,
    bool RevenueGenerationDetected,
    bool WipDataLoaded,
    IReadOnlyList<string>? SchemaDriftMessages = null,
    double LedgerArInvoicedSinceLatestPosted = 0.0,
    int LedgerArInvoicedSincePeriod = 0,
    double NetServiceRevenue12Mo = 0.0,
    double DirectLaborCost12Mo = 0.0,
    double NetMultiplier = 0.0,
    double NetProfit12Mo = 0.0,
    double DaysSalesOutstanding = 0.0,
    bool FirmHealthDataLoaded = false,
    IReadOnlyList<string>? LoaderFailureMessages = null,
    // Phase 12-5a: split the firmwide AR Outstanding into invoices currently
    // tied to a non-Resolved Mcp.CollectionsCase ("InCollections") vs the
    // rest ("Regular"). Both are CAD-equivalent. When the caller didn't
    // hand in an active-invoice set (legacy callers), both stay at zero
    // and consumers should keep falling back to ArFirmwideOutstanding.
    double ArFirmwideOutstandingInCollections = 0.0,
    double ArFirmwideOutstandingRegular = 0.0,
    double GlRevenue12Mo = 0.0,
    double GlExpenses12Mo = 0.0,
    double GlNetIncome12Mo = 0.0,
    int GlPnlFromPeriod = 0,
    int GlPnlToPeriod = 0,
    int? GlPnlMaxPostedPeriod = null,
    bool GlPnlDataLoaded = false);

public sealed record TrendPayerAmountRow(
    string Wbs1,
    string PayerName,
    double Amount);

public sealed record WipProjectBreakdownRow(
    string Wbs1,
    double Earned,
    double Overbilled,
    double Net,
    string Period);

public sealed record UtilizationProjectRow(
    string Wbs1,
    double BillableHours,
    double TotalHours)
{
    public double NonBillableHours => Math.Max(0.0, TotalHours - BillableHours);
    public double UtilizationPct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
}

internal sealed record CalRow(string Period, DateTime StartDate, DateTime EndDate);
internal sealed record PrAgg(string Period, double Revenue, double Billed, double Ar, double UnbilledNet, double UnbilledEarned, double Overbilled);
internal sealed record SeriesPeriod(string Period, DateTime EndDate, double Revenue, double Billed, double Ar, double UnbilledNet, double UnbilledEarned, double Overbilled);
internal sealed record BuiltSeries(List<SeriesPeriod> Periods, double LatestUnbilledEarned, double LatestOverbilled, double LatestUnbilledNet, string LatestPeriod);

public sealed class ExecutiveSummaryDeltekLoader
{
    private readonly DeltekOdbcOptions _odbcOptions;
    private readonly FinancialsOptions _financialsOptions;
    private readonly CashFinancialsService _cashService;
    private readonly ArFinancialsService _arService;

    public ExecutiveSummaryDeltekLoader(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
    {
        _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
        _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
        _cashService = new CashFinancialsService(_odbcOptions, _financialsOptions);
        _arService = new ArFinancialsService(_odbcOptions, _financialsOptions);
        if (!string.IsNullOrWhiteSpace(odbcOptions.Catalog))
            ExecutiveSummaryLoaderSupport.Catalog = DeltekCatalogValidator.ValidateCatalog(odbcOptions.Catalog);
    }

    public Task<ExecutiveSummaryDeltekData?> TryLoadAsync(IEnumerable<string> wbs1List, CancellationToken ct)
        => TryLoadAsync(wbs1List, activeCaseInvoices: null, ct);

    // Phase 12-5a: caller can pass an active-collections-case invoice set to
    // get the AR Outstanding split (Regular vs InCollections). Existing
    // callers using the no-set overload above continue to get legacy behavior
    // with both new fields zeroed.
    public Task<ExecutiveSummaryDeltekData?> TryLoadAsync(
        IEnumerable<string> wbs1List,
        IReadOnlySet<(string Wbs1, string Invoice)>? activeCaseInvoices,
        CancellationToken ct)
    {
        var wbs1 = (wbs1List ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Note: empty wbs1 list is allowed. Cash, firmwide AR, and firmwide WIP-draft
        // do not depend on a project list — losing all of them when scope filters to
        // zero projects would silently hide the firm's headline financials.

        return Task.Run<ExecutiveSummaryDeltekData?>(() =>
        {
            using var ctxWbs1 = Serilog.Context.LogContext.PushProperty("Wbs1Count", wbs1.Count);
            using var ctxCatalog = Serilog.Context.LogContext.PushProperty("Catalog", ExecutiveSummaryLoaderSupport.Catalog);

            using var cn = OpenConnection();
            cn.Open();

            // Preflight: verify the columns the loaders depend on still exist
            // in the live catalog. Memoized per-process per-catalog, so this
            // adds one round-trip on first load and zero on subsequent calls.
            IReadOnlyList<string> schemaDrift;
            try
            {
                schemaDrift = DeltekSchemaValidator
                    .ValidateAsync(cn, ExecutiveSummaryLoaderSupport.Catalog, ct)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Schema validation failed in {Loader}; assuming clean.", nameof(ExecutiveSummaryDeltekLoader));
                schemaDrift = Array.Empty<string>();
            }

            // Two FX rates are tracked separately so cash-on-hand FX can diverge from
            // billed-revenue FX if needed: Cash uses Financials.Cash.UsdToCadRate; AR/
            // revenue/WIP and project rollups elsewhere use Financials.Billed.UsdToCadRate.
            // They default to the same value but can be configured independently.
            var billedFxRate = OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);

            // Capture per-loader exceptions in a list the UI surfaces as an amber
            // "Loader Failures" banner. Without this, a loader catch silently
            // returns Empty and the user sees a $0 tile with no clue why
            // (the AR=$0 and WIP=$0 incidents both manifested this way).
            var loaderFailures = new List<string>();

            CashFinancialsResult cash;
            try { cash = _cashService.Load(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load cash data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                cash = CashFinancialsResult.Empty;
                loaderFailures.Add($"Cash: {ex.GetType().Name}: {ex.Message}");
            }

            RevenueLoadResult revenue;
            try { revenue = RevenueLoader.Load(cn, wbs1, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load revenue data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                revenue = RevenueLoadResult.Empty;
                loaderFailures.Add($"Revenue: {ex.GetType().Name}: {ex.Message}");
            }

            WipLoadResult wip;
            try { wip = WipLoader.Load(cn, wbs1, revenue.PrByPeriod, revenue.Series, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load WIP data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                wip = WipLoadResult.Empty;
                loaderFailures.Add($"WIP: {ex.GetType().Name}: {ex.Message}");
            }

            UtilizationLoadResult utilization;
            try { utilization = UtilizationLoader.Load(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load utilization data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                utilization = UtilizationLoadResult.Empty;
                loaderFailures.Add($"Utilization: {ex.GetType().Name}: {ex.Message}");
            }

            ArFinancialsResult ar;
            try { ar = _arService.Load(cn, wbs1, billedFxRate, activeCaseInvoices, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load AR data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                ar = ArFinancialsResult.Empty;
                loaderFailures.Add($"AR: {ex.GetType().Name}: {ex.Message}");
            }

            FirmHealthLoadResult firmHealth;
            try { firmHealth = FirmHealthLoader.Load(cn, ar.FirmwideOutstandingCadEquiv, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load firm-health metrics in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                firmHealth = FirmHealthLoadResult.Empty;
                loaderFailures.Add($"Net Multiplier / DSO: {ex.GetType().Name}: {ex.Message}");
            }

            GlPnlT12moLoadResult glPnl;
            try { glPnl = GlPnlT12moLoader.Load(cn, _financialsOptions, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load GL P&L T12mo summary in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                glPnl = GlPnlT12moLoadResult.Empty;
                loaderFailures.Add($"GL Net Income: {ex.GetType().Name}: {ex.Message}");
            }

            return Assemble(cash, utilization, ar, wip, revenue, firmHealth, glPnl, wbs1, schemaDrift, loaderFailures);
        }, ct);
    }

    private OdbcConnection OpenConnection()
    {
        var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
        var user = _odbcOptions.User ?? string.Empty;
        var pwd = _odbcOptions.Password ?? string.Empty;
        var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
        return factory.Create();
    }

    /// <summary>
    /// Type-3 silent-zero detection: a loader can return cleanly (no exception)
    /// but produce all zeros when its predicate filtered every row out.
    /// Examples already burned us this sprint: AR=$0 from SQL contamination
    /// (Batch 5), Net Multiplier=0.00 from the .00 account-format mismatch
    /// (Batches 21–23). For tiles where firmwide $0 is structurally
    /// implausible at a 50-person AEC firm, flag it on the same banner that
    /// surfaces loader exceptions.
    /// </summary>
    private static List<string> DetectSuspiciousZeros(
        CashFinancialsResult cash,
        ArFinancialsResult ar,
        FirmHealthLoadResult firmHealth)
    {
        var issues = new List<string>();
        const double zeroThreshold = AnalyticsThresholds.RoundingDollarFloor;

        if (cash.CombinedCadEquivalent <= zeroThreshold)
            issues.Add("Cash Position firmwide is $0 — likely no GLSummary entries for whitelisted bank accounts, or whitelist/format mismatch.");

        if (ar.FirmwideOutstandingCadEquiv <= zeroThreshold)
            issues.Add("AR Outstanding firmwide is $0 — predicate likely filtered every row (SQL contamination, missing column, or empty AR table).");

        if (firmHealth.DataLoaded)
        {
            if (firmHealth.NetServiceRevenue12Mo <= zeroThreshold)
                issues.Add("Trailing-12mo billed is $0 — LedgerAR predicate filtered every row (typically the Account-format trap or wrong account list).");
            if (firmHealth.DirectLaborCost12Mo <= zeroThreshold)
                issues.Add("Trailing-12mo Direct Labor Cost is $0 — tkDetail predicate filtered every row (check LaborCode/WBS1 overhead filters).");
        }

        return issues;
    }

    private static ExecutiveSummaryDeltekData Assemble(
        CashFinancialsResult cash,
        UtilizationLoadResult utilization,
        ArFinancialsResult ar,
        WipLoadResult wip,
        RevenueLoadResult revenue,
        FirmHealthLoadResult firmHealth,
        GlPnlT12moLoadResult glPnl,
        IReadOnlyList<string> scopedWbs1,
        IReadOnlyList<string> schemaDrift,
        IReadOnlyList<string> loaderFailures)
    {
        // Fold Type-3 silent-zero issues into the loader-failures banner.
        var combinedFailures = loaderFailures.ToList();
        combinedFailures.AddRange(DetectSuspiciousZeros(cash, ar, firmHealth));
        var scopedArOutstanding = 0.0;
        if (scopedWbs1.Count > 0)
        {
            var wbs1Set = new HashSet<string>(scopedWbs1, StringComparer.OrdinalIgnoreCase);
            scopedArOutstanding = ar.ProjectRows
                .Where(r => wbs1Set.Contains((r.Wbs1 ?? string.Empty).Trim()))
                .Sum(r => r.Total);
        }

        IReadOnlyList<TrendPayerAmountRow> arPayerRows;
        try
        {
            arPayerRows = ar.ProjectRows
                .Where(r => Math.Abs(r.Total) > AnalyticsThresholds.RoundingDollarFloor)
                .Select(r =>
                {
                    var key = (r.Wbs1 ?? string.Empty).Trim();
                    revenue.PayerByWbs.TryGetValue(key, out var payer);
                    payer = string.IsNullOrWhiteSpace(payer) ? key : payer.Trim();
                    return new TrendPayerAmountRow(key, payer, r.Total);
                })
                .OrderByDescending(r => r.Amount)
                .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                .Take(250)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build AR payer breakdown in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
            arPayerRows = new List<TrendPayerAmountRow>();
        }

        return new ExecutiveSummaryDeltekData(
            cash.Total, cash.CombinedCadEquivalent, cash.Cad, cash.Usa, cash.Bcc, cash.UsdToCadRate, cash.Period, cash.History, cash.PerAccount,
            utilization.Pct, utilization.BillableHours, utilization.TotalHours, utilization.ProjectRows,
            ar.Outstanding, scopedArOutstanding, ar.Over60,
            ar.FirmwideOutstandingCadEquiv, ar.FirmwideOver60CadEquiv,
            ar.FirmwideOutstandingCad, ar.FirmwideOutstandingUsa, ar.UsdToCadRate,
            ar.ProjectRows, ar.InvoiceRows,
            wip.WipUnbilled, wip.WipOverbilled, wip.WipUnbilledNet, wip.WipUnbilledPeriod, wip.WipProjectRows,
            wip.FirmWipUnbilled, wip.FirmWipOverbilled, wip.FirmWipNet,
            revenue.Revenue30, revenue.Revenue90, revenue.Billed30, revenue.Billed90,
            revenue.RevenuePayerRows, revenue.BilledPayerRows, arPayerRows,
            revenue.Series.Periods.Select(p => p.Revenue).ToArray(),
            revenue.Series.Periods.Select(p => p.Billed).ToArray(),
            revenue.Series.Periods.Select(p => p.Ar).ToArray(),
            wip.RevenueGenerationDetected,
            wip.DataLoaded,
            schemaDrift,
            LedgerArInvoicedSinceLatestPosted: revenue.LedgerArInvoicedSinceLatestPosted,
            LedgerArInvoicedSincePeriod: revenue.LedgerArInvoicedSincePeriod,
            NetServiceRevenue12Mo: firmHealth.NetServiceRevenue12Mo,
            DirectLaborCost12Mo: firmHealth.DirectLaborCost12Mo,
            NetMultiplier: firmHealth.NetMultiplier,
            NetProfit12Mo: firmHealth.NetProfit12Mo,
            DaysSalesOutstanding: firmHealth.DaysSalesOutstanding,
            FirmHealthDataLoaded: firmHealth.DataLoaded,
            LoaderFailureMessages: combinedFailures,
            ArFirmwideOutstandingInCollections: ar.FirmwideOutstandingInCollectionsCadEquiv,
            ArFirmwideOutstandingRegular: ar.FirmwideOutstandingRegularCadEquiv,
            GlRevenue12Mo: glPnl.Revenue12Mo,
            GlExpenses12Mo: glPnl.Expenses12Mo,
            GlNetIncome12Mo: glPnl.NetIncome12Mo,
            GlPnlFromPeriod: glPnl.FromPeriod,
            GlPnlToPeriod: glPnl.ToPeriod,
            GlPnlMaxPostedPeriod: glPnl.MaxPostedPeriod,
            GlPnlDataLoaded: glPnl.DataLoaded);
    }
}

internal static class ExecutiveSummaryLoaderSupport
{
    internal static string Catalog { get; set; } = "C0000052267P_1_KOR00000000";

    /// <summary>
    /// Chunk size for WBS1 IN-list parameters across Deltek ODBC queries.
    /// SQL Server allows up to 2100 parameters per batch; 80 keeps us well
    /// under that ceiling with headroom for additional parameters (date
    /// ranges, org filters, table joins) that some queries layer on top.
    /// </summary>
    internal const int OdbcParameterChunkSize = 80;

    internal static string GetTrimmed(IDataRecord r, int i)
        => DataReaderHelpers.GetTrimmed(r, i);

    internal static DateTime GetDate(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return DateTime.MinValue;
        var v = r.GetValue(i);
        if (v is DateTime dt) return dt.Date;
        if (DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed.Date;
        return DateTime.MinValue;
    }

    internal static DateTime? GetDateOrNull(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        var d = GetDate(r, i);
        return d == DateTime.MinValue ? null : d;
    }

    internal static DateTime? MergeOldest(DateTime? a, DateTime? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return a.Value <= b.Value ? a : b;
    }

    internal static double GetDouble(IDataRecord r, int i)
        => DataReaderHelpers.GetDouble(r, i);

    internal static string MakeInListPlaceholders(int count)
        => string.Join(", ", Enumerable.Repeat("?", count));

    internal static void AddInListParameters(OdbcCommand cmd, List<string> vals)
    {
        foreach (var v in vals)
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
    }

    internal static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
    {
        for (var i = 0; i < src.Count; i += size)
            yield return src.GetRange(i, Math.Min(size, src.Count - i));
    }
}
