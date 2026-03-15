#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
namespace Kor.Operations.Financials
{
    public sealed record ExecutiveSummaryDeltekData(
        double CashTotal,
        double CashCad,
        double CashUsa,
        double CashBcc,
        string CashPeriod,
        IReadOnlyList<CashHistoryPoint> CashHistory,
        double UtilizationPct30,
        double UtilizationBillableHours30,
        double UtilizationTotalHours30,
        IReadOnlyList<UtilizationProjectRow> UtilizationProjectRows,
        double ArOutstanding,
        double ArOver60,
        IReadOnlyList<ArProjectOutstandingRow> ArProjectRows,
        IReadOnlyList<ArInvoiceOutstandingRow> ArInvoiceRows,
        // Watchlist WIP (Unbilled Earned) is gross contract asset (sum of positive Unbilled or proxy).
        double WipUnbilled,
        double WipOverbilled,
        double WipUnbilledNet,
        string WipUnbilledPeriod,
        IReadOnlyList<WipProjectBreakdownRow> WipProjectRows,
        // Firmwide WIP (same as-of period, proxy when Unbilled not populated).
        double FirmWipUnbilled,
        double FirmWipOverbilled,
        double FirmWipNet,
        double WipPreInvoice,
        double Revenue30,
        double Revenue90,
        double Billed30,
        double Billed90,
        IReadOnlyList<TrendPayerAmountRow> RevenuePayerRows,
        IReadOnlyList<TrendPayerAmountRow> BilledPayerRows,
        IReadOnlyList<TrendPayerAmountRow> ArPayerRows,
        double[] RevenueSeries,
        double[] BilledSeries,
        double[] ArSeries);

    public sealed record TrendPayerAmountRow(
        string Wbs1,
        string PayerName,
        double Amount);

    public sealed record CashHistoryPoint(
        string Period,
        double Cad,
        double Usa,
        double Bcc)
    {
        public double Total => Cad + Usa + Bcc;
    }

    public sealed record ArProjectOutstandingRow(
        string Wbs1,
        double Total,
        double Current,
        double Aged31To60,
        double Aged61To90,
        double Aged90Plus,
        DateTime? OldestInvoiceDate);

    public sealed record ArInvoiceOutstandingRow(
        string Wbs1,
        DateTime? InvoiceDate,
        DateTime? DueDate,
        int DaysPastDue,
        double Balance);

    public sealed record WipProjectBreakdownRow(
        string Wbs1,
        double Earned,
        double Overbilled,
        double Net,
        string Period);

    public sealed record UtilizationProjectRow(
        string Wbs1,
        double BillableHours,
        double TotalHours)
    {
        public double NonBillableHours => Math.Max(0.0, TotalHours - BillableHours);
        public double UtilizationPct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
    }

    public sealed class ExecutiveSummaryDeltekLoader
    {
        private const string Catalog = "C0000052267P_1_KOR00000000";
        private readonly DeltekOdbcOptions _odbcOptions;

        public ExecutiveSummaryDeltekLoader(DeltekOdbcOptions odbcOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
        }

        public Task<ExecutiveSummaryDeltekData?> TryLoadAsync(IEnumerable<string> wbs1List, CancellationToken ct)
        {
            var wbs1 = (wbs1List ?? Array.Empty<string>())
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (wbs1.Count == 0)
                return Task.FromResult<ExecutiveSummaryDeltekData?>(null);

            // Keep ODBC work off the UI thread.
            return Task.Run<ExecutiveSummaryDeltekData?>(() => LoadImpl(wbs1, ct), ct);
        }

