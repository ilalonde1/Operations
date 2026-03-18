#nullable enable
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
    IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows)
{
    internal static readonly ArLoadResult Empty = new(0.0, 0.0, new List<ArProjectOutstandingRow>(), new List<ArInvoiceOutstandingRow>());
}

internal static class ArLoader
{
    public static ArLoadResult Load(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var result = LoadInvoiceArBalances(cn, wbs1, ct);
        return new ArLoadResult(result.Outstanding, result.Over60, result.ProjectRows, result.InvoiceRows);
    }

    private static (double Outstanding, double Over60, IReadOnlyList<ArProjectOutstandingRow> ProjectRows, IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows) LoadInvoiceArBalances(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
    {
        var asOf = DateTime.Today.Date;
        var byWbs = new Dictionary<string, ArProjectOutstandingRow>(StringComparer.OrdinalIgnoreCase);
        var invoiceRows = new List<ArInvoiceOutstandingRow>(256);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, 80))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(InvBalanceSourceCurrency,0)) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) <= 30
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS CurrentAmt,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) BETWEEN 31 AND 60
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt31To60,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) BETWEEN 61 AND 90
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt61To90,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) > 90
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt90Plus,
    MIN(COALESCE(InvoiceDate, DueDate)) AS OldestInvoiceDate
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var wbs1Key = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (wbs1Key.Length == 0)
                    continue;

                var total = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                var current = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
                var aged31 = ExecutiveSummaryLoaderSupport.GetDouble(r, 3);
                var aged61 = ExecutiveSummaryLoaderSupport.GetDouble(r, 4);
                var aged90 = ExecutiveSummaryLoaderSupport.GetDouble(r, 5);
                var oldest = ExecutiveSummaryLoaderSupport.GetDateOrNull(r, 6);

                if (Math.Abs(total) < 0.005)
                    continue;

                if (byWbs.TryGetValue(wbs1Key, out var existing))
                {
                    byWbs[wbs1Key] = existing with
                    {
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
    WBS1,
    InvoiceDate,
    DueDate,
    COALESCE(InvBalanceSourceCurrency,0) AS OpenBalance
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND ABS(COALESCE(InvBalanceSourceCurrency,0)) > 0.004;";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmdDetail, chunk);

            using var regDetail = ct.Register(() => { try { cmdDetail.Cancel(); } catch { } });
            using var rd = cmdDetail.ExecuteReader();
            while (rd.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 0);
                if (w.Length == 0) continue;
                var invoiceDate = ExecutiveSummaryLoaderSupport.GetDateOrNull(rd, 1);
                var dueDate = ExecutiveSummaryLoaderSupport.GetDateOrNull(rd, 2);
                var bal = ExecutiveSummaryLoaderSupport.GetDouble(rd, 3);
                var anchor = dueDate ?? invoiceDate;
                var dpd = anchor.HasValue ? Math.Max(0, (int)(asOf - anchor.Value.Date).TotalDays) : 0;
                invoiceRows.Add(new ArInvoiceOutstandingRow(w, invoiceDate, dueDate, dpd, bal));
            }
        }

        var rows = byWbs.Values
            .OrderByDescending(x => x.Aged90Plus)
            .ThenByDescending(x => x.Total)
            .ToList();
        var detail = invoiceRows
            .OrderByDescending(x => x.DaysPastDue)
            .ThenByDescending(x => x.Balance)
            .ToList();
        var outstanding = rows.Sum(x => x.Total);
        var over60 = rows.Sum(x => x.Aged61To90 + x.Aged90Plus);
        return (outstanding, over60, rows, detail);
    }
}
