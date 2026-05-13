#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Cash-position canonical service for KOR's GL-derived cash balances. Owns the
    /// same SQL and currency-bucketing logic that Kor.Operations.App's CashLoader uses
    /// (so MCP-side answers match the WPF cash tile by construction).
    ///
    /// TODO (consolidation batch): retire Kor.Operations.App\Financials\Loaders\CashLoader.cs
    /// and have ExecutiveSummaryDeltekLoader call CashFinancialsService.LoadAsync directly.
    /// Today both implementations exist temporarily; the SQL must stay in sync between
    /// them. Predicates/exclusions/account override sets are read from FinancialsOptions
    /// so the App.config / appsettings.Production.json values stay the source of truth.
    /// </summary>
    public sealed class CashFinancialsService
    {
        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public CashFinancialsService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public async Task<CashFinancialsResult> LoadAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var fxRate = OrgFx.ParseUsdToCadRate(_financialsOptions.CashUsdToCadRate);
                var usdAccounts = ParseAccountSet(_financialsOptions.CashUsdAccounts);
                var whitelist = ParseAccountSet(_financialsOptions.CashAccountWhitelist);

                using var cn = CreateConnection();
                cn.Open();

                var banks = LoadBankAccounts(cn, whitelist, ct);
                if (banks.Count == 0)
                    return new CashFinancialsResult("n/a", 0, 0, 0, 0, fxRate,
                        Array.Empty<CashAccountRow>(), Array.Empty<CashHistoryRow>());

                var todayPeriod = DateTime.Today.ToString("yyyyMM", CultureInfo.InvariantCulture);
                var targetPeriod = FindLatestGlPeriodForAccounts(cn, banks, todayPeriod, ct);
                if (string.IsNullOrWhiteSpace(targetPeriod))
                    targetPeriod = todayPeriod;

                var (history, perAccount) = LoadCashHistory(cn, banks, targetPeriod, usdAccounts, ct);
                if (history.Count == 0)
                    return new CashFinancialsResult(targetPeriod, 0, 0, 0, 0, fxRate, perAccount, history);

                var latest = history[^1];
                var combined = latest.Cad + (latest.Usa * fxRate) + latest.Bcc;
                return new CashFinancialsResult(latest.Period, latest.Cad, latest.Usa, latest.Bcc, combined, fxRate, perAccount, history);
            }, ct).ConfigureAwait(false);
        }

        private (IReadOnlyList<CashHistoryRow> History, IReadOnlyList<CashAccountRow> PerAccount) LoadCashHistory(
            OdbcConnection cn, List<BankAcct> banks, string targetPeriod, HashSet<string> usdAccounts, CancellationToken ct)
        {
            var byPeriod = new Dictionary<string, (double Cad, double Usa, double Bcc)>(StringComparer.OrdinalIgnoreCase);
            var byAccountPeriod = new Dictionary<(string Company, string Account, string Org, string Period), double>();

            foreach (var chunk in Chunk(banks, 25))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));
                cmd.CommandText = $@"
SELECT Period, Account, Org, SUM(COALESCE(Amount,0)) AS Amt
FROM [{_catalog}].dbo.GLSummary
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
                    var period = GetTrimmed(r, 0);
                    if (period.Length != 6 || !period.All(char.IsDigit)) continue;
                    var acct = GetTrimmed(r, 1);
                    var org = GetTrimmed(r, 2);
                    var amt = GetDouble(r, 3);

                    var match = chunk.FirstOrDefault(x =>
                        string.Equals(x.Account, acct, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Org, org, StringComparison.OrdinalIgnoreCase));
                    if (match == null) continue;

                    var key = (match.Company, match.Account, match.Org, period);
                    byAccountPeriod[key] = (byAccountPeriod.TryGetValue(key, out var prev) ? prev : 0.0) + amt;

                    byPeriod.TryGetValue(period, out var cur);
                    var classification = MatchesAccountSet(match.Account, usdAccounts)
                        ? "USA"
                        : (!string.IsNullOrWhiteSpace(match.Org)
                            ? match.Org.Trim().ToUpperInvariant()
                            : (match.Company ?? string.Empty).Trim().ToUpperInvariant());
                    switch (classification)
                    {
                        case "USA": cur.Usa += amt; break;
                        case "BCC": cur.Bcc += amt; break;
                        default:    cur.Cad += amt; break;
                    }
                    byPeriod[period] = cur;
                }
            }

            var ordered = byPeriod.Keys
                .Where(p => p.Length == 6 && p.All(char.IsDigit))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            if (ordered.Count == 0)
                return (Array.Empty<CashHistoryRow>(), BuildZeroPerAccountRows(banks, usdAccounts));

            var cumulative = new List<CashHistoryRow>(ordered.Count);
            double runCad = 0, runUsa = 0, runBcc = 0;
            foreach (var p in ordered)
            {
                var v = byPeriod[p];
                runCad += v.Cad; runUsa += v.Usa; runBcc += v.Bcc;
                cumulative.Add(new CashHistoryRow(p, runCad, runUsa, runBcc));
            }

            var perAccount = BuildPerAccountRows(banks, byAccountPeriod, ordered, usdAccounts);
            if (cumulative.Count > 12) cumulative = cumulative.TakeLast(12).ToList();
            return (cumulative, perAccount);
        }

        private List<BankAcct> LoadBankAccounts(OdbcConnection cn, HashSet<string> whitelist, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Company, Account, Org
FROM [{_catalog}].dbo.CFGBanks
WHERE COALESCE(Account,'') <> '';";

            var list = new List<BankAcct>(64);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var company = GetTrimmed(r, 0);
                var account = GetTrimmed(r, 1);
                var org = GetTrimmed(r, 2);
                if (company.Length == 0 || account.Length == 0) continue;
                if (string.IsNullOrWhiteSpace(org)) org = company;
                if (whitelist.Count > 0 && !MatchesAccountSet(account, whitelist)) continue;
                list.Add(new BankAcct(company, account, org));
            }
            return list
                .GroupBy(b => string.Join("|", b.Company, b.Account, b.Org), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private string FindLatestGlPeriodForAccounts(OdbcConnection cn, List<BankAcct> accts, string todayPeriod, CancellationToken ct)
        {
            string latest = string.Empty;
            foreach (var chunk in Chunk(accts, 40))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));
                cmd.CommandText = $@"