        private OdbcConnection CreateVpConnection()
        {
            // App.config keys:
            // - Vp.Dsn (default: Deltek)
            // - Vp.User / Vp.Password (optional)
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var user = _odbcOptions.User ?? string.Empty;
            var pwd = _odbcOptions.Password ?? string.Empty;
            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private ExecutiveSummaryDeltekData LoadImpl(List<string> wbs1, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var dsnUsed = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;

            using var cn = CreateVpConnection();
            cn.Open();

            Dictionary<string, CalRow> calendar;
            try { calendar = TryLoadCalendar(cn, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Calendar failed: " + ex.GetType().Name + ": " + ex.Message);
                calendar = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, PrAgg> prByPeriod;
            try { prByPeriod = LoadPrSummaryByPeriod(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader PRSummaryMain failed: " + ex.GetType().Name + ": " + ex.Message);
                prByPeriod = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);
            }

            BuiltSeries series;
            try { series = BuildSeries(prByPeriod, calendar, points: 12); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Series failed: " + ex.GetType().Name + ": " + ex.Message);
                series = new BuiltSeries(new List<SeriesPeriod>(), 0.0, 0.0, 0.0, "n/a");
            }

            var cutoff30 = DateTime.Today.AddDays(-30);
            var cutoff90 = DateTime.Today.AddDays(-90);

            var revenue30 = series.Periods.Where(p => p.EndDate >= cutoff30).Sum(p => p.Revenue);
            var revenue90 = series.Periods.Where(p => p.EndDate >= cutoff90).Sum(p => p.Revenue);
            var billed30 = series.Periods.Where(p => p.EndDate >= cutoff30).Sum(p => p.Billed);
            var billed90 = series.Periods.Where(p => p.EndDate >= cutoff90).Sum(p => p.Billed);
            var recentPeriods = series.Periods
                .Where(p => p.EndDate >= cutoff90)
                .Select(p => (p.Period ?? string.Empty).Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

                        var unbilledColumnHasAny = prByPeriod.Values.Any(p =>
                Math.Abs(p.UnbilledNet) > 1e-9 ||
                Math.Abs(p.UnbilledEarned) > 1e-9 ||
                Math.Abs(p.Overbilled) > 1e-9);
            var wipUnbilled = series.LatestUnbilledEarned;
            var wipOverbilled = series.LatestOverbilled;
            var wipUnbilledNet = series.LatestUnbilledNet;
            var wipUnbilledPeriod = series.LatestPeriod;
            IReadOnlyList<WipProjectBreakdownRow> wipProjectRows = new List<WipProjectBreakdownRow>();
            if (!unbilledColumnHasAny && !string.IsNullOrWhiteSpace(wipUnbilledPeriod) && wipUnbilledPeriod.Length == 6)
            {
                try
                {
                    var wipProxy = LoadWipProxyBalanceByProject(cn, wbs1, wipUnbilledPeriod, ct);
                    wipUnbilled = wipProxy.Earned;
                    wipOverbilled = wipProxy.Overbilled;
                    wipUnbilledNet = wipProxy.Net;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ExecutiveSummaryDeltekLoader Watchlist WIP proxy failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            try
            {
                wipProjectRows = LoadWipProjectBreakdownByProject(cn, wbs1, wipUnbilledPeriod, useUnbilledAsOf: unbilledColumnHasAny, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader WIP project breakdown failed: " + ex.GetType().Name + ": " + ex.Message);
                wipProjectRows = new List<WipProjectBreakdownRow>();
            }

            // Firmwide WIP: uses same as-of closed period for side-by-side context.
            (double Earned, double Overbilled, double Net) firmWip;
            try { firmWip = LoadFirmwideWipProxyBalance(cn, wipUnbilledPeriod, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Firmwide WIP failed: " + ex.GetType().Name + ": " + ex.Message);
                firmWip = (0.0, 0.0, 0.0);
            }

            double wipPreInvoice;
            try { wipPreInvoice = LoadPreInvoiceWip(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader ARPreInvoice failed: " + ex.GetType().Name + ": " + ex.Message);
                wipPreInvoice = 0.0;
            }

            CashBalances cash;
            try { cash = LoadCashBalances(cn, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Cash failed: " + ex.GetType().Name + ": " + ex.Message);
                cash = new CashBalances("n/a", 0, 0, 0, new List<CashHistoryPoint>());
            }

            UtilAgg util30;
            IReadOnlyList<UtilizationProjectRow> utilByProject;
            try { util30 = LoadUtilization30(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Utilization failed: " + ex.GetType().Name + ": " + ex.Message);
                util30 = new UtilAgg(0, 0);
                utilByProject = new List<UtilizationProjectRow>();
            }
            try { utilByProject = LoadUtilization30ProjectRows(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader Utilization by project failed: " + ex.GetType().Name + ": " + ex.Message);
                utilByProject = new List<UtilizationProjectRow>();
            }

            (double Outstanding, double Over60, IReadOnlyList<ArProjectOutstandingRow> ProjectRows, IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows) ar;
            try { ar = LoadInvoiceArBalances(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader AR failed: " + ex.GetType().Name + ": " + ex.Message);
                ar = (0.0, 0.0, new List<ArProjectOutstandingRow>(), new List<ArInvoiceOutstandingRow>());
            }

            Dictionary<string, string> payerByWbs;
            try { payerByWbs = LoadPayerByWbs1(cn, wbs1, ct); }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader payer mapping failed: " + ex.GetType().Name + ": " + ex.Message);
                payerByWbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            IReadOnlyList<TrendPayerAmountRow> revenuePayerRows;
            IReadOnlyList<TrendPayerAmountRow> billedPayerRows;
            try
            {
                revenuePayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Revenue", payerByWbs, ct);
                billedPayerRows = LoadPrAmountByProjectForPeriods(cn, wbs1, recentPeriods, amountField: "Billed", payerByWbs, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader revenue/billing payer breakdown failed: " + ex.GetType().Name + ": " + ex.Message);
                revenuePayerRows = new List<TrendPayerAmountRow>();
                billedPayerRows = new List<TrendPayerAmountRow>();
            }

            IReadOnlyList<TrendPayerAmountRow> arPayerRows;
            try
            {
                arPayerRows = ar.ProjectRows
                    .Where(r => Math.Abs(r.Total) > 0.004)
                    .Select(r =>
                    {
                        var key = (r.Wbs1 ?? string.Empty).Trim();
                        payerByWbs.TryGetValue(key, out var payer);
                        payer = string.IsNullOrWhiteSpace(payer) ? key : payer.Trim();
                        return new TrendPayerAmountRow(key, payer, r.Total);
                    })
                    .OrderByDescending(r => r.Amount)
                    .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                    .Take(250)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader AR payer breakdown failed: " + ex.GetType().Name + ": " + ex.Message);
                arPayerRows = new List<TrendPayerAmountRow>();
            }

            return new ExecutiveSummaryDeltekData(
                CashTotal: cash.Total,
                CashCad: cash.Cad,
                CashUsa: cash.Usa,
                CashBcc: cash.Bcc,
                CashPeriod: cash.Period,
                CashHistory: cash.History,
                UtilizationPct30: util30.Pct,
                UtilizationBillableHours30: util30.BillableHours,
                UtilizationTotalHours30: util30.TotalHours,
                UtilizationProjectRows: utilByProject,
                ArOutstanding: ar.Outstanding,
                ArOver60: ar.Over60,
                ArProjectRows: ar.ProjectRows,
                ArInvoiceRows: ar.InvoiceRows,
                WipUnbilled: wipUnbilled,
                WipOverbilled: wipOverbilled,
                WipUnbilledNet: wipUnbilledNet,
                WipUnbilledPeriod: wipUnbilledPeriod,
                WipProjectRows: wipProjectRows,
                FirmWipUnbilled: firmWip.Earned,
                FirmWipOverbilled: firmWip.Overbilled,
                FirmWipNet: firmWip.Net,
                WipPreInvoice: wipPreInvoice,
                Revenue30: revenue30,
                Revenue90: revenue90,
                Billed30: billed30,
                Billed90: billed90,
                RevenuePayerRows: revenuePayerRows,
                BilledPayerRows: billedPayerRows,
                ArPayerRows: arPayerRows,
                RevenueSeries: series.Periods.Select(p => p.Revenue).ToArray(),
                BilledSeries: series.Periods.Select(p => p.Billed).ToArray(),
                ArSeries: series.Periods.Select(p => p.Ar).ToArray());
        }

        private sealed record CalRow(string Period, DateTime StartDate, DateTime EndDate);

        private static Dictionary<string, CalRow> TryLoadCalendar(OdbcConnection cn, CancellationToken ct)
        {
            // In your environment CFGAcctngCalendarData may be empty; we fallback to YYYYMM month-end.
            var map = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
SELECT Period, StartDate, EndDate
FROM [{Catalog}].dbo.CFGAcctngCalendarData
ORDER BY Period;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = GetTrimmed(r, 0);
                if (period.Length == 0) continue;

                var start = GetDate(r, 1);
                var end = GetDate(r, 2);
                if (start == DateTime.MinValue || end == DateTime.MinValue) continue;

                map[period] = new CalRow(period, start, end);
            }

            return map;
        }

        private sealed record PrAgg(string Period, double Revenue, double Billed, double Ar, double UnbilledNet, double UnbilledEarned, double Overbilled);

        private static Dictionary<string, PrAgg> LoadPrSummaryByPeriod(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            var acc = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT Period,
       SUM(COALESCE(Revenue,0))  AS Revenue,
       SUM(COALESCE(Billed,0))   AS Billed,
       SUM(COALESCE(AR,0))       AS AR,
       SUM(COALESCE(Unbilled,0)) AS UnbilledNet,
       SUM(CASE WHEN COALESCE(Unbilled,0) > 0 THEN COALESCE(Unbilled,0) ELSE 0 END) AS UnbilledEarned,
       SUM(CASE WHEN COALESCE(Unbilled,0) < 0 THEN -COALESCE(Unbilled,0) ELSE 0 END) AS Overbilled
FROM [{Catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY Period;";
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();

                    var period = GetTrimmed(r, 0);
                    if (period.Length == 0) continue;

                    var rev = GetDouble(r, 1);
                    var billed = GetDouble(r, 2);
                    var ar = GetDouble(r, 3);
                    var unbilledNet = GetDouble(r, 4);
                    var unbilledEarned = GetDouble(r, 5);
                    var overbilled = GetDouble(r, 6);

                    if (acc.TryGetValue(period, out var existing))
                    {
                        acc[period] = existing with
                        {
                            Revenue = existing.Revenue + rev,
                            Billed = existing.Billed + billed,
                            Ar = existing.Ar + ar,
                            UnbilledNet = existing.UnbilledNet + unbilledNet,
                            UnbilledEarned = existing.UnbilledEarned + unbilledEarned,
                            Overbilled = existing.Overbilled + overbilled
                        };
                    }
                    else
                    {
                        acc[period] = new PrAgg(period, rev, billed, ar, unbilledNet, unbilledEarned, overbilled);
                    }
                }
            }

            return acc;
        }

        private static List<TrendPayerAmountRow> LoadPrAmountByProjectForPeriods(
            OdbcConnection cn,
            List<string> wbs1,
            List<string> periods,
            string amountField,
            IReadOnlyDictionary<string, string> payerByWbs,
            CancellationToken ct)
        {
            if (wbs1.Count == 0 || periods.Count == 0)
                return new List<TrendPayerAmountRow>();

            var field = string.Equals(amountField, "Billed", StringComparison.OrdinalIgnoreCase) ? "Billed" : "Revenue";
            var byWbs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;

                var periodPlaceholders = MakeInListPlaceholders(periods.Count);
                cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE({field},0)) AS Amount
FROM [{Catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND Period IN ({periodPlaceholders})
GROUP BY WBS1;";

                AddInListParameters(cmd, chunk);
                foreach (var period in periods)
                {
                    cmd.Parameters.Add(new OdbcParameter
                    {
                        OdbcType = OdbcType.VarChar,
                        Value = period
                    });
                }

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var amt = GetDouble(r, 1);
                    if (Math.Abs(amt) < 0.004) continue;

                    if (byWbs.TryGetValue(w, out var existing))
                        byWbs[w] = existing + amt;
                    else
                        byWbs[w] = amt;
                }
            }

            return byWbs
                .Where(kvp => Math.Abs(kvp.Value) > 0.004)
                .Select(kvp =>
                {
                    payerByWbs.TryGetValue(kvp.Key, out var payer);
                    payer = string.IsNullOrWhiteSpace(payer) ? kvp.Key : payer.Trim();
                    return new TrendPayerAmountRow(kvp.Key, payer, kvp.Value);
                })
                .OrderByDescending(r => r.Amount)
                .ThenBy(r => r.PayerName, StringComparer.OrdinalIgnoreCase)
                .Take(250)
                .ToList();
        }

        private static Dictionary<string, string> LoadPayerByWbs1(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (wbs1.Count == 0)
                return map;

            bool clientColumnsWorked = false;
            try
            {
                foreach (var chunk in Chunk(wbs1, 80))
                {
                    using var cmd = cn.CreateCommand();
                    cmd.CommandTimeout = 120;
                    cmd.CommandText = $@"
SELECT
    WBS1,
    COALESCE(NULLIF(LTRIM(RTRIM(ClientName)),''), NULLIF(LTRIM(RTRIM(ClientID)),''), NULLIF(LTRIM(RTRIM(Name)),''), WBS1) AS PayerName
FROM [{Catalog}].dbo.PR
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '');";
                    AddInListParameters(cmd, chunk);

                    using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        ct.ThrowIfCancellationRequested();
                        var w = GetTrimmed(r, 0);
                        if (w.Length == 0) continue;
                        var payer = GetTrimmed(r, 1);
                        map[w] = payer.Length == 0 ? w : payer;
                    }
                }

                clientColumnsWorked = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecutiveSummaryDeltekLoader payer query (ClientName/ClientID) failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (clientColumnsWorked)
                return map;

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 120;
                cmd.CommandText = $@"
SELECT
    WBS1,
    COALESCE(NULLIF(LTRIM(RTRIM(Name)),''), WBS1) AS PayerName
FROM [{Catalog}].dbo.PR
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '');";
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var payer = GetTrimmed(r, 1);
                    map[w] = payer.Length == 0 ? w : payer;
                }
            }

            return map;
        }

        private sealed record SeriesPeriod(string Period, DateTime EndDate, double Revenue, double Billed, double Ar, double UnbilledNet, double UnbilledEarned, double Overbilled);
        private sealed record BuiltSeries(List<SeriesPeriod> Periods, double LatestUnbilledEarned, double LatestOverbilled, double LatestUnbilledNet, string LatestPeriod);

        private static BuiltSeries BuildSeries(Dictionary<string, PrAgg> prByPeriod, Dictionary<string, CalRow> cal, int points)
        {
            // In some environments PRSummaryMain.Unbilled is not populated (all zeros).
            // When that's true, we derive a proxy unbilled activity as (Revenue - Billed) per period,
            // and compute a balance proxy as the cumulative sum through the latest closed period.
            var unbilledColumnHasAny =
                prByPeriod.Values.Any(p =>
                    Math.Abs(p.UnbilledNet) > 1e-9 ||
                    Math.Abs(p.UnbilledEarned) > 1e-9 ||
                    Math.Abs(p.Overbilled) > 1e-9);

            DateTime PeriodEnd(string period)
            {
                if (cal.TryGetValue(period, out var c)) return c.EndDate.Date;

                // Fallback: treat YYYYMM as month end.
                if (period.Length == 6 &&
                    int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                    int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                    m >= 1 && m <= 12)
                {
                    return new DateTime(y, m, 1).AddMonths(1).AddDays(-1);
                }

                return DateTime.MinValue;
            }

            var list = prByPeriod.Values
                .Select(p =>
                {
                    var unbilledNet = p.UnbilledNet;
                    var unbilledEarned = p.UnbilledEarned;
                    var overbilled = p.Overbilled;

                    // Some environments do not populate PRSummaryMain.Unbilled; derive a usable proxy.
                    // This proxy is period activity, not a true balance.
                    if (!unbilledColumnHasAny &&
                        Math.Abs(unbilledNet) < 1e-9 && Math.Abs(unbilledEarned) < 1e-9 && Math.Abs(overbilled) < 1e-9 &&
                        (Math.Abs(p.Revenue) > 1e-9 || Math.Abs(p.Billed) > 1e-9))
                    {
                        var proxy = p.Revenue - p.Billed;
                        unbilledNet = proxy;
                        if (proxy > 0) unbilledEarned = proxy;
                        else if (proxy < 0) overbilled = -proxy;
                    }

                    return new SeriesPeriod(p.Period, PeriodEnd(p.Period), p.Revenue, p.Billed, p.Ar, unbilledNet, unbilledEarned, overbilled);
                })
                .Where(p => p.EndDate != DateTime.MinValue)
                .OrderBy(p => p.EndDate)
                .ToList();

            // Prefer closed periods (period end <= today).
            var closed = list.Where(p => p.EndDate <= DateTime.Today).ToList();
            var use = closed.Count > 0 ? closed : list;

            // Keep charts compact.
            if (use.Count > points)
                use = use.TakeLast(points).ToList();

            if (!unbilledColumnHasAny)
            {
                // Balance proxy: cumulative (Revenue - Billed) through the latest closed period.
                var closedAll = closed.Count > 0 ? closed : list;
                var latestPeriod = closedAll.Count == 0 ? "n/a" : closedAll[^1].Period;
                var balanceNet = closedAll.Sum(p => p.UnbilledNet);
                var balanceEarned = Math.Max(balanceNet, 0.0);
                var balanceOverbilled = Math.Max(-balanceNet, 0.0);
                return new BuiltSeries(use, balanceEarned, balanceOverbilled, balanceNet, latestPeriod);
            }

            // True Unbilled column case: use the latest closed period values.
            var latestClosed = use.Count == 0 ? null : use[^1];
            var latestEarned = latestClosed == null ? 0.0 : latestClosed.UnbilledEarned;
            var latestOverbilled = latestClosed == null ? 0.0 : latestClosed.Overbilled;
            var latestNet = latestClosed == null ? 0.0 : latestClosed.UnbilledNet;
            var latestPeriodOut = latestClosed == null ? "n/a" : latestClosed.Period;
            return new BuiltSeries(use, latestEarned, latestOverbilled, latestNet, latestPeriodOut);
        }

        private static double LoadPreInvoiceWip(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            // Draft/pre-invoice WIP (invoicing pipeline): open pre-invoice detail amount not applied to an invoice.
            // Note: Deltek columns may be string/bit/int depending on config; CAST keeps the filters stable.
            double total = 0.0;
            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT
    SUM(CASE
            WHEN (COALESCE(d.Amount,0) - COALESCE(d.PaidAmount,0)) < 0 THEN 0
            ELSE (COALESCE(d.Amount,0) - COALESCE(d.PaidAmount,0))
        END) AS DraftWip
FROM [{Catalog}].dbo.ARPreInvoice h
JOIN [{Catalog}].dbo.ARPreInvoiceDetail d
  ON d.PreInvoice = h.PreInvoice
WHERE h.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND ISNULL(LTRIM(RTRIM(CAST(h.Cancelled AS varchar(10)))),''0'') NOT IN (''1'',''Y'',''YES'',''TRUE'')
  AND ISNULL(LTRIM(RTRIM(CAST(h.AppliedInvoice AS varchar(50)))),'''') = '''';";
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                var v = cmd.ExecuteScalar();
                total += ScalarToDouble(v);
            }
            return total;
        }

        private sealed record BankAcct(string Company, string Account, string Org);
        private sealed record CashBalances(
            string Period,
            double Cad,
            double Usa,
            double Bcc,
            IReadOnlyList<CashHistoryPoint> History)
        {
            public double Total => Cad + Usa + Bcc;
        }

        private static CashBalances LoadCashBalances(OdbcConnection cn, CancellationToken ct)
        {
            // Best-effort cash position from bank account GL balances.
            // We compute ending balance by summing GLSummary.Amount across all periods <= targetPeriod
            // for the cash/bank accounts listed in CFGBanks.

            var banks = LoadBankAccounts(cn, ct);
            if (banks.Count == 0)
                return new CashBalances("n/a", 0, 0, 0, new List<CashHistoryPoint>());

            var todayPeriod = DateTime.Today.ToString("yyyyMM", CultureInfo.InvariantCulture);
            var targetPeriod = FindLatestGlPeriodForAccounts(cn, banks, todayPeriod, ct);
            if (string.IsNullOrWhiteSpace(targetPeriod))
                targetPeriod = todayPeriod;

            var history = LoadCashHistory(cn, banks, targetPeriod, ct);
            if (history.Count == 0)
                return new CashBalances(targetPeriod, 0, 0, 0, history);

            var latest = history[^1];
            return new CashBalances(latest.Period, latest.Cad, latest.Usa, latest.Bcc, history);
        }

        private static List<CashHistoryPoint> LoadCashHistory(OdbcConnection cn, List<BankAcct> banks, string targetPeriod, CancellationToken ct)
        {
            var byPeriod = new Dictionary<string, (double Cad, double Usa, double Bcc)>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(banks, 25))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;

                var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));

                cmd.CommandText = $@"
SELECT Period, Account, Org, SUM(COALESCE(Amount,0)) AS Amt
FROM [{Catalog}].dbo.GLSummary
WHERE Period <= ?
  AND ({clauses})
GROUP BY Period, Account, Org;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = targetPeriod });
                foreach (var b in chunk)
                {
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
                }

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();

                    var period = GetTrimmed(r, 0);
                    if (period.Length != 6 || !period.All(char.IsDigit))
                        continue;

                    var acct = GetTrimmed(r, 1);
                    var org = GetTrimmed(r, 2);
                    var amt = GetDouble(r, 3);

                    var match = chunk.FirstOrDefault(x =>
                        string.Equals(x.Account, acct, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Org, org, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                        continue;

                    byPeriod.TryGetValue(period, out var cur);
                    switch ((match.Company ?? string.Empty).Trim().ToUpperInvariant())
                    {
                        case "USA":
                            cur.Usa += amt;
                            break;
                        case "BCC":
                            cur.Bcc += amt;
                            break;
                        case "CAD":
                        default:
                            cur.Cad += amt;
                            break;
                    }
                    byPeriod[period] = cur;
                }
            }

            var ordered = byPeriod.Keys
                .Where(p => p.Length == 6 && p.All(char.IsDigit))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
                return new List<CashHistoryPoint>();

            var cumulative = new List<CashHistoryPoint>(ordered.Count);
            double runCad = 0.0, runUsa = 0.0, runBcc = 0.0;
            foreach (var period in ordered)
            {
                var p = byPeriod[period];
                runCad += p.Cad;
                runUsa += p.Usa;
                runBcc += p.Bcc;
                cumulative.Add(new CashHistoryPoint(period, runCad, runUsa, runBcc));
            }

            if (cumulative.Count > 12)
                cumulative = cumulative.TakeLast(12).ToList();

            return cumulative;
        }

        private static List<BankAcct> LoadBankAccounts(OdbcConnection cn, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
SELECT Company, Account, Org
FROM [{Catalog}].dbo.CFGBanks
WHERE COALESCE(Account,'') <> '';";

            var list = new List<BankAcct>(64);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var company = GetTrimmed(r, 0);
                var account = GetTrimmed(r, 1);
                var org = GetTrimmed(r, 2);

                if (company.Length == 0 || account.Length == 0)
                    continue;

                // Org is frequently NULL in CFGBanks; GLSummary commonly uses the company code as org.
                if (string.IsNullOrWhiteSpace(org))
                    org = company;

                list.Add(new BankAcct(company, account, org));
            }

            return list
                .GroupBy(b => string.Join("|", b.Company, b.Account, b.Org), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static string FindLatestGlPeriodForAccounts(OdbcConnection cn, List<BankAcct> accts, string todayPeriod, CancellationToken ct)
        {
            string latest = string.Empty;

            foreach (var chunk in Chunk(accts, 40))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 120;

                var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));
                cmd.CommandText = $@"
SELECT MAX(Period) AS MaxPeriod
FROM [{Catalog}].dbo.GLSummary
WHERE Period <= ? AND ({clauses});";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = todayPeriod });
                foreach (var b in chunk)
                {
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
                }

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                var v = cmd.ExecuteScalar();
                var p = (v == null || v == DBNull.Value) ? string.Empty : (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
                p = p.Trim();

                if (p.Length > 0 && string.CompareOrdinal(p, latest) > 0)
                    latest = p;
            }

            return latest;
        }

        private sealed record UtilAgg(double BillableHours, double TotalHours)
        {
            public double Pct => TotalHours <= 0.0 ? 0.0 : (BillableHours / TotalHours);
        }

        private static UtilAgg LoadUtilization30(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            // Definition (matches current UI copy): billable hours / total charged hours over the last 30 days.
            // Billable is approximated by BillExt > 0.

            var start = DateTime.Today.AddDays(-30);

            double billable = 0.0;
            double total = 0.0;

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT
    SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN COALESCE(BillExt,0) > 0 THEN (COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) ELSE 0 END) AS BillableHours
FROM [{Catalog}].dbo.tkDetail
WHERE TransDate >= ?
  AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND (COALESCE(LineItemApprovalStatus,'') = '' OR COALESCE(LineItemApprovalStatus,'') = 'A');";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    total += GetDouble(r, 0);
                    billable += GetDouble(r, 1);
                }
            }

