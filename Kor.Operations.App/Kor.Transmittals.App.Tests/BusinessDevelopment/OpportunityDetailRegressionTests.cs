#nullable enable
using System.IO;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Core.Models;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// A double-click on an RFP opens the RFP, not the record editor. Ian, 2026-10-13, on
/// the dashboard: "What does Save do? What are you Saving??? Where's the actual RFP meat
/// and bones info?" The editor had eleven metadata fields and no posting, link or text.
///
/// Covers the read view's picking rules and the two double-click entry points by source.
/// Does not drive WPF or the stores; a third entry point added later that opens the
/// editor directly would escape, and so would a posting whose URL is wrong at the source.
/// </summary>
public sealed class OpportunityDetailRegressionTests
{
    private static readonly Guid BcBid = Guid.NewGuid();
    private static readonly Guid Bonfire = Guid.NewGuid();

    [Fact]
    public void Build_NewestPostingFirst_LongestDescription_LinkFromTheNewest()
    {
        var older = new OpportunityObservation
        {
            Id = 1, OpportunitySourceId = BcBid, Title = "Older title", Url = "https://bcbid/1",
            Description = new string('x', 400), PostedDateUtc = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        };
        var newer = new OpportunityObservation
        {
            Id = 2, OpportunitySourceId = Bonfire, Title = "Newer title", Url = "https://bonfire/2",
            Description = "short", PostedDateUtc = DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
        };
        var names = new Dictionary<Guid, string> { [BcBid] = "BcBid" };

        var actual = OpportunityDetailViewModel.Build(new[] { older, newer }, names);

        Assert.Equal(new[] { "Newer title", "Older title" }, actual.Sources.Select(s => s.Title));
        Assert.Equal("source", actual.Sources[0].Source);   // Bonfire is not in the enabled list: shown, not dropped
        Assert.Equal("BcBid", actual.Sources[1].Source);
        Assert.Equal("https://bonfire/2", actual.PrimaryUrl);
        Assert.Equal(400, actual.Description.Length);         // the longest text wins, not the newest
        Assert.Equal("2026-10-05", actual.Sources[0].Posted);
    }

    [Fact]
    public void Build_NoPostings_SaysSo_AndOffersNoLink()
    {
        var actual = OpportunityDetailViewModel.Build(Array.Empty<OpportunityObservation>(), new Dictionary<Guid, string>());
        Assert.Null(actual.PrimaryUrl);
        Assert.Empty(actual.Sources);
        Assert.Contains("No source posting", actual.Description);
    }

    [Fact]
    public void Build_PostingWithoutDescription_SaysSoInsteadOfBlank()
    {
        var bare = new OpportunityObservation { Id = 3, OpportunitySourceId = BcBid, Title = "T", Url = "https://bcbid/3", IngestedAtUtc = DateTimeOffset.Parse("2026-10-07T12:00:00Z") };
        var actual = OpportunityDetailViewModel.Build(new[] { bare }, new Dictionary<Guid, string>());
        Assert.Contains("no description", actual.Description);
        Assert.Equal("2026-10-07 (ingested)", actual.Sources[0].Posted);
        Assert.Equal("https://bcbid/3", actual.PrimaryUrl);
    }

    [Fact]
    public void DoubleClick_OpensTheReadView_BeforeAnyEditor()
    {
        var dashboard = Source("BusinessDevelopment/Workspace/DashboardView.xaml.cs");
        var open = dashboard[dashboard.IndexOf("private async Task OpenOpportunityDialogAsync", StringComparison.Ordinal)..];
        var readView = open.IndexOf("new OpportunityDetailWindow(", StringComparison.Ordinal);
        var editor = open.IndexOf("new OpportunityEntryDialog(", StringComparison.Ordinal);
        Assert.True(readView >= 0 && editor > readView,
            "EXPECTED the dashboard double-click to show the RFP read view before the editor; ACTUAL editor first or no read view");
        Assert.Contains("if (!detail.EditRequested)", open);

        var opportunities = Source("Opportunities/OpportunitiesView.xaml.cs");
        var handler = opportunities[opportunities.IndexOf("OpportunitiesGrid_MouseDoubleClick(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private async Task", StringComparison.Ordinal)];
        Assert.Contains("ViewSelectedAsync()", handler);
        Assert.DoesNotContain("EditSelectedAsync()", handler);
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Kor.Operations.App", path));
    }
}
