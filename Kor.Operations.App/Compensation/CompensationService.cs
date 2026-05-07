#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Operations.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using static Kor.Operations.Data.DataReaderHelpers;

namespace Kor.Operations.Compensation;

public sealed class CompensationService
{
    private readonly DeltekOdbcOptions _opts;
    private readonly CompensationOptions _compOpts;
    private readonly DatabaseOptions _dbOpts;
    private readonly double _usdToCadRate;
    private readonly ILogger<CompensationService>? _log;

    public CompensationService(
        DeltekOdbcOptions opts,
        CompensationOptions compOpts,
        DatabaseOptions dbOpts,
        FinancialsOptions financialsOpts,
        ILogger<CompensationService>? log = null)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _compOpts = compOpts ?? throw new ArgumentNullException(nameof(compOpts));
        _dbOpts = dbOpts ?? throw new ArgumentNullException(nameof(dbOpts));
        _usdToCadRate = OrgFx.ParseUsdToCadRate(financialsOpts?.BilledUsdToCadRate);
        _log = log;
    }

    public Task<CompensationLoadResult> LoadAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            var rows = LoadActiveEmployeesSync(ct);
            var aggs = LoadHoursTrailing12MoSync(ct);
            var fixedFeeRatios = LoadFixedFeeWbs3RatiosSync(ct);
            var personFixedFee = LoadPersonFixedFeeBillExtByWbs3Sync(ct);
            var (snapshots, snapshotDate) = LoadLatestProductivityScoresSync(ct);
            var partnerImputed = _opts.PartnerImputedCostRate > 0 ? _opts.PartnerImputedCostRate : 250.0;

            var allocatedFixedFeeByEmployee = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (employeeId, wbs1, wbs2, wbs3, billExt) in personFixedFee)
            {
                var ratio = fixedFeeRatios.TryGetValue((wbs1, wbs2, wbs3), out var value) ? value : 1.0;
                var allocated = billExt * ratio;
                allocatedFixedFeeByEmployee[employeeId] = allocatedFixedFeeByEmployee.TryGetValue(employeeId, out var prior)
                    ? prior + allocated
                    : allocated;
            }

            foreach (var row in rows)
            {
                if (aggs.TryGetValue(row.EmployeeId, out var a))
                {
                    row.AnnualizedHoursActual = a.TotalHrs;
                    row.AnnualizedBillableHoursActual = a.BillableHrs;
                    row.UtilizationActual = a.TotalHrs > 0 ? a.BillableHrs / a.TotalHrs : 0.0;
                    row.OvertimeHoursActual = a.OvtHrs;
                    row.OvertimeCostActual = a.OvtCost;
                    row.CostRateActual = row.IsPartner
                        ? partnerImputed
                        : (a.TotalHrs > 0 ? a.LaborCost / a.TotalHrs : row.CostRateActual);
                    row.AnnualizedLaborCostActual = row.IsPartner
                        ? a.TotalHrs * partnerImputed
                        : a.LaborCost;

                    var allocatedEligible = allocatedFixedFeeByEmployee.TryGetValue(row.EmployeeId, out var alloc) ? alloc : 0.0;
                    var fixedFeeRevenue = allocatedEligible + Math.Max(0.0, a.FixedFeeBillExt - a.FixedFeeBillExtAllocatable);
                    row.AnnualizedFixedFeeRevenueActual = fixedFeeRevenue;
                    row.AnnualizedRevenueActual = a.TmRevenue + fixedFeeRevenue;
                }

                if (snapshots.TryGetValue(row.EmployeeId, out var score))
                    row.ProductivityScoreActual = score;
            }

            var employeeRows = rows.Where(r => !r.IsPartner).ToList();
            var employeeCount = employeeRows.Count;
            var employeeTotalHours = employeeRows.Sum(r => r.AnnualizedHoursActual);
            var employeeBillableHours = employeeRows.Sum(r => r.AnnualizedBillableHoursActual);
            var employeeLaborCost = employeeRows.Sum(r => r.AnnualizedLaborCostActual);
            var employeeRevenue = employeeRows.Sum(r => r.AnnualizedRevenueActual);

            var firm = new CompensationFirmContext(() => rows)
            {
                FirmRevenue = LoadFirmRevenueTrailing12MoSync(ct),
                FirmLaborCost = employeeLaborCost,
                PoolRate = _compOpts.PoolRate,
                FirmUtilization = employeeTotalHours > 0 ? employeeBillableHours / employeeTotalHours : 0.0,
                FirmAnnualizedHoursPerHead = employeeCount > 0 ? employeeTotalHours / employeeCount : 0.0,
                EmployeeMarginPerBillableHour = employeeBillableHours > 0
                    ? Math.Max(0.0, (employeeRevenue - employeeLaborCost) / employeeBillableHours)
                    : 0.0,
                ProductivitySnapshotDate = snapshotDate
            };

            foreach (var row in rows)
                row.Firm = firm;

            firm.RecomputeTotal();

            _log?.LogInformation(
                "Compensation: loaded {Count} active staff; firm revenue {Revenue:C0}, labor {Labor:C0}, GP {GrossProfit:C0}, pool {Pool:C0}.",
                rows.Count, firm.FirmRevenue, firm.FirmLaborCost, firm.FirmGrossProfit, firm.FirmBonusPool);

            return new CompensationLoadResult(rows, firm);
        }, ct);

    private List<EmployeeCompensationRow> LoadActiveEmployeesSync(CancellationToken ct)
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
            () => new Dictionary<string, string>());

        var result = new List<EmployeeCompensationRow>();
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    e.Employee,
    e.FirstName,
    e.LastName,
    e.Title,
    ec.Region,
    ec.HireDate,
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
            var employeeId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            result.Add(new EmployeeCompensationRow
            {
                EmployeeId = employeeId,
                FirstName = GetTrimmed(r, 1),
                LastName = GetTrimmed(r, 2),
                Title = GetTrimmed(r, 3),
                Region = GetTrimmed(r, 4),
                HireDate = GetDate(r, 5),
                BillingRateActual = GetDouble(r, 6),
                CostRateActual = GetDouble(r, 7)
            });
        }

        return result;
    }

    private Dictionary<string, EmployeeAggregates> LoadHoursTrailing12MoSync(CancellationToken ct)
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
            () => new Dictionary<string, string>());
        var (startDate, endDateInclusive, _, _) = GetTrailing12MoWindow();
        var endExclusive = endDateInclusive.AddDays(1);

        var result = new Dictionary<string, EmployeeAggregates>(StringComparer.OrdinalIgnoreCase);
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // Dollar columns (LaborCost, OvtCost, TmRevenue, FixedFeeBillExt,
        // FixedFeeBillExtAllocatable) are denominated in the project's
        // currency. Join PR for the master row's Org and FX-convert USA-org
        // dollars to CAD-equivalent so per-employee aggregates and the
        // firm-wide compensation pool size off a single currency. Hours
        // columns are currency-agnostic and stay raw.
        var fxRate = _usdToCadRate;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80)
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS OvtHrs,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN (COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS LaborCost,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN (COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS OvtCost,
    SUM(CASE WHEN COALESCE(pr.Fee, -1) = 0
              AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
              AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
             THEN
               CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
                    THEN COALESCE(t.BillExt, 0) * ?
                    ELSE COALESCE(t.BillExt, 0)
               END
             ELSE 0 END) AS TmRevenue,
    SUM(CASE WHEN COALESCE(pr.Fee, 0) > 0
             THEN
               CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
                    THEN COALESCE(t.BillExt, 0) * ?
                    ELSE COALESCE(t.BillExt, 0)
               END
             ELSE 0 END) AS FixedFeeBillExt,
    SUM(CASE WHEN COALESCE(pr.Fee, 0) > 0
              AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
              AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
             THEN
               CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
                    THEN COALESCE(t.BillExt, 0) * ?
                    ELSE COALESCE(t.BillExt, 0)
               END
             ELSE 0 END) AS FixedFeeBillExtAllocatable
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1 AND pr.WBS2 = t.WBS2 AND pr.WBS3 = t.WBS3
LEFT JOIN [{catalog}].dbo.PR prMaster
       ON prMaster.WBS1 = t.WBS1
      AND (prMaster.WBS2 IS NULL OR LTRIM(RTRIM(prMaster.WBS2)) = '')
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= ? AND t.TransDate < ?
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee";
        // Rate parameters in CASE-FX expression order (LaborCost, OvtCost,
        // TmRevenue, FixedFeeBillExt, FixedFeeBillExtAllocatable), then date
        // window. Order matters — ODBC binds positionally.
        for (var i = 0; i < 5; i++)
            cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Double, Value = fxRate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = startDate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = endExclusive });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var employeeId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            result[employeeId] = new EmployeeAggregates(
                GetDouble(r, 1),
                GetDouble(r, 2),
                GetDouble(r, 3),
                GetDouble(r, 4),
                GetDouble(r, 5),
                GetDouble(r, 6),
                GetDouble(r, 7),
                GetDouble(r, 8));
        }

        return result;
    }

    private double LoadFirmRevenueTrailing12MoSync(CancellationToken ct)
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
            () => new Dictionary<string, string>());
        var (_, _, startPeriod, endPeriod) = GetTrailing12MoWindow();

        // PRSummaryMain.BilledFee/Revenue are denominated in the project's currency;
        // bucket by pr.Org so USA-org rows can be FX-converted to CAD-equivalent
        // before summing into the firmwide compensation pool. Without this the
        // pool sized off mixed CAD+USD totals.
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    COALESCE(SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE sm.Revenue END), 0) AS Amount
FROM [{catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = sm.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period >= ? AND sm.Period <= ?
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END";
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Int, Value = startPeriod });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Int, Value = endPeriod });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = GetTrimmed(r, 0);
            var amt = GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                usaTotal += amt;
            else
                cadTotal += amt;
        }
        return cadTotal + (usaTotal * _usdToCadRate);
    }

    private Dictionary<(string Wbs1, string Wbs2, string Wbs3), double> LoadFixedFeeWbs3RatiosSync(CancellationToken ct)
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
            () => new Dictionary<string, string>());

        var result = new Dictionary<(string, string, string), double>();
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.WBS2,
    pr.WBS3,
    COALESCE(sm.RecognizedRevenue, 0) AS RecognizedRevenue,
    COALESCE(td.LaborBillExt, 0) AS LaborBillExt
