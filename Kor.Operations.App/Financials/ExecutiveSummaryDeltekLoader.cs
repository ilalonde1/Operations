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
    int LedgerArInvoicedSincePeriod = 0);

public sealed record TrendPayerAmountRow(
    string Wbs1,
    string PayerName,
    double Amount);

public sealed record CashHistoryPoint(
    string Period,
    double Cad,
    double Usa,
    double Bcc)
{
    public double Total => Cad + Usa + Bcc;
}

public sealed record ArProjectOutstandingRow(
    string Wbs1,
    string ProjectName,
    string Pm,
    double Total,
    double Current,
    double Aged31To60,
    double Aged61To90,
    double Aged90Plus,
    DateTime? OldestInvoiceDate);

public sealed record ArInvoiceOutstandingRow(
    string Wbs1,
    string ProjectName,
    string Pm,
    DateTime? InvoiceDate,
    DateTime? DueDate,
    int DaysPastDue,
    double Balance);

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

    public ExecutiveSummaryDeltekLoader(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
    {
        _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
        _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
        if (!string.IsNullOrWhiteSpace(odbcOptions.Catalog))
            ExecutiveSummaryLoaderSupport.Catalog = DeltekCatalogValidator.ValidateCatalog(odbcOptions.Catalog);
    }

    public Task<ExecutiveSummaryDeltekData?> TryLoadAsync(IEnumerable<string> wbs1List, CancellationToken ct)
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

            CashLoadResult cash;
            try { cash = CashLoader.Load(cn, _financialsOptions, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load cash data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                cash = CashLoadResult.Empty;
            }

            RevenueLoadResult revenue;
            try { revenue = RevenueLoader.Load(cn, wbs1, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load revenue data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                revenue = RevenueLoadResult.Empty;
            }

            WipLoadResult wip;
            try { wip = WipLoader.Load(cn, wbs1, revenue.PrByPeriod, revenue.Series, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load WIP data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                wip = WipLoadResult.Empty;
            }

            UtilizationLoadResult utilization;
            try { utilization = UtilizationLoader.Load(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load utilization data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                utilization = UtilizationLoadResult.Empty;
            }

            ArLoadResult ar;
            try { ar = ArLoader.Load(cn, wbs1, billedFxRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load AR data in {Loader}.", nameof(ExecutiveSummaryDeltekLoader));
                ar = ArLoadResult.Empty;
            }

            return Assemble(cash, utilization, ar, wip, revenue, wbs1, schemaDrift);
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

    private static ExecutiveSummaryDeltekData Assemble(
        CashLoadResult cash,
        UtilizationLoadResult utilization,
        ArLoadResult ar,
        WipLoadResult wip,
        RevenueLoadResult revenue,
        IReadOnlyList<string> scopedWbs1,
        IReadOnlyList<string> schemaDrift)
    {
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
            LedgerArInvoicedSincePeriod: revenue.LedgerArInvoicedSincePeriod);
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
    {
        if (r.IsDBNull(i)) return string.Empty;
        var v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        return v.Trim();
    }

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
    {
        if (r.IsDBNull(i)) return 0.0;
        return ScalarToDouble(r.GetValue(i));
    }

    internal static double ScalarToDouble(object? v)
    {
        if (v == null || v == DBNull.Value) return 0.0;
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is decimal m) return (double)m;
        if (v is long l) return l;
        if (v is int n) return n;
        if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0.0;
    }

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
