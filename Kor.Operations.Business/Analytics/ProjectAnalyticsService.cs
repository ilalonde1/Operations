#nullable enable
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using static Kor.Operations.Data.DataReaderHelpers;

namespace Kor.Operations.PMTools;

public sealed class ProjectAnalyticsService
{
    private const int Engineering = 10;
    private const int Drafting = 20;
    private const int Checking = 30;
    private const int Inspection = 40;
    private const int DocPrep = 50;
    private const int General = 60;
    private const int Admin = 70;
    private const int NonBillable = 80;

    private readonly DeltekOdbcOptions _opts;
    private readonly string _catalog;
    private readonly double _usdToCadRate;
    private readonly HistoricalPeerBudgetEstimator? _peerBudgetEstimator;

    public ProjectAnalyticsService(
        DeltekOdbcOptions opts,
        FinancialsOptions? financialsOpts = null,
        HistoricalPeerBudgetEstimator? peerBudgetEstimator = null)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _catalog = DeltekCatalogValidator.ResolveCatalog(opts.Catalog);
        _usdToCadRate = OrgFx.ParseUsdToCadRate(financialsOpts?.BilledUsdToCadRate);
        _peerBudgetEstimator = peerBudgetEstimator;
    }

    public List<HistoricalProjectRow> LoadProjectRowsSync(CancellationToken ct)
    {
        var u1 = _opts.EngRate;
        var u2 = _opts.DraftRate;
        var u3 = (u1 > 0 && u2 > 0) ? 1.0 / (1.0 / u1 + 1.0 / u2) : 0.0;

        var rows = new List<HistoricalProjectRow>();
        using var cn = CreateConnection();
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        var inspMonthEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var inspMonthStart = inspMonthEnd.AddMonths(-1);
        var inspMonthStartStr = inspMonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var inspMonthEndStr = inspMonthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
    ISNULL(unposted.UnpostedFeeBilled, 0) AS UnpostedFeeBilled,
    pctf.CustConstructionType,
    pctf.CustProjectCategory,
    pctf.CustDraftingType,
    em2.FirstName AS DmFirstName,
    em2.LastName AS DmLastName,
    pctf.CustDraftingManager,
    ISNULL(inspCnt.TotalInspections, 0) AS TotalInspections,
    ISNULL(inspCnt.LastMonthInspections, 0) AS LastMonthInspections,
    pr.ClientID,
    ISNULL(hourly.HourlyRevenue, 0) AS HourlyRevenue,
    pr.Org
FROM [{_catalog}].dbo.PR pr
LEFT JOIN [{_catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN [{_catalog}].dbo.EMMain em
    ON em.Employee = pr.ProjMgr
LEFT JOIN [{_catalog}].dbo.EMMain em2
    ON em2.Employee = pctf.CustDraftingManager
LEFT JOIN (
    SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
    FROM [{_catalog}].dbo.PRSummaryMain
    GROUP BY WBS1
) billed ON billed.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(CASE WHEN LaborCode IN ({Engineering}, {Checking}) THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
        SUM(CASE WHEN LaborCode = {Drafting} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
        SUM(CASE WHEN LaborCode = {Inspection} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS InspHrs,
        SUM(CASE WHEN LaborCode = {DocPrep} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DocPrepHrs,
        SUM(CASE WHEN LaborCode = {General} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS GenHrs,
        SUM(CASE WHEN LaborCode = {Admin} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS AdminHrs,
        SUM(CASE WHEN LaborCode = {NonBillable} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS NonBillHrs,
        SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0))                                           AS TotalAllHrs,
        SUM(CASE WHEN LaborCode NOT IN ({Admin}, {NonBillable})
              AND WBS1 NOT LIKE '[A-Z]%'
              AND WBS1 NOT LIKE '9[A-Z]%'
              AND WBS1 NOT LIKE '99%'
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs
    FROM [{_catalog}].dbo.tkDetail
    WHERE COALESCE(LineItemApprovalStatus,'') <> 'R'
    GROUP BY WBS1
) labor ON labor.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1, SUM(COALESCE(Amount, 0)) AS SubCost
    FROM [{_catalog}].dbo.apDetail
    GROUP BY WBS1
) sub ON sub.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS ArTotal,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), CAST(GETDATE() AS date)) <= 30
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS ArCurrent,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), CAST(GETDATE() AS date)) BETWEEN 31 AND 60
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar31To60,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), CAST(GETDATE() AS date)) BETWEEN 61 AND 90
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar61To90,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), CAST(GETDATE() AS date)) > 90
                 THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Ar90Plus
    FROM [{_catalog}].dbo.AR
    WHERE ABS(COALESCE(InvBalanceSourceCurrency, 0)) > 0.004
    GROUP BY WBS1
) ar ON ar.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1, SUM(UnpostedAmt) AS UnpostedFeeBilled
    FROM (
        SELECT
            arP.WBS1,
            arP.Period,
            arP.ArAmt - COALESCE(prP.PostedAmt, 0) AS UnpostedAmt
        FROM (
            SELECT WBS1, Period, SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS ArAmt
            FROM [{_catalog}].dbo.AR
            WHERE COALESCE(InvBalanceSourceCurrency, 0) > 0
            GROUP BY WBS1, Period
        ) arP
        LEFT JOIN (
            SELECT WBS1, Period,
                   SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS PostedAmt
            FROM [{_catalog}].dbo.PRSummaryMain
            GROUP BY WBS1, Period
        ) prP
            ON prP.WBS1 = arP.WBS1 AND prP.Period = arP.Period
        WHERE arP.ArAmt - COALESCE(prP.PostedAmt, 0) > 0
    ) gap
    GROUP BY WBS1
) unposted ON unposted.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        COUNT(*) AS TotalInspections,
        SUM(CASE WHEN TransDate >= '{inspMonthStartStr}' AND TransDate < '{inspMonthEndStr}' THEN 1 ELSE 0 END) AS LastMonthInspections
    FROM [{_catalog}].dbo.tkDetail
    WHERE LaborCode = {Inspection}
      AND COALESCE(LineItemApprovalStatus,'') <> 'R'
    GROUP BY WBS1
) inspCnt ON inspCnt.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
    FROM [{_catalog}].dbo.PRSummaryMain sm
    INNER JOIN [{_catalog}].dbo.PR prInner
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

            var org = GetTrimmed(r, 37);
            var fx = OrgFx.IsUsaOrg(org) ? _usdToCadRate : 1.0;
            var fee = GetDouble(r, 9) * fx;
            var hourlyRev = GetDouble(r, 36) * fx;
            var totalFee = fee + hourlyRev;

            var target = _opts.TargetBillingRate > 0 ? _opts.TargetBillingRate : 185.0;
            var estEng = (totalFee > 0 && u1 > 0) ? (totalFee / target) * (u3 / u1) : 0.0;
            var estDraft = (totalFee > 0 && u2 > 0) ? (totalFee / target) * (u3 / u2) : 0.0;

            rows.Add(new HistoricalProjectRow
            {
                Wbs1 = wbs1,
                Name = GetTrimmed(r, 1),
                Pm = BuildPmDisplay(GetTrimmed(r, 4), GetTrimmed(r, 2), GetTrimmed(r, 3)),
                Phase = GetTrimmed(r, 5),
                Status = GetTrimmed(r, 6),
                OpenDate = GetDate(r, 7),
                CloseDate = GetDate(r, 8),
                Fee = fee,
                HourlyRevenue = hourlyRev,
                FeeBilled = GetDouble(r, 10) * fx,
                EngHrs = GetDouble(r, 11),
                DraftHrs = GetDouble(r, 12),
                InspHrs = GetDouble(r, 13),
                DocPrepHrs = GetDouble(r, 14),
                GenHrs = GetDouble(r, 15),
                AdminHrs = GetDouble(r, 16),
                NonBillHrs = GetDouble(r, 17),
                TotalAllHrs = GetDouble(r, 18),
                BillableHrs = GetDouble(r, 19),
                SubCost = GetDouble(r, 20) * fx,
                ArTotal = GetDouble(r, 21) * fx,
                ArCurrent = GetDouble(r, 22) * fx,
                Ar31To60 = GetDouble(r, 23) * fx,
                Ar61To90 = GetDouble(r, 24) * fx,
                Ar90Plus = GetDouble(r, 25) * fx,
                UnpostedFeeBilled = GetDouble(r, 26) * fx,
                ConstructionType = GetTrimmed(r, 27),
                ProjectCategory = GetTrimmed(r, 28),
                DraftingType = GetTrimmed(r, 29),
                DraftingManager = BuildPmDisplay(GetTrimmed(r, 32), GetTrimmed(r, 30), GetTrimmed(r, 31)),
                TotalInspections = (int)GetDouble(r, 33),
                LastMonthInspections = (int)GetDouble(r, 34),
                ClientId = GetTrimmed(r, 35),
                EstEngBudget = estEng,
                EstDraftBudget = estDraft,
                Org = org,
            });
        }

        ApplyPeerBudgetEstimates(rows);
        return rows;
    }

    public Dictionary<string, List<PeriodRevenue>> LoadRevenueTimelineSync(CancellationToken ct)
    {
        var result = new Dictionary<string, List<PeriodRevenue>>(StringComparer.OrdinalIgnoreCase);
        using var cn = CreateConnection();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT WBS1, Period,
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS Revenue,
       SUM(COALESCE(Billed, 0)) AS Billed
FROM [{_catalog}].dbo.PRSummaryMain
GROUP BY WBS1, Period
ORDER BY WBS1, Period;";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var wbs1 = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(wbs1)) continue;
            var revenue = GetDouble(r, 2);
            var billed = GetDouble(r, 3);
            if (revenue == 0 && billed == 0) continue;

            if (!result.TryGetValue(wbs1, out var list))
                result[wbs1] = list = new List<PeriodRevenue>();
            list.Add(new PeriodRevenue(GetTrimmed(r, 1), revenue, billed));
        }

        return result;
    }

    public void AttachRevenueTimelines(List<HistoricalProjectRow> rows, Dictionary<string, List<PeriodRevenue>> timeline)
    {
        foreach (var row in rows)
        {
            if (!timeline.TryGetValue(row.Wbs1, out var periods)) continue;
            if (OrgFx.IsUsaOrg(row.Org) && _usdToCadRate != 1.0)
            {
                var rate = _usdToCadRate;
                row.RevenueTimeline = periods
                    .Select(p => new PeriodRevenue(p.Period, p.Revenue * rate, p.Billed * rate))
                    .ToList();
            }
            else
            {
                row.RevenueTimeline = periods;
            }
        }
    }

    private void ApplyPeerBudgetEstimates(List<HistoricalProjectRow> rows)
    {
        if (_peerBudgetEstimator == null)
            return;

        var peerPool = new List<HistoricalPeerProject>();
        foreach (var row in rows)
        {
            var st = (row.Status ?? "").Trim();
            if (st.Equals("A", StringComparison.OrdinalIgnoreCase)
                || st.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (row.TotalEngDraft < 50 || row.TotalFee <= 0) continue;
            peerPool.Add(new HistoricalPeerProject
            {
                Wbs1 = row.Wbs1,
                Fee = row.TotalFee,
                Phase = (row.Phase ?? "").Trim(),
                ConstructionType = (row.ConstructionType ?? "").Trim(),
                ProjectCategory = (row.ProjectCategory ?? "").Trim(),
                EngHrs = row.EngHrs,
                DraftHrs = row.DraftHrs,
            });
        }

        foreach (var row in rows)
        {
            var (peerEng, peerDraft, pc) = _peerBudgetEstimator(
                row.TotalFee,
                row.Phase,
                row.ConstructionType,
                row.ProjectCategory,
                peerPool,
                row.Wbs1);
            if (pc >= 3)
            {
                row.EstEngBudget = peerEng;
                row.EstDraftBudget = peerDraft;
                row.BudgetPeerCount = pc;
            }
        }
    }

    private OdbcConnection CreateConnection()
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "", () => new Dictionary<string, string>());
        return factory.Create();
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
        return DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
