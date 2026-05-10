#nullable enable
#pragma warning disable SA1649
using System;
using System.Data.Odbc;
using System.Globalization;
using System.Threading;
using Kor.Operations.Data;
using Serilog;

namespace Kor.Operations.Financials.Loaders;

/// <summary>
/// Firm-level health metrics computed at the catalog level over a trailing
/// 12-month window. Two AEC-industry standards live here:
///   - Net Multiplier = Net Service Revenue / Direct Labor Cost. The single
///     most-cited firm-health KPI in the structural engineering industry;
///     ZweigGroup benchmarks call &gt;= 3.0 healthy.
///   - DSO (Days Sales Outstanding) = AR / annualized billed * 365. The
///     velocity dimension AR Outstanding alone cannot show — how many days
///     a typical invoice sits before collection.
///
/// Both metrics share the trailing-12mo billed query so they are loaded
/// together to avoid a redundant round-trip.
/// </summary>
internal sealed record FirmHealthLoadResult(
    double NetServiceRevenue12Mo,
    double DirectLaborCost12Mo,
    double NetMultiplier,
    double NetProfit12Mo,
    double DaysSalesOutstanding,
    bool DataLoaded)
{
    internal static readonly FirmHealthLoadResult Empty = new(0.0, 0.0, 0.0, 0.0, 0.0, false);
}

internal static class FirmHealthLoader
{
    public static FirmHealthLoadResult Load(
        OdbcConnection cn,
        double arOutstandingCadEquiv,
        double usdToCadRate,
        CancellationToken ct)
    {
        // Anchor the trailing-12mo window to the first of the calendar month so
        // the date-based DLC filter (tkDetail.TransDate) covers exactly the same
        // span as the period-int-based NSR filter (LedgerAR.Period). Without this,
        // NSR includes the whole starting month while DLC starts on day-of-month
        // so the Net Multiplier denominator misses up to ~30 days of labor.
        var firstOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var since = firstOfThisMonth.AddMonths(-12);
        var sincePeriodInt = since.Year * 100 + since.Month;

        double nsr;
        try { nsr = LoadTrailing12MoBilled(cn, sincePeriodInt, usdToCadRate, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load trailing-12mo billed in {Loader}.", nameof(FirmHealthLoader));
            return FirmHealthLoadResult.Empty;
        }

        double dlc;
        try { dlc = LoadTrailing12MoDirectLaborCost(cn, since, usdToCadRate, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load trailing-12mo direct labor cost in {Loader}.", nameof(FirmHealthLoader));
            return FirmHealthLoadResult.Empty;
        }

        // Net Multiplier: undefined when DLC is zero — display as 0 (caller can
        // hide / show "n/a" based on DataLoaded flag) rather than divide-by-zero.
        var netMultiplier = dlc > AnalyticsThresholds.RoundingDollarFloor ? nsr / dlc : 0.0;

        // Net Profit (T12mo) = NSR − DLC. The dollar version of Net Multiplier.
        // NOT true bottom-line profit — overhead (rent, software, admin staff,
        // IT) is NOT subtracted, so this is closer to "labor margin." The KPI
        // description and dictionary entry are explicit about that.
        var netProfit = nsr - dlc;

        // DSO: also undefined without a billing run rate. Same pattern.
        var dso = nsr > AnalyticsThresholds.RoundingDollarFloor
            ? (arOutstandingCadEquiv / nsr) * 365.0
            : 0.0;

        return new FirmHealthLoadResult(nsr, dlc, netMultiplier, netProfit, dso, DataLoaded: true);
    }

    /// <summary>
    /// Net Service Revenue proxy: trailing-12mo invoiced revenue from LedgerAR
    /// (TransType='IN', accounts 4001/4003/4210/4220/4240 — Daler's canonical
    /// list, 2026-05-05 transcript). Bucketed by Org so USA-org invoices are
    /// FX-converted to CAD-equivalent before summing.
    /// </summary>
    private static double LoadTrailing12MoBilled(
        OdbcConnection cn, int sincePeriodInt, double usdToCadRate, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // Account codes at KOR are stored with a ".00" suffix (e.g. '4001.00'),
        // and other Deltek installations may store them without padding or with
        // trailing spaces. Match on the 4-char prefix so the predicate works
        // regardless of catalog-level formatting choices.
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-Amount) AS Invoiced
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4210', '4220', '4240')
  AND Period >= ?
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = sincePeriodInt });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                usaTotal += amt;
            else
                cadTotal += amt;
        }
        return cadTotal + (usaTotal * usdToCadRate);
    }

    /// <summary>
    /// Direct Labor Cost: trailing-12mo SUM(RegAmt+OvtAmt+SpecialOvtAmt) for
    /// timesheet entries against direct (non-overhead) projects with billable
    /// LaborCodes (excludes Admin=70 and NonBillable=80, plus overhead WBS1
    /// prefixes 99%, 9[A-Z]%, [A-Z]%). Mirrors the billable-hours filter the
    /// rest of the financials surface uses for utilization headlines, so the
    /// numerator/denominator stay congruent.
    ///
    /// FX: tkDetail.RegAmt is denominated in the project's currency (verified
    /// 2026-05-06 against KOR's catalog — same employee charges to a CAD
    /// project at $53.90/hr and to a USA project at $39.51/hr ≈ 53.90/1.36).
    /// Bucket by pr.Org so USA-org charges are FX-converted to CAD-equivalent
    /// before summing — matches the LoadTrailing12MoBilled numerator and the
    /// rest of the financials surface.
    /// </summary>
    private static double LoadTrailing12MoDirectLaborCost(
        OdbcConnection cn, DateTime since, double usdToCadRate, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(t.RegAmt,0) + COALESCE(t.OvtAmt,0) + COALESCE(t.SpecialOvtAmt,0)) AS LaborCost
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail t
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE t.TransDate >= ?
  AND t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
  AND t.WBS1 NOT LIKE '[A-Z]%'
  AND t.WBS1 NOT LIKE '9[A-Z]%'
  AND t.WBS1 NOT LIKE '99%'
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = since });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                usaTotal += amt;
            else
                cadTotal += amt;
        }
        return cadTotal + (usaTotal * usdToCadRate);
    }
}
