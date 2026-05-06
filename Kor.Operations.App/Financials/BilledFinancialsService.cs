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
    /// Builds a billed P&amp;L directly from Vantagepoint sub-ledgers. When
    /// <c>orgFilter</c> is null, rows from Org="USA" are converted to CAD
    /// equivalent using <c>Financials.Billed.UsdToCadRate</c>; single-org
    /// calls return the ledger currency for that org so the presenter can show
    /// the CAD and USD views separately.
    /// </summary>
    public sealed class BilledFinancialsService
    {
        // Revenue accounts captured from KOR's chart-of-accounts (4xxx range, stored as
        // credits = negative amounts). 4240 and 4500 are excluded because their entries
        // are stored with the opposite sign convention (data-quality quirks on those
        // accounts). Adjust via Financials.Billed.RevenueAccounts AppSetting if needed.
        private static readonly string[] DefaultRevenueAccounts =
        {
            "4001.00", // Billed Fee Revenue
            "4003.00", // Billed Labour Revenue
            "4210.00", // Reimbursable Consultant Revenue
            "4220.00", // Reimbursable Expense Revenue
            "4250.00", // Management & Administration
            "4260.00", // Intercompany Revenue
        };
        private static readonly AccountRange[] DefaultExpenseRanges =
        {
            new("5000.00", "5999.99"),
            new("6000.00", "6999.99"),
            new("7000.00", "7999.99")
        };
        private static readonly AccountRange[] DefaultOtherIncomeRanges = { new("8000.00", "8999.99") };

        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public BilledFinancialsService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ValidateCatalog(
                string.IsNullOrWhiteSpace(odbcOptions.Catalog) ? "C0000052267P_1_KOR00000000" : odbcOptions.Catalog);
        }

        public async Task<BilledFinancialsResult> BuildAsync(
            DateTime fromDate,
            DateTime toDate,
            string? orgFilter,
            bool forceRefresh,
            CancellationToken ct)
        {
            _ = forceRefresh;

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var periods = BuildMonthPeriods(fromDate, toDate);
                if (periods.Count == 0)
                    throw new InvalidOperationException("No accounting periods found for the selected date range.");

                var minPeriod = periods.Min();
                var maxPeriod = periods.Max();
                var org = NormalizeOrgFilter(orgFilter);
                var revenueAccounts = ParseAccounts(_financialsOptions.BilledRevenueAccounts, DefaultRevenueAccounts);
                var expenseRanges = ParseRanges(_financialsOptions.BilledExpenseAccountRanges, DefaultExpenseRanges);
                var otherIncomeRanges = ParseRanges(_financialsOptions.BilledOtherIncomeAccountRanges, DefaultOtherIncomeRanges);
                var usdToCadRate = ReadDecimal(_financialsOptions.BilledUsdToCadRate, 1.36m);
                var convertUsaToCad = org == null;

                using var cn = CreateConnection();
                cn.Open();

                var revenueRows = LoadRevenue(cn, minPeriod, maxPeriod, org, revenueAccounts, ct);
                var expenseRows = LoadLedgerRanges(cn, minPeriod, maxPeriod, org, expenseRanges, ct);
                var otherIncomeRows = LoadLedgerRanges(cn, minPeriod, maxPeriod, org, otherIncomeRanges, ct);

                var accountNames = LoadAccountNames(
                    cn,
                    revenueRows.Concat(expenseRows).Concat(otherIncomeRows).Select(r => r.Account),
                    ct);

                var lines = BuildLines(periods, revenueRows, expenseRows, otherIncomeRows, convertUsaToCad, usdToCadRate, accountNames);
                var revenueTrend = TrendFor(lines, "Total Revenue", periods);
                var expenseTrend = TrendFor(lines, "Total Expenses", periods);
                var otherIncomeTrend = TrendFor(lines, "Total Other Income", periods);
                var netTrend = revenueTrend
                    .Select((v, i) => v + (i < otherIncomeTrend.Length ? otherIncomeTrend[i] : 0m) - (i < expenseTrend.Length ? expenseTrend[i] : 0m))
                    .ToArray();
                var trendLabels = TrendLabels(periods);
                var maxBilledPeriod = LoadMaxBilledPeriod(cn, org, revenueAccounts, ct);
                var reconciliationEndPeriod = maxBilledPeriod.HasValue
                    ? Math.Min(maxPeriod, maxBilledPeriod.Value)
                    : maxPeriod;
                var reconciliation = LoadReconciliation(
                    cn,
                    org,
                    revenueAccounts,
                    minPeriod,
                    reconciliationEndPeriod,
                    convertUsaToCad,
                    usdToCadRate,
                    lines,
                    ct);

                return new BilledFinancialsResult(
                    periods.ToArray(),
                    periods.Select(PeriodColumnHeader).ToArray(),
                    lines,
                    netTrend,
                    revenueTrend,
                    expenseTrend,
                    trendLabels,
                    maxBilledPeriod,
                    reconciliation);
            }, ct).ConfigureAwait(false);
        }

        private List<LedgerAmountRow> LoadRevenue(
            OdbcConnection cn,
            int minPeriod,
            int maxPeriod,
            string? orgFilter,
            IReadOnlyList<string> accounts,
            CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(-Amount) AS Amount
FROM [{_catalog}].dbo.LedgerAR
WHERE Period BETWEEN ? AND ?
  AND TransType = 'IN'
  AND Account IN ({MakePlaceholders(accounts.Count)})
  AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            foreach (var account in accounts)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = account });
            AddNullableOrgParameters(cmd, orgFilter);

            return ReadAmountRows(cmd, ct);
        }

        private List<LedgerAmountRow> LoadLedgerRanges(
            OdbcConnection cn,
            int minPeriod,
            int maxPeriod,
            string? orgFilter,
            IReadOnlyList<AccountRange> ranges,
            CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var rangeWhere = string.Join(
                "\n      OR ",
                ranges.Select(_ => "(RIGHT(REPLICATE('0', 13) + Account, 13) BETWEEN RIGHT(REPLICATE('0', 13) + ?, 13) AND RIGHT(REPLICATE('0', 13) + ?, 13))"));

            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(Amount) AS Amount
FROM (
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerAP   WHERE Period BETWEEN ? AND ?
    UNION ALL
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerEX   WHERE Period BETWEEN ? AND ?
    UNION ALL
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerMisc WHERE Period BETWEEN ? AND ?
) x
WHERE (
      {rangeWhere}
)
AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            for (var i = 0; i < 3; i++)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            }

            foreach (var range in ranges)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = range.StartAccount });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = range.EndAccount });
            }

            AddNullableOrgParameters(cmd, orgFilter);
            return ReadAmountRows(cmd, ct);
        }

        private BilledPostedReconciliation LoadReconciliation(
            OdbcConnection cn,
            string? orgFilter,
            IReadOnlyList<string> revenueAccounts,
            int rangeStartPeriod,
            int rangeEndPeriod,
            bool convertUsaToCad,
            decimal usdToCadRate,
            IReadOnlyList<BilledLine> lines,
            CancellationToken ct)
        {
            var billedRange = SumLineRange(lines, "Total Revenue", rangeStartPeriod, rangeEndPeriod);
            var maxPostedPeriod = LoadMaxPostedPeriod(cn, orgFilter, ct);

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(-Amount) AS Amount
FROM [{_catalog}].dbo.GLSummary
WHERE Period BETWEEN ? AND ?
  AND Account IN ({MakePlaceholders(revenueAccounts.Count)})
  AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = rangeStartPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = rangeEndPeriod });
            foreach (var account in revenueAccounts)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = account });
            AddNullableOrgParameters(cmd, orgFilter);

            var posted = ReadAmountRows(cmd, ct).Sum(r => ApplyCurrency(r, convertUsaToCad, usdToCadRate));
            return new BilledPostedReconciliation(billedRange, posted, billedRange - posted, maxPostedPeriod);
        }

        private static IReadOnlyList<BilledLine> BuildLines(
            IReadOnlyList<int> periods,
            IReadOnlyList<LedgerAmountRow> revenueRows,
            IReadOnlyList<LedgerAmountRow> expenseRows,
            IReadOnlyList<LedgerAmountRow> otherIncomeRows,
            bool convertUsaToCad,
            decimal usdToCadRate,
            IReadOnlyDictionary<string, string> accountNames)
        {
            var lines = new List<BilledLine>();

            AddDetailLines(lines, "Revenue", 0, revenueRows);
            AddSectionTotal(lines, "Revenue", "Total Revenue", 0);

            AddDetailLines(lines, "Expenses", 1, expenseRows);
            AddSectionTotal(lines, "Expenses", "Total Expenses", 1);

            AddDetailLines(lines, "Other Income", 2, otherIncomeRows);
            AddSectionTotal(lines, "Other Income", "Total Other Income", 2);

            var revenueTotal = FindAmounts(lines, "Total Revenue");
            var expenseTotal = FindAmounts(lines, "Total Expenses");
            var otherIncomeTotal = FindAmounts(lines, "Total Other Income");
            lines.Add(new BilledLine("Summary", "Total Revenue", "GrandTotal", -1, 10, revenueTotal));
            lines.Add(new BilledLine("Summary", "Total Expenses", "GrandTotal", -1, 20, expenseTotal));
            lines.Add(new BilledLine("Summary", "Total Other Income", "GrandTotal", -1, 25, otherIncomeTotal));
            lines.Add(new BilledLine("Summary", "Net Income", "GrandTotal", -1, 30, periods.ToDictionary(
                p => p,
                p => Get(revenueTotal, p) + Get(otherIncomeTotal, p) - Get(expenseTotal, p))));

            return lines
                .OrderBy(l => l.SectionSort)
                .ThenBy(l => l.LineSort)
                .ThenBy(l => l.LineItem, StringComparer.OrdinalIgnoreCase)
                .ToList();

            void AddDetailLines(List<BilledLine> target, string section, int sectionSort, IReadOnlyList<LedgerAmountRow> rows)
            {
                foreach (var group in rows.GroupBy(r => r.Account).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var amounts = periods.ToDictionary(
                        p => p,
                        p => group.Where(r => r.Period == p).Sum(r => ApplyCurrency(r, convertUsaToCad, usdToCadRate)));
                    target.Add(new BilledLine(section, FormatAccountLabel(group.Key, accountNames), "Detail", sectionSort, AccountSort(group.Key), amounts, group.Key));
                }
            }

            void AddSectionTotal(List<BilledLine> target, string section, string lineItem, int sectionSort)
            {
                var detail = target
                    .Where(l => string.Equals(l.Section, section, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(l.RowKind, "Detail", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var amounts = periods.ToDictionary(p => p, p => detail.Sum(l => Get(l.AmountsByPeriod, p)));
                target.Add(new BilledLine(section, lineItem, "SectionTotal", sectionSort, int.MaxValue - 10, amounts));
            }
        }

        private static decimal[] TrendFor(IReadOnlyList<BilledLine> lines, string lineItem, IReadOnlyList<int> periods)
        {
            var amounts = FindAmounts(lines, lineItem);
            var source = periods.Select(p => Get(amounts, p)).ToArray();
            return source.Length <= 12 ? source : source.Skip(source.Length - 12).ToArray();
        }

        private static IReadOnlyDictionary<int, decimal> FindAmounts(IReadOnlyList<BilledLine> lines, string lineItem)
            => lines.FirstOrDefault(l => string.Equals(l.LineItem, lineItem, StringComparison.OrdinalIgnoreCase))?.AmountsByPeriod
               ?? new Dictionary<int, decimal>();

        private static decimal SumLineRange(IReadOnlyList<BilledLine> lines, string lineItem, int startPeriod, int endPeriod)
            => FindAmounts(lines, lineItem).Where(kvp => kvp.Key >= startPeriod && kvp.Key <= endPeriod).Sum(kvp => kvp.Value);

        private static decimal ApplyCurrency(LedgerAmountRow row, bool convertUsaToCad, decimal usdToCadRate)
            => convertUsaToCad && string.Equals(row.Org, "USA", StringComparison.OrdinalIgnoreCase)
                ? row.Amount * usdToCadRate
                : row.Amount;

        private static decimal Get(IReadOnlyDictionary<int, decimal> amounts, int period)
            => amounts.TryGetValue(period, out var value) ? value : 0m;

        private List<LedgerAmountRow> ReadAmountRows(OdbcCommand cmd, CancellationToken ct)
        {
            var rows = new List<LedgerAmountRow>(256);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (r.IsDBNull(0) || r.IsDBNull(1) || r.IsDBNull(3))
                    continue;

                rows.Add(new LedgerAmountRow(
                    Account: Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                    Period: Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                    Org: r.IsDBNull(2) ? "" : Convert.ToString(r.GetValue(2), CultureInfo.InvariantCulture) ?? "",
                    Amount: Convert.ToDecimal(r.GetValue(3), CultureInfo.InvariantCulture)));
            }

            return rows;
        }

        public async Task<IReadOnlyList<BilledLedgerTransaction>> LoadLedgerTransactionsAsync(
            string account,
            int period,
            string? orgFilter,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(account))
                return Array.Empty<BilledLedgerTransaction>();

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;

                var whereOrg = string.IsNullOrWhiteSpace(orgFilter) ? "" : " AND l.Org = ? ";

                cmd.CommandText = $@"
SELECT
    l.Source,
    l.Period,
    MAX(l.TransDate) AS TransDate,
    COALESCE(NULLIF(l.Invoice, ''), NULLIF(l.Voucher, ''), NULLIF(l.RefNo, ''), '(none)') AS DocumentNo,
    COALESCE(
        NULLIF(cc.Name, ''),
        NULLIF(cv.Name, ''),
        NULLIF(LTRIM(RTRIM(COALESCE(em.FirstName, '') + ' ' + COALESCE(em.LastName, ''))), ''),
        NULLIF(l.Vendor, ''),
        NULLIF(l.Employee, ''),
        '(unmapped)') AS Counterparty,
    l.Account,
    l.TransType,
    MAX(COALESCE(NULLIF(l.Desc1, ''), NULLIF(l.Desc2, ''), '')) AS Description,
    SUM(l.SignedAmount) AS Amount,
    COUNT(*) AS EntryCount
FROM
(
    SELECT 'AR' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate,
           l.Desc1, l.Desc2, l.Amount, -l.Amount AS SignedAmount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{_catalog}].dbo.LedgerAR l
    WHERE l.Period = ? AND l.Account = ? AND l.TransType = 'IN' {whereOrg}
    UNION ALL
    SELECT 'AP' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate,
           l.Desc1, l.Desc2, l.Amount, l.Amount AS SignedAmount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{_catalog}].dbo.LedgerAP l
    WHERE l.Period = ? AND l.Account = ? {whereOrg}
    UNION ALL
    SELECT 'EX' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate,
           l.Desc1, l.Desc2, l.Amount, l.Amount AS SignedAmount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{_catalog}].dbo.LedgerEX l
    WHERE l.Period = ? AND l.Account = ? {whereOrg}
    UNION ALL
    SELECT 'Misc' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate,
           l.Desc1, l.Desc2, l.Amount, l.Amount AS SignedAmount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{_catalog}].dbo.LedgerMisc l
    WHERE l.Period = ? AND l.Account = ? {whereOrg}
) l
LEFT JOIN
(
    SELECT ar.Invoice, ar.WBS1, ar.ClientID,
           ROW_NUMBER() OVER (PARTITION BY ar.Invoice, ar.WBS1
                              ORDER BY COALESCE(ar.InvoiceDate, ar.DueDate) DESC) AS rn
    FROM [{_catalog}].dbo.AR ar
    WHERE ar.ClientID IS NOT NULL AND LTRIM(RTRIM(ar.ClientID)) <> ''
) arx
  ON arx.Invoice = l.Invoice AND arx.WBS1 = l.WBS1 AND arx.rn = 1
