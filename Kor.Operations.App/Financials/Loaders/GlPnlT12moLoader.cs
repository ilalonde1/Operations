#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Threading;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Serilog;

namespace Kor.Operations.Financials.Loaders;

/// <summary>
/// Trailing-12-months-posted bottom-line P&amp;L from Deltek's GLSummary,
/// the same source the GL P&amp;L tab uses. Unlike the Net Multiplier /
/// Labor Margin tile (NSR - DLC), this number includes all chart-of-
/// accounts overhead, so it answers "did the firm actually make money
/// this past year?".
/// </summary>
internal sealed record GlPnlT12moLoadResult(
    double Revenue12Mo,
    double Expenses12Mo,
    double NetIncome12Mo,
    int FromPeriod,
    int ToPeriod,
    int? MaxPostedPeriod,
    bool DataLoaded)
{
    internal static readonly GlPnlT12moLoadResult Empty =
        new(0.0, 0.0, 0.0, 0, 0, null, false);
}

internal static class GlPnlT12moLoader
{
    private static readonly HashSet<short> DefaultIncomeGroupTypes = new() { 4, 8 };
    private static readonly HashSet<short> DefaultExpenseGroupTypes = new() { 5, 6, 7 };

    public static GlPnlT12moLoadResult Load(
        OdbcConnection cn,
        FinancialsOptions financialsOptions,
        double usdToCadRate,
        CancellationToken ct)
    {
        try
        {
            // Anchor to the latest posted GL period. Deltek often lags the
            // calendar close, so this is "trailing 12 months posted" rather
            // than a calendar T12 that could silently include missing months.
            var maxPosted = LoadMaxGlPeriod(cn, ct);
            if (!maxPosted.HasValue)
                return GlPnlT12moLoadResult.Empty;

            var toPeriod = maxPosted.Value;
            var toY = toPeriod / 100;
            var toM = toPeriod % 100;
            if (toM < 1 || toM > 12)
                return GlPnlT12moLoadResult.Empty;

            var toDate = new DateTime(toY, toM, 1);
            var fromDate = toDate.AddMonths(-11);
            var fromPeriod = fromDate.Year * 100 + fromDate.Month;

            var tableNo = ResolveTableNo(cn, financialsOptions, ct);
            if (tableNo == 0)
                return GlPnlT12moLoadResult.Empty;

            var income = ParseGroupTypeSet(financialsOptions.PnLIncomeGroupTypes, DefaultIncomeGroupTypes);
            var expense = ParseGroupTypeSet(financialsOptions.PnLExpenseGroupTypes, DefaultExpenseGroupTypes);
            var flipSign = ReadFlipSign(financialsOptions);

            var (rev, exp, net) = LoadAggregates(
                cn, tableNo, fromPeriod, toPeriod, income, expense, flipSign, usdToCadRate, ct);

            return new GlPnlT12moLoadResult(
                Revenue12Mo: rev,
                Expenses12Mo: exp,
                NetIncome12Mo: net,
                FromPeriod: fromPeriod,
                ToPeriod: toPeriod,
                MaxPostedPeriod: maxPosted,
                DataLoaded: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load GL P&L T12mo summary in {Loader}.", nameof(GlPnlT12moLoader));
            return GlPnlT12moLoadResult.Empty;
        }
    }

    private static int? LoadMaxGlPeriod(OdbcConnection cn, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $"SELECT MAX(Period) FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary;";
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
        var v = cmd.ExecuteScalar();
        if (v == null || v == DBNull.Value)
            return null;

        var raw = Convert.ToInt32(v, CultureInfo.InvariantCulture);
        return raw > 0 ? raw : null;
    }

    private static short ResolveTableNo(OdbcConnection cn, FinancialsOptions opts, CancellationToken ct)
    {
        // Pick the same GL table the P&L tab opens to by default. Penalize
        // balance sheet / AR / AP names so a non-P&L table is not selected.
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        var like = string.IsNullOrWhiteSpace(opts.PnLGlTableNameLike)
            ? "%Income Statement%"
            : opts.PnLGlTableNameLike;
        cmd.CommandText = $"SELECT TableNo, TableName FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLTable WHERE TableName LIKE ? ORDER BY TableNo;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
        using var r = cmd.ExecuteReader();
        var best = (Score: int.MinValue, TableNo: (short)0);
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var tn = r.IsDBNull(0) ? (short)0 : r.GetInt16(0);
            var name = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            var s = ScoreTable(name);
            if (s > best.Score)
                best = (s, tn);
        }

        return best.Score == int.MinValue ? (short)0 : best.TableNo;
    }

