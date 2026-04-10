#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Loads historical project data from Deltek for the analytics grid.
    /// Independent of <see cref="Kor.Operations.Financials.FinancialsService"/> — owns its own query.
    /// </summary>
    internal sealed class HistoricalAnalyticsService
    {
        private readonly DeltekOdbcOptions _opts;

        public HistoricalAnalyticsService(DeltekOdbcOptions opts)
            => _opts = opts ?? throw new ArgumentNullException(nameof(opts));

        public async Task<(List<HistoricalProjectRow> Rows, FirmUtilizationStats Utilization)> LoadAsync(CancellationToken ct = default)
        {
            var rowsTask = Task.Run(() => LoadSync(ct), ct);
            var timelineTask = Task.Run(() => LoadRevenueTimelineSync(ct), ct);
            var utilizationTask = Task.Run(() => LoadFirmUtilizationSync(ct), ct);
            await Task.WhenAll(rowsTask, timelineTask, utilizationTask).ConfigureAwait(false);

            var rows = rowsTask.Result;
            var timeline = timelineTask.Result;

            foreach (var row in rows)
            {
                if (timeline.TryGetValue(row.Wbs1, out var periods))
                    row.RevenueTimeline = periods;
            }
            return (rows, utilizationTask.Result);
        }

        private List<HistoricalProjectRow> LoadSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            // Harmonic mean for CalcBudget estimation — mirrors FinancialsService.CalcBudget
            var u1 = _opts.EngRate;
            var u2 = _opts.DraftRate;
            var u3 = (u1 > 0 && u2 > 0) ? 1.0 / (1.0 / u1 + 1.0 / u2) : 0.0;

            var rows = new List<HistoricalProjectRow>();
            using var cn = factory.Create();
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Column ordinals:
            //  0  pr.WBS1          1  pr.Name         2  em.FirstName     3  em.LastName
            //  4  pr.ProjMgr       5  CustProjectPhase 6  pr.Status       7  pr.OpenDate
            //  8  pr.CloseDate     9  pr.Fee          10  FeeBilled
            // 11  EngHrs          12  DraftHrs        13  ChkHrs          14  InspHrs
            // 15  DocPrepHrs      16  GenHrs          17  AdminHrs        18  NonBillHrs
            // 19  TotalAllHrs     20  BillableHrs     21  SubCost
            // 22  ArTotal         23  ArCurrent       24  Ar31To60
            // 25  Ar61To90        26  Ar90Plus
            // 27  CustConstructionType  28  CustProjectCategory  29  CustDraftingType
            // 30  DmFirstName  31  DmLastName  32  CustDraftingManager(id)
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    em.FirstName,
    em.LastName,
    pr.ProjMgr,
    pctf.CustProjectPhase,
    pr.Status,
    pr.OpenDate,
    pr.CloseDate,
    pr.Fee,
    ISNULL(billed.FeeBilled, 0)   AS FeeBilled,
    ISNULL(labor.EngHrs, 0)       AS EngHrs,
    ISNULL(labor.DraftHrs, 0)     AS DraftHrs,
    ISNULL(labor.ChkHrs, 0)       AS ChkHrs,
    ISNULL(labor.InspHrs, 0)      AS InspHrs,
    ISNULL(labor.DocPrepHrs, 0)   AS DocPrepHrs,
    ISNULL(labor.GenHrs, 0)       AS GenHrs,
    ISNULL(labor.AdminHrs, 0)     AS AdminHrs,
    ISNULL(labor.NonBillHrs, 0)   AS NonBillHrs,
    ISNULL(labor.TotalAllHrs, 0)  AS TotalAllHrs,
    ISNULL(labor.BillableHrs, 0)  AS BillableHrs,
    ISNULL(sub.SubCost, 0)        AS SubCost,
    ISNULL(ar.ArTotal, 0)         AS ArTotal,
    ISNULL(ar.ArCurrent, 0)       AS ArCurrent,
    ISNULL(ar.Ar31To60, 0)        AS Ar31To60,
    ISNULL(ar.Ar61To90, 0)        AS Ar61To90,
    ISNULL(ar.Ar90Plus, 0)        AS Ar90Plus,
    pctf.CustConstructionType,
    pctf.CustProjectCategory,
    pctf.CustDraftingType,
    em2.FirstName AS DmFirstName,
    em2.LastName AS DmLastName,
    pctf.CustDraftingManager
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN [{catalog}].dbo.EMMain em
    ON em.Employee = pr.ProjMgr
LEFT JOIN [{catalog}].dbo.EMMain em2
    ON em2.Employee = pctf.CustDraftingManager
LEFT JOIN (
    SELECT WBS1, SUM(Revenue) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1
) billed ON billed.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(CASE WHEN LaborCode = 10 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS EngHrs,
        SUM(CASE WHEN LaborCode = 20 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS DraftHrs,
        SUM(CASE WHEN LaborCode = 30 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS ChkHrs,
        SUM(CASE WHEN LaborCode = 40 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS InspHrs,
        SUM(CASE WHEN LaborCode = 50 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS DocPrepHrs,
        SUM(CASE WHEN LaborCode = 60 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS GenHrs,
        SUM(CASE WHEN LaborCode = 70 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS AdminHrs,
        SUM(CASE WHEN LaborCode = 80 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS NonBillHrs,
        SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0))                                           AS TotalAllHrs,
        SUM(CASE WHEN LaborCode NOT IN (70, 80) THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS BillableHrs
    FROM [{catalog}].dbo.tkDetail
    GROUP BY WBS1
) labor ON labor.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1, SUM(COALESCE(Amount, 0)) AS SubCost
    FROM [{catalog}].dbo.apDetail
    GROUP BY WBS1
) sub ON sub.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS ArTotal,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), GETDATE()) <= 30
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS ArCurrent,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), GETDATE()) BETWEEN 31 AND 60
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar31To60,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), GETDATE()) BETWEEN 61 AND 90
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar61To90,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), GETDATE()) > 90
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar90Plus
    FROM [{catalog}].dbo.AR
    WHERE COALESCE(InvBalanceSourceCurrency, 0) <> 0
    GROUP BY WBS1
) ar ON ar.WBS1 = pr.WBS1
WHERE (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND pr.WBS1 NOT LIKE '[A-Z]%'
  AND pr.WBS1 NOT LIKE '9[A-Z]%'
  AND pr.WBS1 NOT LIKE '99%'
ORDER BY pr.Fee DESC;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;

                var fee = GetDouble(r, 9);

                // Mirror FinancialsService.CalcBudget — single configurable target rate
                var target = _opts.TargetBillingRate > 0 ? _opts.TargetBillingRate : 185.0;
                var estEng   = (fee > 0 && u1 > 0) ? (fee / target) * (u3 / u1) : 0.0;
                var estDraft = (fee > 0 && u2 > 0) ? (fee / target) * (u3 / u2) : 0.0;

                var pm = BuildPmDisplay(GetTrimmed(r, 4), GetTrimmed(r, 2), GetTrimmed(r, 3));

                rows.Add(new HistoricalProjectRow
                {
                    Wbs1       = wbs1,
                    Name       = GetTrimmed(r, 1),
                    Pm         = pm,
                    Phase      = GetTrimmed(r, 5),
                    Status     = GetTrimmed(r, 6),
                    OpenDate   = GetDate(r, 7),
                    CloseDate  = GetDate(r, 8),
                    Fee        = fee,
                    FeeBilled  = GetDouble(r, 10),
                    EngHrs     = GetDouble(r, 11),
                    DraftHrs   = GetDouble(r, 12),
                    ChkHrs     = GetDouble(r, 13),
                    InspHrs    = GetDouble(r, 14),
                    DocPrepHrs = GetDouble(r, 15),
                    GenHrs     = GetDouble(r, 16),
                    AdminHrs   = GetDouble(r, 17),
                    NonBillHrs = GetDouble(r, 18),
                    TotalAllHrs  = GetDouble(r, 19),
                    BillableHrs  = GetDouble(r, 20),
                    SubCost      = GetDouble(r, 21),
                    ArTotal      = GetDouble(r, 22),
                    ArCurrent    = GetDouble(r, 23),
                    Ar31To60     = GetDouble(r, 24),
                    Ar61To90     = GetDouble(r, 25),
                    Ar90Plus     = GetDouble(r, 26),
                    ConstructionType = GetTrimmed(r, 27),
                    ProjectCategory  = GetTrimmed(r, 28),
                    DraftingType     = GetTrimmed(r, 29),
                    DraftingManager  = BuildPmDisplay(GetTrimmed(r, 32), GetTrimmed(r, 30), GetTrimmed(r, 31)),
                    EstEngBudget   = estEng,
                    EstDraftBudget = estDraft,
                });
            }
            return rows;
        }

        private Dictionary<string, List<PeriodRevenue>> LoadRevenueTimelineSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            var result = new Dictionary<string, List<PeriodRevenue>>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT WBS1, Period, SUM(COALESCE(Revenue,0)) AS Revenue, SUM(COALESCE(Billed,0)) AS Billed
