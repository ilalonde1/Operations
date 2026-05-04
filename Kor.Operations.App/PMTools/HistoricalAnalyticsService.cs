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
using static Kor.Operations.Data.DataReaderHelpers;
using Kor.Operations.Shared;

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

        public async Task<(List<HistoricalProjectRow> Rows, FirmUtilizationStats Utilization, List<EmployeeProjectHours> EmployeeHours, List<EmployeeWeeklyHours> WeeklyUtilization, List<EmployeeRate> EmployeeRates)> LoadAsync(CancellationToken ct = default)
        {
            var rowsTask = Task.Run(() => LoadSync(ct), ct);
            var timelineTask = Task.Run(() => LoadRevenueTimelineSync(ct), ct);
            var utilizationTask = Task.Run(() => LoadFirmUtilizationSync(ct), ct);
            var employeeTask = Task.Run(() => LoadEmployeeProjectSync(ct), ct);
            var weeklyTask = Task.Run(() => LoadEmployeeWeeklyUtilizationSync(ct), ct);
            var ratesTask = Task.Run(() => LoadEmployeeRatesSync(ct), ct);
            await Task.WhenAll(rowsTask, timelineTask, utilizationTask, employeeTask, weeklyTask, ratesTask).ConfigureAwait(false);

            var rows = rowsTask.Result;
            var timeline = timelineTask.Result;

            foreach (var row in rows)
            {
                if (timeline.TryGetValue(row.Wbs1, out var periods))
                    row.RevenueTimeline = periods;
            }
            return (rows, utilizationTask.Result, employeeTask.Result, weeklyTask.Result, ratesTask.Result);
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
            // 11  EngHrs (incl Chk)  12  DraftHrs     13  InspHrs
            // 14  DocPrepHrs      15  GenHrs          16  AdminHrs        17  NonBillHrs
            // 18  TotalAllHrs     19  BillableHrs     20  SubCost
            // 21  ArTotal         22  ArCurrent       23  Ar31To60
            // 24  Ar61To90        25  Ar90Plus
            // 26  CustConstructionType  27  CustProjectCategory  28  CustDraftingType
            // 29  DmFirstName  30  DmLastName  31  CustDraftingManager(id)
            // 32  TotalInspections  33  LastMonthInspections  34  ClientID  35  HourlyRevenue
            var inspMonthEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var inspMonthStart = inspMonthEnd.AddMonths(-1);
            var inspMonthStartStr = inspMonthStart.ToString("yyyy-MM-dd");
            var inspMonthEndStr = inspMonthEnd.ToString("yyyy-MM-dd");
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
    pctf.CustDraftingManager,
    ISNULL(inspCnt.TotalInspections, 0) AS TotalInspections,
    ISNULL(inspCnt.LastMonthInspections, 0) AS LastMonthInspections,
    pr.ClientID,
    ISNULL(hourly.HourlyRevenue, 0) AS HourlyRevenue
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN [{catalog}].dbo.EMMain em
    ON em.Employee = pr.ProjMgr
LEFT JOIN [{catalog}].dbo.EMMain em2
    ON em2.Employee = pctf.CustDraftingManager
LEFT JOIN (
    SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1
) billed ON billed.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(CASE WHEN LaborCode IN (10, 30) THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
        SUM(CASE WHEN LaborCode = 20 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
        SUM(CASE WHEN LaborCode = 40 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS InspHrs,
        SUM(CASE WHEN LaborCode = 50 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DocPrepHrs,
        SUM(CASE WHEN LaborCode = 60 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS GenHrs,
        SUM(CASE WHEN LaborCode = 70 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS AdminHrs,
        SUM(CASE WHEN LaborCode = 80 THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS NonBillHrs,
        SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0))                                           AS TotalAllHrs,
        SUM(CASE WHEN LaborCode NOT IN (70, 80)
              AND WBS1 NOT LIKE '[A-Z]%'
              AND WBS1 NOT LIKE '9[A-Z]%'
              AND WBS1 NOT LIKE '99%'
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs
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
                  OR (DueDate IS NULL AND InvoiceDate IS NULL)
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar90Plus
    FROM [{catalog}].dbo.AR
    WHERE COALESCE(InvBalanceSourceCurrency, 0) <> 0
    GROUP BY WBS1
) ar ON ar.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        COUNT(*) AS TotalInspections,
        SUM(CASE WHEN TransDate >= '{inspMonthStartStr}' AND TransDate < '{inspMonthEndStr}' THEN 1 ELSE 0 END) AS LastMonthInspections
    FROM [{catalog}].dbo.tkDetail
    WHERE LaborCode = 40
    GROUP BY WBS1
) inspCnt ON inspCnt.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
    FROM [{catalog}].dbo.PRSummaryMain sm
    INNER JOIN [{catalog}].dbo.PR prInner
        ON prInner.WBS1 = sm.WBS1 AND prInner.WBS2 = sm.WBS2 AND prInner.WBS3 = sm.WBS3
    WHERE prInner.Fee = 0
      AND prInner.WBS2 IS NOT NULL AND LTRIM(RTRIM(prInner.WBS2)) <> ''
      AND prInner.WBS3 IS NOT NULL AND LTRIM(RTRIM(prInner.WBS3)) <> ''
    GROUP BY sm.WBS1
    HAVING SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) > 0
) hourly ON hourly.WBS1 = pr.WBS1
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
                var hourlyRev = GetDouble(r, 35);
                var totalFee = fee + hourlyRev;

                // Mirror FinancialsService.CalcBudget — single configurable target rate, uses TotalFee
                var target = _opts.TargetBillingRate > 0 ? _opts.TargetBillingRate : 185.0;
                var estEng   = (totalFee > 0 && u1 > 0) ? (totalFee / target) * (u3 / u1) : 0.0;
                var estDraft = (totalFee > 0 && u2 > 0) ? (totalFee / target) * (u3 / u2) : 0.0;

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
                    HourlyRevenue = hourlyRev,
                    FeeBilled  = GetDouble(r, 10),
                    EngHrs     = GetDouble(r, 11),
                    DraftHrs   = GetDouble(r, 12),
                    InspHrs    = GetDouble(r, 13),
                    DocPrepHrs = GetDouble(r, 14),
                    GenHrs     = GetDouble(r, 15),
                    AdminHrs   = GetDouble(r, 16),
                    NonBillHrs = GetDouble(r, 17),
                    TotalAllHrs  = GetDouble(r, 18),
                    BillableHrs  = GetDouble(r, 19),
                    SubCost      = GetDouble(r, 20),
                    ArTotal      = GetDouble(r, 21),
                    ArCurrent    = GetDouble(r, 22),
                    Ar31To60     = GetDouble(r, 23),
                    Ar61To90     = GetDouble(r, 24),
                    Ar90Plus     = GetDouble(r, 25),
                    ConstructionType = GetTrimmed(r, 26),
                    ProjectCategory  = GetTrimmed(r, 27),
                    DraftingType     = GetTrimmed(r, 28),
                    DraftingManager  = BuildPmDisplay(GetTrimmed(r, 31), GetTrimmed(r, 29), GetTrimmed(r, 30)),
                    TotalInspections = (int)GetDouble(r, 32),
                    LastMonthInspections = (int)GetDouble(r, 33),
                    ClientId       = GetTrimmed(r, 34),
                    EstEngBudget   = estEng,
                    EstDraftBudget = estDraft,
                });
            }

            // Second pass: peer-based budget estimation replaces formula
            // Build peer pool from closed projects with meaningful hours
            var peerPool = new List<PeerBudgetEstimator.PeerProject>();
            foreach (var row in rows)
            {
                var st = (row.Status ?? "").Trim();
                if (st.Equals("A", StringComparison.OrdinalIgnoreCase)
                    || st.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (row.TotalEngDraft < 50 || row.TotalFee <= 0) continue;
                peerPool.Add(new PeerBudgetEstimator.PeerProject
                {
                    Wbs1 = row.Wbs1, Fee = row.TotalFee,
                    Phase = (row.Phase ?? "").Trim(),
                    ConstructionType = (row.ConstructionType ?? "").Trim(),
                    ProjectCategory = (row.ProjectCategory ?? "").Trim(),
                    EngHrs = row.EngHrs, DraftHrs = row.DraftHrs,
                });
            }
            foreach (var row in rows)
            {
                var (peerEng, peerDraft, pc) = PeerBudgetEstimator.Estimate(row.TotalFee, row.Phase, row.ConstructionType, row.ProjectCategory, peerPool, row.Wbs1);
                if (pc >= 3)
                {
                    row.EstEngBudget = peerEng;
                    row.EstDraftBudget = peerDraft;
                    row.BudgetPeerCount = pc;
                }
                // else keep formula-based values
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
SELECT WBS1, Period,
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS Revenue,
       SUM(COALESCE(Billed, 0)) AS Billed
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
    SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN LaborCode NOT IN (70, 80)
              AND WBS1 NOT LIKE '[A-Z]%'
              AND WBS1 NOT LIKE '9[A-Z]%'
              AND WBS1 NOT LIKE '99%'
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs
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

        private List<EmployeeProjectHours> LoadEmployeeProjectSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            var result = new List<EmployeeProjectHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    t.WBS1,
    SUM(CASE WHEN t.LaborCode IN (10, 30) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = 20 THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80)
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs,
    MIN(ec.HireDate) AS HireDate
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = empId;
                result.Add(new EmployeeProjectHours
                {
                    EmployeeId = empId,
                    EmployeeName = name,
                    Wbs1 = GetTrimmed(r, 3),
                    EngHrs = GetDouble(r, 4),
                    DraftHrs = GetDouble(r, 5),
                    BillableHrs = GetDouble(r, 6),
                    TotalHrs = GetDouble(r, 7),
                    HireDate = r.IsDBNull(8) ? null : Convert.ToDateTime(r.GetValue(8)),
                });
            }
            return result;
        }

        private List<EmployeeWeeklyHours> LoadEmployeeWeeklyUtilizationSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            var result = new List<EmployeeWeeklyHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    DATEADD(day, -DATEDIFF(day, '18991231', CAST(t.TransDate AS date)) % 7, CAST(t.TransDate AS date)) AS WeekStart,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80)
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= DATEADD(week, -12, CAST(GETDATE() AS date))
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
GROUP BY t.Employee, e.FirstName, e.LastName,
         DATEADD(day, -DATEDIFF(day, '18991231', CAST(t.TransDate AS date)) % 7, CAST(t.TransDate AS date))
