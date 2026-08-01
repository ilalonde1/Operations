#nullable enable
using Kor.Operations.Financials;
using Kor.Operations.Financials.CfoMetrics;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class CfoMetricTests
{
    [Fact]
    public void DeliveryConfidenceMetric_MapsExpectedBuckets()
    {
        var metric = new DeliveryConfidenceMetric();

        Assert.Equal(0m, metric.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.HighConfidence)));
        Assert.Equal(1m, metric.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.Watch)));
        Assert.Equal(2m, metric.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.AtRisk)));
        Assert.Equal(3m, metric.ComputeValue(ProjectData.Create(deliveryConfidenceLevel: DeliveryConfidenceLevel.Critical)));
    }

    [Fact]
    public void PercentHoursSpentMetric_ComputesFractionAndHandlesZeroBudget()
    {
        var metric = new PercentHoursSpentMetric();

        var normal = ProjectData.Create(hoursSpent: 50m, hoursBudgeted: 200m);
        var divZero = ProjectData.Create(hoursSpent: 50m, hoursBudgeted: 0m);

        Assert.Equal(0.25m, metric.ComputeValue(normal));
        Assert.Equal(0m, metric.ComputeValue(divZero));
    }

    [Fact]
    public void BudgetBurnRateMetric_ComputesRatioAndHandlesZeroPercentBilled()
    {
        var metric = new BudgetBurnRateMetric();

        var normal = ProjectData.Create(hoursSpent: 100m, hoursBudgeted: 200m, percentBilled: 0.25m);
        var divZero = ProjectData.Create(hoursSpent: 100m, hoursBudgeted: 200m, percentBilled: 0m);

        Assert.Equal(2.0m, metric.ComputeValue(normal));
        Assert.Equal(0m, metric.ComputeValue(divZero));
    }

    [Fact]
    public void PortfolioHealthCountsMetric_ReturnsBucketCounts()
    {
        var counts = new PortfolioHealthCounts(Healthy: 10, Watch: 3, Critical: 2);
        var ctx = ProjectData.Create(portfolioCounts: counts);

        Assert.Equal(10m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Healthy).ComputeValue(ctx));
        Assert.Equal(3m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Watch).ComputeValue(ctx));
        Assert.Equal(2m, new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Critical).ComputeValue(ctx));
    }

    [Fact]
    public void CfoMetricRegistry_ReturnsKnownMetricsByName()
    {
        var registry = new CfoMetricRegistry();

        Assert.True(registry.GetAllMetrics().Count >= 4);
        Assert.IsType<PercentHoursSpentMetric>(registry.TryGetByName("Percent Hours Spent"));
    }
}
