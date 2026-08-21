#nullable enable
using System;
using System.IO;
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.App.Tests.PMTools;

public sealed class HistoricalAnalyticsTooltipTests
{
    [Fact]
    public void FeePerHourTooltipsBindToObservedComparisonRate()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.App", "PMTools", "HistoricalAnalyticsWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.App", "PMTools", "HistoricalAnalyticsViewModel.cs"));

        Assert.DoesNotContain("$185/hr portfolio median", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("typical project earns about $185/hr", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FeePerHrComparisonTooltip", xaml, StringComparison.Ordinal);
        Assert.Contains("FeePerHrComparisonRate", xaml, StringComparison.Ordinal);
        Assert.Contains("FeePerHrComparisonRate => MedianFeePerHr", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void FeePerHourTooltipFormatsTheObservedPortfolioMedian()
    {
        var tooltip = HistoricalAnalyticsTooltipText.FeePerHourComparison(380);

        Assert.Contains("observed portfolio median", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$380", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("$185", tooltip, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        // There is no Kor.Operations.sln at the repo root - the only solution file is
        // Kor.Operations.App/Kor.Operations.App.sln. Anchor on a directory instead.
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "Kor.Operations.App")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root.");
        return dir;
    }
}
