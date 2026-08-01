#nullable enable
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using static Kor.Operations.Data.DataReaderHelpers;

namespace Kor.Operations.PMTools;

public sealed class EmployeeAnalyticsService
{
    private const int Engineering = 10;
    private const int Drafting = 20;
    private const int Checking = 30;
    private const int Admin = 70;
    private const int NonBillable = 80;

    private readonly DeltekOdbcOptions _opts;
    private readonly string _catalog;

    public EmployeeAnalyticsService(DeltekOdbcOptions opts)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _catalog = DeltekCatalogValidator.ResolveCatalog(opts.Catalog);
    }

    public List<EmployeeProjectHours> LoadEmployeeProjectHoursSync(CancellationToken ct)
    {
        var result = new List<EmployeeProjectHours>();
        using var cn = CreateConnection();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    t.WBS1,
    SUM(CASE WHEN t.LaborCode IN ({Engineering}, {Checking}) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = {Drafting} THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({Admin}, {NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs,
    MIN(ec.HireDate) AS HireDate
FROM [{_catalog}].dbo.tkDetail t
LEFT JOIN [{_catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{_catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1;";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var empId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(empId)) continue;
            result.Add(new EmployeeProjectHours
            {
                EmployeeId = empId,
                EmployeeName = BuildName(empId, GetTrimmed(r, 1), GetTrimmed(r, 2)),
                Wbs1 = GetTrimmed(r, 3),
                EngHrs = GetDouble(r, 4),
                DraftHrs = GetDouble(r, 5),
                BillableHrs = GetDouble(r, 6),
                TotalHrs = GetDouble(r, 7),
                HireDate = r.IsDBNull(8) ? null : Convert.ToDateTime(r.GetValue(8), CultureInfo.InvariantCulture),
            });
        }

        return result;
    }

    public List<EmployeeWeeklyHours> LoadEmployeeWeeklyUtilizationSync(CancellationToken ct)
    {
        var result = new List<EmployeeWeeklyHours>();
        using var cn = CreateConnection();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    DATEADD(day, -DATEDIFF(day, '18991231', CAST(t.TransDate AS date)) % 7, CAST(t.TransDate AS date)) AS WeekStart,
    SUM(CASE WHEN t.LaborCode NOT IN ({Admin}, {NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{_catalog}].dbo.tkDetail t
LEFT JOIN [{_catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{_catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= DATEADD(week, -12, CAST(GETDATE() AS date))
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
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
            result.Add(new EmployeeWeeklyHours
            {
                EmployeeId = empId,
                EmployeeName = BuildName(empId, GetTrimmed(r, 1), GetTrimmed(r, 2)),
                WeekStart = GetDate(r, 3) ?? DateTime.MinValue,
                BillableHrs = GetDouble(r, 4),
                TotalHrs = GetDouble(r, 5),
            });
        }

        return result;
    }

    public List<EmployeeRate> LoadEmployeeRatesSync(CancellationToken ct)
    {
        var result = new List<EmployeeRate>();
        using var cn = CreateConnection();
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
FROM [{_catalog}].dbo.EMMain e
LEFT JOIN [{_catalog}].dbo.EMCompany ec ON ec.Employee = e.Employee
WHERE UPPER(COALESCE(ec.Status, 'A')) = 'A'
ORDER BY e.LastName, e.FirstName";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var empId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(empId)) continue;
            var rawCost = GetDouble(r, 4);
            var isPartner = empId.StartsWith("P", StringComparison.OrdinalIgnoreCase);
            result.Add(new EmployeeRate
            {
                EmployeeId = empId,
                EmployeeName = BuildName(empId, GetTrimmed(r, 1), GetTrimmed(r, 2)),
                BillingRate = GetDouble(r, 3),
                CostRate = rawCost,
                IsPartner = isPartner,
                EffectiveCostRate = isPartner ? _opts.PartnerImputedCostRate : rawCost,
            });
        }

        return result;
    }

    public List<QuarterlyEmployeeHours> LoadQuarterlyEmployeeHoursSync(CancellationToken ct)
    {
        var result = new List<QuarterlyEmployeeHours>();
        using var cn = CreateConnection();
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
    SUM(CASE WHEN t.LaborCode IN ({Engineering}, {Checking}) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = {Drafting} THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({Admin}, {NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{_catalog}].dbo.tkDetail t
LEFT JOIN [{_catalog}].dbo.EMMain e ON e.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate >= '2020-01-01'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1,
         DATEPART(YEAR, t.TransDate), DATEPART(QUARTER, t.TransDate);";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var empId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(empId)) continue;
            result.Add(new QuarterlyEmployeeHours
            {
                EmployeeId = empId,
                EmployeeName = BuildName(empId, GetTrimmed(r, 1), GetTrimmed(r, 2)),
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

    private OdbcConnection CreateConnection()
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "", () => new Dictionary<string, string>());
        return factory.Create();
    }

    private static string BuildName(string fallback, string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
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