LEFT JOIN [{_catalog}].dbo.Clendor cc ON cc.ClientID = arx.ClientID
LEFT JOIN [{_catalog}].dbo.Clendor cv ON cv.Vendor = l.Vendor
LEFT JOIN [{_catalog}].dbo.EMMain em ON em.Employee = l.Employee
GROUP BY
    l.Source, l.Period,
    COALESCE(NULLIF(l.Invoice, ''), NULLIF(l.Voucher, ''), NULLIF(l.RefNo, ''), '(none)'),
    COALESCE(
        NULLIF(cc.Name, ''),
        NULLIF(cv.Name, ''),
        NULLIF(LTRIM(RTRIM(COALESCE(em.FirstName, '') + ' ' + COALESCE(em.LastName, ''))), ''),
        NULLIF(l.Vendor, ''),
        NULLIF(l.Employee, ''),
        '(unmapped)'),
    l.Account, l.TransType
ORDER BY ABS(SUM(l.SignedAmount)) DESC, MAX(l.TransDate) DESC;";

                for (var i = 0; i < 4; i++)
                {
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = period });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = account.Trim() });
                    if (!string.IsNullOrWhiteSpace(orgFilter))
                        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter!.Trim() });
                }

                var rows = new List<BilledLedgerTransaction>(128);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();

                    rows.Add(new BilledLedgerTransaction(
                        Source: r.IsDBNull(0) ? "" : Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                        Period: r.IsDBNull(1) ? period : Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        TransDate: r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2), CultureInfo.InvariantCulture),
                        DocumentNo: r.IsDBNull(3) ? "" : Convert.ToString(r.GetValue(3), CultureInfo.InvariantCulture) ?? "",
                        Counterparty: r.IsDBNull(4) ? "" : Convert.ToString(r.GetValue(4), CultureInfo.InvariantCulture) ?? "",
                        Account: r.IsDBNull(5) ? "" : Convert.ToString(r.GetValue(5), CultureInfo.InvariantCulture) ?? "",
                        TransType: r.IsDBNull(6) ? "" : Convert.ToString(r.GetValue(6), CultureInfo.InvariantCulture) ?? "",
                        Description: r.IsDBNull(7) ? "" : Convert.ToString(r.GetValue(7), CultureInfo.InvariantCulture) ?? "",
                        Amount: r.IsDBNull(8) ? 0m : Convert.ToDecimal(r.GetValue(8), CultureInfo.InvariantCulture),
                        EntryCount: r.IsDBNull(9) ? 0 : Convert.ToInt32(r.GetValue(9), CultureInfo.InvariantCulture)));
                }

                return (IReadOnlyList<BilledLedgerTransaction>)rows;
            }, ct).ConfigureAwait(false);
        }

        private Dictionary<string, string> LoadAccountNames(OdbcConnection cn, IEnumerable<string> accounts, CancellationToken ct)
        {
            var distinct = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (distinct.Count == 0)
                return result;

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Name
FROM [{_catalog}].dbo.CA
WHERE Account IN ({MakePlaceholders(distinct.Count)});";
            foreach (var acct in distinct)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = acct });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (r.IsDBNull(0))
                    continue;
                var account = (Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "").Trim();
                var name = r.IsDBNull(1) ? "" : (Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "").Trim();
                if (account.Length == 0)
                    continue;
                result[account] = name;
            }
            return result;
        }

        private static string FormatAccountLabel(string account, IReadOnlyDictionary<string, string> accountNames)
        {
            if (accountNames.TryGetValue(account, out var name) && !string.IsNullOrWhiteSpace(name))
                return $"{name} ({account})";
            return account;
        }

        private int? LoadMaxBilledPeriod(OdbcConnection cn, string? orgFilter, IReadOnlyList<string> revenueAccounts, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT MAX(Period)
FROM [{_catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND Account IN ({MakePlaceholders(revenueAccounts.Count)})
  AND (? IS NULL OR Org = ?);";
            foreach (var account in revenueAccounts)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = account });
            AddNullableOrgParameters(cmd, orgFilter);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return null;
            var period = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return period > 0 ? period : null;
        }

        private int? LoadMaxPostedPeriod(OdbcConnection cn, string? orgFilter, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $"SELECT MAX(Period) FROM [{_catalog}].dbo.GLSummary WHERE (? IS NULL OR Org = ?);";
            AddNullableOrgParameters(cmd, orgFilter);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return null;
            var period = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return period > 0 ? period : null;
        }

        private OdbcConnection CreateConnection()
        {
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var user = _odbcOptions.User ?? string.Empty;
            var pwd = _odbcOptions.Password ?? string.Empty;
            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private static void AddNullableOrgParameters(OdbcCommand cmd, string? orgFilter)
        {
            object value = string.IsNullOrWhiteSpace(orgFilter) ? DBNull.Value : orgFilter.Trim();
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = value });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = value });
        }

        private static string? NormalizeOrgFilter(string? orgFilter)
            => string.IsNullOrWhiteSpace(orgFilter) ? null : orgFilter.Trim();

        private static List<int> BuildMonthPeriods(DateTime fromDate, DateTime toDate)
        {
            var start = new DateTime(fromDate.Year, fromDate.Month, 1);
            var end = new DateTime(toDate.Year, toDate.Month, 1);
            if (end < start)
                (start, end) = (end, start);

            var periods = new List<int>();
            for (var current = start; current <= end; current = current.AddMonths(1))
                periods.Add((current.Year * 100) + current.Month);
            return periods;
        }

        private static string PeriodColumnHeader(int period)
        {
            var year = period / 100;
            var month = period % 100;
            if (month < 1 || month > 12)
                return period.ToString(CultureInfo.InvariantCulture);
            var date = new DateTime(year, month, 1);
            return $"{date.ToString("MMM-yy", CultureInfo.InvariantCulture)} ({period})";
        }

        private static string[] TrendLabels(IReadOnlyList<int> periods)
        {
            var slice = periods.Count <= 12 ? periods : periods.Skip(periods.Count - 12).ToList();
            return slice.Select(p =>
            {
                var year = p / 100;
                var month = p % 100;
                if (month < 1 || month > 12)
                    return p.ToString(CultureInfo.InvariantCulture);
                var date = new DateTime(year, month, 1);
                return $"{date.ToString("MMM", CultureInfo.InvariantCulture)}\n{date.ToString("yy", CultureInfo.InvariantCulture)}";
            }).ToArray();
        }

        private static decimal ReadDecimal(string raw, decimal fallback)
        {
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                return value;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return value;
            return fallback;
        }

        private static IReadOnlyList<string> ParseAccounts(string raw, IReadOnlyList<string> fallback)
        {
            var accounts = (raw ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return accounts.Count == 0 ? fallback : accounts;
        }

        private static IReadOnlyList<AccountRange> ParseRanges(string raw, IReadOnlyList<AccountRange> fallback)
        {
            var ranges = new List<AccountRange>();
            foreach (var part in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pieces = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pieces.Length == 2)
                    ranges.Add(new AccountRange(pieces[0], pieces[1]));
            }

            return ranges.Count == 0 ? fallback : ranges;
        }

        private static string MakePlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static int AccountSort(string account)
        {
            var digits = new string((account ?? "").Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
        }

        private sealed record LedgerAmountRow(string Account, int Period, string Org, decimal Amount);

        private sealed record AccountRange(string StartAccount, string EndAccount);
    }

    public sealed record BilledFinancialsResult(
        int[] Periods,
        string[] PeriodColumnNames,
        IReadOnlyList<BilledLine> Lines,
        decimal[] NetIncomeTrendValues,
        decimal[] RevenueTrendValues,
        decimal[] ExpenseTrendValues,
        string[] TrendLabels,
        int? MaxBilledPeriod,
        BilledPostedReconciliation Reconciliation);

    public sealed record BilledLine(
        string Section,
        string LineItem,
        string RowKind,
        int SectionSort,
        int LineSort,
        IReadOnlyDictionary<int, decimal> AmountsByPeriod,
        string? Account = null);

    public sealed record BilledLedgerTransaction(
        string Source,
        int Period,
        DateTime? TransDate,
        string DocumentNo,
        string Counterparty,
        string Account,
        string TransType,
        string Description,
        decimal Amount,
        int EntryCount);

    public sealed record BilledPostedReconciliation(
        decimal BilledRangeCad,
        decimal PostedRangeCad,
        decimal DeltaCad,
        int? MaxPostedPeriod);
}
