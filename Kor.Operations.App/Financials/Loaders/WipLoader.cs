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

internal sealed record WipLoadResult(
    double WipUnbilled,
    double WipOverbilled,
    double WipUnbilledNet,
    string WipUnbilledPeriod,
    IReadOnlyList<WipProjectBreakdownRow> WipProjectRows,
    double FirmWipUnbilled,
    double FirmWipOverbilled,
    double FirmWipNet,
    bool RevenueGenerationDetected,
    bool DataLoaded)
{
    internal static readonly WipLoadResult Empty = new(
        0.0,
        0.0,
        0.0,
        "n/a",
        new List<WipProjectBreakdownRow>(),
        0.0,
        0.0,
        0.0,
        false,
        false);
}

internal static class WipLoader
{
    public static WipLoadResult Load(OdbcConnection cn, List<string> wbs1, Dictionary<string, PrAgg> prByPeriod, BuiltSeries series, double usdToCadRate, CancellationToken ct)
    {
        var unbilledColumnHasAny = prByPeriod.Values.Any(p =>
            Math.Abs(p.UnbilledNet) > 1e-9 ||
            Math.Abs(p.UnbilledEarned) > 1e-9 ||
            Math.Abs(p.Overbilled) > 1e-9);

        // RG-detection guards the WIP card from showing meaningless zeros when the
        // catalog has no revenue/unbilled signal at all. Two qualifying signals:
        //   1. The Unbilled column itself has data — that IS the WIP signal, no
        //      proxy needed. Sign-agnostic check (Math.Abs above).
        //   2. As a fallback, recent Revenue magnitude is >= 1% of Billed.
        //      Math.Abs is required because PRSummaryMain.Revenue is stored with
        //      Deltek's credit-side sign convention (negative = recognized);
        //      a signed comparison would always fail at catalogs like KOR's.
        var revenueGenerationDetected = unbilledColumnHasAny ||
            DetectRevenueGeneration(cn, prByPeriod, ct);
        if (!revenueGenerationDetected)
            return new WipLoadResult(
                0.0, 0.0, 0.0, "n/a",
                new List<WipProjectBreakdownRow>(),
                0.0, 0.0, 0.0,
                RevenueGenerationDetected: false,
                DataLoaded: true);

        var wipUnbilledPeriod = series.LatestPeriod;

        // Drilldown rows are the source of truth: the breakdown SQL queries
        // PRSummaryMain per WBS1 with the cumulative-balance window AND the
        // sign flip applied (PRSummaryMain.Unbilled and Revenue are stored
        // with the Deltek credit-side convention — negative values mean
        // earned-not-billed). Headline values are derived by summing rows so
        // Σrows == headline by construction. The previous path used
        // series.LatestUnbilled* which was a single-period delta with no sign
        // flip, producing two bugs simultaneously: wrong window AND inverted
        // earned/overbilled classification (e.g. a project with $X recognized
        // and $0 billed showed as overbilled when it was earned-not-billed).
        IReadOnlyList<WipProjectBreakdownRow> wipProjectRows;
        try
        {
            wipProjectRows = LoadWipProjectBreakdownByProject(cn, wbs1, wipUnbilledPeriod, useUnbilledAsOf: unbilledColumnHasAny, usdToCadRate, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load WIP project breakdown in {Loader} for Period={Period}.", nameof(WipLoader), wipUnbilledPeriod);
            wipProjectRows = new List<WipProjectBreakdownRow>();
        }

        var wipUnbilled = wipProjectRows.Sum(r => r.Earned);
        var wipOverbilled = wipProjectRows.Sum(r => r.Overbilled);
        var wipUnbilledNet = wipProjectRows.Sum(r => r.Net);

        (double Earned, double Overbilled, double Net) firmWip;
        try { firmWip = LoadFirmwideWipProxyBalance(cn, wipUnbilledPeriod, usdToCadRate, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load firmwide WIP balances in {Loader} for Period={Period}.", nameof(WipLoader), wipUnbilledPeriod);
            firmWip = (0.0, 0.0, 0.0);
        }

        return new WipLoadResult(
            wipUnbilled,
            wipOverbilled,
            wipUnbilledNet,
            wipUnbilledPeriod,
            wipProjectRows,
            firmWip.Earned,
            firmWip.Overbilled,
            firmWip.Net,
            true,
            true);
    }

    private static bool DetectRevenueGeneration(OdbcConnection cn, Dictionary<string, PrAgg> prByPeriod, CancellationToken ct)
    {
        var recentPeriods = prByPeriod.Values
            .Where(p => p.Period.Length == 6 && p.Period.All(char.IsDigit))
            .Select(p => p.Period)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (recentPeriods.Count == 0)
            return false;

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    SUM(COALESCE(Revenue, 0)) AS RawRevenue,
    SUM(COALESCE(Billed, 0)) AS Billed
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
WHERE Period IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(recentPeriods.Count)});";
        foreach (var period in recentPeriods)
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return false;

        var recentRevenue = ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
        var recentBilled = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
        // Math.Abs handles the credit-side storage convention (Revenue stored
        // as -Amount). A signed comparison reads negative values as "no
        // revenue" and incorrectly suppresses the WIP card.
        return recentBilled > 0.0 && Math.Abs(recentRevenue) > 0.01 * recentBilled;
    }

