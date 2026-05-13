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
    /// Canonical 30-day utilization service. Owns the same billable predicate
    /// and project drilldown that the WPF Utilization tile uses.
    /// </summary>
    public sealed class UtilizationService
    {
        private const int OdbcParameterChunkSize = 80;

        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public UtilizationService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public UtilizationResult Load(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
            => LoadCore(cn, wbs1, ct);

        public async Task<UtilizationResult> LoadAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _ = _financialsOptions; // Stored for constructor symmetry with the other Business financial services.
                using var cn = CreateConnection();
                cn.Open();
                return LoadCore(cn, new List<string>(), ct);
            }, ct).ConfigureAwait(false);
        }

        private UtilizationResult LoadCore(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            UtilAgg util30;
            try { util30 = LoadUtilization30Firmwide(cn, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load utilization totals in {Loader}.", nameof(UtilizationService));
                return UtilizationResult.Empty;
            }

            IReadOnlyList<UtilizationProjectRow> utilByProject;
            try { utilByProject = LoadUtilization30ProjectRows(cn, wbs1: null, ct); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load utilization rows by project in {Loader}.", nameof(UtilizationService));
                utilByProject = Array.Empty<UtilizationProjectRow>();
            }

            return new UtilizationResult(util30.BillableHours, util30.TotalHours, utilByProject);
        }

        private sealed record UtilAgg(double BillableHours, double TotalHours)
        {
            public double Pct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
        }

        // Headline utilization is FIRMWIDE: every active employee's hours, with no scope/WBS1
        // filter, so PTO/holiday/admin land in the denominator. Billable matches Staff Util
        // (LaborCode NOT IN Admin/NonBill, WBS not in overhead prefixes); Staff Util's
        // BillablePct column averages to this number, so the two surfaces tie out.
        private UtilAgg LoadUtilization30Firmwide(OdbcConnection cn, CancellationToken ct)
        {
            var start = DateTime.Today.AddDays(-30);

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    SUM(COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80) -- 70=Admin, 80=NonBillable per Deltek tkDetail.LaborCode
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHours
FROM [{_catalog}].dbo.tkDetail t
LEFT JOIN [{_catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
WHERE t.TransDate >= ?
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R';";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var total = GetDouble(r, 0);
                var billable = GetDouble(r, 1);
                return new UtilAgg(billable, total);
            }
            return new UtilAgg(0, 0);
        }

        private List<UtilizationProjectRow> LoadUtilization30ProjectRows(OdbcConnection cn, List<string>? wbs1, CancellationToken ct)
        {
            var start = DateTime.Today.AddDays(-30);
            var byWbs = new Dictionary<string, UtilizationProjectRow>(StringComparer.OrdinalIgnoreCase);

            var chunks = (wbs1 == null || wbs1.Count == 0)
                ? new[] { (List<string>?)null }.AsEnumerable()
                : Chunk(wbs1, OdbcParameterChunkSize)
                    .Select(c => (List<string>?)c);

            foreach (var chunk in chunks)
            {
                var inWbs1 = chunk != null
                    ? $"  AND t.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})\n"
                    : string.Empty;
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                // Per-project rows must use the SAME billable definition as the firmwide
                // headline (LaborCode-based with overhead WBS exclusions) so summing the
                // drilldown reproduces the headline ratio. Passing null/empty WBS1 runs
                // firmwide with no WBS1 IN clause, matching the headline.
                cmd.CommandText = $@"
SELECT
    t.WBS1,
    SUM(COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN t.LaborCode NOT IN (70, 80) -- 70=Admin, 80=NonBillable per Deltek tkDetail.LaborCode
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHours
FROM [{_catalog}].dbo.tkDetail t
LEFT JOIN [{_catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
WHERE t.TransDate >= ?
{inWbs1}  AND t.WBS1 IS NOT NULL
  AND LTRIM(RTRIM(t.WBS1)) <> ''
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.WBS1;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
                if (chunk != null) AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var total = GetDouble(r, 1);
                    var billable = GetDouble(r, 2);
                    if (Math.Abs(total) < AnalyticsThresholds.RoundingDollarFloor && Math.Abs(billable) < AnalyticsThresholds.RoundingDollarFloor) continue;

                    if (byWbs.TryGetValue(w, out var existing))
                    {
                        byWbs[w] = existing with
                        {
                            BillableHours = existing.BillableHours + billable,
                            TotalHours = existing.TotalHours + total
                        };
                    }
                    else
                    {
                        byWbs[w] = new UtilizationProjectRow(
                            Wbs1: w,
                            BillableHours: billable,
                            TotalHours: total);
                    }
                }
            }

            return byWbs.Values
                .Where(x => x.TotalHours > AnalyticsThresholds.RoundingDollarFloor)
                .OrderByDescending(x => x.UtilizationPct)
                .ThenByDescending(x => x.TotalHours)
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

    public sealed record UtilizationProjectRow(
        string Wbs1,
        double BillableHours,
        double TotalHours)
    {
        public double NonBillableHours => Math.Max(0.0, TotalHours - BillableHours);
        public double UtilizationPct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
    }

    public sealed record UtilizationResult(
        double BillableHours,
        double TotalHours,
        IReadOnlyList<UtilizationProjectRow> ProjectRows)
    {
        public double Pct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);

        public static readonly UtilizationResult Empty = new(0.0, 0.0, Array.Empty<UtilizationProjectRow>());
    }
}
