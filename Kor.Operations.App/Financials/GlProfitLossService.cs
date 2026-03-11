using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Data;
namespace Kor.Operations.Financials
{
    internal sealed class GlProfitLossService
    {
        // Keep aligned with FinancialsService (Deltek catalog).
        private const string Catalog = "C0000052267P_1_KOR00000000";

        public async Task<IReadOnlyList<GlTableInfo>> GetTablesAsync(CancellationToken cancelToken)
        {
            return await Task.Run(() =>
            {
                cancelToken.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 60;
                cmd.CommandText = $"SELECT TableNo, TableName, FilterOrg, FilterCode FROM [{Catalog}].dbo.GLTable ORDER BY TableNo;";

                using var r = cmd.ExecuteReader();
                var list = new List<GlTableInfo>();
                while (r.Read())
                {
                    cancelToken.ThrowIfCancellationRequested();
                    list.Add(new GlTableInfo
                    {
                        TableNo = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                        TableName = r.IsDBNull(1) ? "" : r.GetString(1),
                        FilterOrg = r.IsDBNull(2) ? "" : r.GetString(2),
                        FilterCode = r.IsDBNull(3) ? "" : r.GetString(3),
                    });
                }

                return (IReadOnlyList<GlTableInfo>)list;
            }, cancelToken).ConfigureAwait(false);
        }

        internal sealed record BuildResult(
            DataTable Table,
            int[] Periods,
            string[] PeriodColumnNames,
            decimal[] NetIncomeTrendValues,
            decimal[] RevenueTrendValues,
            decimal[] ExpenseTrendValues,
            string[] TrendLabels);

        internal sealed record LedgerTransactionDrilldownRow(
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

        public async Task<BuildResult> BuildProfitLossAsync(
            short tableNo,
            DateTime fromDate,
            DateTime toDate,
            string? orgFilter,
            bool flipSign,
            bool forceRefresh,
            CancellationToken cancelToken)
        {
            // forceRefresh currently unused; kept to align with UI and future caching.
            _ = forceRefresh;

            return await Task.Run(() =>
            {
                cancelToken.ThrowIfCancellationRequested();

                using var cn = CreateConnection();
                cn.Open();

                var periods = BuildMonthPeriods(fromDate, toDate);
                if (periods.Count == 0)
                    throw new InvalidOperationException("No accounting periods found for the selected date range.");

                var minP = periods.Min();
                var maxP = periods.Max();
                if (!HasAnyGlSummaryInRange(cn, minP, maxP, orgFilter, cancelToken))
                    throw new InvalidOperationException("No GL summary data found for the selected date range.");

                var periodColumnNames = periods.Select(PeriodColumnHeader).ToArray();

                // Load parent groups (sections) and detail groups (line items) for the selected GL table.
                var sections = LoadSections(cn, tableNo, cancelToken);
                var lines = LoadLineGroups(cn, tableNo, cancelToken);

                // Aggregate amounts per (GLGroup, Period) for the table, filtered by org if provided.
                var amounts = LoadAmountsByGroupAndPeriod(cn, tableNo, minP, maxP, orgFilter, flipSign, cancelToken);

                // Build output table.
                var dt = new DataTable("PnL");
                dt.Columns.Add("Section", typeof(string));
                dt.Columns.Add("LineItem", typeof(string));
                dt.Columns.Add("RowKind", typeof(string));           // Detail, SectionTotal, GrandTotal
                dt.Columns.Add("LineGroupCode", typeof(short));
                dt.Columns.Add("SectionSort", typeof(int));
                dt.Columns.Add("LineSort", typeof(int));
                dt.Columns.Add("IsAllZero", typeof(bool));

                foreach (var col in periodColumnNames)
                    dt.Columns.Add(col, typeof(decimal));

                // Executive columns
                dt.Columns.Add("Current", typeof(decimal));
                dt.Columns.Add("Prior", typeof(decimal));
                dt.Columns.Add("MoM", typeof(decimal));
                dt.Columns.Add("MoMPct", typeof(decimal));
                dt.Columns.Add("YTD", typeof(decimal));
                dt.Columns.Add("TTM", typeof(decimal));
                dt.Columns.Add("PctOfRevenue", typeof(decimal));

                // Map periods to columns by index.
                var colByPeriod = new Dictionary<int, string>();
                for (var i = 0; i < periods.Count; i++)
                    colByPeriod[periods[i]] = periodColumnNames[i];

                // If we have section mappings, use them; otherwise, just list all line groups as one section.
                if (sections.Count > 0)
                {
                    foreach (var sec in sections.OrderBy(s => s.SortOrder).ThenBy(s => s.Description))
                    {
                        var secLines = lines.Where(l => l.ParentSectionId == sec.Code).OrderBy(l => l.SortOrder).ThenBy(l => l.Description).ToList();
                        if (secLines.Count == 0)
                            continue;

                        foreach (var line in secLines)
                            AddLine(dt, sec.Description, sec.SortOrder, line.Description, line.SortOrder, line.Code, colByPeriod, amounts);
                    }
                }
                else
                {
                    foreach (var line in lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Description))
                        AddLine(dt, "P&L", 0, line.Description, line.SortOrder, line.Code, colByPeriod, amounts);
                }

                AddSectionTotals(dt, periodColumnNames);
                AddGrandTotals(dt, periodColumnNames);
                ComputeExecutiveColumns(dt, periods.ToArray(), periodColumnNames);

                 var netTrend = GetTrend(dt, "Net Income", periodColumnNames);
                 var revTrend = GetTrend(dt, "Total Revenue", periodColumnNames).Select(Math.Abs).ToArray();
                 var expTrend = GetTrend(dt, "Total Expenses", periodColumnNames).Select(Math.Abs).ToArray();
                 var trendLabels = GetTrendLabels(periods);

                 return new BuildResult(dt, periods.ToArray(), periodColumnNames, netTrend, revTrend, expTrend, trendLabels);
             }, cancelToken).ConfigureAwait(false);
         }

