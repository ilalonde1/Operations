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
/// Ellipsis checks inspect XAML text only; the rendered width is only seen in the PNG.
/// </summary>
public sealed class BdDashboardClarityRegressionTests
{
    // 2026-10-20: Closing soon + New RFPs became one list, "Tenders for KOR"; the tiles say what they count.
    private static readonly string[] DashboardKeeps = { "Tenders for KOR", "Priority Actions", "Structural engineer not chosen yet",
        "Out for bid", "Coming up" };

    private static readonly string[] DashboardLoses =
    {
        "Text=\"New RFPs\"", "Text=\"Reachable Now\"", "Text=\"In Bidding\"", "Text=\"Watching\"",
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
        Assert.True(cards == 2, $"EXPECTED 2 card blocks (tenders, actions); ACTUAL {cards}");

        var vm = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs");
        Assert.DoesNotContain("SectorBars", vm);
        Assert.DoesNotContain("GetOpenStructuralSeatsAsync", vm);
        Assert.DoesNotContain("GetCompetitorWatchAsync", vm);
        Assert.DoesNotContain("GetForwardPipelineAsync", vm);
        Assert.Contains("GetPipelineFunnelAsync", vm);
    }

    // 2026-10-14, Ian on the shipped dashboard: the top row of "Closing soon" was a
    // duplicate BdOpportunityDedup had retired six days earlier; 6 of the 20 rows were.
    // Removed records (DismissedAtUtc set) stay in the click cache and leave both lists.
    // Source-level: a second list added later that reads `opps` instead of `live` escapes.
    [Fact]
    public void Dashboard_ListsOnlyLiveRecords()
    {
        var vm = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs");
        Assert.Contains("var live = opps.Where(o => o.DismissedAtUtc is null && Kor.Opportunities.Data.Ingestion.KorBidFit.IsKorWork(o.Name)).ToList();", vm);
        Assert.Contains("Replace(LatestRfps, live", vm);
        Assert.Contains("Replace(Deadlines, live", vm);
        Assert.Contains("Replace(Deadlines, live", vm);
        Assert.DoesNotContain("Replace(LatestRfps, opps", vm);
        Assert.DoesNotContain("Replace(Deadlines, opps", vm);

        // The card's second line names the buyer and the local closing time; the
        // "(stored offset)" dump is gone from every list.
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml");
        Assert.Contains("Binding Detail", xaml);
        Assert.DoesNotContain("stored offset", xaml);
        Assert.DoesNotContain("(stored offset)", Source("Kor.Operations.App/Opportunities/OpportunityDeadlineDisplay.cs"));
    }

    [Fact]
    public void TenderList_LongNamesTrimWithEllipsis_AndNeverScrollsSideways()
    {
        // 2026-10-20, Ian's screenshot: a long title gave the Closing soon list a horizontal scrollbar.
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml");
        var list = Regex.Match(xaml, @"<ListBox\b[^>]*x:Name=""DeadlinesList""[^>]*>", RegexOptions.Singleline);
        Assert.True(list.Success, "EXPECTED the tender list; ACTUAL none");
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", list.Value);
        Assert.Matches(@"<TextBlock Text=""\{Binding Name\}""[^>]*TextTrimming=""CharacterEllipsis""[^>]*ToolTip=""\{Binding Name\}""", xaml);
        Assert.Matches(@"<TextBlock Text=""\{Binding Detail\}""[^>]*TextTrimming=""CharacterEllipsis""", xaml);
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
        // Every row, never a first-N by name presented as the whole (2026-10-22: 12 of 30 architects, 12 of 16 competitors).
        Assert.Contains("GetOpenStructuralSeatsAsync(200", Source("Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsViewModel.cs"));

        var competition = Source("Kor.Operations.App/Opportunities/CompetitionInfoView.xaml");
        Assert.Contains("Header=\"Competitor watch\"", competition);
        Assert.Contains("CompetitorRow_DoubleClick", competition);
        Assert.Contains("GetCompetitorWatchAsync(200", Source("Kor.Operations.App/Opportunities/CompetitionInfoViewModel.cs"));
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
