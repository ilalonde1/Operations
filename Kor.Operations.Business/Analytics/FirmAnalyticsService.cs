#nullable enable
using System.Data.Odbc;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using static Kor.Operations.Data.DataReaderHelpers;

namespace Kor.Operations.PMTools;

public sealed class FirmAnalyticsService
{
    private const int Admin = 70;
    private const int NonBillable = 80;

    private readonly DeltekOdbcOptions _opts;
    private readonly string _catalog;

    public FirmAnalyticsService(DeltekOdbcOptions opts)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _catalog = DeltekCatalogValidator.ResolveCatalog(opts.Catalog);
    }

    public FirmUtilizationStats LoadFirmUtilizationSync(CancellationToken ct)
    {
        using var cn = CreateConnection();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    YEAR(TransDate) AS Yr,
    SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN LaborCode NOT IN ({Admin}, {NonBillable})
              AND WBS1 NOT LIKE '[A-Z]%'
              AND WBS1 NOT LIKE '9[A-Z]%'
              AND WBS1 NOT LIKE '99%'
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs
FROM [{_catalog}].dbo.tkDetail
WHERE TransDate IS NOT NULL
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
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

    private OdbcConnection CreateConnection()
    {
        var dsn = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
        var factory = new VpOdbcDsnFactory(dsn, _opts.User ?? "", _opts.Password ?? "", () => new Dictionary<string, string>());
        return factory.Create();
    }
}