        private static int ToPeriod(DateTime d) => (d.Year * 100) + d.Month;

        private static void AddLine(
            DataTable dt,
            string section,
            int sectionSort,
            string lineItem,
            int lineSort,
            short glGroup,
            Dictionary<int, string> colByPeriod,
            Dictionary<(short GlGroup, int Period), decimal> amounts)
        {
            var row = dt.NewRow();
            row["Section"] = section;
            row["LineItem"] = lineItem;
            row["RowKind"] = "Detail";
            row["LineGroupCode"] = glGroup;
            row["SectionSort"] = sectionSort;
            row["LineSort"] = lineSort;

            foreach (var kvp in colByPeriod)
            {
                var period = kvp.Key;
                var col = kvp.Value;
                row[col] = amounts.TryGetValue((glGroup, period), out var a) ? a : 0m;
            }

            dt.Rows.Add(row);
        }

        private static void AddSectionTotals(DataTable dt, string[] periodColumnNames)
        {
            var sections = dt.Rows.Cast<DataRow>()
                .Where(r => string.Equals(Convert.ToString(r["RowKind"]), "Detail", StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => new
                {
                    Section = Convert.ToString(r["Section"]) ?? "",
                    Sort = r["SectionSort"] is int i ? i : 0
                })
                .OrderBy(g => g.Key.Sort)
                .ThenBy(g => g.Key.Section, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in sections)
            {
                var tr = dt.NewRow();
                tr["Section"] = g.Key.Section;
                tr["LineItem"] = "Total";
                tr["RowKind"] = "SectionTotal";
                tr["LineGroupCode"] = DBNull.Value;
                tr["SectionSort"] = g.Key.Sort;
                tr["LineSort"] = int.MaxValue - 10;

                foreach (var col in periodColumnNames)
                {
                    var sum = 0m;
                    foreach (var r in g)
                        sum += r[col] is decimal d ? d : 0m;
                    tr[col] = sum;
                }

                dt.Rows.Add(tr);
            }
        }

        private static void AddGrandTotals(DataTable dt, string[] periodColumnNames)
        {
            static bool IsExpenseSection(string s) => s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) >= 0;
            static bool IsIncomeSection(string s)
                => s.IndexOf("revenue", StringComparison.OrdinalIgnoreCase) >= 0
                   || (s.IndexOf("income", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) < 0);

            var detailRows = dt.Rows.Cast<DataRow>()
                .Where(r => string.Equals(Convert.ToString(r["RowKind"]), "Detail", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var incomeRows = detailRows.Where(r => IsIncomeSection(Convert.ToString(r["Section"]) ?? "")).ToList();
            var expenseRows = detailRows.Where(r => IsExpenseSection(Convert.ToString(r["Section"]) ?? "")).ToList();

            AddGrand("Total Revenue", incomeRows);
            AddGrand("Total Expenses", expenseRows);
            AddGrand("Net Income", detailRows);

            void AddGrand(string lineItem, List<DataRow> src)
            {
                var r = dt.NewRow();
                r["Section"] = "Summary";
                r["LineItem"] = lineItem;
                r["RowKind"] = "GrandTotal";
                r["LineGroupCode"] = DBNull.Value;
                // Keep Summary at the top for exec-first scanning.
                r["SectionSort"] = -1;
                r["LineSort"] = lineItem switch
                {
                    "Total Revenue" => int.MaxValue - 100,
                    "Total Expenses" => int.MaxValue - 99,
                    _ => int.MaxValue - 98
                };

                foreach (var col in periodColumnNames)
                {
                    var sum = 0m;
                    foreach (var rr in src)
                        sum += rr[col] is decimal d ? d : 0m;
                    r[col] = sum;
                }

                dt.Rows.Add(r);
            }
        }

        private static void ComputeExecutiveColumns(DataTable dt, int[] periods, string[] periodColumnNames)
        {
            if (periods.Length == 0)
                return;

            var curIdx = periods.Length - 1;
            var priorIdx = periods.Length >= 2 ? periods.Length - 2 : -1;

            var curCol = periodColumnNames[curIdx];
            var priorCol = priorIdx >= 0 ? periodColumnNames[priorIdx] : null;

            // Revenue denominator uses grand total "Total Revenue" current period.
            var revenueCurrent = 0m;
            foreach (DataRow r in dt.Rows)
            {
                if (!string.Equals(Convert.ToString(r["RowKind"]), "GrandTotal", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(Convert.ToString(r["LineItem"]), "Total Revenue", StringComparison.OrdinalIgnoreCase))
                    continue;
                revenueCurrent = r[curCol] is decimal d ? d : 0m;
                break;
            }

            var fyStartMonth = ReadInt("Financials.PnL.FiscalYearStartMonth", 4);
            fyStartMonth = Math.Clamp(fyStartMonth, 1, 12);

            var curFy = FiscalYear(periods[curIdx], fyStartMonth);
            var ytdIdx = periods
                .Select((p, idx) => new { p, idx })
                .Where(x => FiscalYear(x.p, fyStartMonth) == curFy && x.idx <= curIdx)
                .Select(x => x.idx)
                .ToList();

            var ttmStart = Math.Max(0, periods.Length - 12);
            var ttmIdx = Enumerable.Range(ttmStart, periods.Length - ttmStart).ToList();

            foreach (DataRow r in dt.Rows)
            {
                var cur = r[curCol] is decimal cd ? cd : 0m;
                var prior = (priorCol != null && r[priorCol] is decimal pd) ? pd : 0m;
                var mom = cur - prior;

                r["Current"] = cur;
                r["Prior"] = prior;
                r["MoM"] = mom;

                if (prior != 0m)
                    r["MoMPct"] = mom / Math.Abs(prior);

                r["YTD"] = Sum(r, ytdIdx);
                r["TTM"] = Sum(r, ttmIdx);

                if (revenueCurrent != 0m)
                    r["PctOfRevenue"] = cur / Math.Abs(revenueCurrent);

                r["IsAllZero"] = IsAllZero(r);
            }

            decimal Sum(DataRow row, List<int> idxs)
            {
                var s = 0m;
                foreach (var i in idxs)
                {
                    var col = periodColumnNames[i];
                    s += row[col] is decimal d ? d : 0m;
                }
                return s;
            }

            bool IsAllZero(DataRow row)
            {
                foreach (var col in periodColumnNames)
                {
                    if (row[col] is decimal d && d != 0m)
                        return false;
                }
                return true;
            }
        }

        private static decimal[] GetTrend(DataTable dt, string lineItem, string[] periodColumnNames)
        {
            var row = dt.Rows.Cast<DataRow>()
                .FirstOrDefault(r =>
                    string.Equals(Convert.ToString(r["RowKind"]), "GrandTotal", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Convert.ToString(r["LineItem"]), lineItem, StringComparison.OrdinalIgnoreCase));

            if (row == null)
                return Array.Empty<decimal>();

            var values = periodColumnNames.Select(c => row[c] is decimal d ? d : 0m).ToArray();
            if (values.Length <= 12)
                return values;
            return values.Skip(values.Length - 12).ToArray();
        }

        private static string[] GetTrendLabels(List<int> periods)
        {
            if (periods == null || periods.Count == 0)
                return Array.Empty<string>();

            var slice = (periods.Count <= 12) ? periods : periods.Skip(periods.Count - 12).ToList();
            return slice.Select(PeriodChartLabel).ToArray();
        }

        private static int FiscalYear(int yyyymm, int fyStartMonth)
        {
            var year = yyyymm / 100;
            var month = yyyymm % 100;
            return month >= fyStartMonth ? year : year - 1;
        }

        private static int ReadInt(string key, int @default)
        {
            var raw = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(raw))
                return @default;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out v))
                return v;
            return @default;
        }

