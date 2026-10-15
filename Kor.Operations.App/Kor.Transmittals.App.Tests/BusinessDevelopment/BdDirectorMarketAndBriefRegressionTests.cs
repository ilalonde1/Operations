#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers eight-tab/caption/literal/date contracts, database-store load/acknowledgement,
/// market read sizes/frequency, Dashboard deadline semantics, retained retired citations,
/// shared passage resolution and the absence of runtime generation. No real HTTP, SQL, model,
/// knowledge text or WPF rendering is used. Same-class miss: a binding can name an
/// existing property yet truncate it at a narrow window size; render review is separate.
/// All market/brief content below is explicitly illustrative, not production evidence.
/// </summary>
public sealed class BdDirectorMarketAndBriefRegressionTests
{
    [Fact]
    public void Window_HasEightTabs_StaticCaptions_AndPlainWords()
    {
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml");
        var tabs = XDocument.Parse(xaml).Descendants().Where(e => e.Name.LocalName == "TabItem").Select(e => (string?)e.Attribute("Header"));
        Assert.Equal(new[] { "The number", "People", "Funnel", "Accounts", "Asks & decisions", "Knowledge", "Market", "This week" }, tabs);
        Assert.Contains("{x:Static director:BdDirectorViewModel.MarketCaption}", xaml, StringComparison.Ordinal);
        Assert.Contains("{x:Static director:BdDirectorViewModel.BriefCaption}", xaml, StringComparison.Ordinal);
        Assert.Equal("What is coming: signals on the firms we watch, planned projects not yet procured, RFPs closing, and seats no structural engineer holds yet. All of it is what the BD Dashboard already reads.", BdDirectorViewModel.MarketCaption);
        Assert.Equal("The Director's brief: five questions a week over these tabs and the rule bank, written from the week's export and imported here. Open a citation to read the passage.", BdDirectorViewModel.BriefCaption);
        Assert.True(Regex.Matches(xaml, "ContentTemplate=\"\\{StaticResource PassagePane\\}\"").Count == 2, "EXPECTED the same passage template in Knowledge and This week.");
        foreach (var file in DirectorFiles())
        {
            var content = File.ReadAllText(file);
            var literals = Path.GetExtension(file) == ".xaml"
                ? XDocument.Parse(content).Descendants().Attributes().Where(a => new[] { "Text", "Header", "Content", "ToolTip", "Title" }.Contains(a.Name.LocalName))
                    .Select(a => a.Value).Where(v => !v.StartsWith("{", StringComparison.Ordinal))
                : Regex.Matches(Regex.Replace(content, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline), "\"((?:\\\\.|[^\"\\\\])*)\"").Select(m => m.Groups[1].Value);
            foreach (var literal in literals) Assert.False(Regex.IsMatch(literal, @"\b(Overwatch|Bazaar|engagement|CRM|canonical|UPN|staff id)\b", RegexOptions.IgnoreCase), literal);
        }
    }

