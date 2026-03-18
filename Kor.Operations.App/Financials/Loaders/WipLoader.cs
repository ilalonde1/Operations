#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Kor.Operations.Data;

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
    double WipPreInvoice)
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
        0.0);
}

internal static class WipLoader
{
    public static WipLoadResult Load(OdbcConnection cn, List<string> wbs1, Dictionary<string, PrAgg> prByPeriod, BuiltSeries series, CancellationToken ct)
    {
        var unbilledColumnHasAny = prByPeriod.Values.Any(p =>
            Math.Abs(p.UnbilledNet) > 1e-9 ||
            Math.Abs(p.UnbilledEarned) > 1e-9 ||
            Math.Abs(p.Overbilled) > 1e-9);
        var wipUnbilled = series.LatestUnbilledEarned;
        var wipOverbilled = series.LatestOverbilled;
        var wipUnbilledNet = series.LatestUnbilledNet;
        var wipUnbilledPeriod = series.LatestPeriod;

        IReadOnlyList<WipProjectBreakdownRow> wipProjectRows = new List<WipProjectBreakdownRow>();
        if (!unbilledColumnHasAny && !string.IsNullOrWhiteSpace(wipUnbilledPeriod) && wipUnbilledPeriod.Length == 6)
        {
            try
            {
                var wipProxy = LoadWipProxyBalanceByProject(cn, wbs1, wipUnbilledPeriod, ct);
                wipUnbilled = wipProxy.Earned;
                wipOverbilled = wipProxy.Overbilled;
                wipUnbilledNet = wipProxy.Net;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Watchlist WIP proxy failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        try
        {
            wipProjectRows = LoadWipProjectBreakdownByProject(cn, wbs1, wipUnbilledPeriod, useUnbilledAsOf: unbilledColumnHasAny, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ExecutiveSummaryDeltekLoader WIP project breakdown failed: " + ex.GetType().Name + ": " + ex.Message);
            wipProjectRows = new List<WipProjectBreakdownRow>();
        }

        (double Earned, double Overbilled, double Net) firmWip;
        try { firmWip = LoadFirmwideWipProxyBalance(cn, wipUnbilledPeriod, ct); }
        catch (Exception ex)
        {
            Debug.WriteLine("ExecutiveSummaryDeltekLoader Firmwide WIP failed: " + ex.GetType().Name + ": " + ex.Message);
            firmWip = (0.0, 0.0, 0.0);
        }

        double wipPreInvoice;
        try { wipPreInvoice = LoadPreInvoiceWip(cn, wbs1, ct); }
        catch (Exception ex)
        {
            Debug.WriteLine("ExecutiveSummaryDeltekLoader ARPreInvoice failed: " + ex.GetType().Name + ": " + ex.Message);
            wipPreInvoice = 0.0;
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
            wipPreInvoice);
    }

    private static double LoadPreInvoiceWip(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        double total = 0.0;
        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    SUM(CASE
            WHEN (COALESCE(d.Amount,0) - COALESCE(d.PaidAmount,0)) < 0 THEN 0
            ELSE (COALESCE(d.Amount,0) - COALESCE(d.PaidAmount,0))
        END) AS DraftWip
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.ARPreInvoice h
JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.ARPreInvoiceDetail d
  ON d.PreInvoice = h.PreInvoice
WHERE h.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND ISNULL(LTRIM(RTRIM(CAST(h.Cancelled AS varchar(10)))),''0'') NOT IN (''1'',''Y'',''YES'',''TRUE'')
  AND ISNULL(LTRIM(RTRIM(CAST(h.AppliedInvoice AS varchar(50)))),'''') = '''';";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            total += ExecutiveSummaryLoaderSupport.ScalarToDouble(v);
        }
        return total;
    }

    private static (double Earned, double Overbilled, double Net) LoadWipProxyBalanceByProject(OdbcConnection cn, List<string> wbs1, string asOfPeriod, CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
            return (0.0, 0.0, 0.0);

        double earned = 0.0;
        double overbilled = 0.0;
        double net = 0.0;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    SUM(CASE WHEN Net > 0 THEN Net ELSE 0 END) AS Earned,
    SUM(CASE WHEN Net < 0 THEN -Net ELSE 0 END) AS Overbilled,
    SUM(Net) AS Net
FROM (
    SELECT WBS1, SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
    FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
      AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
    GROUP BY WBS1
) x;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                earned += ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
                overbilled += ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                net += ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
            }
        }

        return (earned, overbilled, net);
    }

    private static (double Earned, double Overbilled, double Net) LoadFirmwideWipProxyBalance(OdbcConnection cn, string asOfPeriod, CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
            period = DateTime.Today.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture);

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    SUM(CASE WHEN Net > 0 THEN Net ELSE 0 END) AS Earned,
    SUM(CASE WHEN Net < 0 THEN -Net ELSE 0 END) AS Overbilled,
    SUM(Net) AS Net
FROM (
    SELECT WBS1, SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
    FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
    GROUP BY WBS1
) x;";

        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0.0, 0.0, 0.0);

        var earned = ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
        var overbilled = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
        var net = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
        return (earned, overbilled, net);
    }

    private static List<WipProjectBreakdownRow> LoadWipProjectBreakdownByProject(
        OdbcConnection cn,
        List<string> wbs1,
        string asOfPeriod,
        bool useUnbilledAsOf,
        CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
            period = DateTime.Today.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture);

        var rows = new Dictionary<string, WipProjectBreakdownRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            if (useUnbilledAsOf)
            {
                cmd.CommandText = $@"
SELECT
    p.WBS1,
    COALESCE(p.Unbilled,0) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain p
JOIN (
    SELECT WBS1, MAX(Period) AS MaxPeriod
    FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
      AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
    GROUP BY WBS1
) x
  ON x.WBS1 = p.WBS1
 AND x.MaxPeriod = p.Period;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }
            else
            {
                cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
WHERE Period <= ?
  AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var net = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                var earned = Math.Max(net, 0.0);
                var over = Math.Max(-net, 0.0);

                rows[w] = new WipProjectBreakdownRow(w, earned, over, net, period);
            }
        }

        return rows.Values
            .Where(x => Math.Abs(x.Earned) > 0.004 || Math.Abs(x.Overbilled) > 0.004 || Math.Abs(x.Net) > 0.004)
            .OrderByDescending(x => x.Overbilled)
            .ThenByDescending(x => x.Earned)
            .ToList();
    }
}