        private static List<int> BuildMonthPeriods(DateTime fromDate, DateTime toDate)
        {
            var a = new DateTime(fromDate.Year, fromDate.Month, 1);
            var b = new DateTime(toDate.Year, toDate.Month, 1);
            if (b < a) (a, b) = (b, a);

            var list = new List<int>();
            var cur = a;
            while (cur <= b)
            {
                list.Add(ToPeriod(cur));
                cur = cur.AddMonths(1);
            }

            return list;
        }

        private static bool HasAnyGlSummaryInRange(OdbcConnection cn, int minPeriod, int maxPeriod, string? orgFilter, CancellationToken cancelToken)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;

            var whereOrg = "";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                whereOrg = " AND Org = ? ";

            cmd.CommandText = $@"
SELECT TOP 1 Period
FROM [{Catalog}].dbo.GLSummary
WHERE Period >= ? AND Period <= ?
{whereOrg};";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();
                return !r.IsDBNull(0);
            }

            return false;
        }

        private static string PeriodColumnHeader(int period)
        {
            // Prefer deterministic labels. CFGPostControl dates appear inconsistent in this environment.
            var y = period / 100;
            var m = period % 100;
            if (m < 1 || m > 12) return period.ToString(CultureInfo.InvariantCulture);
            var d = new DateTime(y, m, 1);
            return $"{d.ToString("MMM-yy", CultureInfo.InvariantCulture)} ({period})";
        }

        private static string PeriodChartLabel(int period)
        {
            var y = period / 100;
            var m = period % 100;
            if (m < 1 || m > 12) return period.ToString(CultureInfo.InvariantCulture);
            var d = new DateTime(y, m, 1);
            // Two-line label reads cleanly under narrow bars.
            return $"{d.ToString("MMM", CultureInfo.InvariantCulture)}\n{d.ToString("yy", CultureInfo.InvariantCulture)}";
        }

        private sealed class SectionDef
        {
            public short Code { get; init; }
            public string Description { get; init; } = "";
            public short SortOrder { get; init; }
        }

        private sealed class LineDef
        {
            public short Code { get; init; }
            public string Description { get; init; } = "";
            public short SortOrder { get; init; }
            public short? ParentSectionId { get; init; }
        }

        private static List<SectionDef> LoadSections(OdbcConnection cn, short tableNo, CancellationToken cancelToken)
        {
            // Sections come from GLParentHeading/GLParentGroup.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
SELECT h.GLGroup, pg.Description, h.SortOrder
FROM [{Catalog}].dbo.GLParentHeading h
JOIN [{Catalog}].dbo.GLParentGroup pg
  ON pg.Code = h.GLGroup
WHERE h.TableNo = ?
ORDER BY h.SortOrder;";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

            using var r = cmd.ExecuteReader();
            var list = new List<SectionDef>();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();
                list.Add(new SectionDef
                {
                    Code = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                    Description = r.IsDBNull(1) ? "" : r.GetString(1),
                    SortOrder = r.IsDBNull(2) ? (short)0 : r.GetInt16(2),
                });
            }

            return list;
        }

        private static List<LineDef> LoadLineGroups(OdbcConnection cn, short tableNo, CancellationToken cancelToken)
        {
            // Line items come from GLParentDetail (child group IDs) and GLGroup/GLGroupHeading.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
SELECT d.DetailGroupID AS ChildGroupId,
       g.Description,
       COALESCE(gh.SortOrder, 0) AS SortOrder,
       d.GLGroup AS ParentSectionId
FROM [{Catalog}].dbo.GLParentDetail d
JOIN [{Catalog}].dbo.GLGroup g
  ON g.Code = d.DetailGroupID
LEFT JOIN [{Catalog}].dbo.GLGroupHeading gh
  ON gh.TableNo = d.TableNo AND gh.GLGroup = d.DetailGroupID
WHERE d.TableNo = ?
ORDER BY ParentSectionId, SortOrder, g.Description;";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

            var list = new List<LineDef>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    cancelToken.ThrowIfCancellationRequested();
                    list.Add(new LineDef
                    {
                        Code = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                        Description = r.IsDBNull(1) ? "" : r.GetString(1),
                        SortOrder = r.IsDBNull(2) ? (short)0 : Convert.ToInt16(r.GetValue(2), CultureInfo.InvariantCulture),
                        ParentSectionId = r.IsDBNull(3) ? null : (short?)r.GetInt16(3),
                    });
                }
            }

            // If no parent detail exists for this table, fall back to all groups in GLGroupHeading for the table.
            if (list.Count == 0)
            {
                cmd.Parameters.Clear();
                cmd.CommandText = $@"
SELECT gh.GLGroup, g.Description, gh.SortOrder
FROM [{Catalog}].dbo.GLGroupHeading gh
JOIN [{Catalog}].dbo.GLGroup g
  ON g.Code = gh.GLGroup
WHERE gh.TableNo = ?
ORDER BY gh.SortOrder, g.Description;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

                using (var r2 = cmd.ExecuteReader())
                {
                    while (r2.Read())
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        list.Add(new LineDef
                        {
                            Code = r2.IsDBNull(0) ? (short)0 : r2.GetInt16(0),
                            Description = r2.IsDBNull(1) ? "" : r2.GetString(1),
                            SortOrder = r2.IsDBNull(2) ? (short)0 : r2.GetInt16(2),
                            ParentSectionId = null
                        });
                    }
                }
            }

            return list;
        }

        private static Dictionary<(short GlGroup, int Period), decimal> LoadAmountsByGroupAndPeriod(
            OdbcConnection cn,
            short tableNo,
            int minPeriod,
            int maxPeriod,
            string? orgFilter,
            bool flipSign,
            CancellationToken cancelToken)
        {
            // Join GLGroupDetail ranges to GLSummary and roll up per group + period.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 180;

            var whereOrg = "";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                whereOrg = " AND s.Org = ? ";

            // Normalize accounts for BETWEEN comparisons (lexical safety).
            cmd.CommandText = $@"
SELECT gd.GLGroup,
       s.Period,
       SUM(s.Amount) AS Amount
FROM [{Catalog}].dbo.GLSummary s
JOIN [{Catalog}].dbo.GLGroupDetail gd
  ON gd.TableNo = ?
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) >= RIGHT(REPLICATE('0', 13) + gd.StartAccount, 13)
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) <= RIGHT(REPLICATE('0', 13) + gd.EndAccount, 13)
WHERE s.Period >= ? AND s.Period <= ?
{whereOrg}
GROUP BY gd.GLGroup, s.Period;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });

            using var r = cmd.ExecuteReader();
            var dict = new Dictionary<(short, int), decimal>();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();

                if (r.IsDBNull(0) || r.IsDBNull(1) || r.IsDBNull(2))
                    continue;

                var group = r.GetInt16(0);
                var period = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture);
                var amt = Convert.ToDecimal(r.GetValue(2), CultureInfo.InvariantCulture);
                if (flipSign)
                    amt = -amt;

                dict[(group, period)] = amt;
            }

            return dict;
        }

        private static OdbcConnection CreateConnection()
        {
            var dsn = ConfigurationManager.AppSettings["Vp.Dsn"] ?? "Deltek";
            var user = ConfigurationManager.AppSettings["Vp.User"] ?? string.Empty;
            var pwd = ConfigurationManager.AppSettings["Vp.Password"] ?? string.Empty;
            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            return factory.Create();
        }

        public async Task<IReadOnlyList<LedgerTransactionDrilldownRow>> LoadLineItemTransactionsAsync(
            short tableNo,
            short glGroup,
            int period,
            string? orgFilter,
            bool flipSign,
            CancellationToken cancelToken)
        {
            return await Task.Run(() =>
            {
                cancelToken.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;

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
    SUM(l.Amount) AS Amount,
    COUNT(*) AS EntryCount
FROM
(
    SELECT 'AR' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{Catalog}].dbo.LedgerAR l
    WHERE l.Period = ? {whereOrg}
    UNION ALL
    SELECT 'AP' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{Catalog}].dbo.LedgerAP l
    WHERE l.Period = ? {whereOrg}
    UNION ALL
    SELECT 'EX' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{Catalog}].dbo.LedgerEX l
    WHERE l.Period = ? {whereOrg}
    UNION ALL
    SELECT 'Misc' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{Catalog}].dbo.LedgerMisc l
    WHERE l.Period = ? {whereOrg}
) l
LEFT JOIN
(
    SELECT Invoice, WBS1, MAX(ClientID) AS ClientID
    FROM [{Catalog}].dbo.AR
    GROUP BY Invoice, WBS1
) arx
  ON arx.Invoice = l.Invoice
 AND arx.WBS1 = l.WBS1
