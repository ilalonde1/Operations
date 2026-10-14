#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Data.MajorProjects;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The 2026-10-13 clarity pass ("Clarity is King"). The dashboard answers three questions
/// plus the funnel; everything else lives on the page that owns it. These gates hold the
/// shape: if a block creeps back onto the dashboard, or a moved block has no home, they fail.
///
/// Covers XAML text and the pure chart builder. Does not render WPF, so a block that is
/// present but unreadable (clipped, overlapped) escapes, and so does a page whose grid
/// binds to a property the view model no longer sets.
/// </summary>
public sealed class BdDashboardClarityRegressionTests
{
    private static readonly string[] DashboardKeeps = { "Closing soon", "New RFPs", "Priority Actions", "Reachable Now", "In Bidding", "Watching" };

    private static readonly string[] DashboardLoses =
    {
        "Pipeline by sector", "By market", "By Stage", "Value by Market", "Open Structural Seats",
        "Competitor Watch", "Forward Pipeline", "Total pipeline", "Deadlines - Next 6 Months", "Pipeline Funnel",
    };

    [Fact]
    public void Dashboard_HasFourBlocks_AndNoneOfTheNineThatMoved()
    {
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml");
        foreach (var keep in DashboardKeeps)
            Assert.True(xaml.Contains(keep, StringComparison.Ordinal), $"EXPECTED the dashboard to keep '{keep}'; ACTUAL missing");
        foreach (var gone in DashboardLoses)
            Assert.False(xaml.Contains(gone, StringComparison.Ordinal), $"EXPECTED '{gone}' to have left the dashboard; ACTUAL still there");

        // The whole-screen card count: four cards plus the funnel row, not thirteen blocks.
        var cards = Regex.Matches(xaml, @"Style=""\{StaticResource Card\}""").Count;
        Assert.True(cards == 3, $"EXPECTED 3 card blocks (closing, new, actions); ACTUAL {cards}");

        var vm = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs");
        Assert.DoesNotContain("SectorBars", vm);
        Assert.DoesNotContain("GetOpenStructuralSeatsAsync", vm);
        Assert.DoesNotContain("GetCompetitorWatchAsync", vm);
        Assert.DoesNotContain("GetForwardPipelineAsync", vm);
        Assert.Contains("GetPipelineFunnelAsync", vm);
    }

    [Fact]
    public void MovedBlocks_EachHaveExactlyOneHome()
    {
        var pipeline = Source("Kor.Operations.App/Opportunities/PrimePipelineWindow.xaml");
        foreach (var chart in new[] { "By sector", "By market", "By stage", "Value by market (CAD $M)", "Total pipeline", "Open RFPs", "Upcoming projects", "Pipeline value (CAD)" })
            Assert.True(pipeline.Contains(chart, StringComparison.Ordinal), $"EXPECTED the Pipeline window to carry '{chart}'; ACTUAL missing");
        // One bar template, not four copies of it.
        Assert.Single(Regex.Matches(pipeline, @"x:Key=""ChartBarTemplate"""));
        Assert.Single(Regex.Matches(pipeline, @"MultiBinding Converter=""\{StaticResource BarHeight\}"""));
        Assert.Equal(4, Regex.Matches(pipeline, @"ItemTemplate=""\{StaticResource ChartBarTemplate\}""").Count);

        var relationships = Source("Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsView.xaml");
        Assert.Contains("Architects without a settled structural partner", relationships);
        Assert.Contains("OpenSeatsGrid_MouseDoubleClick", relationships);
        Assert.Contains("GetOpenStructuralSeatsAsync(12", Source("Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsViewModel.cs"));

        var competition = Source("Kor.Operations.App/Opportunities/CompetitionInfoView.xaml");
        Assert.Contains("Header=\"Competitor watch\"", competition);
        Assert.Contains("CompetitorRow_DoubleClick", competition);
        Assert.Contains("GetCompetitorWatchAsync(12", Source("Kor.Operations.App/Opportunities/CompetitionInfoViewModel.cs"));
    }

    [Fact]
    public void PipelineCharts_CountsAndBars_FromRows()
    {
        PrimePipelineRow Row(string type, string? sector, string? province, string? stage, decimal? cad) =>
            new(type, "1", "P", null, sector, stage, cad, province, null, null, null)
            { EstimatedValue = cad, EstimatedValueCurrency = cad is null ? null : "CAD" };

        var overview = PipelineCharts.Build(new[]
        {
            Row("Open RFP", "school", "BC", "Design", 2_000_000m),
            Row("Open RFP", "School", "AB", "Design", null),
            Row("Pipeline Project", "Hospital", "BC", "CapitalPlan", 3_000_000m),
        });

        Assert.Equal("3", overview.TotalPipelineDisplay);
        Assert.Equal("2", overview.OpenRfpsDisplay);
        Assert.Equal("1", overview.UpcomingProjectsDisplay);
        Assert.Equal("$5.0M", overview.PipelineValueDisplay);
        Assert.Equal(new[] { "School", "Hospital" }, overview.SectorBars.Select(b => b.Label));   // case folded, count-ordered
        Assert.Equal("2", overview.SectorBars[0].Display);
        Assert.Equal(new[] { "BC", "AB" }, overview.MarketBars.Select(b => b.Label));
        Assert.Equal("$5.0M", overview.MarketValueBars.Single(b => b.Label == "BC").Display);
        Assert.Equal("$0.0M", overview.MarketValueBars.Single(b => b.Label == "AB").Display);   // a missing amount is excluded, not invented
        Assert.Equal("0", PipelineCharts.Overview.Empty.TotalPipelineDisplay);
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
}
