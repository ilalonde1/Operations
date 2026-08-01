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

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Canonical AR-position service for KOR. Owns the same SQL and
    /// currency-bucketing logic that the WPF AR tile uses, so MCP-side
    /// answers match the App by construction.
    /// </summary>
    public sealed class ArFinancialsService
    {
        private const int OdbcParameterChunkSize = 80;

        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly FinancialsOptions _financialsOptions;
        private readonly string _catalog;

        public ArFinancialsService(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _catalog = DeltekCatalogValidator.ResolveCatalog(odbcOptions.Catalog);
        }

        public ArFinancialsResult Load(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
            => Load(cn, wbs1, usdToCadRate, activeCaseInvoices: null, ct);

        // Phase 12-5a: optional `activeCaseInvoices` set (WBS1, InvoiceNumber pairs)
        // - when present, the result also reports OutstandingInCollections /
        // OutstandingRegular split. Existing callers that pass `null` get the
        // legacy behavior with both new fields zeroed.
        public ArFinancialsResult Load(
            OdbcConnection cn,
            List<string> wbs1,
            double usdToCadRate,
            IReadOnlySet<(string Wbs1, string Invoice)>? activeCaseInvoices,
            CancellationToken ct)
            => LoadCore(cn, wbs1, usdToCadRate, activeCaseInvoices, ct);

        public Task<ArFinancialsResult> LoadAsync(CancellationToken ct)
            => LoadAsync(activeCaseInvoices: null, ct);

        public async Task<ArFinancialsResult> LoadAsync(
            IReadOnlySet<(string Wbs1, string Invoice)>? activeCaseInvoices,
            CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cn = CreateConnection();
                cn.Open();
                var usdToCadRate = OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);
                return LoadCore(cn, new List<string>(), usdToCadRate, activeCaseInvoices, ct);
            }, ct).ConfigureAwait(false);
        }

        private ArFinancialsResult LoadCore(
            OdbcConnection cn,
            List<string> wbs1,
            double usdToCadRate,
            IReadOnlySet<(string Wbs1, string Invoice)>? activeCaseInvoices,
            CancellationToken ct)
        {
            // Drilldown rows are loaded FIRMWIDE (every WBS1 with open AR), not scoped to
            // the current snapshot's project list, so sum rows reconcile to the firmwide
            // AR Outstanding tile. wbs1 is kept on the signature for callers that want
            // a scoped subset, but the executive AR tile uses the firmwide breakdown.
            var result = LoadInvoiceArBalances(cn, wbs1: null, usdToCadRate, ct);
            var firmwide = LoadFirmwideArTotals(cn, usdToCadRate, ct);

            // Segregate by walking the firmwide invoice rows already loaded.
            // InvoiceRows are CAD-equivalent (FX applied per row), so summing
            // matched rows directly produces a CAD-equiv InCollections figure
            // that can be subtracted from FirmwideOutstandingCadEquiv to derive
            // Regular. Empty/null set means both fields zero and caller falls back to
            // the legacy headline.
            var inCollections = 0.0;
            if (activeCaseInvoices is { Count: > 0 })
            {
                foreach (var inv in result.InvoiceRows)
                {
                    if (string.IsNullOrWhiteSpace(inv.Wbs1) || string.IsNullOrWhiteSpace(inv.Invoice))
                    {
                        continue;
                    }

                    if (activeCaseInvoices.Contains((inv.Wbs1.Trim(), inv.Invoice.Trim())))
                    {
                        inCollections += inv.Balance;
                    }
                }
            }
            var regular = firmwide.OutstandingCadEquiv - inCollections;

            return new ArFinancialsResult(
                result.Outstanding,
                result.Over60,
                result.ProjectRows,
                result.InvoiceRows,
                firmwide.OutstandingCadEquiv,
                firmwide.Over60CadEquiv,
                firmwide.OutstandingCad,
                firmwide.OutstandingUsa,
                usdToCadRate,
                FirmwideOutstandingInCollectionsCadEquiv: inCollections,
                FirmwideOutstandingRegularCadEquiv: regular);
        }

        private (double OutstandingCadEquiv, double Over60CadEquiv, double OutstandingCad, double OutstandingUsa)
            LoadFirmwideArTotals(OdbcConnection cn, double usdToCadRate, CancellationToken ct)
        {
            var asOf = DateTime.Today.Date;
            // Bucket by Org (joined from PR via WBS1) so USA balances can be FX-converted
            // before being summed with CAD. Falls back to CAD bucket when Org is null/missing.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    Bucket,
    SUM(InvBalance)         AS Outstanding,
    SUM(InvBalanceOver60)   AS Over60
FROM (
    SELECT
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
        COALESCE(ar.InvBalanceSourceCurrency,0) AS InvBalance,
        CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END AS InvBalanceOver60
    FROM [{_catalog}].dbo.AR ar
    LEFT JOIN [{_catalog}].dbo.PR pr
      ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
    WHERE ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
) x
GROUP BY Bucket;";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });

            var cadOutstanding = 0.0;
            var usaOutstanding = 0.0;
            var cadOver60 = 0.0;
            var usaOver60 = 0.0;

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var bucket = GetTrimmed(r, 0);
                var outstanding = GetDouble(r, 1);
                var over60 = GetDouble(r, 2);
                if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                {
                    usaOutstanding += outstanding;
                    usaOver60 += over60;
                }
                else
                {
                    cadOutstanding += outstanding;
                    cadOver60 += over60;
                }
            }

            var totalCadEquiv = cadOutstanding + (usaOutstanding * usdToCadRate);
            var over60CadEquiv = cadOver60 + (usaOver60 * usdToCadRate);
            return (totalCadEquiv, over60CadEquiv, cadOutstanding, usaOutstanding);
        }

        // wbs1=null means firmwide (no WBS1 filter); pass a list to scope to specific projects.
        private (double Outstanding, double Over60, IReadOnlyList<ArProjectOutstandingRow> ProjectRows, IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows)
            LoadInvoiceArBalances(OdbcConnection cn, List<string>? wbs1, double usdToCadRate, CancellationToken ct)
        {
            var asOf = DateTime.Today.Date;
            var byWbs = new Dictionary<string, ArProjectOutstandingRow>(StringComparer.OrdinalIgnoreCase);
            var invoiceRows = new List<ArInvoiceOutstandingRow>(256);

            // Firmwide path runs one query with no WBS filter; scoped path chunks the wbs1 list.
            // A single sentinel chunk (null) drives the firmwide branch through the same loop.
            var chunks = (wbs1 == null || wbs1.Count == 0)
                ? new[] { (List<string>?)null }.AsEnumerable()
                : Chunk(wbs1, OdbcParameterChunkSize)
                    .Select(c => (List<string>?)c);

            foreach (var chunk in chunks)
            {
                var inWbs1 = chunk != null
                    ? $"WHERE ar.WBS1 IN ({MakeInListPlaceholders(chunk.Count)}) AND "
                    : "WHERE ";
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                // Group by WBS1 + Org so USA-org rows can be FX-converted to CAD-equivalent.
                // Without this, the scoped AR drilldown sums in source currency while the
                // firmwide AR headline (LoadFirmwideArTotals) is CAD-equiv - they'd diverge.
                cmd.CommandText = $@"
SELECT
    ar.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(ar.InvBalanceSourceCurrency,0)) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) <= 30
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS CurrentAmt,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 31 AND 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt31To60,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 61 AND 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt61To90,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt90Plus,
    MIN(COALESCE(ar.InvoiceDate, ar.DueDate)) AS OldestInvoiceDate,
    MAX(COALESCE(pr.Name,'')) AS ProjectName,
    MAX(COALESCE(pr.ProjMgr,'')) AS ProjMgr,
    MAX(COALESCE(em.FirstName,'')) AS PmFirstName,
    MAX(COALESCE(em.LastName,'')) AS PmLastName
