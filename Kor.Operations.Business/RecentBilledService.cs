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
    /// Firmwide recent billed AND earned roll-up anchored to the latest
    /// closed periods present in PRSummaryMain. Billed = SUM(PRSummaryMain.Billed).
    /// Earned = SUM(BilledFee else Revenue). Used by Collection Exposure
    /// and Earned-vs-Invoiced tools.
    /// </summary>
    public sealed class RecentBilledService
    {
        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public RecentBilledService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public RecentBilledResult Load(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
            => LoadCore(cn, usdToCadRate, ct);

        public async Task<RecentBilledResult> LoadAsync(CancellationToken ct)
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

        private RecentBilledResult LoadCore(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
        {
            List<string> periods;
            try { periods = LoadLatestPeriods(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load latest billed periods in {Service}.", nameof(RecentBilledService));
                return RecentBilledResult.Empty;
            }

            if (periods.Count < 1)
                return RecentBilledResult.Empty;

            Dictionary<(string Period, string Bucket), (double Billed, double Earned)> perPeriod;
            try { perPeriod = LoadPerPeriodFirmwide(cn, periods, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load recent billed/earned totals in {Service}.", nameof(RecentBilledService));
                return RecentBilledResult.Empty;
            }

            double billed30 = 0.0;
            double billed90 = 0.0;
            double earned30 = 0.0;
            double earned90 = 0.0;
            for (var i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                perPeriod.TryGetValue((period, "CAD"), out var cad);
                perPeriod.TryGetValue((period, "USA"), out var usa);
                var billedTotal = cad.Billed + (usa.Billed * usdToCadRate);
                var earnedTotal = cad.Earned + (usa.Earned * usdToCadRate);
                if (i == 0)
                {
                    billed30 = billedTotal;
                    earned30 = earnedTotal;
                }
                billed90 += billedTotal;
                earned90 += earnedTotal;
            }

            return new RecentBilledResult(
                LatestPeriod: periods[0],
                Periods: periods,
                Billed30: billed30,
                Billed90: billed90,
                UsdToCadRate: usdToCadRate,
                DataLoaded: true,
                Earned30: earned30,
                Earned90: earned90);
        }

        private List<string> LoadLatestPeriods(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT TOP 3 Period
FROM [{_catalog}].dbo.PRSummaryMain
WHERE Period IS NOT NULL AND LTRIM(RTRIM(Period)) <> ''
GROUP BY Period
ORDER BY Period DESC;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var periods = new List<string>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var period = GetTrimmed(r, 0);
                if (period.Length == 6 && period.All(char.IsDigit))
                    periods.Add(period);
            }
            return periods;
        }

        private Dictionary<(string Period, string Bucket), (double Billed, double Earned)> LoadPerPeriodFirmwide(
            OdbcConnection cn, IReadOnlyList<string> periods, CancellationToken ct)
        {
            if (periods.Count == 0)
                return new Dictionary<(string Period, string Bucket), (double Billed, double Earned)>();

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var periodPlaceholders = MakeInListPlaceholders(periods.Count);
            cmd.CommandText = $@"
SELECT
    sm.Period,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed, 0)) AS Billed,
    SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS Earned
FROM [{_catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period IN ({periodPlaceholders})
GROUP BY sm.Period, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
            AddInListParameters(cmd, periods);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var result = new Dictionary<(string Period, string Bucket), (double Billed, double Earned)>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var period = GetTrimmed(r, 0);
                var bucket = GetTrimmed(r, 1);
                if (period.Length == 0 || bucket.Length == 0) continue;
                result[(period, bucket)] = (Billed: GetDouble(r, 2), Earned: GetDouble(r, 3));
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

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListParameters(OdbcCommand cmd, IEnumerable<string> values)
        {
            foreach (var v in values)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
            }
        }
    }

    public sealed record RecentBilledResult(
        string LatestPeriod,
        IReadOnlyList<string> Periods,
        double Billed30,
        double Billed90,
        double UsdToCadRate,
        bool DataLoaded,
        double Earned30 = 0.0,
        double Earned90 = 0.0)
    {
        public static readonly RecentBilledResult Empty = new(
            "n/a", Array.Empty<string>(), 0.0, 0.0, 1.36, DataLoaded: false);
    }
}
