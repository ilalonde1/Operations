#nullable enable
using System.Globalization;
using Kor.Operations.Financials;
using Kor.Operations.Financials.CfoMetrics;
namespace Kor.Operations.Tests;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunAll();
            Console.WriteLine("ALL TESTS PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("TESTS FAILED");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void RunAll()
    {
        TestDeliveryConfidenceMetric();
        TestPercentHoursSpentMetric();
        TestBudgetBurnRateMetric();
        TestPortfolioHealthCountsMetric();
        TestRegistryLookup();
    }

    private static void TestDeliveryConfidenceMetric()
    {
        var m = new DeliveryConfidenceMetric();

        AssertEqual(0m, m.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.HighConfidence)), "DeliveryConfidence High");
        AssertEqual(1m, m.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.Stable)), "DeliveryConfidence Watch");
        AssertEqual(2m, m.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.AtRisk)), "DeliveryConfidence AtRisk");
        AssertEqual(3m, m.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.Critical)), "DeliveryConfidence Critical");
    }

    private static void TestPercentHoursSpentMetric()
    {
        var m = new PercentHoursSpentMetric();

        var ctx = ProjectData.Create(hoursSpent: 50m, hoursBudgeted: 200m);
        AssertNear(0.25m, m.ComputeValue(ctx), 0.000001m, "PercentHoursSpent 25%");

        var ctx0 = ProjectData.Create(hoursSpent: 50m, hoursBudgeted: 0m);
        AssertEqual(0m, m.ComputeValue(ctx0), "PercentHoursSpent div0");
    }

    private static void TestBudgetBurnRateMetric()
    {
        var m = new BudgetBurnRateMetric();

        // PercentHoursSpent comes from hoursSpent/hoursBudgeted
        var ctx = ProjectData.Create(hoursSpent: 100m, hoursBudgeted: 200m, percentBilled: 0.25m);
        AssertNear(2.0m, m.ComputeValue(ctx), 0.000001m, "BurnRate 2x");

        var ctx0 = ProjectData.Create(hoursSpent: 100m, hoursBudgeted: 200m, percentBilled: 0m);
        AssertEqual(0m, m.ComputeValue(ctx0), "BurnRate div0");
    }

    private static void TestPortfolioHealthCountsMetric()
    {
        var c = new PortfolioHealthCounts(Healthy: 10, Watch: 3, Critical: 2);
        var ctx = ProjectData.Create(portfolioCounts: c);

        AssertEqual(10m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Healthy).ComputeValue(ctx), "Portfolio Healthy");
        AssertEqual(3m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Watch).ComputeValue(ctx), "Portfolio Watch");
        AssertEqual(2m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Critical).ComputeValue(ctx), "Portfolio Critical");
    }

    private static void TestRegistryLookup()
    {
        var reg = new CfoMetricRegistry();
        var all = reg.GetAllMetrics();
        AssertTrue(all.Count >= 4, "Registry contains metrics");

        var m = reg.TryGetByName("Percent Hours Spent");
        AssertTrue(m is PercentHoursSpentMetric, "Registry lookup");
    }

    private static void AssertTrue(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"ASSERT FAILED: {name}");
    }

    private static void AssertEqual(decimal expected, decimal actual, string name)
    {
        if (expected != actual)
            throw new InvalidOperationException($"ASSERT FAILED: {name}. expected={expected.ToString(CultureInfo.InvariantCulture)} actual={actual.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void AssertNear(decimal expected, decimal actual, decimal tol, string name)
    {
        if (Math.Abs(expected - actual) > tol)
            throw new InvalidOperationException($"ASSERT FAILED: {name}. expected={expected.ToString(CultureInfo.InvariantCulture)} actual={actual.ToString(CultureInfo.InvariantCulture)} tol={tol.ToString(CultureInfo.InvariantCulture)}");
    }
}
