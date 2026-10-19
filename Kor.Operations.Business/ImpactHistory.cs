#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.Financials
{
    /// <summary>One closed month of the firm's cash cycle, CAD-equivalent.</summary>
    /// <param name="Invoiced">Invoices on the revenue accounts, the same lines FirmHealthService's DSO bills on.</param>
    /// <param name="Collected">Cash receipts applied to the AR control account (LedgerAR TransType CR, account 1200).</param>
    /// <param name="ArEnd">AR at month end: today's AR rebuilt backwards through each later month's change on account 1200.</param>
    /// <param name="Dso">ArEnd over the trailing twelve months' invoicing × 365, FirmHealthService's formula; 0 without a year of history.</param>
    public sealed record ImpactMonth(int Period, double Invoiced, double Collected, double ArEnd, double Dso);

    /// <summary>A raw LedgerAR month for one currency bucket, before conversion.</summary>
    public sealed record ImpactLedgerRow(int Period, string Bucket, double Invoiced, double Collected, double ArChange);

    /// <summary>
    /// The scoreboard's "before" line: invoiced, collected, AR and DSO per month from Deltek's LedgerAR (18 Oct 2026). The cumulative
    /// balance of account 1200 cannot be used as AR (−$21.9M on the day: opening balances predate the ledger), so month-end AR is
    /// today's AR walked back through the monthly changes on 1200, which do reconcile month to month.
    /// </summary>
    public static class ImpactHistory
    {
        private const string Bucket = "CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END";
        private const string Acct = "LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4)";

        public static string Sql(string catalog) => $@"
SELECT Period, {Bucket} AS Bucket,
       SUM(CASE WHEN TransType = 'IN' AND {Acct} IN ('4001', '4003', '4210', '4220', '4240') THEN -Amount ELSE 0 END) AS Invoiced,
       SUM(CASE WHEN TransType = 'CR' AND {Acct} = '1200' THEN -Amount ELSE 0 END) AS Collected,
       SUM(CASE WHEN {Acct} = '1200' THEN Amount ELSE 0 END) AS ArChange
FROM [{catalog}].dbo.LedgerAR
WHERE Period >= ?
GROUP BY Period, {Bucket};";

        public static async Task<IReadOnlyList<ImpactLedgerRow>> LoadAsync(DeltekOdbcOptions odbc, int fromPeriod, CancellationToken ct) =>
            await Task.Run(() =>
            {
                var catalog = DeltekCatalogValidator.ResolveCatalog(odbc.Catalog);
                var dsn = string.IsNullOrWhiteSpace(odbc.Dsn) ? "Deltek" : odbc.Dsn;
                using var cn = new VpOdbcDsnFactory(dsn, odbc.User, odbc.Password, () => new Dictionary<string, string>()).Create();
                cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = Sql(catalog);
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = fromPeriod });
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel */ } });
                using var r = cmd.ExecuteReader();
                var rows = new List<ImpactLedgerRow>();
                while (r.Read())
                    rows.Add(new ImpactLedgerRow(Convert.ToInt32(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture),
                        r.GetString(1), Num(r, 2), Num(r, 3), Num(r, 4)));
                return (IReadOnlyList<ImpactLedgerRow>)rows;
            }, ct).ConfigureAwait(false);

        /// <summary>
        /// Converts and walks the months. Months after <paramref name="lastClosedPeriod"/> (the open month) still count in the
        /// walk-back from <paramref name="arNowCadEquiv"/> but are not returned. DSO needs twelve months of invoicing behind it.
        /// </summary>
        public static IReadOnlyList<ImpactMonth> Build(IEnumerable<ImpactLedgerRow> rows, double usdToCadRate, double arNowCadEquiv, int lastClosedPeriod)
        {
            var months = rows
                .GroupBy(r => r.Period)
                .Select(g => (Period: g.Key,
                    Invoiced: g.Sum(r => r.Invoiced * Fx(r, usdToCadRate)),
                    Collected: g.Sum(r => r.Collected * Fx(r, usdToCadRate)),
                    Change: g.Sum(r => r.ArChange * Fx(r, usdToCadRate))))
                .OrderBy(m => m.Period)
                .ToList();
            var arEnd = new double[months.Count];
            var ar = arNowCadEquiv;
            for (var i = months.Count - 1; i >= 0; i--)
            {
                arEnd[i] = ar;          // AR at the end of month i
                ar -= months[i].Change; // AR at the end of the month before
            }
            var result = new List<ImpactMonth>();
            for (var i = 0; i < months.Count; i++)
            {
                if (months[i].Period > lastClosedPeriod) continue;
                var year = i >= 11 ? months.Skip(i - 11).Take(12).Sum(m => m.Invoiced) : 0.0;
                result.Add(new ImpactMonth(months[i].Period, months[i].Invoiced, months[i].Collected, arEnd[i],
                    year > 1.0 ? arEnd[i] / year * 365.0 : 0.0));
            }
            return result;
        }

        /// <summary>yyyyMM of the month before <paramref name="today"/>: the last closed month.</summary>
        public static int LastClosedPeriod(DateTime today)
        {
            var last = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            return last.Year * 100 + last.Month;
        }

        public static int PeriodMonthsBefore(int period, int months)
        {
            var d = new DateTime(period / 100, period % 100, 1).AddMonths(-months);
            return d.Year * 100 + d.Month;
        }

        private static double Fx(ImpactLedgerRow r, double usdToCadRate) => r.Bucket == "USA" ? usdToCadRate : 1.0;

        private static double Num(OdbcDataReader r, int i) => r.IsDBNull(i) ? 0.0 : Convert.ToDouble(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
    }
}