FROM [{catalog}].dbo.PRSummaryMain
GROUP BY WBS1, Period
ORDER BY WBS1, Period;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;
                var period = GetTrimmed(r, 1);
                var revenue = GetDouble(r, 2);
                var billed = GetDouble(r, 3);
                if (revenue == 0 && billed == 0) continue;

                if (!result.TryGetValue(wbs1, out var list))
                    result[wbs1] = list = new List<PeriodRevenue>();
                list.Add(new PeriodRevenue(period, revenue, billed));
            }
            return result;
        }

        private FirmUtilizationStats LoadFirmUtilizationSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Query ALL tkDetail hours firm-wide — no WBS1 filter.
            // Billable = LaborCode NOT IN (70, 80), matching Staff Utilization definition.
            cmd.CommandText = $@"
SELECT
    YEAR(TransDate) AS Yr,
    SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN LaborCode NOT IN (70, 80)
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0) ELSE 0 END) AS BillableHrs
FROM [{catalog}].dbo.tkDetail
WHERE TransDate IS NOT NULL
GROUP BY YEAR(TransDate)
ORDER BY YEAR(TransDate);";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var byYear = new Dictionary<int, (double Total, double Billable)>();
            var totalAll = 0.0;
            var billableAll = 0.0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var yr = (int)GetDouble(r, 0);
                var total = GetDouble(r, 1);
                var billable = GetDouble(r, 2);
                if (yr > 0)
                    byYear[yr] = (total, billable);
                totalAll += total;
                billableAll += billable;
            }
            return new FirmUtilizationStats
            {
                TotalHrs = totalAll,
                BillableHrs = billableAll,
                BillablePct = totalAll > 0 ? billableAll / totalAll : 0,
                ByYear = byYear,
            };
        }

        private static string BuildPmDisplay(string pmId, string first, string last)
        {
            var full = $"{first} {last}".Trim();
            return !string.IsNullOrWhiteSpace(full) ? full : pmId;
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
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0.0;
        }

        private static DateTime? GetDate(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return null;
            var v = r.GetValue(i);
            if (v is DateTime dt) return dt;
            if (DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
            return null;
        }
    }
}
