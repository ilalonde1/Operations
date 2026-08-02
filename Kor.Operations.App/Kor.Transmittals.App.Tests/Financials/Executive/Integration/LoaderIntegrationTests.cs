#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Financials;
using Kor.Operations.Financials.Loaders;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive.Integration;

/// <summary>
/// Real-Deltek integration tests for the financial loaders. These tests are
/// the safety net the synthetic xUnit suite cannot provide: they catch SQL
/// regressions (column drift, predicate bugs, FX bucket flips) by hitting
/// the live ODBC connection. Tagged Category=Integration so the hermetic CI
/// run skips them; engineers run them manually after changes touching SQL.
///
/// Credentials come from <see cref="IntegrationFixture"/>: DELTEK_DSN /
/// DELTEK_USER / DELTEK_PASSWORD if set, otherwise the machine-level
/// KOR_ODBC_USER / KOR_ODBC_PASSWORD against DSN <c>Deltek</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LoaderIntegrationTests
{
    private const double DefaultUsdToCadRate = 1.36;

    [Fact]
    public void BilledFinancialsService_LoadPartnerBilledRevenueByPeriod_ReconcilesToDalerDeck()
    {
        using var cn = IntegrationFixture.OpenDeltekConnection();
        var svc = new BilledFinancialsService(IntegrationFixture.Options(), new FinancialsOptions());

        var rows = svc.LoadPartnerBilledRevenueByPeriod(cn, 202601, 202605, CancellationToken.None);
        var totals = rows
            .GroupBy(r => (r.Period, r.OrgBucket))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        Assert.Equal(446_467.11m, totals[(202601, "CAD")]);
        Assert.Equal(53_517.50m, totals[(202601, "USA")]);
        Assert.Equal(428_971.85m, totals[(202602, "CAD")]);
        Assert.Equal(25_950.00m, totals[(202602, "USA")]);
        Assert.Equal(380_897.49m, totals[(202603, "CAD")]);
        Assert.Equal(57_073.20m, totals[(202603, "USA")]);
        Assert.Equal(325_310.15m, totals[(202604, "CAD")]);
        Assert.Equal(29_908.32m, totals[(202604, "USA")]);
        Assert.Equal(299_013.25m, totals[(202605, "CAD")]);
        Assert.Equal(10_000.00m, totals[(202605, "USA")]);

        Assert.Equal(1_880_659.85m, rows.Where(r => r.OrgBucket == "CAD").Sum(r => r.Amount));
        Assert.Equal(176_449.02m, rows.Where(r => r.OrgBucket == "USA").Sum(r => r.Amount));
    }

    [Fact]
    public async Task Schema_ValidateAsync_ReturnsKnownColumnSet()
    {
        using var cn = IntegrationFixture.OpenDeltekConnection();
        var catalog = ExecutiveSummaryLoaderSupport.Catalog;

        var missing = await DeltekSchemaValidator.ValidateAsync(cn, catalog, CancellationToken.None);

        Assert.NotNull(missing);
        // Empty list is the happy path; if anything is reported missing we
        // surface it in the assertion message so the engineer can see exactly
        // which loader dependency drifted out of the catalog.
        Assert.True(
            missing.Count == 0,
            $"Schema drift detected on catalog '{catalog}': {string.Join(", ", missing)}");
    }

    [Fact]
    public void ArFinancialsService_Load_FirmwideTotalsAreSensible()
    {
        using var cn = IntegrationFixture.OpenDeltekConnection();

        var odbcOptions = new DeltekOdbcOptions
        {
            Dsn = Environment.GetEnvironmentVariable("DELTEK_DSN") ?? "Deltek",
            User = Environment.GetEnvironmentVariable("DELTEK_USER") ?? string.Empty,
            Password = Environment.GetEnvironmentVariable("DELTEK_PASSWORD") ?? string.Empty,
            Catalog = ExecutiveSummaryLoaderSupport.Catalog,
        };
        var svc = new ArFinancialsService(
            odbcOptions,
            new FinancialsOptions { BilledUsdToCadRate = DefaultUsdToCadRate.ToString(CultureInfo.InvariantCulture) });
        var result = svc.Load(cn, wbs1: new List<string>(), DefaultUsdToCadRate, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.ProjectRows);
        Assert.NotNull(result.InvoiceRows);

        // Outstanding cannot be negative in source-currency aggregation; if it
        // is, the FX bucket math has flipped a sign somewhere.
        Assert.True(
            result.FirmwideOutstandingCadEquiv >= 0,
            $"Firmwide AR CAD-equiv came back negative: {result.FirmwideOutstandingCadEquiv:C0}");
        Assert.True(
            result.FirmwideOutstandingCad >= 0,
            $"Firmwide AR CAD bucket came back negative: {result.FirmwideOutstandingCad:C0}");
        Assert.True(
            result.FirmwideOutstandingUsa >= 0,
            $"Firmwide AR USA bucket came back negative: {result.FirmwideOutstandingUsa:C0}");

        // CAD-equiv must equal CAD + USA*fx (within rounding).
        var expectedCadEquiv = result.FirmwideOutstandingCad + (result.FirmwideOutstandingUsa * DefaultUsdToCadRate);
        Assert.Equal(expectedCadEquiv, result.FirmwideOutstandingCadEquiv, 2);

        // KOR has been carrying open AR for years — a $0 firmwide tile means
        // either (a) the company is fully collected (extremely unlikely) or
        // (b) the SQL stopped returning rows. Treat $0 as a regression.
        Assert.True(
            result.FirmwideOutstandingCadEquiv > 0,
            "Firmwide AR returned $0 — likely SQL regression (predicate/column drift).");
    }

    [Fact]
    public void RevenueLoader_Load_LedgerArSegmentReturnsValidPeriod()
    {
        using var cn = IntegrationFixture.OpenDeltekConnection();

        // Pull a handful of recently-active WBS1 so the loader has scope to
        // run its LedgerAR query against. Picks projects with the most recent
        // PRSummaryMain rows — a reasonable proxy for "active" without
        // hard-coding project numbers that could rotate over time.
        var wbs1 = LoadActiveWbs1(cn, take: 50);
        Assert.NotEmpty(wbs1);

        var result = RevenueLoader.Load(cn, wbs1, DefaultUsdToCadRate, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Series);
        Assert.NotEmpty(result.Series.Periods);

        // LedgerAR-since values: amount must be non-negative; period (if set)
        // must be a valid YYYYMM. Period of 0 is allowed (no closed period to
        // anchor against), but if it's set, it must parse.
        Assert.True(
            result.LedgerArInvoicedSinceLatestPosted >= 0,
            $"LedgerAR-since came back negative: {result.LedgerArInvoicedSinceLatestPosted:C0}");

        if (result.LedgerArInvoicedSincePeriod > 0)
        {
            var year = result.LedgerArInvoicedSincePeriod / 100;
            var month = result.LedgerArInvoicedSincePeriod % 100;
            Assert.InRange(year, 2000, 2100);
            Assert.InRange(month, 1, 12);
        }

        // Trend windows derive from the series; if Periods is non-empty the
        // last-period revenue cannot be NaN/Infinity.
        Assert.False(double.IsNaN(result.Revenue30) || double.IsInfinity(result.Revenue30));
        Assert.False(double.IsNaN(result.Revenue90) || double.IsInfinity(result.Revenue90));
    }

    private static List<string> LoadActiveWbs1(OdbcConnection cn, int take)
    {
        var wbs1 = new List<string>(capacity: take);
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 60;
        cmd.CommandText = $@"
SELECT TOP {take} ps.WBS1
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain ps
WHERE ps.WBS1 IS NOT NULL AND LTRIM(RTRIM(ps.WBS1)) <> ''
GROUP BY ps.WBS1
ORDER BY MAX(ps.Period) DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var v = (r.GetValue(0)?.ToString() ?? string.Empty).Trim();
            if (v.Length > 0) wbs1.Add(v);
        }
        return wbs1;
    }
}