LEFT JOIN [{Catalog}].dbo.Clendor cc
  ON cc.ClientID = arx.ClientID
LEFT JOIN [{Catalog}].dbo.Clendor cv
  ON cv.Vendor = l.Vendor
LEFT JOIN [{Catalog}].dbo.EMMain em
  ON em.Employee = l.Employee
WHERE EXISTS
(
    SELECT 1
    FROM [{Catalog}].dbo.GLGroupDetail gd
    WHERE gd.TableNo = ?
      AND gd.GLGroup = ?
      AND RIGHT(REPLICATE('0', 13) + l.Account, 13) >= RIGHT(REPLICATE('0', 13) + gd.StartAccount, 13)
      AND RIGHT(REPLICATE('0', 13) + l.Account, 13) <= RIGHT(REPLICATE('0', 13) + gd.EndAccount, 13)
)
GROUP BY
    l.Source,
    l.Period,
    COALESCE(NULLIF(l.Invoice, ''), NULLIF(l.Voucher, ''), NULLIF(l.RefNo, ''), '(none)'),
    COALESCE(
        NULLIF(cc.Name, ''),
        NULLIF(cv.Name, ''),
        NULLIF(LTRIM(RTRIM(COALESCE(em.FirstName, '') + ' ' + COALESCE(em.LastName, ''))), ''),
        NULLIF(l.Vendor, ''),
        NULLIF(l.Employee, ''),
        '(unmapped)'),
    l.Account,
    l.TransType