            return new UtilAgg(billable, total);
        }

        private static List<UtilizationProjectRow> LoadUtilization30ProjectRows(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            var start = DateTime.Today.AddDays(-30);
            var byWbs = new Dictionary<string, UtilizationProjectRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN COALESCE(BillExt,0) > 0 THEN (COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) ELSE 0 END) AS BillableHours
FROM [{Catalog}].dbo.tkDetail
WHERE TransDate >= ?
  AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND (COALESCE(LineItemApprovalStatus,'') = '' OR COALESCE(LineItemApprovalStatus,'') = 'A')
GROUP BY WBS1;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var total = GetDouble(r, 1);
                    var billable = GetDouble(r, 2);
                    if (Math.Abs(total) < 0.004 && Math.Abs(billable) < 0.004) continue;

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
                .Where(x => x.TotalHours > 0.004)
                .OrderByDescending(x => x.UtilizationPct)
                .ThenByDescending(x => x.TotalHours)
                .ToList();
        }

        private static (double Outstanding, double Over60, IReadOnlyList<ArProjectOutstandingRow> ProjectRows, IReadOnlyList<ArInvoiceOutstandingRow> InvoiceRows) LoadInvoiceArBalances(OdbcConnection cn, List<string> wbs1, CancellationToken ct)
        {
            var asOf = DateTime.Today.Date;
            var byWbs = new Dictionary<string, ArProjectOutstandingRow>(StringComparer.OrdinalIgnoreCase);
            var invoiceRows = new List<ArInvoiceOutstandingRow>(256);

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(InvBalanceSourceCurrency,0)) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) <= 30
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS CurrentAmt,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) BETWEEN 31 AND 60
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt31To60,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) BETWEEN 61 AND 90
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt61To90,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) > 90
             THEN COALESCE(InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt90Plus,
    MIN(COALESCE(InvoiceDate, DueDate)) AS OldestInvoiceDate
FROM [{Catalog}].dbo.AR
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var wbs1Key = GetTrimmed(r, 0);
                    if (wbs1Key.Length == 0)
                        continue;

                    var total = GetDouble(r, 1);
                    var current = GetDouble(r, 2);
                    var aged31 = GetDouble(r, 3);
                    var aged61 = GetDouble(r, 4);
                    var aged90 = GetDouble(r, 5);
                    var oldest = GetDateOrNull(r, 6);

                    if (Math.Abs(total) < 0.005)
                        continue;

                    if (byWbs.TryGetValue(wbs1Key, out var existing))
                    {
                        byWbs[wbs1Key] = existing with
                        {
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
                            total,
                            current,
                            aged31,
                            aged61,
                            aged90,
                            oldest);
                    }
                }

                // Line-level AR rows for invoice drilldown.
                using var cmdDetail = cn.CreateCommand();
                cmdDetail.CommandTimeout = 180;
                cmdDetail.CommandText = $@"
