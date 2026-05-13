#nullable enable
#pragma warning disable SA1649
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
    /// Canonical firmwide backlog service. Mirrors the WPF Financials backlog
    /// formula, including T&amp;M hourly revenue on the fee side and the LedgerAR
    /// unposted-billing overlay on the billed side.
    /// </summary>
    public sealed class BacklogService
    {
        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public BacklogService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public BacklogResult Load(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
            => LoadCore(cn, usdToCadRate, ct);

        public async Task<BacklogResult> LoadAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();
                var usdToCadRate = OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);
                return LoadCore(cn, usdToCadRate, ct);
            }, ct).ConfigureAwait(false);
        }

        private BacklogResult LoadCore(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
        {
            List<ActiveProjectRow> activeProjects;
            try { activeProjects = LoadActiveProjectsFirmwide(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load active projects in {Service}.", nameof(BacklogService));
                activeProjects = new List<ActiveProjectRow>();
            }

            Dictionary<string, double> feeBilled;
            try { feeBilled = LoadFeeBilledFirmwide(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load posted fee billed in {Service}.", nameof(BacklogService));
                feeBilled = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, double> hourlyRevenue;
            try { hourlyRevenue = LoadHourlyRevenueFirmwide(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load hourly revenue in {Service}.", nameof(BacklogService));
                hourlyRevenue = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, double> unpostedFeeBilled;
            try { unpostedFeeBilled = LoadUnpostedFeeBilledFirmwide(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load unposted fee billed in {Service}.", nameof(BacklogService));
                unpostedFeeBilled = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            var rows = new List<BacklogProjectRow>();
            double totalFeeSum = 0.0;
            double feeBilledSum = 0.0;
            double unpostedFeeBilledSum = 0.0;
            double backlogSum = 0.0;

            foreach (var project in activeProjects)
            {
                ct.ThrowIfCancellationRequested();
                var fx = string.Equals(project.Org, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                feeBilled.TryGetValue(project.Wbs1, out var posted);
                hourlyRevenue.TryGetValue(project.Wbs1, out var hourly);
                unpostedFeeBilled.TryGetValue(project.Wbs1, out var unposted);

                var totalFee = (project.Fee + hourly) * fx;
                var postedFeeBilled = posted * fx;
                var unpostedFee = unposted * fx;
                var billedTotal = postedFeeBilled + unpostedFee;
                var backlog = totalFee - billedTotal;
                var percentBilled = SafeDiv(billedTotal, totalFee);

                totalFeeSum += totalFee;
                feeBilledSum += postedFeeBilled;
                unpostedFeeBilledSum += unpostedFee;
                backlogSum += backlog;

                rows.Add(new BacklogProjectRow(
                    Wbs1: project.Wbs1,
                    ProjectName: project.ProjectName,
                    ClientId: project.ClientId,
                    ClientName: project.ClientName,
                    Pm: project.Pm,
                    Org: project.Org,
                    TotalFee: totalFee,
                    FeeBilled: postedFeeBilled,
                    UnpostedFeeBilled: unpostedFee,
                    Backlog: backlog,
                    PercentBilled: percentBilled));
            }

            return new BacklogResult(
                FirmwideTotalFee: totalFeeSum,
                FirmwideFeeBilled: feeBilledSum,
                FirmwideUnpostedFeeBilled: unpostedFeeBilledSum,
                FirmwideBacklog: backlogSum,
                FirmwidePercentBilled: SafeDiv(feeBilledSum + unpostedFeeBilledSum, totalFeeSum),
                ActiveProjectCount: activeProjects.Count,
                TopProjects: rows.OrderByDescending(r => r.Backlog).Take(50).ToList(),
                DataLoaded: true);
        }

        private List<ActiveProjectRow> LoadActiveProjectsFirmwide(OdbcConnection cn, CancellationToken ct)
        {
            const string sqlTemplate = @"
SELECT
    pr.WBS1,
    pr.Org,
    COALESCE(pr.Fee, 0) AS Fee,
    COALESCE(pr.Name, '') AS ProjectName,
    COALESCE(pr.ClientID, '') AS ClientID,
    COALESCE(cc.Name, '') AS ClientName,
    COALESCE(pr.ProjMgr, '') AS ProjMgr,
    COALESCE(em.FirstName, '') AS PmFirstName,
    COALESCE(em.LastName, '') AS PmLastName
FROM [{0}].dbo.PR pr
LEFT JOIN [{0}].dbo.Clendor cc ON cc.ClientID = pr.ClientID
LEFT JOIN [{0}].dbo.EMMain em ON em.Employee = pr.ProjMgr
WHERE UPPER(COALESCE(pr.Status,'')) = 'A'
  AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND (pr.WBS3 IS NULL OR LTRIM(RTRIM(pr.WBS3)) = '');";
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = string.Format(CultureInfo.InvariantCulture, sqlTemplate, _catalog);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var rows = new List<ActiveProjectRow>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (wbs1.Length == 0) continue;
                var projMgr = GetTrimmed(r, 6);
                rows.Add(new ActiveProjectRow(
                    Wbs1: wbs1,
                    Org: GetTrimmed(r, 1),
                    Fee: GetDouble(r, 2),
                    ProjectName: GetTrimmed(r, 3),
                    ClientId: GetTrimmed(r, 4),
                    ClientName: GetTrimmed(r, 5),
                    Pm: BuildEmployeeDisplay(projMgr, GetTrimmed(r, 7), GetTrimmed(r, 8))));
            }
            return rows;
        }

        private Dictionary<string, double> LoadFeeBilledFirmwide(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
FROM [{_catalog}].dbo.PRSummaryMain
GROUP BY WBS1;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (!string.IsNullOrWhiteSpace(wbs1))
                    result[wbs1] = GetDouble(r, 1);
            }
            return result;
        }

        private Dictionary<string, double> LoadHourlyRevenueFirmwide(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
FROM [{_catalog}].dbo.PRSummaryMain sm
INNER JOIN [{_catalog}].dbo.PR pr
    ON pr.WBS1 = sm.WBS1 AND pr.WBS2 = sm.WBS2 AND pr.WBS3 = sm.WBS3
WHERE pr.Fee = 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
GROUP BY sm.WBS1
HAVING SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) > 0;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (!string.IsNullOrWhiteSpace(wbs1))
                    result[wbs1] = GetDouble(r, 1);
            }
            return result;
        }

        private Dictionary<string, double> LoadUnpostedFeeBilledFirmwide(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT WBS1, SUM(UnpostedAmt) AS UnpostedFeeBilled
FROM (
    SELECT
        invP.WBS1,
        invP.Period,
        invP.InvAmt - COALESCE(prP.PostedAmt, 0) AS UnpostedAmt
    FROM (
        SELECT WBS1, Period, SUM(-Amount) AS InvAmt
        FROM [{_catalog}].dbo.LedgerAR
        WHERE TransType = 'IN'
          AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4210', '4220', '4240')
        GROUP BY WBS1, Period
    ) invP
    LEFT JOIN (
        SELECT WBS1, Period,
               SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS PostedAmt
        FROM [{_catalog}].dbo.PRSummaryMain
        GROUP BY WBS1, Period
    ) prP
        ON prP.WBS1 = invP.WBS1 AND prP.Period = invP.Period
    WHERE invP.InvAmt - COALESCE(prP.PostedAmt, 0) > 0
) gap
GROUP BY WBS1;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (!string.IsNullOrWhiteSpace(wbs1))
                    result[wbs1] = GetDouble(r, 1);
            }
            return result;
        }

        private OdbcConnection CreateConnection()
        {
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User, _odbcOptions.Password, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private static string GetTrimmed(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? string.Empty : (Convert.ToString(r.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

        private static double GetDouble(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(r.GetValue(ordinal), CultureInfo.InvariantCulture);

        private static string BuildEmployeeDisplay(string employee, string first, string last)
        {
            var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return string.IsNullOrWhiteSpace(employee) ? name : $"{name} ({employee})";
            return employee ?? string.Empty;
        }

        private static double SafeDiv(double n, double d)
            => d > AnalyticsThresholds.RoundingDollarFloor ? n / d : 0.0;
    }

    public sealed record BacklogProjectRow(
        string Wbs1,
        string ProjectName,
        string ClientId,
        string ClientName,
        string Pm,
        string Org,
        double TotalFee,
        double FeeBilled,
        double UnpostedFeeBilled,
        double Backlog,
        double PercentBilled);

    public sealed record BacklogResult(
        double FirmwideTotalFee,
        double FirmwideFeeBilled,
        double FirmwideUnpostedFeeBilled,
        double FirmwideBacklog,
        double FirmwidePercentBilled,
        int ActiveProjectCount,
        IReadOnlyList<BacklogProjectRow> TopProjects,
        bool DataLoaded)
    {
        public static readonly BacklogResult Empty = new(
            0.0, 0.0, 0.0, 0.0, 0.0, 0,
            Array.Empty<BacklogProjectRow>(),
            DataLoaded: false);
    }

    internal sealed record ActiveProjectRow(
        string Wbs1,
        string Org,
        double Fee,
        string ProjectName,
        string ClientId,
        string ClientName,
        string Pm);
}
