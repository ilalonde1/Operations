#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.App.Options;
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
/// Covers eight-tab/caption/literal/date contracts, fake-handler Basic auth/routes,
/// market read sizes/frequency, Dashboard deadline semantics, retained retired citations,
/// shared passage resolution and run-now disabling/reload. No real HTTP, SQL, model,
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
        Assert.Equal("The Director's brief: five questions a week over these tabs and the rule bank, answered with the claims each answer rests on. Open a citation to read the passage.", BdDirectorViewModel.BriefCaption);
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
    public async Task Client_UsesMcpOptions_BasicAuth_AndDirectorRoutes()
    {
        using var fixture = new DirectorMarketFixture();
        var latest = await fixture.Brief.GetLatestAsync(CancellationToken.None);
        await fixture.Brief.RunNowAsync(CancellationToken.None);
        await fixture.Brief.AcknowledgeAsync(1, CancellationToken.None);
        Assert.True(latest.Count == 5, "EXPECTED five synthetic sections.");
        Assert.Equal(new[] { "/director-brief/latest", "/director-brief/run-now", "/director-brief/1/acknowledge" }, fixture.Handler.Requests.Select(r => r.Path));
        Assert.All(fixture.Handler.Requests, r => Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture:fixture")), r.Auth));
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Post }, fixture.Handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task Citations_ResolveClaimAndPassage_AndRetiredClaimsStayVisible()
    {
        using var fixture = new DirectorMarketFixture();
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
        using var fixture = new DirectorMarketFixture();
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
    public async Task RunNow_DisablesWhilePending_ThenReloads()
    {
        using var fixture = new DirectorMarketFixture();
        fixture.Handler.PendingRun = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claims = new Mock<IKnowledgeClaimStore>(); fixture.SetupClaims(claims);
        using var vm = Create(fixture, Mock.Of<IBdDirectorStore>(), claims.Object);
        var run = vm.RunBriefAsync(CancellationToken.None);
        Assert.True(vm.BriefRunning && !vm.CanRunBrief);
        await vm.RunBriefAsync(CancellationToken.None);
        fixture.Handler.PendingRun.SetResult(true);
        await run;
        Assert.True(!vm.BriefRunning && vm.CanRunBrief && vm.BriefSections.Count == 5);
        Assert.True(fixture.Handler.Requests.Count(r => r.Path.EndsWith("run-now", StringComparison.Ordinal)) == 1);
    }

    [Fact]
    public void Director_StillContributesZeroInlineDates()
    {
        foreach (var file in DirectorFiles()) Assert.False(Regex.IsMatch(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy"), file);
    }

    private static BdDirectorViewModel Create(DirectorMarketFixture fixture, IBdDirectorStore reads, IKnowledgeClaimStore claims) => new(reads,
        Mock.Of<IPursuitOverwatchStore>(), Mock.Of<ICrmEngagementStore>(), Mock.Of<IBdAttributionStore>(), Mock.Of<IBdDirectorLedgerStore>(),
        Mock.Of<IDeltekClientContextService>(), claims, fixture.Dashboard.Object, fixture.Opportunities.Object, fixture.Brief);
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
    internal static DirectorBriefClient Brief() => new(new McpServerOptions());
}

internal sealed class DirectorMarketFixture : IDisposable
{
    internal Mock<IBdDashboardStore> Dashboard { get; } = new(MockBehavior.Strict);
    internal Mock<IOpportunityStore> Opportunities { get; } = new(MockBehavior.Strict);
    internal BriefHandler Handler { get; } = new();
    private readonly HttpClient _http;
    internal DirectorBriefClient Brief { get; }
    internal DirectorMarketFixture()
    {
        _http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
        Brief = new DirectorBriefClient(new McpServerOptions { ServiceUrl = "https://fixture.invalid", Username = "fixture", Password = "fixture" }, _http);
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
        var citations = JsonSerializer.Serialize(new[] { new DirectorCitationDto(1, 101, "Illustrative: confirm the buyer's need.", "Illustrative book A"),
            new DirectorCitationDto(2, 102, "Illustrative: agree on the next step.", "Illustrative book B") });
        Handler.Rows = new[] { "ThisWeek", "Pursuits", "Accounts", "Market", "Asks" }.Select((section, i) => new DirectorBriefDto(i + 1, now.UtcDateTime, now.Date,
            section, "Illustrative brief · " + section, "Illustrative synthesis for layout review, not generated from firm records.",
            "Illustrative recommendation: arrange a short conversation.", 0, 0, 0, null, null, citations)).ToArray();
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
    public void Dispose() => _http.Dispose();
    internal sealed class BriefHandler : HttpMessageHandler
    {
        internal IReadOnlyList<DirectorBriefDto> Rows { get; set; } = Array.Empty<DirectorBriefDto>();
        internal List<(string Path, HttpMethod Method, string? Auth)> Requests { get; } = new();
        internal TaskCompletionSource<bool>? PendingRun { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, request.Method, request.Headers.Authorization?.ToString()));
            if (path.EndsWith("run-now", StringComparison.Ordinal) && PendingRun is not null) await PendingRun.Task.WaitAsync(ct);
            return request.Method == HttpMethod.Get ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Rows), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