FROM [{_catalog}].dbo.AR ar
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{_catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
GROUP BY ar.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                if (chunk != null) AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var wbs1Key = GetTrimmed(r, 0);
                    if (wbs1Key.Length == 0)
                        continue;

                    var bucket = GetTrimmed(r, 1);
                    var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;

                    var total = GetDouble(r, 2) * fx;
                    var current = GetDouble(r, 3) * fx;
                    var aged31 = GetDouble(r, 4) * fx;
                    var aged61 = GetDouble(r, 5) * fx;
                    var aged90 = GetDouble(r, 6) * fx;
                    var oldest = GetDateOrNull(r, 7);
                    var projectName = GetTrimmed(r, 8);
                    var pm = BuildEmployeeDisplay(
                        GetTrimmed(r, 9),
                        GetTrimmed(r, 10),
                        GetTrimmed(r, 11));

                    if (Math.Abs(total) < 0.005)
                        continue;

                    if (byWbs.TryGetValue(wbs1Key, out var existing))
                    {
                        byWbs[wbs1Key] = existing with
                        {
                            ProjectName = string.IsNullOrWhiteSpace(existing.ProjectName) ? projectName : existing.ProjectName,
                            Pm = string.IsNullOrWhiteSpace(existing.Pm) ? pm : existing.Pm,
                            Total = existing.Total + total,
                            Current = existing.Current + current,
                            Aged31To60 = existing.Aged31To60 + aged31,
                            Aged61To90 = existing.Aged61To90 + aged61,
                            Aged90Plus = existing.Aged90Plus + aged90,
                            OldestInvoiceDate = MergeOldest(existing.OldestInvoiceDate, oldest)
                        };
                    }
                    else
                    {
                        byWbs[wbs1Key] = new ArProjectOutstandingRow(
                            wbs1Key,
                            projectName,
                            pm,
                            total,
                            current,
                            aged31,
                            aged61,
                            aged90,
                            oldest);
                    }
                }

                using var cmdDetail = cn.CreateCommand();
                cmdDetail.CommandTimeout = SqlTimeouts.Batch;
                // Pull invoice number + client identity alongside the open balance so
                // the AR drilldown can answer "which Deltek invoice is this?" and
                // "who do I call?" without a second lookup. Clendor is Deltek's
                // combined client/vendor master keyed by ClientID.
                cmdDetail.CommandText = $@"