ORDER BY e.LastName, e.FirstName, WeekStart;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = empId;
                result.Add(new EmployeeWeeklyHours
                {
                    EmployeeId = empId,
                    EmployeeName = name,
                    WeekStart = GetDate(r, 3) ?? DateTime.MinValue,
                    BillableHrs = GetDouble(r, 4),
                    TotalHrs = GetDouble(r, 5),
                });
            }
            return result;
        }

        /// <summary>
        /// Loads per-employee billing and cost rates from Deltek EMCompany
        /// (ProvBillRate / ProvCostRate). Partners (EmployeeId starting with 'P')
        /// have no native cost rate because they're compensated via distributions;
        /// their EffectiveCostRate is set to the configured imputed Partner rate
        /// (DeltekOdbcOptions.PartnerImputedCostRate, default $250/hr).
        /// </summary>
        private List<EmployeeRate> LoadEmployeeRatesSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            var result = new List<EmployeeRate>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    e.Employee,
    e.FirstName,
    e.LastName,
    COALESCE(ec.ProvBillRate, 0) AS BillingRate,
    COALESCE(ec.ProvCostRate, 0) AS CostRate
FROM [{catalog}].dbo.EMMain e
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = e.Employee
WHERE UPPER(COALESCE(ec.Status, 'A')) = 'A'
ORDER BY e.LastName, e.FirstName";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = empId;

                var billing = GetDouble(r, 3);
                var rawCost = GetDouble(r, 4);
                var isPartner = empId.StartsWith("P", StringComparison.OrdinalIgnoreCase);
                var effectiveCost = isPartner ? _opts.PartnerImputedCostRate : rawCost;

                result.Add(new EmployeeRate
                {
                    EmployeeId = empId,
                    EmployeeName = name,
                    BillingRate = billing,
                    CostRate = rawCost,
                    IsPartner = isPartner,
                    EffectiveCostRate = effectiveCost,
                });
            }

            return result;
        }

        internal List<QuarterlyEmployeeHours> LoadQuarterlyEmployeeHoursSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = string.IsNullOrWhiteSpace(_opts.Catalog) ? "C0000052267P_1_KOR00000000" : _opts.Catalog;
            var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
                              () => new Dictionary<string, string>());

            var result = new List<QuarterlyEmployeeHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    t.WBS1,
    DATEPART(YEAR, t.TransDate)    AS Yr,
    DATEPART(QUARTER, t.TransDate) AS Qtr,
    SUM(CASE WHEN t.LaborCode IN (10, 30) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = 20 THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80)
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate >= '2020-01-01'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1,
         DATEPART(YEAR, t.TransDate), DATEPART(QUARTER, t.TransDate);";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = empId;
                result.Add(new QuarterlyEmployeeHours
                {
                    EmployeeId = empId,
                    EmployeeName = name,
                    Wbs1 = GetTrimmed(r, 3),
                    Year = (int)GetDouble(r, 4),
                    Quarter = (int)GetDouble(r, 5),
                    EngHrs = GetDouble(r, 6),
                    DraftHrs = GetDouble(r, 7),
                    BillableHrs = GetDouble(r, 8),
                    TotalHrs = GetDouble(r, 9),
                });
            }
            return result;
        }

        private static string BuildPmDisplay(string pmId, string first, string last)
        {
            var full = $"{first} {last}".Trim();
            return !string.IsNullOrWhiteSpace(full) ? full : pmId;
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
