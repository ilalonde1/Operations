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
    /// Canonical WIP snapshot service. Auto-detects whether Deltek Revenue
    /// Generation populates PRSummaryMain.Unbilled and falls back to the
    /// Billed-Revenue proxy when it does not.
    /// </summary>
    public sealed class WipFinancialsService
    {
        private const int OdbcParameterChunkSize = 80;

        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public WipFinancialsService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public WipFinancialsResult Load(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
            => LoadCore(cn, wbs1, usdToCadRate, ct);

        public async Task<WipFinancialsResult> LoadAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();
                var usdToCadRate = OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);
                return LoadCore(cn, new List<string>(), usdToCadRate, ct);
            }, ct).ConfigureAwait(false);
        }

        private WipFinancialsResult LoadCore(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
        {
            var asOfPeriod = LoadMaxPrSummaryPeriod(cn, ct);
            if (string.IsNullOrWhiteSpace(asOfPeriod) || asOfPeriod.Length != 6 || !asOfPeriod.All(char.IsDigit))
            {
                return new WipFinancialsResult(
                    0.0, 0.0, 0.0, "n/a",
                    Array.Empty<WipProjectBreakdownRow>(),
                    0.0, 0.0, 0.0,
                    RevenueGenerationDetected: false,
                    DataLoaded: true);
            }

            var unbilledColumnHasAny = UnbilledColumnHasAny(cn, ct);

            // RG-detection guards the WIP card from showing meaningless zeros when the
            // catalog has no revenue/unbilled signal at all. Two qualifying signals:
            //   1. The Unbilled column itself has data - that IS the WIP signal, no
            //      proxy needed. Sign-agnostic check.
            //   2. As a fallback, recent Revenue magnitude is >= 1% of Billed.
            //      Math.Abs is required because PRSummaryMain.Revenue is stored with
            //      Deltek's credit-side sign convention (negative = recognized);
            //      a signed comparison would always fail at catalogs like KOR's.
            var revenueGenerationDetected = unbilledColumnHasAny || DetectRevenueGeneration(cn, ct);
            if (!revenueGenerationDetected)
            {
                return new WipFinancialsResult(
                    0.0, 0.0, 0.0, "n/a",
                    Array.Empty<WipProjectBreakdownRow>(),
                    0.0, 0.0, 0.0,
                    RevenueGenerationDetected: false,
                    DataLoaded: true);
            }

            IReadOnlyList<WipProjectBreakdownRow> wipProjectRows;
            try
            {
                wipProjectRows = LoadWipProjectBreakdownByProject(cn, wbs1, asOfPeriod, useUnbilledAsOf: unbilledColumnHasAny, usdToCadRate, ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load WIP project breakdown in {Loader} for Period={Period}.", nameof(WipFinancialsService), asOfPeriod);
                wipProjectRows = Array.Empty<WipProjectBreakdownRow>();
            }

            var wipUnbilled = wipProjectRows.Sum(r => r.Earned);
            var wipOverbilled = wipProjectRows.Sum(r => r.Overbilled);
            var wipUnbilledNet = wipProjectRows.Sum(r => r.Net);

            (double Earned, double Overbilled, double Net) firmWip;
            try { firmWip = LoadFirmwideWipProxyBalance(cn, asOfPeriod, usdToCadRate, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load firmwide WIP balances in {Loader} for Period={Period}.", nameof(WipFinancialsService), asOfPeriod);
                firmWip = (0.0, 0.0, 0.0);
            }

            return new WipFinancialsResult(
                wipUnbilled,
                wipOverbilled,
                wipUnbilledNet,
                asOfPeriod,
                wipProjectRows,
                firmWip.Earned,
                firmWip.Overbilled,
                firmWip.Net,
                true,
                true);
        }

        private string? LoadMaxPrSummaryPeriod(OdbcConnection cn, CancellationToken ct)
        {
            try
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $"SELECT MAX(Period) FROM [{_catalog}].dbo.PRSummaryMain;";
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return null;
                var p = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
                return (p.Length == 6 && p.All(char.IsDigit)) ? p : null;
            }
            catch
            {
                return null;
            }
        }

        private bool UnbilledColumnHasAny(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT TOP 1 1
FROM [{_catalog}].dbo.PRSummaryMain
WHERE ABS(COALESCE(Unbilled,0)) > 0.01;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            return v != null && v != DBNull.Value;
        }

        private bool DetectRevenueGeneration(OdbcConnection cn, CancellationToken ct)
        {
            var recentPeriods = new List<string>();
            using (var periodCmd = cn.CreateCommand())
            {
                periodCmd.CommandTimeout = SqlTimeouts.Batch;
                periodCmd.CommandText = $@"
SELECT TOP 3 Period
FROM [{_catalog}].dbo.PRSummaryMain
GROUP BY Period
ORDER BY Period DESC;";
                using var periodReg = ct.Register(() => { try { periodCmd.Cancel(); } catch { } });
                using var periodReader = periodCmd.ExecuteReader();
                while (periodReader.Read())
                {
                    var period = GetTrimmed(periodReader, 0);
                    if (period.Length == 6 && period.All(char.IsDigit))
                    {
                        recentPeriods.Add(period);
                    }
                }
            }

            if (recentPeriods.Count == 0)
                return false;

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    SUM(COALESCE(Revenue, 0)) AS RawRevenue,
    SUM(COALESCE(Billed, 0)) AS Billed
FROM [{_catalog}].dbo.PRSummaryMain
WHERE Period IN ({MakeInListPlaceholders(recentPeriods.Count)});";
            foreach (var period in recentPeriods)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return false;

            var recentRevenue = GetDouble(r, 0);
            var recentBilled = GetDouble(r, 1);
            // Math.Abs handles the credit-side storage convention (Revenue stored
            // as -Amount). A signed comparison reads negative values as "no
            // revenue" and incorrectly suppresses the WIP card.
            return recentBilled > 0.0 && Math.Abs(recentRevenue) > 0.01 * recentBilled;
        }

        private (double Earned, double Overbilled, double Net) LoadFirmwideWipProxyBalance(OdbcConnection cn, string asOfPeriod, double usdToCadRate, CancellationToken ct)
        {
            var period = (asOfPeriod ?? string.Empty).Trim();
            if (period.Length != 6 || !period.All(char.IsDigit))
            {
                // Don't fall back to a calendar month - that anchors WIP to a period Deltek
                // may not have posted yet (or to a future month). If MAX(Period) is unavailable,
                // return zeros so the tile shows "no data" rather than fabricated numbers.
                period = LoadMaxPrSummaryPeriod(cn, ct) ?? string.Empty;
                if (period.Length != 6 || !period.All(char.IsDigit))
                    return (0.0, 0.0, 0.0);
            }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Per-project Net (source currency), bucketed by master-project Org. Earned vs
            // Overbilled split happens after FX conversion so a USA project's sign in CAD
            // matches its sign in USD (FX is positive multiplier). Net is computed as
            // Billed - Revenue (sign-flipped from raw storage) so positive=earned-not-billed
            // and negative=overbilled - matches Deltek's credit-side sign convention on
            // PRSummaryMain.Revenue.
            cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{_catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            double earned = 0.0, overbilled = 0.0, net = 0.0;
            while (r.Read())
            {
                var bucket = GetTrimmed(r, 1);
                var rawNet = GetDouble(r, 2);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var nNet = rawNet * fx;
                if (nNet > 0) earned += nNet;
                else if (nNet < 0) overbilled += -nNet;
                net += nNet;
            }
            return (earned, overbilled, net);
        }

        private List<WipProjectBreakdownRow> LoadWipProjectBreakdownByProject(
            OdbcConnection cn,
            List<string> wbs1,
            string asOfPeriod,
            bool useUnbilledAsOf,
            double usdToCadRate,
            CancellationToken ct)
        {
            var period = (asOfPeriod ?? string.Empty).Trim();
            if (period.Length != 6 || !period.All(char.IsDigit))
            {
                period = LoadMaxPrSummaryPeriod(cn, ct) ?? string.Empty;
                if (period.Length != 6 || !period.All(char.IsDigit))
                    return new List<WipProjectBreakdownRow>();
            }

            var rows = new Dictionary<string, WipProjectBreakdownRow>(StringComparer.OrdinalIgnoreCase);
            var chunks = (wbs1 == null || wbs1.Count == 0)
                ? new[] { (List<string>?)null }.AsEnumerable()
                : Chunk(wbs1, OdbcParameterChunkSize).Select(c => (List<string>?)c);

            foreach (var chunk in chunks)
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var inWbs1 = chunk != null
                    ? $"  AND sm.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})\n"
                    : string.Empty;

                // Both branches join PR for Org so the per-project Net can be FX-converted
                // to CAD-equivalent before being split into earned/overbilled. PRSummaryMain
                // stores Revenue and Unbilled with Deltek's credit-side sign convention
                // (negative = recognized revenue), so both branches flip the sign here so
                // downstream code reads positive=earned and negative=overbilled cleanly.
                if (useUnbilledAsOf)
                {
                    cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-COALESCE(sm.Unbilled,0)) AS Net,
    MAX(COALESCE(pr.Name,'')) AS ProjectName,
    MAX(COALESCE(pr.ClientID,'')) AS ClientID,
    MAX(COALESCE(cc.Name,'')) AS ClientName,
    MAX(COALESCE(pr.ProjMgr,'')) AS ProjMgr,
    MAX(COALESCE(em.FirstName,'')) AS PmFirstName,
    MAX(COALESCE(em.LastName,'')) AS PmLastName
FROM [{_catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{_catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
LEFT JOIN [{_catalog}].dbo.Clendor cc
  ON cc.ClientID = pr.ClientID
WHERE sm.Period <= ?
{inWbs1}GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                    if (chunk != null) AddInListParameters(cmd, chunk);
                }
                else
                {
                    cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net,
    MAX(COALESCE(pr.Name,'')) AS ProjectName,
    MAX(COALESCE(pr.ClientID,'')) AS ClientID,
    MAX(COALESCE(cc.Name,'')) AS ClientName,
    MAX(COALESCE(pr.ProjMgr,'')) AS ProjMgr,
    MAX(COALESCE(em.FirstName,'')) AS PmFirstName,
    MAX(COALESCE(em.LastName,'')) AS PmLastName
FROM [{_catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{_catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
LEFT JOIN [{_catalog}].dbo.Clendor cc
  ON cc.ClientID = pr.ClientID
WHERE sm.Period <= ?
{inWbs1}GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                    if (chunk != null) AddInListParameters(cmd, chunk);
                }

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var bucket = GetTrimmed(r, 1);
                    var rawNet = GetDouble(r, 2);
                    var projectName = GetTrimmed(r, 3);
                    var clientId = GetTrimmed(r, 4);
                    var clientName = GetTrimmed(r, 5);
                    var projMgr = GetTrimmed(r, 6);
                    var pmFirst = GetTrimmed(r, 7);
                    var pmLast = GetTrimmed(r, 8);
                    var pm = BuildEmployeeDisplay(projMgr, pmFirst, pmLast);
                    var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                    var net = rawNet * fx;
                    var earned = Math.Max(net, 0.0);
                    var over = Math.Max(-net, 0.0);

                    rows[w] = new WipProjectBreakdownRow(
                        Wbs1: w,
                        Earned: earned,
                        Overbilled: over,
                        Net: net,
                        Period: period,
                        ProjectName: projectName,
                        ClientId: clientId,
                        ClientName: clientName,
                        Pm: pm);
                }
            }

            return rows.Values
                .Where(x => Math.Abs(x.Earned) > AnalyticsThresholds.RoundingDollarFloor || Math.Abs(x.Overbilled) > AnalyticsThresholds.RoundingDollarFloor || Math.Abs(x.Net) > AnalyticsThresholds.RoundingDollarFloor)
                .OrderByDescending(x => x.Overbilled)
                .ThenByDescending(x => x.Earned)
                .ToList();
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

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListParameters(OdbcCommand cmd, IEnumerable<string> values)
        {
            foreach (var v in values)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
            }
        }

        private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
                yield return source.Skip(i).Take(size).ToList();
        }
    }

    public sealed record WipProjectBreakdownRow(
        string Wbs1,
        double Earned,
        double Overbilled,
        double Net,
        string Period,
        string ProjectName = "",
        string ClientId = "",
        string ClientName = "",
        string Pm = "");

    public sealed record WipFinancialsResult(
        double WipUnbilled,
        double WipOverbilled,
        double WipUnbilledNet,
        string WipUnbilledPeriod,
        IReadOnlyList<WipProjectBreakdownRow> WipProjectRows,
        double FirmWipUnbilled,
        double FirmWipOverbilled,
        double FirmWipNet,
        bool RevenueGenerationDetected,
        bool DataLoaded)
    {
        public static readonly WipFinancialsResult Empty = new(
            0.0, 0.0, 0.0, "n/a",
            Array.Empty<WipProjectBreakdownRow>(),
            0.0, 0.0, 0.0,
            RevenueGenerationDetected: false,
            DataLoaded: false);
    }
}