    [Fact]
    public async Task Brief_LoadsOnceOnLoad_RefreshReadsAgain_AndTabsHaveNoReadHandler()
    {
        var fixture = new DirectorMarketFixture();
        var director = new Mock<IBdDirectorStore>();
        fixture.SetupSignals(director);
        director.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorPerson>());
        director.Setup(s => s.GetFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdDirectorFunnel(new Dictionary<int, double>(), Array.Empty<BdDirectorUndecidedPursuit>(), 0, 0, 0, 0));
        director.Setup(s => s.GetAccountsAsync(1000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorAccount>());
        director.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BdTargetActuals(new Dictionary<BdTargetKey, decimal>(), 0, 0));
        var claims = new Mock<IKnowledgeClaimStore>();
        fixture.SetupClaims(claims);
        claims.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeSourceSummary>());
        claims.Setup(s => s.ListConceptsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeConceptRow>());
        using var vm = Create(fixture, director.Object, claims.Object);
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal("Up to date.", vm.StatusMessage);
        Assert.True(vm.BriefSections.Count == 5, "EXPECTED five imported sections on load.");
        fixture.BriefStore.Verify(s => s.GetLatestAsync(It.IsAny<CancellationToken>()), Times.Once);
        var xaml = XDocument.Parse(Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"));
        Assert.All(xaml.Descendants().Where(e => e.Name.LocalName is "TabControl" or "TabItem"), e => Assert.Null(e.Attribute("SelectionChanged")));
        await vm.LoadAsync(CancellationToken.None);
        fixture.BriefStore.Verify(s => s.GetLatestAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Director_HasNoHttpClientOrRunAction()
    {
        Assert.DoesNotContain(typeof(BdDirectorViewModel).Assembly.GetTypes(), t => t.Name == "DirectorBriefClient");
        foreach (var file in DirectorFiles())
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("run-now", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Run now", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Citations_ResolveClaimAndPassage_AndRetiredClaimsStayVisible()
    {
        var fixture = new DirectorMarketFixture();
        var claims = new Mock<IKnowledgeClaimStore>(MockBehavior.Strict);
        fixture.SetupClaims(claims);
        using var vm = Create(fixture, new Mock<IBdDirectorStore>().Object, claims.Object);
        await vm.LoadBriefAsync(CancellationToken.None);
        var citation = vm.BriefSections[0].Citations[1];
        Assert.Contains("Retired", citation.Status, StringComparison.Ordinal);
        vm.SelectedBriefCitation = citation;
        await vm.PassageLoadTask;
        Assert.Equal(102L, vm.SelectedPassage?.Id);
        Assert.Contains("Retired", vm.CitationStatus, StringComparison.Ordinal);
        claims.Verify(s => s.GetClaimAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        claims.Verify(s => s.GetPassageAsync(102, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.BriefSections.All(s => s.Citations.Count == 2), "EXPECTED both citations retained in all five sections.");
    }

    [Fact]
    public async Task Market_LoadReadsOnce_UsesDashboardDeadlineRule_AndTabsHaveNoReadHandler()
    {
        var fixture = new DirectorMarketFixture();
        var director = new Mock<IBdDirectorStore>(MockBehavior.Strict);
        fixture.SetupSignals(director);
        using var vm = Create(fixture, director.Object, Mock.Of<IKnowledgeClaimStore>());
        await vm.LoadMarketAsync(CancellationToken.None);
        director.Verify(s => s.GetRecentSignalsAsync(30, 25, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Dashboard.Verify(s => s.GetForwardPipelineAsync(12, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Dashboard.Verify(s => s.GetOpenStructuralSeatsAsync(10, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Dashboard.Verify(s => s.GetPipelineFunnelAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Opportunities.Verify(s => s.ListAsync(It.IsAny<CancellationToken>(), 5000, false, false), Times.Once);
        Assert.True(vm.MarketSignals.Count == 3 && vm.ForwardPipeline.Count == 3 && vm.OpenSeats.Count == 2 && vm.MarketDeadlines.Count == 3,
            "EXPECTED 3 signals / 3 forward / 2 seats / 3 live 30-day deadlines.");
        Assert.DoesNotContain(vm.MarketDeadlines, d => d.Name.Contains("Excluded", StringComparison.Ordinal));
        var xaml = XDocument.Parse(Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"));
        Assert.All(xaml.Descendants().Where(e => e.Name.LocalName is "TabControl" or "TabItem"), e => Assert.Null(e.Attribute("SelectionChanged")));
    }

    [Fact]
    public async Task Acknowledge_PassesActorAndReloads_ImportedDateIsDisplayed()
    {
        var fixture = new DirectorMarketFixture();
        var claims = new Mock<IKnowledgeClaimStore>(); fixture.SetupClaims(claims);
        using var vm = Create(fixture, Mock.Of<IBdDirectorStore>(), claims.Object);
        await vm.LoadBriefAsync(CancellationToken.None);
        var row = vm.BriefSections[0];
        Assert.Equal(row.Brief.ImportedAtUtc, row.Generated);
        await vm.AcknowledgeBriefAsync(row, "Fixture actor", CancellationToken.None);
        fixture.BriefStore.Verify(s => s.AcknowledgeAsync(row.Brief.Id, "Fixture actor", It.IsAny<CancellationToken>()), Times.Once);
        fixture.BriefStore.Verify(s => s.GetLatestAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.False(vm.BriefSections[0].CanAcknowledge);
    }

    [Fact]
    public void Director_StillContributesZeroInlineDates()
    {
        foreach (var file in DirectorFiles()) Assert.False(Regex.IsMatch(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy"), file);
    }

    private static BdDirectorViewModel Create(DirectorMarketFixture fixture, IBdDirectorStore reads, IKnowledgeClaimStore claims)
    {
        var pursuits = new Mock<IPursuitOverwatchStore>();
        pursuits.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PursuitOverwatchRow>());
        var records = new Mock<ICrmEngagementStore>();
        records.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CrmEngagement>());
        var attribution = new Mock<IBdAttributionStore>();
        attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new BdAttributionSummary(0, 0m, 0, 0, 0, Array.Empty<AttributionSourceRow>(), Array.Empty<AttributionOwnerRow>()));
        var ledger = new Mock<IBdDirectorLedgerStore>();
        ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdTarget>());
        ledger.Setup(s => s.ListAsksAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdAsk>());
        ledger.Setup(s => s.ListDecisionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDecision>());
        return new(reads, pursuits.Object, records.Object, attribution.Object, ledger.Object,
            Mock.Of<IDeltekClientContextService>(), claims, fixture.Dashboard.Object, fixture.Opportunities.Object, fixture.Brief);
    }
    private static IEnumerable<string> DirectorFiles() => Directory.EnumerateFiles(Path.Combine(Root(), "Kor.Operations.App/BusinessDevelopment/Director"))
        .Where(f => Path.GetExtension(f) is ".cs" or ".xaml");
    private static string Source(string path) => File.ReadAllText(Path.Combine(Root(), path));
    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}

internal static class DirectorMarketDefaults
{
    internal static IBdDashboardStore Dashboard()
    {
        var mock = new Mock<IBdDashboardStore>();
        mock.Setup(s => s.GetForwardPipelineAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdForwardPipelineRow>());
        mock.Setup(s => s.GetOpenStructuralSeatsAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdOpenStructuralSeatRow>());
        mock.Setup(s => s.GetPipelineFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PipelineFunnel(0, 0, 0));
        return mock.Object;
    }
    internal static IOpportunityStore Opportunities()
    {
        var mock = new Mock<IOpportunityStore>();
        mock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), 5000, false, false)).ReturnsAsync(Array.Empty<Opportunity>());
        return mock.Object;
    }
    internal static IDirectorBriefStore Brief()
    {
        var mock = new Mock<IDirectorBriefStore>();
        mock.Setup(s => s.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DirectorBriefRow>());
        return mock.Object;
    }
}

internal sealed class DirectorMarketFixture
{
    internal Mock<IBdDashboardStore> Dashboard { get; } = new(MockBehavior.Strict);
    internal Mock<IOpportunityStore> Opportunities { get; } = new(MockBehavior.Strict);
    internal Mock<IDirectorBriefStore> BriefStore { get; } = new(MockBehavior.Strict);
    internal IDirectorBriefStore Brief => BriefStore.Object;
    internal IReadOnlyList<DirectorBriefRow> Rows { get; private set; } = Array.Empty<DirectorBriefRow>();
    internal DirectorMarketFixture()
    {
        var now = DateTimeOffset.UtcNow;
        Dashboard.Setup(s => s.GetForwardPipelineAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(Enumerable.Range(1, 3)
            .Select(i => new BdForwardPipelineRow(i, "Illustrative planned project " + i, "Illustrative proponent", "BC", "Education", i * 10000000m, "Planning")).ToArray());
        Dashboard.Setup(s => s.GetOpenStructuralSeatsAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(Enumerable.Range(1, 2)
            .Select(i => new BdOpenStructuralSeatRow(i, "Illustrative firm " + i, "BC", "Open", "High", "Illustrative opening for a conversation.")).ToArray());
        Dashboard.Setup(s => s.GetPipelineFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PipelineFunnel(3, 3, 2));
        var opportunities = Enumerable.Range(1, 3).Select(i => new Opportunity { Id = i, Name = "Illustrative RFP " + i, BuyerName = "Illustrative buyer", ProjectProvince = "BC", SubmissionDeadlineUtc = now.AddDays(i * 4) }).ToList();
        opportunities.Add(new Opportunity { Id = 4, Name = "Excluded: dismissed", SubmissionDeadlineUtc = now.AddDays(2), DismissedAtUtc = now });
        opportunities.Add(new Opportunity { Id = 5, Name = "Excluded: past", SubmissionDeadlineUtc = now.AddDays(-1) });
        opportunities.Add(new Opportunity { Id = 6, Name = "Excluded: beyond 30 days", SubmissionDeadlineUtc = now.AddDays(31) });
        Opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), 5000, false, false)).ReturnsAsync(opportunities);
        var citations = new[] { new DirectorCitation(1, 101, "Illustrative: confirm the buyer's need.", "Illustrative book A"),
            new DirectorCitation(2, 102, "Illustrative: agree on the next step.", "Illustrative book B") };
        Rows = new[] { "ThisWeek", "Pursuits", "Accounts", "Market", "Asks" }.Select((section, i) => new DirectorBriefRow(i + 1, DateOnly.FromDateTime(now.Date),
            section, "Illustrative brief · " + section, "Illustrative synthesis for layout review, written in session.",
            "Illustrative recommendation: arrange a short conversation.", citations, "Fixture writer", now, null, null)).ToArray();
        BriefStore.Setup(s => s.GetLatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Rows);
        BriefStore.Setup(s => s.AcknowledgeAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((long id, string by, CancellationToken ct) => Rows = Rows.Select(r => r.Id == id && r.AcknowledgedAtUtc is null
                ? r with { AcknowledgedAtUtc = DateTimeOffset.UtcNow, AcknowledgedBy = by } : r).ToArray())
            .Returns(Task.CompletedTask);
    }
    internal void SetupSignals(Mock<IBdDirectorStore> store) => store.Setup(s => s.GetRecentSignalsAsync(30, 25, It.IsAny<CancellationToken>())).ReturnsAsync(
        Enumerable.Range(1, 3).Select(i => new BdDirectorSignal(i, "Illustrative firm " + i, "RecentWin", "Illustrative early signal " + i, DateTimeOffset.UtcNow.AddDays(-i), i)).ToArray());
    internal void SetupClaims(Mock<IKnowledgeClaimStore> store)
    {
        store.Setup(s => s.GetClaimAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) =>
            new KnowledgeClaimRow(id, id, "Illustrative book " + (id == 1 ? "A" : "B"), id + 100, "Illustrative reference", "Rule", "Illustrative cited statement " + id,
                0.9m, Array.Empty<string>(), null, id == 2 ? DateTimeOffset.UnixEpoch : null, id == 2 ? "Illustrative retirement reason." : null));
        store.Setup(s => s.GetPassageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) =>
            new KnowledgePassageText(id, "Illustrative book", "Illustrative reference", "ILLUSTRATIVE passage for layout review. No book text was read to create this fixture."));
    }
}
