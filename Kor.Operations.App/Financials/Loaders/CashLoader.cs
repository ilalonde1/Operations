#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using Kor.Operations.Data;

namespace Kor.Operations.Financials.Loaders;

internal sealed record CashLoadResult(
    string Period,
    double Cad,
    double Usa,
    double Bcc,
    IReadOnlyList<CashHistoryPoint> History)
{
    public double Total => Cad + Usa + Bcc;

    internal static readonly CashLoadResult Empty = new("n/a", 0.0, 0.0, 0.0, new List<CashHistoryPoint>());
}

internal static class CashLoader
{
    private sealed record BankAcct(string Company, string Account, string Org);

    public static CashLoadResult Load(OdbcConnection cn, CancellationToken ct)
    {
        var cash = LoadCashBalances(cn, ct);
        return new CashLoadResult(cash.Period, cash.Cad, cash.Usa, cash.Bcc, cash.History);
    }

    private sealed record CashBalances(
        string Period,
        double Cad,
        double Usa,
        double Bcc,
        IReadOnlyList<CashHistoryPoint> History)
    {
        public double Total => Cad + Usa + Bcc;
    }

    private static CashBalances LoadCashBalances(OdbcConnection cn, CancellationToken ct)
    {
        var banks = LoadBankAccounts(cn, ct);
        if (banks.Count == 0)
            return new CashBalances("n/a", 0, 0, 0, new List<CashHistoryPoint>());

        var todayPeriod = DateTime.Today.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var targetPeriod = FindLatestGlPeriodForAccounts(cn, banks, todayPeriod, ct);
        if (string.IsNullOrWhiteSpace(targetPeriod))
            targetPeriod = todayPeriod;

        var history = LoadCashHistory(cn, banks, targetPeriod, ct);
        if (history.Count == 0)
            return new CashBalances(targetPeriod, 0, 0, 0, history);

        var latest = history[^1];
        return new CashBalances(latest.Period, latest.Cad, latest.Usa, latest.Bcc, history);
    }

    private static List<CashHistoryPoint> LoadCashHistory(OdbcConnection cn, List<BankAcct> banks, string targetPeriod, CancellationToken ct)
    {
        var byPeriod = new Dictionary<string, (double Cad, double Usa, double Bcc)>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(banks, 25))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));

            cmd.CommandText = $@"
SELECT Period, Account, Org, SUM(COALESCE(Amount,0)) AS Amt
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary
WHERE Period <= ?
  AND ({clauses})
GROUP BY Period, Account, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = targetPeriod });
            foreach (var b in chunk)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (period.Length != 6 || !period.All(char.IsDigit))
                    continue;

                var acct = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var org = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 2);
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 3);

                var match = chunk.FirstOrDefault(x =>
                    string.Equals(x.Account, acct, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Org, org, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                    continue;

                byPeriod.TryGetValue(period, out var cur);
                switch ((match.Company ?? string.Empty).Trim().ToUpperInvariant())
                {
                    case "USA":
                        cur.Usa += amt;
                        break;
                    case "BCC":
                        cur.Bcc += amt;
                        break;
                    case "CAD":
                    default:
                        cur.Cad += amt;
                        break;
                }
                byPeriod[period] = cur;
            }
        }

        var ordered = byPeriod.Keys
            .Where(p => p.Length == 6 && p.All(char.IsDigit))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
            return new List<CashHistoryPoint>();

        var cumulative = new List<CashHistoryPoint>(ordered.Count);
        double runCad = 0.0, runUsa = 0.0, runBcc = 0.0;
        foreach (var period in ordered)
        {
            var p = byPeriod[period];
            runCad += p.Cad;
            runUsa += p.Usa;
            runBcc += p.Bcc;
            cumulative.Add(new CashHistoryPoint(period, runCad, runUsa, runBcc));
        }

        if (cumulative.Count > 12)
            cumulative = cumulative.TakeLast(12).ToList();

        return cumulative;
    }

    private static List<BankAcct> LoadBankAccounts(OdbcConnection cn, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT Company, Account, Org
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.CFGBanks
WHERE COALESCE(Account,'') <> '';";

        var list = new List<BankAcct>(64);
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();

            var company = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var account = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
            var org = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 2);

            if (company.Length == 0 || account.Length == 0)
                continue;

            if (string.IsNullOrWhiteSpace(org))
                org = company;

            list.Add(new BankAcct(company, account, org));
        }

        return list
            .GroupBy(b => string.Join("|", b.Company, b.Account, b.Org), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string FindLatestGlPeriodForAccounts(OdbcConnection cn, List<BankAcct> accts, string todayPeriod, CancellationToken ct)
    {
        string latest = string.Empty;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(accts, 40))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));
            cmd.CommandText = $@"
SELECT MAX(Period) AS MaxPeriod
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary
WHERE Period <= ? AND ({clauses});";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = todayPeriod });
            foreach (var b in chunk)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            var p = (v == null || v == DBNull.Value) ? string.Empty : (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
            p = p.Trim();

            if (p.Length > 0 && string.CompareOrdinal(p, latest) > 0)
                latest = p;
        }

        return latest;
    }
}