ORDER BY ABS(SUM(l.Amount)) DESC, MAX(l.TransDate) DESC;";

                for (var i = 0; i < 4; i++)
                {
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = period });
                    if (!string.IsNullOrWhiteSpace(orgFilter))
                        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter!.Trim() });
                }
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = glGroup });

                var rows = new List<LedgerTransactionDrilldownRow>(256);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    cancelToken.ThrowIfCancellationRequested();

                    var amount = r.IsDBNull(8) ? 0m : Convert.ToDecimal(r.GetValue(8), CultureInfo.InvariantCulture);
                    if (flipSign)
                        amount = -amount;

                    rows.Add(new LedgerTransactionDrilldownRow(
                        Source: r.IsDBNull(0) ? "" : Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                        Period: r.IsDBNull(1) ? period : Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        TransDate: r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2), CultureInfo.InvariantCulture),
                        DocumentNo: r.IsDBNull(3) ? "" : Convert.ToString(r.GetValue(3), CultureInfo.InvariantCulture) ?? "",
                        Counterparty: r.IsDBNull(4) ? "" : Convert.ToString(r.GetValue(4), CultureInfo.InvariantCulture) ?? "",
                        Account: r.IsDBNull(5) ? "" : Convert.ToString(r.GetValue(5), CultureInfo.InvariantCulture) ?? "",
                        TransType: r.IsDBNull(6) ? "" : Convert.ToString(r.GetValue(6), CultureInfo.InvariantCulture) ?? "",
                        Description: r.IsDBNull(7) ? "" : Convert.ToString(r.GetValue(7), CultureInfo.InvariantCulture) ?? "",
                        Amount: amount,
                        EntryCount: r.IsDBNull(9) ? 0 : Convert.ToInt32(r.GetValue(9), CultureInfo.InvariantCulture)));
                }

                return (IReadOnlyList<LedgerTransactionDrilldownRow>)rows;
            }, cancelToken).ConfigureAwait(false);
        }

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListIntParameters(OdbcCommand cmd, List<int> vals)
        {
            foreach (var v in vals)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = v });
        }
    }

    internal sealed class GlTableInfo
    {
        public short TableNo { get; init; }
        public string TableName { get; init; } = "";
        public string FilterOrg { get; init; } = "";
        public string FilterCode { get; init; } = "";

        public string Display => $"{TableNo} - {TableName}";
    }
}
