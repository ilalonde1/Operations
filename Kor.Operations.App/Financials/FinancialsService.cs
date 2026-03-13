#nullable enable
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
    public sealed class FinancialsService
    {
        // Excel contract constants
        private const string Catalog = "C0000052267P_1_KOR00000000";

        // Excel "U" constants (see attached dashboard)
        private const double U1 = 550; // Eng rate constant
        private const double U2 = 550; // Draft rate constant
        private static readonly double U3 = 1.0 / (Math.Pow(U1, -1.0) + Math.Pow(U2, -1.0));

        private static readonly object CacheLock = new();
        private static FinancialsSnapshot? _cache;

        public Task<FinancialsSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken ct)
        {
            lock (CacheLock)
            {
                if (!forceRefresh && _cache != null)
                    return Task.FromResult(_cache);
            }

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var snap = LoadSnapshot(ct);
                lock (CacheLock)
                    _cache = snap;
                return snap;
            }, ct);
        }

        private static FinancialsSnapshot LoadSnapshot(CancellationToken ct)
        {
            var refreshedAt = DateTimeOffset.Now;

            var projects = new List<ProjectBaseRow>();
            var feeBilledByWbs1 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var hrsByWbs1AndLabor = new Dictionary<(string Wbs1, int LaborCode), double>();

            // Use the same Deltek/Vantagepoint connection mechanism as the Transmittals feature.
            var dsn = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.VpDsn] ?? "Deltek";
            var user = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.VpUser] ?? string.Empty;
            var pwd = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.VpPassword] ?? string.Empty;
            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());

            using var cn = factory.Create();
            try
            {
                cn.Open();
            }
            catch (OdbcException ex)
            {
                throw new InvalidOperationException("ODBC connection failed.", ex);
            }

            // 1) Base project list (watchlist + active + top-level only)
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandTimeout = 60;
                cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.Status,
    pr.ProjMgr,
    em.FirstName,
    em.LastName,
    pctf.CustProjectPhase AS Phase,
    pctf.CustActualGFA AS GFA,
    pr.Fee,
    pctf.CustWatchlist
 FROM [{Catalog}].dbo.PR pr
 LEFT JOIN (
     SELECT
         WBS1,
         MAX(CustProjectPhase) AS CustProjectPhase,
         MAX(CustActualGFA) AS CustActualGFA,
         MAX(CustWatchlist) AS CustWatchlist
     FROM [{Catalog}].dbo.ProjectCustomTabFields
     GROUP BY WBS1
 ) pctf
     ON pctf.WBS1 = pr.WBS1
 LEFT JOIN [{Catalog}].dbo.EMMain em
     ON em.Employee = pr.ProjMgr
 WHERE
     (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
     AND UPPER(LTRIM(RTRIM(pr.Status))) IN ('A', 'ACTIVE')
     AND UPPER(LTRIM(RTRIM(pctf.CustWatchlist))) IN ('Y', 'YES', 'TRUE', '1')
 ORDER BY pr.WBS1;";

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();

                    var wbs1 = GetTrimmed(r, 0);
                    if (string.IsNullOrWhiteSpace(wbs1))
                        continue;

                    var name = GetTrimmed(r, 1);
                    var projMgr = GetTrimmed(r, 3);
                    var firstName = GetTrimmed(r, 4);
                    var lastName = GetTrimmed(r, 5);
                    var pm = BuildPmDisplay(projMgr, firstName, lastName);

                    projects.Add(new ProjectBaseRow
                    {
                        Wbs1 = wbs1,
                        Name = name,
                        Pm = pm,
                        Phase = GetTrimmed(r, 6),
                        Gfa = GetDouble(r, 7),
                        Fee = GetDouble(r, 8),
                    });
                }
            }

             var wbs1List = projects.Select(p => p.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
#if DEBUG
            var dupWbs1 = projects
                .GroupBy(p => p.Wbs1, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();
            if (dupWbs1.Count > 0)
                System.Diagnostics.Debug.Fail("Financials base query produced duplicate WBS1 rows: " + string.Join(", ", dupWbs1));
#endif
            if (wbs1List.Count == 0)
            {
                return new FinancialsSnapshot
                {
                    RefreshedAt = refreshedAt,
                    Headline = new FinancialsHeadlineKpis(),
                    Rows = new List<FinancialsProjectRow>()
                };
            }

            // 2) Fee billed (sum revenue across ALL rows for WBS1; no period filter)
            foreach (var chunk in Chunk(wbs1List, 100))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 120;
                cmd.CommandText = $@"
SELECT WBS1, SUM(Revenue) AS FeeBilled
FROM [{Catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var billed = GetDouble(r, 1);
                    if (string.IsNullOrWhiteSpace(wbs1))
                        continue;
                    feeBilledByWbs1[wbs1] = billed;
                }
            }

            // 3) Hours (sum reg+ovt) grouped by (WBS1, LaborCode), then pivot
            foreach (var chunk in Chunk(wbs1List, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT WBS1, LaborCode, SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0)) AS Hrs
FROM [{Catalog}].dbo.tkDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, LaborCode;";
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var laborObj = r.IsDBNull(1) ? null : r.GetValue(1);
                    if (string.IsNullOrWhiteSpace(wbs1) || laborObj == null)
                        continue;

                    if (!TryParseLaborCode(laborObj, out var laborCode))
                        continue;

                    var hrs = GetDouble(r, 2);
                    hrsByWbs1AndLabor[(wbs1, laborCode)] = hrs;
                }
            }

            // 4) Compute row KPIs (preserve Excel semantics)
            var rows = new List<FinancialsProjectRow>(projects.Count);
            foreach (var p in projects)
            {
                ct.ThrowIfCancellationRequested();

                var feeBilled = feeBilledByWbs1.TryGetValue(p.Wbs1, out var fb) ? fb : 0.0;

                var eng = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 10);
                var draft = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 20);
                var chk = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 30);
                var insp = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 40);
                var docPrep = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 50);
                var gen = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 60);
                var admin = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 70);
                var nonBill = GetHrs(hrsByWbs1AndLabor, p.Wbs1, 80);

                var draftBudget = CalcBudget(p.Gfa, p.Fee, U2);
                var engBudget = CalcBudget(p.Gfa, p.Fee, U1);

                var feeHoursDen = eng + draft + chk + insp;
                var billedHoursDen = eng + draft + chk + insp + docPrep + gen + admin + nonBill;

                rows.Add(new FinancialsProjectRow
                {
                    Wbs1 = p.Wbs1,
                    Name = p.Name,
                    Phase = p.Phase,
                    Pm = p.Pm,

                    Gfa = p.Gfa,
                    Fee = p.Fee,
                    FeeBilled = feeBilled,
                    PercentBilled = SafeDiv(feeBilled, p.Fee),

                    EngHrs = eng,
                    DraftHrs = draft,
                    ChkHrs = chk,
                    InspHrs = insp,
                    DocPrepHrs = docPrep,
                    GenHrs = gen,
                    AdminHrs = admin,
                    NonBillHrs = nonBill,

                    DraftBudget = draftBudget,
                    EngBudget = engBudget,
                    DraftPercent = SafeDiv(draft, draftBudget),
                    EngPercent = SafeDiv(eng, engBudget),

                    FeePerHours = feeHoursDen > 0 ? (p.Fee / feeHoursDen) : 0.0,
                    BilledPerHours = SafeDiv(feeBilled, billedHoursDen),
                });
            }

            // 5) Compute headline KPIs (match Excel)
            var headline = ComputeHeadline(rows);

            return new FinancialsSnapshot
            {
                RefreshedAt = refreshedAt,
                Headline = headline,
                Rows = rows
            };
        }

        private static FinancialsHeadlineKpis ComputeHeadline(List<FinancialsProjectRow> rows)
        {
            var totalFees = rows.Sum(r => r.Fee);
            var totalFeeBilled = rows.Sum(r => r.FeeBilled);
            var totalGfa = rows.Sum(r => r.Gfa);

            var hoursSpent = rows.Sum(r => r.EngHrs + r.DraftHrs + r.ChkHrs + r.InspHrs + r.DocPrepHrs + r.GenHrs + r.AdminHrs + r.NonBillHrs);
            var hoursBudgeted = rows.Sum(r => r.DraftBudget + r.EngBudget);

            var totalUnbilled = totalFees - totalFeeBilled;
            var percentFeeUnbilled = SafeDiv(totalUnbilled, totalFees);

            var feeWhereGfa = rows.Where(r => r.Gfa > 0).Sum(r => r.Fee);
            var gfaWhereGfa = rows.Where(r => r.Gfa > 0).Sum(r => r.Gfa);
            var avgFeePerFt2 = gfaWhereGfa > 0 ? (feeWhereGfa / gfaWhereGfa) : 0.0;

            var hoursRemaining = hoursBudgeted - hoursSpent;
            var percentHoursSpent = SafeDiv(hoursSpent, hoursBudgeted);
            var teamDaysRemaining = hoursRemaining / 7.5 / 35.0;

            return new FinancialsHeadlineKpis
            {
                Projects = rows.Count,
                TotalFees = totalFees,
                TotalFeeBilled = totalFeeBilled,
                TotalGfa = totalGfa,
                HoursSpent = hoursSpent,
                HoursBudgeted = hoursBudgeted,
                TotalUnbilled = totalUnbilled,
                PercentFeeUnbilled = percentFeeUnbilled,
                AvgFeePerFt2 = avgFeePerFt2,
                HoursRemaining = hoursRemaining,
                PercentHoursSpent = percentHoursSpent,
                TeamDaysRemaining = teamDaysRemaining
            };
        }

        private static double CalcBudget(double gfa, double fee, double rate)
        {
            // Excel semantics:
            // If GFA > 0: ROUNDUP(GFA / Rate, 0)
            // Else if Fee > 0: Fee / 125 * (U3 / Rate)
            // Else: "" (treated as 0 for our numeric dashboard)
            if (gfa > 0 && rate > 0)
                return Math.Ceiling(gfa / rate);
            if (fee > 0 && rate > 0)
                return (fee / 125.0) * (U3 / rate);
            return 0.0;
        }

        private static double SafeDiv(double num, double den)
            => den == 0.0 ? 0.0 : (num / den);

        private static double GetHrs(Dictionary<(string Wbs1, int LaborCode), double> map, string wbs1, int laborCode)
            => map.TryGetValue((wbs1, laborCode), out var v) ? v : 0.0;

        private static string BuildPmDisplay(string pmId, string first, string last)
        {
            var full = $"{first} {last}".Trim();
            if (!string.IsNullOrWhiteSpace(full))
                return full;
            return pmId;
        }

        private static string GetTrimmed(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return "";
            var v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";
            return v.Trim();
        }

        private static double GetDouble(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return 0.0;
            var v = r.GetValue(i);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is long l) return l;
            if (v is int n) return n;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0.0;
        }

        private static bool TryParseLaborCode(object v, out int code)
        {
            code = 0;
            if (v is int i) { code = i; return true; }
            if (v is short s) { code = s; return true; }
            if (v is long l) { code = (int)l; return true; }
            var str = Convert.ToString(v, CultureInfo.InvariantCulture);
            return int.TryParse(str?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        }

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListParameters(OdbcCommand cmd, List<string> vals)
        {
            foreach (var v in vals)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
        {
            for (var i = 0; i < src.Count; i += size)
                yield return src.GetRange(i, Math.Min(size, src.Count - i));
        }

        private sealed class ProjectBaseRow
        {
            public string Wbs1 { get; set; } = "";
            public string Name { get; set; } = "";
            public string Phase { get; set; } = "";
            public string Pm { get; set; } = "";
            public double Gfa { get; set; }
            public double Fee { get; set; }
        }
    }

    public sealed class FinancialsSnapshot
    {
        public DateTimeOffset RefreshedAt { get; set; }
        public FinancialsHeadlineKpis Headline { get; set; } = new();
        public List<FinancialsProjectRow> Rows { get; set; } = new();
    }

    public sealed class FinancialsHeadlineKpis
    {
        public int Projects { get; set; }
        public double TotalFees { get; set; }
        public double TotalFeeBilled { get; set; }
        public double TotalGfa { get; set; }
        public double HoursSpent { get; set; }
        public double HoursBudgeted { get; set; }
        public double TotalUnbilled { get; set; }
        public double PercentFeeUnbilled { get; set; }
        public double AvgFeePerFt2 { get; set; }
        public double HoursRemaining { get; set; }
        public double PercentHoursSpent { get; set; }
        public double TeamDaysRemaining { get; set; }
    }

    public sealed class FinancialsProjectRow
    {
        public string Wbs1 { get; set; } = "";
        public string Name { get; set; } = "";
        public string Phase { get; set; } = "";
        public string Pm { get; set; } = "";

        public double Gfa { get; set; }
        public double Fee { get; set; }
        public double FeeBilled { get; set; }
        public double PercentBilled { get; set; }

        public double EngHrs { get; set; }
        public double DraftHrs { get; set; }
        public double ChkHrs { get; set; }
        public double InspHrs { get; set; }
        public double DocPrepHrs { get; set; }
        public double GenHrs { get; set; }
        public double AdminHrs { get; set; }
        public double NonBillHrs { get; set; }

        public double DraftBudget { get; set; }
        public double EngBudget { get; set; }
        public double DraftPercent { get; set; }
        public double EngPercent { get; set; }
        public double FeePerHours { get; set; }
        public double BilledPerHours { get; set; }
    }
}