SELECT
    ar.WBS1,
    COALESCE(ar.Invoice,'') AS Invoice,
    COALESCE(ar.ClientID,'') AS ClientID,
    COALESCE(cc.Name,'') AS ClientName,
    ar.InvoiceDate,
    ar.DueDate,
    COALESCE(ar.InvBalanceSourceCurrency,0) AS OpenBalance,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    COALESCE(pr.Name,'') AS ProjectName,
    COALESCE(pr.ProjMgr,'') AS ProjMgr,
    COALESCE(em.FirstName,'') AS PmFirstName,
    COALESCE(em.LastName,'') AS PmLastName
FROM [{_catalog}].dbo.AR ar
LEFT JOIN [{_catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{_catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
LEFT JOIN [{_catalog}].dbo.Clendor cc
  ON cc.ClientID = ar.ClientID
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004;";
                if (chunk != null) AddInListParameters(cmdDetail, chunk);

                using var regDetail = ct.Register(() => { try { cmdDetail.Cancel(); } catch { } });
                using var rd = cmdDetail.ExecuteReader();
                while (rd.Read())
                {
                    var w = GetTrimmed(rd, 0);
                    if (w.Length == 0) continue;
                    var invoice = GetTrimmed(rd, 1);
                    var clientId = GetTrimmed(rd, 2);
                    var clientName = GetTrimmed(rd, 3);
                    var invoiceDate = GetDateOrNull(rd, 4);
                    var dueDate = GetDateOrNull(rd, 5);
                    var bal = GetDouble(rd, 6);
                    var bucket = GetTrimmed(rd, 7);
                    var projectName = GetTrimmed(rd, 8);
                    var pm = BuildEmployeeDisplay(
                        GetTrimmed(rd, 9),
                        GetTrimmed(rd, 10),
                        GetTrimmed(rd, 11));
                    var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                    var anchor = dueDate ?? invoiceDate;
                    var dpd = anchor.HasValue ? Math.Max(0, (int)(asOf - anchor.Value.Date).TotalDays) : 0;
                    invoiceRows.Add(new ArInvoiceOutstandingRow(
                        w, projectName, pm, invoice, clientId, clientName,
                        invoiceDate, dueDate, dpd, bal * fx));
                }
            }

            var rows = byWbs.Values
                .OrderByDescending(x => x.Aged90Plus)
                .ThenByDescending(x => x.Total)
                .ToList();
            var detail = invoiceRows
                .OrderByDescending(x => x.DaysPastDue)
                .ThenByDescending(x => Math.Abs(x.Balance))
                .ToList();
            var outstanding = rows.Sum(x => x.Total);
            var over60 = rows.Sum(x => x.Aged61To90 + x.Aged90Plus);
            return (outstanding, over60, rows, detail);
        }

        private OdbcConnection CreateConnection()
        {
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User, _odbcOptions.Password, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private static string BuildEmployeeDisplay(string employee, string first, string last)
        {
            var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return string.IsNullOrWhiteSpace(employee) ? name : $"{name} ({employee})";
            return employee ?? string.Empty;
        }

        private static string GetTrimmed(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? string.Empty : (Convert.ToString(r.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

        private static double GetDouble(OdbcDataReader r, int ordinal)
            => r.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(r.GetValue(ordinal), CultureInfo.InvariantCulture);

        private static DateTime? GetDateOrNull(OdbcDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return null;
            var value = r.GetValue(ordinal);
            if (value is DateTime dt) return dt.Date;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                ? parsed.Date
                : null;
        }

        private static DateTime? MergeOldest(DateTime? left, DateTime? right)
        {
            if (!left.HasValue) return right;
            if (!right.HasValue) return left;
            return left.Value <= right.Value ? left : right;
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

    public sealed record ArProjectOutstandingRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        double Total,
        double Current,
        double Aged31To60,
        double Aged61To90,
        double Aged90Plus,
        DateTime? OldestInvoiceDate);

    public sealed record ArInvoiceOutstandingRow(
        string Wbs1,
        string ProjectName,
        string Pm,
        string Invoice,
        string ClientId,
        string ClientName,
        DateTime? InvoiceDate,
        DateTime? DueDate,
        int DaysPastDue,
        double Balance);

    public sealed record ArFinancialsResult(
        double Outstanding,
        double Over60,
        IReadOnlyList<ArProjectOutstandingRow> ProjectRows,
        IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows,
        double FirmwideOutstandingCadEquiv,
        double FirmwideOver60CadEquiv,
        double FirmwideOutstandingCad,
        double FirmwideOutstandingUsa,
        double UsdToCadRate,
        double FirmwideOutstandingInCollectionsCadEquiv = 0.0,
        double FirmwideOutstandingRegularCadEquiv = 0.0)
    {
        public static readonly ArFinancialsResult Empty = new(
            0.0, 0.0,
            Array.Empty<ArProjectOutstandingRow>(),
            Array.Empty<ArInvoiceOutstandingRow>(),
            0.0, 0.0, 0.0, 0.0, 1.36, 0.0, 0.0);
    }
}
