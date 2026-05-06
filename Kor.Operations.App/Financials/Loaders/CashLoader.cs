#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.Financials.Loaders;

internal sealed record CashLoadResult(
    string Period,
    double Cad,
    double Usa,
    double Bcc,
    double CombinedCadEquivalent,
    double UsdToCadRate,
    IReadOnlyList<CashAccountBalanceRow> PerAccount,
    IReadOnlyList<CashHistoryPoint> History)
{
    public double Total => Cad + Usa + Bcc;

    internal static readonly CashLoadResult Empty = new("n/a", 0.0, 0.0, 0.0, 0.0, 1.36, new List<CashAccountBalanceRow>(), new List<CashHistoryPoint>());
}

public sealed record CashAccountBalanceRow(
    string Company,
    string Account,
    string Org,
    double Balance);

internal static class CashLoader
{
    private sealed record BankAcct(string Company, string Account, string Org);

    public static CashLoadResult Load(OdbcConnection cn, FinancialsOptions financialsOptions, CancellationToken ct)
    {
        var cash = LoadCashBalances(cn, financialsOptions, ct);
        return new CashLoadResult(cash.Period, cash.Cad, cash.Usa, cash.Bcc, cash.CombinedCadEquivalent, cash.UsdToCadRate, cash.PerAccount, cash.History);
    }

    private sealed record CashBalances(
        string Period,
        double Cad,
        double Usa,
        double Bcc,
        double CombinedCadEquivalent,
        double UsdToCadRate,
        IReadOnlyList<CashAccountBalanceRow> PerAccount,
        IReadOnlyList<CashHistoryPoint> History)
    {
        public double Total => Cad + Usa + Bcc;
    }

    private sealed record CashHistoryLoadResult(
        IReadOnlyList<CashHistoryPoint> History,
        IReadOnlyList<CashAccountBalanceRow> PerAccount);

    private static CashBalances LoadCashBalances(OdbcConnection cn, FinancialsOptions financialsOptions, CancellationToken ct)
    {
        var fxRate = ReadUsdToCadRate(financialsOptions);
        var usdAccounts = ParseUsdAccounts(financialsOptions);
        var banks = LoadBankAccounts(cn, financialsOptions, ct);
        if (banks.Count == 0)
            return new CashBalances("n/a", 0, 0, 0, 0, fxRate, new List<CashAccountBalanceRow>(), new List<CashHistoryPoint>());

        var todayPeriod = DateTime.Today.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var targetPeriod = FindLatestGlPeriodForAccounts(cn, banks, todayPeriod, ct);
        if (string.IsNullOrWhiteSpace(targetPeriod))
            targetPeriod = todayPeriod;

        var cashHistory = LoadCashHistory(cn, banks, targetPeriod, usdAccounts, ct);
        var history = cashHistory.History;
        if (history.Count == 0)
            return new CashBalances(targetPeriod, 0, 0, 0, 0, fxRate, cashHistory.PerAccount, history);

        var latest = history[^1];
        var combined = latest.Cad + (latest.Usa * fxRate) + latest.Bcc;
        return new CashBalances(latest.Period, latest.Cad, latest.Usa, latest.Bcc, combined, fxRate, cashHistory.PerAccount, history);
    }

    private static CashHistoryLoadResult LoadCashHistory(OdbcConnection cn, List<BankAcct> banks, string targetPeriod, HashSet<string> usdAccounts, CancellationToken ct)
    {
        var byPeriod = new Dictionary<string, (double Cad, double Usa, double Bcc)>(StringComparer.OrdinalIgnoreCase);
        var byAccountPeriod = new Dictionary<(string Company, string Account, string Org, string Period), double>();

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

                var key = (match.Company, match.Account, match.Org, period);
                byAccountPeriod[key] = (byAccountPeriod.TryGetValue(key, out var prev) ? prev : 0.0) + amt;

                byPeriod.TryGetValue(period, out var cur);
                // Account-level override beats Org-based bucketing — handles USD accounts
                // that live inside CAD entities (e.g., 1120 Scotiabank Partnership USD CHQ).
                var classification = MatchesUsdAccount(match.Account, usdAccounts)
                    ? "USA"
                    : (!string.IsNullOrWhiteSpace(match.Org)
                        ? match.Org.Trim().ToUpperInvariant()
                        : (match.Company ?? string.Empty).Trim().ToUpperInvariant());

                switch (classification)
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
            return new CashHistoryLoadResult(new List<CashHistoryPoint>(), BuildZeroPerAccountRows(banks));

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

        var perAccount = BuildPerAccountRows(banks, byAccountPeriod, ordered);

        if (cumulative.Count > 12)
            cumulative = cumulative.TakeLast(12).ToList();

        return new CashHistoryLoadResult(cumulative, perAccount);
    }

    private static List<BankAcct> LoadBankAccounts(OdbcConnection cn, FinancialsOptions financialsOptions, CancellationToken ct)
    {
        var whitelist = ParseAccountWhitelist(financialsOptions);
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

            if (whitelist.Count > 0 && !whitelist.Contains(account))
                continue;

            list.Add(new BankAcct(company, account, org));
        }

        return list
            .GroupBy(b => string.Join("|", b.Company, b.Account, b.Org), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static IReadOnlyList<CashAccountBalanceRow> BuildZeroPerAccountRows(List<BankAcct> banks)
        => banks
            .OrderBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Account, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Org, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CashAccountBalanceRow(b.Company, b.Account, b.Org, 0.0))
            .ToList();

    private static IReadOnlyList<CashAccountBalanceRow> BuildPerAccountRows(
        List<BankAcct> banks,
        Dictionary<(string Company, string Account, string Org, string Period), double> byAccountPeriod,
        List<string> orderedPeriods)
    {
        var balances = banks.ToDictionary(
            b => (b.Company, b.Account, b.Org),
            _ => 0.0);

        foreach (var period in orderedPeriods)
        {
            foreach (var b in banks)
            {
                var key = (b.Company, b.Account, b.Org, period);
                if (byAccountPeriod.TryGetValue(key, out var amount))
                    balances[(b.Company, b.Account, b.Org)] += amount;
            }
        }

        return banks
            .OrderBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Account, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Org, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CashAccountBalanceRow(b.Company, b.Account, b.Org, balances[(b.Company, b.Account, b.Org)]))
            .ToList();
    }

    private static HashSet<string> ParseAccountWhitelist(FinancialsOptions financialsOptions)
    {
        var raw = financialsOptions?.CashAccountWhitelist ?? string.Empty;
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ParseUsdAccounts(FinancialsOptions financialsOptions)
    {
        var raw = financialsOptions?.CashUsdAccounts ?? string.Empty;
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Match either the raw account number or its decimal-trimmed form so users can
    // configure "1120" or "1120.00" interchangeably.
    private static bool MatchesUsdAccount(string account, HashSet<string> usdAccounts)
    {
        if (usdAccounts.Count == 0 || string.IsNullOrWhiteSpace(account)) return false;
        if (usdAccounts.Contains(account)) return true;
        var dot = account.IndexOf('.');
        if (dot > 0 && usdAccounts.Contains(account[..dot])) return true;
        return false;
    }

    private static double ReadUsdToCadRate(FinancialsOptions financialsOptions)
    {
        var raw = financialsOptions?.CashUsdToCadRate ?? string.Empty;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return (double)parsed;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
            return (double)parsed;
        return 1.36;
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
