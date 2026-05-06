#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using Kor.Operations.Data;

namespace Kor.Operations.Financials.Loaders;

internal sealed record ArLoadResult(
    double Outstanding,
    double Over60,
    IReadOnlyList<ArProjectOutstandingRow> ProjectRows,
    IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows,
    double FirmwideOutstandingCadEquiv,
    double FirmwideOver60CadEquiv,
    double FirmwideOutstandingCad,
    double FirmwideOutstandingUsa,
    double UsdToCadRate)
{
    internal static readonly ArLoadResult Empty = new(0.0, 0.0, new List<ArProjectOutstandingRow>(), new List<ArInvoiceOutstandingRow>(), 0.0, 0.0, 0.0, 0.0, 1.36);
}

internal static class ArLoader
{
    public static ArLoadResult Load(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
    {
        // Drilldown rows are loaded FIRMWIDE (every WBS1 with open AR), not scoped to
        // the current snapshot's project list, so Σ rows reconciles to the firmwide
        // AR Outstanding tile. wbs1 is kept on the signature for callers that want
        // a scoped subset, but the executive AR tile uses the firmwide breakdown.
        var result = LoadInvoiceArBalances(cn, wbs1: null, usdToCadRate, ct);
        var firmwide = LoadFirmwideArTotals(cn, usdToCadRate, ct);
        return new ArLoadResult(
            result.Outstanding,
            result.Over60,
            result.ProjectRows,
            result.InvoiceRows,
            firmwide.OutstandingCadEquiv,
            firmwide.Over60CadEquiv,
            firmwide.OutstandingCad,
            firmwide.OutstandingUsa,
            usdToCadRate);
    }

    private static (double OutstandingCadEquiv, double Over60CadEquiv, double OutstandingCad, double OutstandingUsa)
        LoadFirmwideArTotals(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
    {
        var asOf = DateTime.Today.Date;
        // Bucket by Org (joined from PR via WBS1) so USA balances can be FX-converted
        // before being summed with CAD. Falls back to CAD bucket when Org is null/missing.
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    Bucket,
    SUM(InvBalance)         AS Outstanding,
    SUM(InvBalanceOver60)   AS Over60
FROM (
    SELECT
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
        COALESCE(ar.InvBalanceSourceCurrency,0) AS InvBalance,
        CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END AS InvBalanceOver60
    FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
    LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
      ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
    WHERE ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
) x
GROUP BY Bucket;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });

        var cadOutstanding = 0.0;
        var usaOutstanding = 0.0;
        var cadOver60 = 0.0;
        var usaOver60 = 0.0;

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var outstanding = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            var over60 = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
            {
                usaOutstanding += outstanding;
                usaOver60 += over60;
            }
            else
            {
                cadOutstanding += outstanding;
                cadOver60 += over60;
            }
        }

        var totalCadEquiv = cadOutstanding + (usaOutstanding * usdToCadRate);
        var over60CadEquiv = cadOver60 + (usaOver60 * usdToCadRate);
        return (totalCadEquiv, over60CadEquiv, cadOutstanding, usaOutstanding);
    }

    // wbs1=null means firmwide (no WBS1 filter); pass a list to scope to specific projects.
    private static (double Outstanding, double Over60, IReadOnlyList<ArProjectOutstandingRow> ProjectRows, IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows) LoadInvoiceArBalances(OdbcConnection cn, List<string>? wbs1, double usdToCadRate, CancellationToken ct)
    {
        var asOf = DateTime.Today.Date;
        var byWbs = new Dictionary<string, ArProjectOutstandingRow>(StringComparer.OrdinalIgnoreCase);
        var invoiceRows = new List<ArInvoiceOutstandingRow>(256);

        // Firmwide path runs one query with no WBS filter; scoped path chunks the wbs1 list.
        // A single sentinel chunk (null) drives the firmwide branch through the same loop.
        var chunks = (wbs1 == null || wbs1.Count == 0)
            ? new[] { (List<string>?)null }.AsEnumerable()
            : ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize)
                .Select(c => (List<string>?)c);

        foreach (var chunk in chunks)
        {
            var inWbs1 = chunk != null
                ? $"WHERE ar.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)}) AND "
                : "WHERE ";
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Group by WBS1 + Org so USA-org rows can be FX-converted to CAD-equivalent.
            // Without this, the scoped AR drilldown sums in source currency while the
            // firmwide AR headline (LoadFirmwideArTotals) is CAD-equiv — they'd diverge.
            cmd.CommandText = $@"
SELECT
    ar.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(ar.InvBalanceSourceCurrency,0)) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) <= 30
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS CurrentAmt,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 31 AND 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt31To60,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 61 AND 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt61To90,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt90Plus,
    MIN(COALESCE(ar.InvoiceDate, ar.DueDate)) AS OldestInvoiceDate,
    MAX(COALESCE(pr.Name,'')) AS ProjectName,
    MAX(COALESCE(pr.ProjMgr,'')) AS ProjMgr,
    MAX(COALESCE(em.FirstName,'')) AS PmFirstName,
    MAX(COALESCE(em.LastName,'')) AS PmLastName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