SELECT
    WBS1,
    InvoiceDate,
    DueDate,
    COALESCE(InvBalanceSourceCurrency,0) AS OpenBalance
FROM [{Catalog}].dbo.AR
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND ABS(COALESCE(InvBalanceSourceCurrency,0)) > 0.004;";
                AddInListParameters(cmdDetail, chunk);

                using var regDetail = ct.Register(() => { try { cmdDetail.Cancel(); } catch { } });
                using var rd = cmdDetail.ExecuteReader();
                while (rd.Read())
                {
                    var w = GetTrimmed(rd, 0);
                    if (w.Length == 0) continue;
                    var invoiceDate = GetDateOrNull(rd, 1);
                    var dueDate = GetDateOrNull(rd, 2);
                    var bal = GetDouble(rd, 3);
                    var anchor = dueDate ?? invoiceDate;
                    var dpd = anchor.HasValue ? Math.Max(0, (int)(asOf - anchor.Value.Date).TotalDays) : 0;
                    invoiceRows.Add(new ArInvoiceOutstandingRow(w, invoiceDate, dueDate, dpd, bal));
                }
            }

            var rows = byWbs.Values
                .OrderByDescending(x => x.Aged90Plus)
                .ThenByDescending(x => x.Total)
                .ToList();
            var detail = invoiceRows
                .OrderByDescending(x => x.DaysPastDue)
                .ThenByDescending(x => x.Balance)
                .ToList();
            var outstanding = rows.Sum(x => x.Total);
            var over60 = rows.Sum(x => x.Aged61To90 + x.Aged90Plus);
            return (outstanding, over60, rows, detail);
        }

                                private static (double Earned, double Overbilled, double Net) LoadWipProxyBalanceByProject(OdbcConnection cn, List<string> wbs1, string asOfPeriod, CancellationToken ct)
        {
            var period = (asOfPeriod ?? string.Empty).Trim();
            if (period.Length != 6 || !period.All(char.IsDigit))
                return (0.0, 0.0, 0.0);

            double earned = 0.0;
            double overbilled = 0.0;
            double net = 0.0;

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;
                cmd.CommandText = $@"
SELECT
    SUM(CASE WHEN Net > 0 THEN Net ELSE 0 END) AS Earned,
    SUM(CASE WHEN Net < 0 THEN -Net ELSE 0 END) AS Overbilled,
    SUM(Net) AS Net
FROM (
    SELECT WBS1, SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
    FROM [{Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
      AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
    GROUP BY WBS1
) x;";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    earned += GetDouble(r, 0);
                    overbilled += GetDouble(r, 1);
                    net += GetDouble(r, 2);
                }
            }

            return (earned, overbilled, net);
        }
        private static (double Earned, double Overbilled, double Net) LoadFirmwideWipProxyBalance(OdbcConnection cn, string asOfPeriod, CancellationToken ct)
        {
            var period = (asOfPeriod ?? string.Empty).Trim();
            if (period.Length != 6 || !period.All(char.IsDigit))
                period = DateTime.Today.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture);

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 180;
            cmd.CommandText = $@"
