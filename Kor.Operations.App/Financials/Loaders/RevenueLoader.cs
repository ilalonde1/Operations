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
    Dictionary<string, string> PayerByWbs)
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
    public static RevenueLoadResult Load(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        Dictionary<string, CalRow> calendar;
        try { calendar = TryLoadCalendar(cn, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load accounting calendar in {Loader}.", nameof(RevenueLoader));
            calendar = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, PrAgg> prByPeriod;
        try { prByPeriod = LoadPrSummaryByPeriod(cn, wbs1, ct); }
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

        var cutoff30 = DateTime.Today.AddDays(-30);
        var cutoff90 = DateTime.Today.AddDays(-90);

        var revenue30 = series.Periods.Where(p => p.EndDate >= cutoff30).Sum(p => p.Revenue);
        var revenue90 = series.Periods.Where(p => p.EndDate >= cutoff90).Sum(p => p.Revenue);
        var billed30 = series.Periods.Where(p => p.EndDate >= cutoff30).Sum(p => p.Billed);
        var billed90 = series.Periods.Where(p => p.EndDate >= cutoff90).Sum(p => p.Billed);
        var recentPeriods = series.Periods
            .Where(p => p.EndDate >= cutoff90)
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
            revenuePayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Revenue", payerByWbs, ct);
            billedPayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Billed", payerByWbs, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load revenue and billing payer breakdowns in {Loader}.", nameof(RevenueLoader));
            revenuePayerRows = new List<TrendPayerAmountRow>();
            billedPayerRows = new List<TrendPayerAmountRow>();
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
            payerByWbs);
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

    private static Dictionary<string, PrAgg> LoadPrSummaryByPeriod(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var acc = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Period,
       SUM(COALESCE(Revenue,0))  AS Revenue,
       SUM(COALESCE(Billed,0))   AS Billed,
       SUM(COALESCE(AR,0))       AS AR,
       SUM(COALESCE(Unbilled,0)) AS UnbilledNet,
       SUM(CASE WHEN COALESCE(Unbilled,0) > 0 THEN COALESCE(Unbilled,0) ELSE 0 END) AS UnbilledEarned,
       SUM(CASE WHEN COALESCE(Unbilled,0) < 0 THEN -COALESCE(Unbilled,0) ELSE 0 END) AS Overbilled
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY Period;";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (period.Length == 0) continue;

                var rev = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                var billed = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
                var ar = ExecutiveSummaryLoaderSupport.GetDouble(r, 3);
                var unbilledNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 4);
                var unbilledEarned = ExecutiveSummaryLoaderSupport.GetDouble(r, 5);
                var overbilled = ExecutiveSummaryLoaderSupport.GetDouble(r, 6);

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
        CancellationToken ct)
    {
        if (wbs1.Count == 0 || periods.Count == 0)
            return new List<TrendPayerAmountRow>();

        var field = string.Equals(amountField, "Billed", StringComparison.OrdinalIgnoreCase) ? "Billed" : "Revenue";
        var byWbs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var periodPlaceholders = ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(periods.Count);
            cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE({field},0)) AS Amount
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND Period IN ({periodPlaceholders})
GROUP BY WBS1;";

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
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                if (Math.Abs(amt) < 0.004) continue;

                if (byWbs.TryGetValue(w, out var existing))
                    byWbs[w] = existing + amt;
                else
                    byWbs[w] = amt;
            }
        }

        return byWbs
            .Where(kvp => Math.Abs(kvp.Value) > 0.004)
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

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
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