SELECT MAX(Period) AS MaxPeriod
FROM [{_catalog}].dbo.GLSummary
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
                if (p.Length > 0 && string.CompareOrdinal(p, latest) > 0) latest = p;
            }
            return latest;
        }

        private static IReadOnlyList<CashAccountRow> BuildZeroPerAccountRows(List<BankAcct> banks, HashSet<string> usdAccounts)
            => banks
                .OrderBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Account, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Org, StringComparer.OrdinalIgnoreCase)
                .Select(b => new CashAccountRow(b.Company, b.Account, b.Org, ResolveCurrency(b, usdAccounts), 0.0))
                .ToList();

        private static IReadOnlyList<CashAccountRow> BuildPerAccountRows(
            List<BankAcct> banks,
            Dictionary<(string Company, string Account, string Org, string Period), double> byAccountPeriod,
            List<string> orderedPeriods,
            HashSet<string> usdAccounts)
        {
            var balances = banks.ToDictionary(b => (b.Company, b.Account, b.Org), _ => 0.0);
            foreach (var period in orderedPeriods)
                foreach (var b in banks)
                    if (byAccountPeriod.TryGetValue((b.Company, b.Account, b.Org, period), out var amt))
                        balances[(b.Company, b.Account, b.Org)] += amt;

            return banks
                .OrderBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Account, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Org, StringComparer.OrdinalIgnoreCase)
                .Select(b => new CashAccountRow(b.Company, b.Account, b.Org,
                    ResolveCurrency(b, usdAccounts),
                    balances[(b.Company, b.Account, b.Org)]))
                .ToList();
        }

        private static string ResolveCurrency(BankAcct b, HashSet<string> usdAccounts)
        {
            if (MatchesAccountSet(b.Account, usdAccounts)) return "USA";
            var org = (!string.IsNullOrWhiteSpace(b.Org) ? b.Org : b.Company ?? string.Empty).Trim().ToUpperInvariant();
            return org switch { "USA" => "USA", "BCC" => "BCC", _ => "CAD" };
        }

        private static HashSet<string> ParseAccountSet(string? raw)
            => (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static bool MatchesAccountSet(string account, HashSet<string> set)
        {
            if (set.Count == 0 || string.IsNullOrWhiteSpace(account)) return false;
            if (set.Contains(account)) return true;
            var dot = account.IndexOf('.');
            if (dot > 0 && set.Contains(account[..dot])) return true;
            return false;
        }

        private OdbcConnection CreateConnection()
        {
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User, _odbcOptions.Password, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
                yield return source.Skip(i).Take(size).ToList();
        }

        private static string GetTrimmed(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? string.Empty : (Convert.ToString(r.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

        private static double GetDouble(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(r.GetValue(ordinal), CultureInfo.InvariantCulture);

        private sealed record BankAcct(string Company, string Account, string Org);
    }

    public sealed record CashFinancialsResult(
        string Period,
        double Cad,
        double Usa,
        double Bcc,
        double CombinedCadEquivalent,
        double UsdToCadRate,
        IReadOnlyList<CashAccountRow> PerAccount,
        IReadOnlyList<CashHistoryRow> History);

    public sealed record CashAccountRow(string Company, string Account, string Org, string Currency, double Balance);

    public sealed record CashHistoryRow(string Period, double Cad, double Usa, double Bcc);
}
