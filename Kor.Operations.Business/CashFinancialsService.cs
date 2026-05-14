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
using Serilog;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Cash-position canonical service for KOR's GL-derived cash balances. Owns the
    /// same SQL and currency-bucketing logic that the WPF cash tile uses,
    /// so MCP-side answers match the App by construction. Predicates/exclusions/
    /// account override sets are read from FinancialsOptions so the App.config /
    /// appsettings.Production.json values stay the source of truth.
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

        public CashFinancialsResult Load(OdbcConnection cn, CancellationToken ct)
            => LoadCore(cn, ct);

        public async Task<CashFinancialsResult> LoadAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();
                return LoadCore(cn, ct);
            }, ct).ConfigureAwait(false);
        }

        private CashFinancialsResult LoadCore(OdbcConnection cn, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var fxRate = OrgFx.ParseUsdToCadRate(_financialsOptions.CashUsdToCadRate);
            var usdAccounts = ParseAccountSet(_financialsOptions.CashUsdAccounts);
            var whitelist = ParseAccountSet(_financialsOptions.CashAccountWhitelist);

            var banks = LoadBankAccounts(cn, whitelist, ct);
            if (banks.Count == 0)
                return new CashFinancialsResult("n/a", 0, 0, 0, 0, fxRate,
                    Array.Empty<CashAccountBalanceRow>(), Array.Empty<CashHistoryPoint>(),
                    CashUnpostedOverlay.Zero);

            var todayPeriod = DateTime.Today.ToString("yyyyMM", CultureInfo.InvariantCulture);
            var targetPeriod = FindLatestGlPeriodForAccounts(cn, banks, todayPeriod, ct);
            if (string.IsNullOrWhiteSpace(targetPeriod))
                targetPeriod = todayPeriod;

            var (history, perAccount) = LoadCashHistory(cn, banks, targetPeriod, usdAccounts, ct);

            // Sub-ledger overlay: GLSummary lags real activity by ~3 months at KOR.
            // Add per-account sums from Ledger* tables for TransDate > end of
            // targetPeriod so the headline cash position matches the accountant's
            // real-time balance sheet. Same posting-lag pattern BacklogService
            // uses for UnpostedFeeBilled. Posted history (older period points)
            // remain untouched — only the latest snapshot picks up the overlay.
            Dictionary<(string Account, string Org), double> overlay;
            try
            {
                overlay = LoadUnpostedCashOverlay(cn, banks, targetPeriod, ct);
                Log.Information(
                    "CashFinancialsService overlay: banks={Banks}, targetPeriod={Period}, rows={Rows}, total={Total}",
                    banks.Count, targetPeriod, overlay.Count, overlay.Values.Sum());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // If the sub-ledger query fails for any reason, fall back to
                // GL-only numbers rather than blanking the cash tile. Surface
                // the cause so the silent failure mode is debuggable.
                Log.Warning(ex,
                    "CashFinancialsService overlay query failed; falling back to GL-only. banks={Banks}, targetPeriod={Period}",
                    banks.Count, targetPeriod);
                overlay = new Dictionary<(string, string), double>();
            }

            (perAccount, var overlayBuckets) = ApplyOverlay(perAccount, overlay, usdAccounts);

            if (history.Count == 0)
            {
                var combinedNoHistory = overlayBuckets.Cad + (overlayBuckets.Usa * fxRate) + overlayBuckets.Bcc;
                return new CashFinancialsResult(targetPeriod, overlayBuckets.Cad, overlayBuckets.Usa, overlayBuckets.Bcc,
                    combinedNoHistory, fxRate, perAccount, history, overlayBuckets);
            }

            var latestPosted = history[^1];
            var cad = latestPosted.Cad + overlayBuckets.Cad;
            var usa = latestPosted.Usa + overlayBuckets.Usa;
            var bcc = latestPosted.Bcc + overlayBuckets.Bcc;
            var combined = cad + (usa * fxRate) + bcc;
            return new CashFinancialsResult(latestPosted.Period, cad, usa, bcc, combined, fxRate, perAccount, history, overlayBuckets);
        }

        private Dictionary<(string Account, string Org), double> LoadUnpostedCashOverlay(
            OdbcConnection cn, List<BankAcct> banks, string targetPeriod, CancellationToken ct)
        {
            var result = new Dictionary<(string, string), double>();
            if (!TryParsePeriodEnd(targetPeriod, out var lastClosedEnd)) return result;

            // Reduce CFGBanks rows to the set of 4-char account prefixes we need
            // to query. KOR's Account column is varchar 'NNNN.00' but other
            // installs store the bare 'NNNN' — using LEFT(LTRIM(RTRIM(...)),4)
            // sidesteps that drift (same predicate BilledFinancialsService uses).
            var prefixes = banks
                .Select(b => ExtractAccountPrefix(b.Account))
                .Where(p => p.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (prefixes.Count == 0) return result;

            // Allowed (account-prefix, org) keys the GL side recognises. Used
            // to discard sub-ledger rows that hit a cash account but on an Org
            // we don't track (intercompany legs, journals tagged to a holding
            // Org, etc.) so the overlay matches the GL bucketing exactly.
            var bankKeys = banks
                .Select(b => (Prefix: ExtractAccountPrefix(b.Account), Org: (b.Org ?? string.Empty).Trim()))
                .Where(k => k.Prefix.Length > 0)
                .ToHashSet();

            // Query each sub-ledger that can touch a cash account. Filter by
            // account prefix and by TransDate > last closed period end. The
            // sum is the net cash delta not yet rolled up into GLSummary.
            var ledgers = new[] { "LedgerAR", "LedgerAP", "LedgerEX", "LedgerMisc" };
            foreach (var ledger in ledgers)
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var placeholders = string.Join(",", Enumerable.Repeat("?", prefixes.Count));
                cmd.CommandText = $@"
SELECT LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) AS Acct4,
       COALESCE(Org,'') AS Org,
       SUM(COALESCE(Amount,0)) AS Amt
FROM [{_catalog}].dbo.{ledger}
WHERE TransDate > ?
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ({placeholders})
GROUP BY LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4), COALESCE(Org,'');";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = lastClosedEnd });
                foreach (var p in prefixes)
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = p });

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                int rowsRead = 0, rowsKept = 0;
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    rowsRead++;
                    var prefix = GetTrimmed(r, 0);
                    var org = GetTrimmed(r, 1);
                    var amt = GetDouble(r, 2);
                    if (amt == 0.0) continue;

                    // Map ledger prefix back to the canonical (Account, Org)
                    // key that the GL side uses, so ApplyOverlay joins cleanly.
                    var canon = banks.FirstOrDefault(b =>
                        string.Equals(ExtractAccountPrefix(b.Account), prefix, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((b.Org ?? string.Empty).Trim(), org, StringComparison.OrdinalIgnoreCase));
                    if (canon == null)
                    {
                        // Sub-ledger hit a cash account but on an Org the GL
                        // side doesn't track — skip rather than invent a row.
                        continue;
                    }
                    rowsKept++;
                    var key = (canon.Account, canon.Org);
                    result[key] = (result.TryGetValue(key, out var prev) ? prev : 0.0) + amt;
                }
                Log.Information(
                    "CashFinancialsService overlay {Ledger}: rowsRead={Read}, rowsKept={Kept}",
                    ledger, rowsRead, rowsKept);
            }
            return result;
        }

        private static string ExtractAccountPrefix(string account)
        {
            if (string.IsNullOrWhiteSpace(account)) return string.Empty;
            var trimmed = account.Trim();
            return trimmed.Length <= 4 ? trimmed : trimmed[..4];
        }

        private static (IReadOnlyList<CashAccountBalanceRow> PerAccount, CashUnpostedOverlay Buckets) ApplyOverlay(
            IReadOnlyList<CashAccountBalanceRow> perAccount,
            Dictionary<(string Account, string Org), double> overlay,
            HashSet<string> usdAccounts)
        {
            double cad = 0, usa = 0, bcc = 0;
            var augmented = new List<CashAccountBalanceRow>(perAccount.Count);
            foreach (var row in perAccount)
            {
                overlay.TryGetValue((row.Account, row.Org), out var delta);
                augmented.Add(new CashAccountBalanceRow(row.Company, row.Account, row.Org, row.Currency, row.Balance + delta));

                if (delta == 0.0) continue;
                var classification = MatchesAccountSet(row.Account, usdAccounts)
                    ? "USA"
                    : (!string.IsNullOrWhiteSpace(row.Org)
                        ? row.Org.Trim().ToUpperInvariant()
                        : (row.Company ?? string.Empty).Trim().ToUpperInvariant());
                switch (classification)
                {
                    case "USA": usa += delta; break;
                    case "BCC": bcc += delta; break;
                    default:    cad += delta; break;
                }
            }
            return (augmented, new CashUnpostedOverlay(cad, usa, bcc));
        }

        private static bool TryParsePeriodEnd(string period, out DateTime endDate)
        {
            endDate = default;
            if (string.IsNullOrWhiteSpace(period) || period.Length != 6) return false;
            if (!int.TryParse(period[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return false;
            if (!int.TryParse(period[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return false;
            if (m < 1 || m > 12) return false;
            endDate = new DateTime(y, m, DateTime.DaysInMonth(y, m));
            return true;
        }

        private (IReadOnlyList<CashHistoryPoint> History, IReadOnlyList<CashAccountBalanceRow> PerAccount) LoadCashHistory(
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
                return (Array.Empty<CashHistoryPoint>(), BuildZeroPerAccountRows(banks, usdAccounts));

            var cumulative = new List<CashHistoryPoint>(ordered.Count);
            double runCad = 0, runUsa = 0, runBcc = 0;
            foreach (var p in ordered)
            {
                var v = byPeriod[p];
                runCad += v.Cad; runUsa += v.Usa; runBcc += v.Bcc;
                cumulative.Add(new CashHistoryPoint(p, runCad, runUsa, runBcc));
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

        private static IReadOnlyList<CashAccountBalanceRow> BuildZeroPerAccountRows(List<BankAcct> banks, HashSet<string> usdAccounts)
            => banks
                .OrderBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Account, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Org, StringComparer.OrdinalIgnoreCase)
                .Select(b => new CashAccountBalanceRow(b.Company, b.Account, b.Org, ResolveCurrency(b, usdAccounts), 0.0))
                .ToList();

        private static IReadOnlyList<CashAccountBalanceRow> BuildPerAccountRows(
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
                .Select(b => new CashAccountBalanceRow(b.Company, b.Account, b.Org,
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
        IReadOnlyList<CashAccountBalanceRow> PerAccount,
        IReadOnlyList<CashHistoryPoint> History,
        CashUnpostedOverlay UnpostedOverlay)
    {
        public double Total => Cad + Usa + Bcc;

        public static readonly CashFinancialsResult Empty = new(
            "n/a", 0.0, 0.0, 0.0, 0.0, 1.36,
            Array.Empty<CashAccountBalanceRow>(),
            Array.Empty<CashHistoryPoint>(),
            CashUnpostedOverlay.Zero);
    }

    /// <summary>
    /// Sub-ledger overlay applied on top of the GLSummary cumulative cash
    /// balance. Captures transactions with TransDate beyond the latest closed
    /// GL period — Deltek runs ~3 months of posting lag at KOR, so without
    /// this overlay the cash tile lags the accountant's balance sheet by
    /// roughly that window.
    /// </summary>
    public sealed record CashUnpostedOverlay(double Cad, double Usa, double Bcc)
    {
        public double Total => Cad + Usa + Bcc;
        public static readonly CashUnpostedOverlay Zero = new(0.0, 0.0, 0.0);
    }

    public sealed record CashAccountBalanceRow(string Company, string Account, string Org, string Currency, double Balance);

    public sealed record CashHistoryPoint(string Period, double Cad, double Usa, double Bcc)
    {
        public double Total => Cad + Usa + Bcc;
    }
}