GROUP BY ar.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            if (chunk != null) ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var wbs1Key = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (wbs1Key.Length == 0)
                    continue;

                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;

                var total = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                var current = ExecutiveSummaryLoaderSupport.GetDouble(r, 3) * fx;
                var aged31 = ExecutiveSummaryLoaderSupport.GetDouble(r, 4) * fx;
                var aged61 = ExecutiveSummaryLoaderSupport.GetDouble(r, 5) * fx;
                var aged90 = ExecutiveSummaryLoaderSupport.GetDouble(r, 6) * fx;
                var oldest = ExecutiveSummaryLoaderSupport.GetDateOrNull(r, 7);
                var projectName = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 8);
                var pm = BuildEmployeeDisplay(
                    ExecutiveSummaryLoaderSupport.GetTrimmed(r, 9),
                    ExecutiveSummaryLoaderSupport.GetTrimmed(r, 10),
                    ExecutiveSummaryLoaderSupport.GetTrimmed(r, 11));

                if (Math.Abs(total) < 0.005)
                    continue;

                if (byWbs.TryGetValue(wbs1Key, out var existing))
                {
                    byWbs[wbs1Key] = existing with
                    {
                        ProjectName = string.IsNullOrWhiteSpace(existing.ProjectName) ? projectName : existing.ProjectName,
                        Pm = string.IsNullOrWhiteSpace(existing.Pm) ? pm : existing.Pm,
                        Total = existing.Total + total,
                        Current = existing.Current + current,
                        Aged31To60 = existing.Aged31To60 + aged31,
                        Aged61To90 = existing.Aged61To90 + aged61,
                        Aged90Plus = existing.Aged90Plus + aged90,
                        OldestInvoiceDate = ExecutiveSummaryLoaderSupport.MergeOldest(existing.OldestInvoiceDate, oldest)
                    };
                }
                else
                {
                    byWbs[wbs1Key] = new ArProjectOutstandingRow(
                        wbs1Key,
                        projectName,
                        pm,
                        total,
                        current,
                        aged31,
                        aged61,
                        aged90,
                        oldest);
                }
            }

            using var cmdDetail = cn.CreateCommand();
            cmdDetail.CommandTimeout = SqlTimeouts.Batch;
            cmdDetail.CommandText = $@"
SELECT
    ar.WBS1,
    ar.InvoiceDate,
    ar.DueDate,
    COALESCE(ar.InvBalanceSourceCurrency,0) AS OpenBalance,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    COALESCE(pr.Name,'') AS ProjectName,
    COALESCE(pr.ProjMgr,'') AS ProjMgr,
    COALESCE(em.FirstName,'') AS PmFirstName,
    COALESCE(em.LastName,'') AS PmLastName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004;";
            if (chunk != null) ExecutiveSummaryLoaderSupport.AddInListParameters(cmdDetail, chunk);

            using var regDetail = ct.Register(() => { try { cmdDetail.Cancel(); } catch { } });
            using var rd = cmdDetail.ExecuteReader();
            while (rd.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 0);
                if (w.Length == 0) continue;
                var invoiceDate = ExecutiveSummaryLoaderSupport.GetDateOrNull(rd, 1);
                var dueDate = ExecutiveSummaryLoaderSupport.GetDateOrNull(rd, 2);
                var bal = ExecutiveSummaryLoaderSupport.GetDouble(rd, 3);
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 4);
                var projectName = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 5);
                var pm = BuildEmployeeDisplay(
                    ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 6),
                    ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 7),
                    ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 8));
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var anchor = dueDate ?? invoiceDate;
                var dpd = anchor.HasValue ? Math.Max(0, (int)(asOf - anchor.Value.Date).TotalDays) : 0;
                invoiceRows.Add(new ArInvoiceOutstandingRow(w, projectName, pm, invoiceDate, dueDate, dpd, bal * fx));
            }
        }

        var rows = byWbs.Values
            .OrderByDescending(x => x.Aged90Plus)
            .ThenByDescending(x => x.Total)
            .ToList();
        var detail = invoiceRows
            .OrderByDescending(x => x.DaysPastDue)
            .ThenByDescending(x => Math.Abs(x.Balance))
            .ToList();
        var outstanding = rows.Sum(x => x.Total);
        var over60 = rows.Sum(x => x.Aged61To90 + x.Aged90Plus);
        return (outstanding, over60, rows, detail);
    }

    private static string BuildEmployeeDisplay(string employee, string first, string last)
    {
        var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (!string.IsNullOrWhiteSpace(name))
            return string.IsNullOrWhiteSpace(employee) ? name : $"{name} ({employee})";
        return employee ?? string.Empty;
    }
}
