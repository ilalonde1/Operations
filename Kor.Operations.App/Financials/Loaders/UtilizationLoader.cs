#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using Kor.Operations.Data;
using Serilog;

namespace Kor.Operations.Financials.Loaders;

internal sealed record UtilizationLoadResult(
    double BillableHours,
    double TotalHours,
    IReadOnlyList<UtilizationProjectRow> ProjectRows)
{
    public double Pct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);

    internal static readonly UtilizationLoadResult Empty = new(0.0, 0.0, new List<UtilizationProjectRow>());
}

internal static class UtilizationLoader
{
    private sealed record UtilAgg(double BillableHours, double TotalHours)
    {
        public double Pct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
    }

    public static UtilizationLoadResult Load(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        UtilAgg util30;
        IReadOnlyList<UtilizationProjectRow> utilByProject;
        try { util30 = LoadUtilization30(cn, wbs1, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load utilization totals in {Loader}.", nameof(UtilizationLoader));
            util30 = new UtilAgg(0, 0);
            utilByProject = new List<UtilizationProjectRow>();
        }
        try { utilByProject = LoadUtilization30ProjectRows(cn, wbs1, ct); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load utilization rows by project in {Loader}.", nameof(UtilizationLoader));
            utilByProject = new List<UtilizationProjectRow>();
        }

        return new UtilizationLoadResult(util30.BillableHours, util30.TotalHours, utilByProject);
    }

    private static UtilAgg LoadUtilization30(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var start = DateTime.Today.AddDays(-30);

        double billable = 0.0;
        double total = 0.0;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN COALESCE(BillExt,0) > 0 THEN (COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) ELSE 0 END) AS BillableHours
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail
WHERE TransDate >= ?
  AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND (COALESCE(LineItemApprovalStatus,'') = '' OR COALESCE(LineItemApprovalStatus,'') = 'A');";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                total += ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
                billable += ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            }
        }

        return new UtilAgg(billable, total);
    }

    private static List<UtilizationProjectRow> LoadUtilization30ProjectRows(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var start = DateTime.Today.AddDays(-30);
        var byWbs = new Dictionary<string, UtilizationProjectRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN COALESCE(BillExt,0) > 0 THEN (COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) ELSE 0 END) AS BillableHours
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail
WHERE TransDate >= ?
  AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND (COALESCE(LineItemApprovalStatus,'') = '' OR COALESCE(LineItemApprovalStatus,'') = 'A')
GROUP BY WBS1;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var total = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                var billable = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
                if (Math.Abs(total) < 0.004 && Math.Abs(billable) < 0.004) continue;

                if (byWbs.TryGetValue(w, out var existing))
                {
                    byWbs[w] = existing with
                    {
                        BillableHours = existing.BillableHours + billable,
                        TotalHours = existing.TotalHours + total
                    };
                }
                else
                {
                    byWbs[w] = new UtilizationProjectRow(
                        Wbs1: w,
                        BillableHours: billable,
                        TotalHours: total);
                }
            }
        }

        return byWbs.Values
            .Where(x => x.TotalHours > 0.004)
            .OrderByDescending(x => x.UtilizationPct)
            .ThenByDescending(x => x.TotalHours)
            .ToList();
    }
}