SELECT
    SUM(CASE WHEN Net > 0 THEN Net ELSE 0 END) AS Earned,
    SUM(CASE WHEN Net < 0 THEN -Net ELSE 0 END) AS Overbilled,
    SUM(Net) AS Net
FROM (
    SELECT WBS1, SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
    FROM [{Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
    GROUP BY WBS1
) x;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (0.0, 0.0, 0.0);

            var earned = GetDouble(r, 0);
            var overbilled = GetDouble(r, 1);
            var net = GetDouble(r, 2);
            return (earned, overbilled, net);
        }
        private static List<WipProjectBreakdownRow> LoadWipProjectBreakdownByProject(
            OdbcConnection cn,
            List<string> wbs1,
            string asOfPeriod,
            bool useUnbilledAsOf,
            CancellationToken ct)
        {
            var period = (asOfPeriod ?? string.Empty).Trim();
            if (period.Length != 6 || !period.All(char.IsDigit))
                period = DateTime.Today.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture);

            var rows = new Dictionary<string, WipProjectBreakdownRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(wbs1, 80))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 180;

                if (useUnbilledAsOf)
                {
                    cmd.CommandText = $@"
SELECT
    p.WBS1,
    COALESCE(p.Unbilled,0) AS Net
FROM [{Catalog}].dbo.PRSummaryMain p
JOIN (
    SELECT WBS1, MAX(Period) AS MaxPeriod
    FROM [{Catalog}].dbo.PRSummaryMain
    WHERE Period <= ?
      AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
    GROUP BY WBS1
) x
  ON x.WBS1 = p.WBS1
 AND x.MaxPeriod = p.Period;";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                    AddInListParameters(cmd, chunk);
                }
                else
                {
                    cmd.CommandText = $@"
SELECT
    WBS1,
    SUM(COALESCE(Revenue,0) - COALESCE(Billed,0)) AS Net
FROM [{Catalog}].dbo.PRSummaryMain
WHERE Period <= ?
  AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                    AddInListParameters(cmd, chunk);
                }

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var w = GetTrimmed(r, 0);
                    if (w.Length == 0) continue;
                    var net = GetDouble(r, 1);
                    var earned = Math.Max(net, 0.0);
                    var over = Math.Max(-net, 0.0);

                    rows[w] = new WipProjectBreakdownRow(w, earned, over, net, period);
                }
            }

            return rows.Values
                .Where(x => Math.Abs(x.Earned) > 0.004 || Math.Abs(x.Overbilled) > 0.004 || Math.Abs(x.Net) > 0.004)
                .OrderByDescending(x => x.Overbilled)
                .ThenByDescending(x => x.Earned)
                .ToList();
        }
private static string GetTrimmed(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return string.Empty;
            var v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            return v.Trim();
        }

        private static DateTime GetDate(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return DateTime.MinValue;
            var v = r.GetValue(i);
            if (v is DateTime dt) return dt.Date;
            if (DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                return parsed.Date;
            return DateTime.MinValue;
        }

        private static DateTime? GetDateOrNull(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return null;
            var d = GetDate(r, i);
            return d == DateTime.MinValue ? null : d;
        }

        private static DateTime? MergeOldest(DateTime? a, DateTime? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a.Value <= b.Value ? a : b;
        }

        private static double GetDouble(IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return 0.0;
            return ScalarToDouble(r.GetValue(i));
        }

        private static double ScalarToDouble(object? v)
        {
            if (v == null || v == DBNull.Value) return 0.0;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is long l) return l;
            if (v is int n) return n;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0.0;
        }

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListParameters(OdbcCommand cmd, List<string> vals)
        {
            foreach (var v in vals)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
        {
            for (var i = 0; i < src.Count; i += size)
                yield return src.GetRange(i, Math.Min(size, src.Count - i));
        }
    }
}

