#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using Kor.Operations.Data;
using Serilog;

namespace Kor.Operations.Financials.Loaders;

internal sealed record RevenueLoadResult(
    Dictionary<string, PrAgg> PrByPeriod,
    BuiltSeries Series,
    double Revenue30,
    double Revenue90,
    double Billed30,
    double Billed90,
    IReadOnlyList<TrendPayerAmountRow> RevenuePayerRows,
    IReadOnlyList<TrendPayerAmountRow> BilledPayerRows,
    Dictionary<string, string> PayerByWbs,
    // Real-time invoiced from LedgerAR for periods AFTER the latest closed
    // PRSummaryMain period — fills the gap between posted close and today.
    double LedgerArInvoicedSinceLatestPosted = 0.0,
    int LedgerArInvoicedSincePeriod = 0)
{
    internal static readonly RevenueLoadResult Empty = new(
        new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase),
        new BuiltSeries(new List<SeriesPeriod>(), 0.0, 0.0, 0.0, "n/a"),
        0.0,
        0.0,
        0.0,
        0.0,
        new List<TrendPayerAmountRow>(),
        new List<TrendPayerAmountRow>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

internal static class RevenueLoader
{
    public static RevenueLoadResult Load(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
    {
        Dictionary<string, CalRow> calendar;
        try { calendar = TryLoadCalendar(cn, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load accounting calendar in {Loader}.", nameof(RevenueLoader));
            calendar = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, PrAgg> prByPeriod;
        try { prByPeriod = LoadPrSummaryByPeriod(cn, wbs1, usdToCadRate, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load PR summary data by period in {Loader}.", nameof(RevenueLoader));
            prByPeriod = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);
        }

        BuiltSeries series;
        try { series = BuildSeries(prByPeriod, calendar, points: 12); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build revenue series in {Loader}.", nameof(RevenueLoader));
            series = new BuiltSeries(new List<SeriesPeriod>(), 0.0, 0.0, 0.0, "n/a");
        }

        // PRSummaryMain posts on period close (~3 month lag at KOR), so a strict
        // calendar window (today−30 / today−90) collapses to $0 in the steady state
        // and produces misleading "we invoiced $0 in 90 days" headlines. Use the
        // latest closed PERIOD instead — always meaningful, regardless of lag.
        // 30d slot → latest 1 period; 90d slot → last 3 closed periods.
        var sortedPeriods = series.Periods
            .OrderBy(p => p.EndDate)
            .ToList();
        var lastPeriod = sortedPeriods.Count == 0 ? null : sortedPeriods[^1];
        var last3 = sortedPeriods.TakeLast(3).ToList();

        var revenue30 = lastPeriod?.Revenue ?? 0.0;
        var revenue90 = last3.Sum(p => p.Revenue);
        var billed30 = lastPeriod?.Billed ?? 0.0;
        var billed90 = last3.Sum(p => p.Billed);
        var recentPeriods = last3
            .Select(p => (p.Period ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, string> payerByWbs;
        try { payerByWbs = LoadPayerByWbs1(cn, wbs1, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load payer mapping by project in {Loader}.", nameof(RevenueLoader));
            payerByWbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyList<TrendPayerAmountRow> revenuePayerRows;
        IReadOnlyList<TrendPayerAmountRow> billedPayerRows;
        try
        {
            revenuePayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Revenue", payerByWbs, usdToCadRate, ct);
            billedPayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Billed", payerByWbs, usdToCadRate, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load revenue and billing payer breakdowns in {Loader}.", nameof(RevenueLoader));
            revenuePayerRows = new List<TrendPayerAmountRow>();
            billedPayerRows = new List<TrendPayerAmountRow>();
        }

        // Real-time invoiced from LedgerAR for periods after the latest closed
        // PRSummaryMain period. PRSummaryMain has a ~3-month posting lag at KOR;
        // LedgerAR captures invoices the moment they're cut. This fills the gap.
        var ledgerArSinceLatestPosted = 0.0;
        var ledgerArSincePeriodInt = 0;
        if (lastPeriod != null && wbs1.Count > 0)
        {
            if (int.TryParse((lastPeriod.Period ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var latestInt) && latestInt > 0)
            {
                ledgerArSincePeriodInt = NextPeriodInt(latestInt);
                try
                {
                    ledgerArSinceLatestPosted = LoadLedgerArInvoicedSince(cn, wbs1, ledgerArSincePeriodInt, usdToCadRate, ct);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load real-time LedgerAR invoicing in {Loader}.", nameof(RevenueLoader));
                    ledgerArSinceLatestPosted = 0.0;
                }
            }
        }

        return new RevenueLoadResult(
            prByPeriod,
            series,
            revenue30,
            revenue90,
            billed30,
            billed90,
            revenuePayerRows,
            billedPayerRows,
            payerByWbs,
            LedgerArInvoicedSinceLatestPosted: ledgerArSinceLatestPosted,
            LedgerArInvoicedSincePeriod: ledgerArSincePeriodInt);
    }

    private static int NextPeriodInt(int yyyymm)
    {
        var year = yyyymm / 100;
        var month = yyyymm % 100;
        if (month >= 12) return (year + 1) * 100 + 1;
        return year * 100 + (month + 1);
    }

    // Sums LedgerAR invoiced revenue (TransType='IN', accounts 4001/4003/4220/4500)
    // for the scoped WBS1 set, restricted to Period >= sincePeriodInt.
    // Buckets by Org so USA-org rows can be FX-converted to CAD-equivalent.
    // Revenue stored as -Amount per Deltek convention; SUM(-Amount) recovers the
    // positive invoiced figure.
    private static double LoadLedgerArInvoicedSince(
        OdbcConnection cn,
        List<string> wbs1,
        int sincePeriodInt,
        double usdToCadRate,
        CancellationToken ct)
    {
        var cadTotal = 0.0;
        var usaTotal = 0.0;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Account codes at KOR are stored with a ".00" suffix (e.g. '4001.00').
            // Match on 4-char prefix so the predicate is robust to catalog-level
            // formatting choices (some installations store as '4001', some padded).
            cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-Amount) AS Invoiced
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4220', '4500')
  AND Period >= ?
  AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = sincePeriodInt });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                    usaTotal += amt;
                else
                    cadTotal += amt;
            }
        }

        return cadTotal + (usaTotal * usdToCadRate);
    }

    private static Dictionary<string, CalRow> TryLoadCalendar(OdbcConnection cn, CancellationToken ct)
    {
        var map = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT Period, StartDate, EndDate
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.CFGAcctngCalendarData
ORDER BY Period;";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();

            var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            if (period.Length == 0) continue;

            var start = ExecutiveSummaryLoaderSupport.GetDate(r, 1);
            var end = ExecutiveSummaryLoaderSupport.GetDate(r, 2);
            if (start == DateTime.MinValue || end == DateTime.MinValue) continue;

            map[period] = new CalRow(period, start, end);
        }

        return map;
    }

    private static Dictionary<string, PrAgg> LoadPrSummaryByPeriod(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
    {
        var acc = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Bucket by Org so USA-org per-period sums can be FX-converted before being added
            // to the period total (Revenue, Billed, AR, Unbilled split). Otherwise WIP-derived
            // metrics (BuiltSeries, Earned, Overbilled) inherit a CAD+USD mix.
            cmd.CommandText = $@"
SELECT sm.Period,
       CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
       SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue,0) END) AS Revenue,
       SUM(COALESCE(sm.Billed,0))   AS Billed,
       SUM(COALESCE(sm.AR,0))       AS AR,
       SUM(COALESCE(sm.Unbilled,0)) AS UnbilledNet,
       SUM(CASE WHEN COALESCE(sm.Unbilled,0) > 0 THEN COALESCE(sm.Unbilled,0) ELSE 0 END) AS UnbilledEarned,
       SUM(CASE WHEN COALESCE(sm.Unbilled,0) < 0 THEN -COALESCE(sm.Unbilled,0) ELSE 0 END) AS Overbilled
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.Period, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (period.Length == 0) continue;

                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;

                var rev = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                var billed = ExecutiveSummaryLoaderSupport.GetDouble(r, 3) * fx;
                var ar = ExecutiveSummaryLoaderSupport.GetDouble(r, 4) * fx;
                var unbilledNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 5) * fx;
                var unbilledEarned = ExecutiveSummaryLoaderSupport.GetDouble(r, 6) * fx;
                var overbilled = ExecutiveSummaryLoaderSupport.GetDouble(r, 7) * fx;

                if (acc.TryGetValue(period, out var existing))
                {
                    acc[period] = existing with
                    {
                        Revenue = existing.Revenue + rev,
                        Billed = existing.Billed + billed,
                        Ar = existing.Ar + ar,
                        UnbilledNet = existing.UnbilledNet + unbilledNet,
                        UnbilledEarned = existing.UnbilledEarned + unbilledEarned,
                        Overbilled = existing.Overbilled + overbilled
                    };
                }
                else
                {
                    acc[period] = new PrAgg(period, rev, billed, ar, unbilledNet, unbilledEarned, overbilled);
                }
            }
        }

        return acc;
    }

    private static List<TrendPayerAmountRow> LoadPrAmountByProjectForPeriods(
        OdbcConnection cn,
        List<string> wbs1,
        List<string> periods,
        string amountField,
        IReadOnlyDictionary<string, string> payerByWbs,
        double usdToCadRate,
        CancellationToken ct)
    {
        if (wbs1.Count == 0 || periods.Count == 0)
            return new List<TrendPayerAmountRow>();

        var field = string.Equals(amountField, "Billed", StringComparison.OrdinalIgnoreCase)
            ? "COALESCE(sm.Billed, 0)"
            : "CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END";
        var byWbs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var periodPlaceholders = ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(periods.Count);
            cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM({field}) AS Amount
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND sm.Period IN ({periodPlaceholders})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            foreach (var period in periods)
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.VarChar,
                    Value = period
                });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                if (Math.Abs(amt) < AnalyticsThresholds.RoundingDollarFloor) continue;

                if (byWbs.TryGetValue(w, out var existing))
                    byWbs[w] = existing + amt;
                else
                    byWbs[w] = amt;
            }
        }

        return byWbs
            .Where(kvp => Math.Abs(kvp.Value) > AnalyticsThresholds.RoundingDollarFloor)
            .Select(kvp =>
            {
                payerByWbs.TryGetValue(kvp.Key, out var payer);
                payer = string.IsNullOrWhiteSpace(payer) ? kvp.Key : payer.Trim();
                return new TrendPayerAmountRow(kvp.Key, payer, kvp.Value);
            })
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToList();
    }

    private static Dictionary<string, string> LoadPayerByWbs1(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (wbs1.Count == 0)
            return map;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    WBS1,
    COALESCE(NULLIF(LTRIM(RTRIM(Name)),''), WBS1) AS PayerName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '');";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var payer = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                map[w] = payer.Length == 0 ? w : payer;
            }
        }

        return map;
    }

    private static BuiltSeries BuildSeries(Dictionary<string, PrAgg> prByPeriod, Dictionary<string, CalRow> cal, int points)
    {
        var unbilledColumnHasAny =
            prByPeriod.Values.Any(p =>
                Math.Abs(p.UnbilledNet) > 1e-9 ||
                Math.Abs(p.UnbilledEarned) > 1e-9 ||
                Math.Abs(p.Overbilled) > 1e-9);

        DateTime PeriodEnd(string period)
        {
            if (cal.TryGetValue(period, out var c)) return c.EndDate.Date;

            if (period.Length == 6 &&
                int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                m >= 1 && m <= 12)
            {
                return new DateTime(y, m, 1).AddMonths(1).AddDays(-1);
            }

            return DateTime.MinValue;
        }

        var list = prByPeriod.Values
            .Select(p =>
            {
                var unbilledNet = p.UnbilledNet;
                var unbilledEarned = p.UnbilledEarned;
                var overbilled = p.Overbilled;

                if (!unbilledColumnHasAny &&
                    Math.Abs(unbilledNet) < 1e-9 && Math.Abs(unbilledEarned) < 1e-9 && Math.Abs(overbilled) < 1e-9 &&
                    (Math.Abs(p.Revenue) > 1e-9 || Math.Abs(p.Billed) > 1e-9))
                {
                    var proxy = p.Revenue - p.Billed;
                    unbilledNet = proxy;
                    if (proxy > 0) unbilledEarned = proxy;
                    else if (proxy < 0) overbilled = -proxy;
                }

                return new SeriesPeriod(p.Period, PeriodEnd(p.Period), p.Revenue, p.Billed, p.Ar, unbilledNet, unbilledEarned, overbilled);
            })
            .Where(p => p.EndDate != DateTime.MinValue)
            .OrderBy(p => p.EndDate)
            .ToList();

        var closed = list.Where(p => p.EndDate <= DateTime.Today).ToList();
        var use = closed.Count > 0 ? closed : list;

        if (use.Count > points)
            use = use.TakeLast(points).ToList();

        if (!unbilledColumnHasAny)
        {
            var closedAll = closed.Count > 0 ? closed : list;
            var latestPeriod = closedAll.Count == 0 ? "n/a" : closedAll[^1].Period;
            var balanceNet = closedAll.Sum(p => p.UnbilledNet);
            var balanceEarned = Math.Max(balanceNet, 0.0);
            var balanceOverbilled = Math.Max(-balanceNet, 0.0);
            return new BuiltSeries(use, balanceEarned, balanceOverbilled, balanceNet, latestPeriod);
        }

        var latestClosed = use.Count == 0 ? null : use[^1];
        var latestEarned = latestClosed == null ? 0.0 : latestClosed.UnbilledEarned;
        var latestOverbilled = latestClosed == null ? 0.0 : latestClosed.Overbilled;
        var latestNet = latestClosed == null ? 0.0 : latestClosed.UnbilledNet;
        var latestPeriodOut = latestClosed == null ? "n/a" : latestClosed.Period;
        return new BuiltSeries(use, latestEarned, latestOverbilled, latestNet, latestPeriodOut);
    }
}