    private static int ScoreTable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var n = name.Trim().ToLowerInvariant();
        var score = 0;
        if (n.Contains("income statement", StringComparison.Ordinal)) score += 200;
        if (n.Contains("profit", StringComparison.Ordinal) && n.Contains("loss", StringComparison.Ordinal)) score += 200;
        if (n.Contains("p&l", StringComparison.Ordinal) || n.Contains("pnl", StringComparison.Ordinal)) score += 180;
        if (n.Contains("income", StringComparison.Ordinal)) score += 60;
        if (n.Contains("statement", StringComparison.Ordinal)) score += 20;
        if (n.Contains("operating", StringComparison.Ordinal)) score += 20;
        if (n.Contains("balance sheet", StringComparison.Ordinal)) score -= 200;
        if (n.Contains("cash flow", StringComparison.Ordinal) || n.Contains("cashflow", StringComparison.Ordinal)) score -= 200;
        if (n.Contains("ar ", StringComparison.Ordinal) || n.Contains(" a/r", StringComparison.Ordinal) || n.Contains("accounts receivable", StringComparison.Ordinal)) score -= 60;
        if (n.Contains("ap ", StringComparison.Ordinal) || n.Contains(" a/p", StringComparison.Ordinal) || n.Contains("accounts payable", StringComparison.Ordinal)) score -= 60;
        return score;
    }

    private static (double Revenue, double Expenses, double Net) LoadAggregates(
        OdbcConnection cn,
        short tableNo,
        int fromPeriod,
        int toPeriod,
        HashSet<short> incomeGt,
        HashSet<short> expenseGt,
        bool flipSign,
        double usdToCadRate,
        CancellationToken ct)
    {
        // GLSummary -> GLGroupDetail -> GLParentDetail -> GLParentGroup gives
        // the same income/expense classification used by the GL P&L tab.
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(s.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    pg.GroupType,
    SUM(s.Amount) AS Total
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary s
JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLGroupDetail gd
  ON gd.TableNo = ?
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) >= RIGHT(REPLICATE('0', 13) + gd.StartAccount, 13)
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) <= RIGHT(REPLICATE('0', 13) + gd.EndAccount, 13)
JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLParentDetail pd
  ON pd.TableNo = gd.TableNo
 AND pd.DetailGroupID = gd.GLGroup
JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLParentGroup pg
  ON pg.Code = pd.GLGroup
WHERE s.Period >= ? AND s.Period <= ?
GROUP BY
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(s.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END,
    pg.GroupType;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = fromPeriod });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = toPeriod });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
        using var r = cmd.ExecuteReader();

        double incomeCad = 0.0, incomeUsa = 0.0, expenseCad = 0.0, expenseUsa = 0.0;
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var gt = r.IsDBNull(1) ? (short)0 : Convert.ToInt16(r.GetValue(1), CultureInfo.InvariantCulture);
            var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
            if (flipSign)
                amt = -amt;

            var isUsa = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase);
            if (incomeGt.Contains(gt))
            {
                if (isUsa) incomeUsa += amt; else incomeCad += amt;
            }
            else if (expenseGt.Contains(gt))
            {
                if (isUsa) expenseUsa += amt; else expenseCad += amt;
            }
        }

        var income = incomeCad + (incomeUsa * usdToCadRate);
        var expense = expenseCad + (expenseUsa * usdToCadRate);
        var net = income + expense;
        return (Math.Abs(income), Math.Abs(expense), net);
    }

    private static HashSet<short> ParseGroupTypeSet(string raw, HashSet<short> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<short>(fallback);

        var set = new HashSet<short>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (short.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                set.Add(v);
        }

        return set.Count == 0 ? new HashSet<short>(fallback) : set;
    }

    private static bool ReadFlipSign(FinancialsOptions opts)
    {
        var raw = opts.PnLGlFlipSign;
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