FROM [{catalog}].dbo.PR pr
LEFT JOIN (
    SELECT WBS1, WBS2, WBS3, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS RecognizedRevenue
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1, WBS2, WBS3
) sm ON sm.WBS1 = pr.WBS1 AND sm.WBS2 = pr.WBS2 AND sm.WBS3 = pr.WBS3
LEFT JOIN (
    SELECT WBS1, WBS2, WBS3, SUM(BillExt) AS LaborBillExt
    FROM [{catalog}].dbo.tkDetail
    GROUP BY WBS1, WBS2, WBS3
) td ON td.WBS1 = pr.WBS1 AND td.WBS2 = pr.WBS2 AND td.WBS3 = pr.WBS3
WHERE COALESCE(pr.Fee, 0) > 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var wbs1 = GetTrimmed(r, 0);
            var wbs2 = GetTrimmed(r, 1);
            var wbs3 = GetTrimmed(r, 2);
            if (string.IsNullOrWhiteSpace(wbs1) || string.IsNullOrWhiteSpace(wbs2) || string.IsNullOrWhiteSpace(wbs3))
                continue;

            var revenue = GetDouble(r, 3);
            var billExt = GetDouble(r, 4);
            result[(wbs1, wbs2, wbs3)] = billExt > 0 ? revenue / billExt : 1.0;
        }

        return result;
    }

    private List<(string EmployeeId, string Wbs1, string Wbs2, string Wbs3, double BillExt)> LoadPersonFixedFeeBillExtByWbs3Sync(CancellationToken ct)
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "",
            () => new Dictionary<string, string>());
        var (startDate, endDateInclusive, _, _) = GetTrailing12MoWindow();
        var endExclusive = endDateInclusive.AddDays(1);

        var result = new List<(string, string, string, string, double)>();
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // BillExt is denominated in the project's currency. Join PR at the master
        // row for Org and FX-convert USA-org rows so per-employee allocations
        // sum in CAD-equivalent (downstream multiplies by a currency-neutral
        // ratio and aggregates across WBS3, which can span Orgs).
        var fxRate = _usdToCadRate;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    t.WBS1,
    t.WBS2,
    t.WBS3,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN COALESCE(t.BillExt, 0) * ?
             ELSE COALESCE(t.BillExt, 0)
        END
    ) AS BillExt
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1 AND pr.WBS2 = t.WBS2 AND pr.WBS3 = t.WBS3
LEFT JOIN [{catalog}].dbo.PR prMaster
       ON prMaster.WBS1 = t.WBS1
      AND (prMaster.WBS2 IS NULL OR LTRIM(RTRIM(prMaster.WBS2)) = '')
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= ? AND t.TransDate < ?
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(pr.Fee, 0) > 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
GROUP BY t.Employee, t.WBS1, t.WBS2, t.WBS3";
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Double, Value = fxRate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = startDate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = endExclusive });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var employeeId = GetTrimmed(r, 0);
            var wbs1 = GetTrimmed(r, 1);
            var wbs2 = GetTrimmed(r, 2);
            var wbs3 = GetTrimmed(r, 3);
            if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(wbs1)
                || string.IsNullOrWhiteSpace(wbs2) || string.IsNullOrWhiteSpace(wbs3))
            {
                continue;
            }

            result.Add((employeeId, wbs1, wbs2, wbs3, GetDouble(r, 4)));
        }

        return result;
    }

    private (Dictionary<string, double> Scores, DateTime? SnapshotDate) LoadLatestProductivityScoresSync(CancellationToken ct)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        DateTime? latestDate = null;

        using var cn = new SqlConnection(_dbOpts.KorTransmittalsDb);
        cn.Open();
        using var cmd = new SqlCommand(@"
WITH ranked AS (
    SELECT
        EmployeeId,
        SnapshotDate,
        ProductivityScore,
        ROW_NUMBER() OVER (PARTITION BY EmployeeId ORDER BY SnapshotDate DESC) AS rn
    FROM dbo.EmployeeScoreSnapshots
)
SELECT EmployeeId, SnapshotDate, ProductivityScore
FROM ranked
WHERE rn = 1;", cn)
        {
            CommandTimeout = 15
        };

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var employeeId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            var snapshotDate = GetDate(r, 1);
            latestDate = latestDate.HasValue && snapshotDate.HasValue
                ? (latestDate > snapshotDate ? latestDate : snapshotDate)
                : latestDate ?? snapshotDate;
            result[employeeId] = GetDouble(r, 2);
        }

        return (result, latestDate);
    }

    private static (DateTime StartDate, DateTime EndDateInclusive, int StartPeriod, int EndPeriod) GetTrailing12MoWindow()
    {
        var firstOfCurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var startDate = firstOfCurrentMonth.AddMonths(-12);
        var endDate = firstOfCurrentMonth.AddDays(-1);
        var startPeriod = startDate.Year * 100 + startDate.Month;
        var endPeriod = endDate.Year * 100 + endDate.Month;
        return (startDate, endDate, startPeriod, endPeriod);
    }

    private static DateTime? GetDate(IDataRecord record, int index)
    {
        if (record.IsDBNull(index))
            return null;

        var value = record.GetValue(index);
        if (value is DateTime dt)
            return dt;

        return DateTime.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;
    }

    private sealed record EmployeeAggregates(
        double TotalHrs,
        double BillableHrs,
        double OvtHrs,
        double LaborCost,
        double OvtCost,
        double TmRevenue,
        double FixedFeeBillExt,
        double FixedFeeBillExtAllocatable);
}

public sealed record CompensationLoadResult(IReadOnlyList<EmployeeCompensationRow> Rows, CompensationFirmContext Firm);