    private static string? LoadMaxPrSummaryPeriod(OdbcConnection cn, CancellationToken ct)
    {
        try
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $"SELECT MAX(Period) FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value) return null;
            var p = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            return (p.Length == 6 && p.All(char.IsDigit)) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    private static (double Earned, double Overbilled, double Net) LoadFirmwideWipProxyBalance(OdbcConnection cn, string asOfPeriod, double usdToCadRate, CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
        {
            // Don't fall back to a calendar month — that anchors WIP to a period Deltek
            // may not have posted yet (or to a future month). If MAX(Period) is unavailable,
            // return zeros so the tile shows "no data" rather than fabricated numbers.
            period = LoadMaxPrSummaryPeriod(cn, ct) ?? string.Empty;
            if (period.Length != 6 || !period.All(char.IsDigit))
                return (0.0, 0.0, 0.0);
        }

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // Per-project Net (source currency), bucketed by master-project Org. Earned vs
        // Overbilled split happens after FX conversion so a USA project's sign in CAD
        // matches its sign in USD (FX is positive multiplier). Net is computed as
        // Billed - Revenue (sign-flipped from raw storage) so positive=earned-not-billed
        // and negative=overbilled — matches Deltek's credit-side sign convention on
        // PRSummaryMain.Revenue.
        cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        double earned = 0.0, overbilled = 0.0, net = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
            var rawNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
            var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
            var nNet = rawNet * fx;
            if (nNet > 0) earned += nNet;
            else if (nNet < 0) overbilled += -nNet;
            net += nNet;
        }
        return (earned, overbilled, net);
    }

    private static List<WipProjectBreakdownRow> LoadWipProjectBreakdownByProject(
        OdbcConnection cn,
        List<string> wbs1,
        string asOfPeriod,
        bool useUnbilledAsOf,
        double usdToCadRate,
        CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
        {
            period = LoadMaxPrSummaryPeriod(cn, ct) ?? string.Empty;
            if (period.Length != 6 || !period.All(char.IsDigit))
                return new List<WipProjectBreakdownRow>();
        }

        var rows = new Dictionary<string, WipProjectBreakdownRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            // Both branches join PR for Org so the per-project Net can be FX-converted
            // to CAD-equivalent before being split into earned/overbilled. PRSummaryMain
            // stores Revenue and Unbilled with Deltek's credit-side sign convention
            // (negative = recognized revenue), so both branches flip the sign here so
            // downstream code reads positive=earned and negative=overbilled cleanly.
            if (useUnbilledAsOf)
            {
                cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-COALESCE(sm.Unbilled,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
  AND sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }
            else
            {
                cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
  AND sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var rawNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var net = rawNet * fx;
                var earned = Math.Max(net, 0.0);
                var over = Math.Max(-net, 0.0);

                rows[w] = new WipProjectBreakdownRow(w, earned, over, net, period);
            }
        }

        return rows.Values
            .Where(x => Math.Abs(x.Earned) > AnalyticsThresholds.RoundingDollarFloor || Math.Abs(x.Overbilled) > AnalyticsThresholds.RoundingDollarFloor || Math.Abs(x.Net) > AnalyticsThresholds.RoundingDollarFloor)
            .OrderByDescending(x => x.Overbilled)
            .ThenByDescending(x => x.Earned)
            .ToList();
    }
}
