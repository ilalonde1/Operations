#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// BC Housing project pages, read from two real pages saved 2026-10-16: Nanaimo Fifth Street (a
/// full timeline, M'akola as operator) and Kamloops Fortune Drive (no timeline at all).
/// Covers: the sitemap filter, title/city split, address, timeline statuses, the operator sentence,
/// the candidate the pipeline stores, and that the relevance gate keeps it.
/// Does NOT cover: the live site's markup drifting (the run's "no pages" throw and the
/// per-page failure warning do), or how the opportunity is matched to the city's permit row.
/// </summary>
public sealed class BcHousingProjectsProviderTests
{
    private const string FifthUrl = "https://www.bchousing.org/projects-partners/projects/nanaimo-fifth-street-redevelopment";
    private const string KamloopsUrl = "https://www.bchousing.org/projects-partners/projects/kamloops-fortune-drive-and-leigh-road";

    [Fact]
    public void TheSitemapGivesOneUrlPerProjectAndNothingElse()
    {
        var xml = "<urlset><url><loc>https://www.bchousing.org/projects-partners/projects/nanaimo-fifth-street-redevelopment</loc></url>"
                  + "<url><loc>https://www.bchousing.org/projects-partners/projects/nanaimo-fifth-street-redevelopment</loc></url>"
                  + "<url><loc>https://www.bchousing.org/projects-partners/projects</loc></url>"
                  + "<url><loc>https://www.bchousing.org/projects-partners/projects/x/y</loc></url>"
                  + "<url><loc>https://www.bchousing.org/housing-assistance</loc></url>"
                  + "<url><loc>https://www.bchousing.org/projects-partners/projects/nanaimo-250-Bryden-Street</loc></url></urlset>";

        var urls = BcHousingProjectsProvider.ProjectUrls(xml);

        Assert.Equal(new[] { FifthUrl, "https://www.bchousing.org/projects-partners/projects/nanaimo-250-Bryden-Street" }, urls);
    }

    [Fact]
    public void FifthStreetReadsAsTheProjectItIs()
    {
        var p = BcHousingProjectsProvider.Parse(Fixture("bchousing-nanaimo-fifth-street-20261016.html"), FifthUrl)!;

        Assert.Equal("nanaimo-fifth-street-redevelopment", p.Slug);
        Assert.Equal("Nanaimo", p.City);
        Assert.Equal("Fifth Street Redevelopment", p.Name);
        Assert.Equal("564 Fifth Street", p.Address);
        Assert.Contains("167 new rental homes", p.Overview);
        Assert.DoesNotContain("Twitter", p.Overview);
        Assert.Contains(p.Parties, s => s.Contains("M'akola Housing Society will manage", StringComparison.Ordinal));

        Assert.Equal(6, p.Timeline.Count);
        Assert.Equal("2023", p.Timeline[0].When);
        Assert.Equal("completed", p.Timeline[0].Status);
        Assert.Equal("2026", p.Current!.When);
        Assert.Contains("Phase 1 Development Permit", p.Current.Steps);
        Assert.Equal("2027/2028", p.Next!.When);
        Assert.Contains("Phase 1 Building Permit", p.Next.Steps);
    }

    [Fact]
    public void TheCandidateLeadsWithNowAndNextAndKeysOnTheSlug()
    {
        var c = BcHousingProjectsProvider.ToCandidate(
            BcHousingProjectsProvider.Parse(Fixture("bchousing-nanaimo-fifth-street-20261016.html"), FifthUrl)!);

        Assert.Equal("BC Housing", c.Buyer);
        Assert.Equal("nanaimo-fifth-street-redevelopment", c.ExternalReference);
        Assert.Equal("564 Fifth Street, Nanaimo", c.Location);
        Assert.Equal("Nanaimo", c.ProjectCity);
        Assert.Equal("BC", c.ProjectProvince);
        Assert.StartsWith("BC Housing project. Now: 2026: Rezoning complete; Phase 1 Development Permit. Next: 2027/2028: Phase 1 Building Permit", c.Description);
        Assert.Contains("[completed] 2023: Draft design concept", c.Description);
        Assert.True(c.Description!.Length <= 4000);
    }

    [Fact]
    public void APageWithoutATimelineStillBecomesAProject()
    {
        var p = BcHousingProjectsProvider.Parse(Fixture("bchousing-kamloops-no-timeline-20261016.html"), KamloopsUrl)!;

        Assert.Equal("Kamloops", p.City);
        Assert.Empty(p.Timeline);
        Assert.Null(p.Current);
        var c = BcHousingProjectsProvider.ToCandidate(p);
        Assert.StartsWith("BC Housing project.", c.Description);
        Assert.DoesNotContain("Now:", c.Description);
    }

    [Fact]
    public void TheRelevanceGateKeepsBothPages()
    {
        foreach (var (file, url) in new[] { ("bchousing-nanaimo-fifth-street-20261016.html", FifthUrl), ("bchousing-kamloops-no-timeline-20261016.html", KamloopsUrl) })
        {
            var c = BcHousingProjectsProvider.ToCandidate(BcHousingProjectsProvider.Parse(Fixture(file), url)!);
            var verdict = StructuralRelevanceGate.Evaluate(c.Title, c.Description, c.Buyer);
            Assert.True(verdict.Keep, $"{file}: {verdict.RejectReason}");
        }
    }

    [Fact]
    public async Task FetchReadsTheSitemapThenEachPage()
    {
        var sitemap = $"<urlset><url><loc>{FifthUrl}</loc></url><url><loc>{KamloopsUrl}</loc></url></urlset>";
        var bodies = new Dictionary<string, string>
        {
            [BcHousingProjectsProvider.DefaultSitemapUrl] = sitemap,
            [FifthUrl] = Fixture("bchousing-nanaimo-fifth-street-20261016.html"),
            [KamloopsUrl] = Fixture("bchousing-kamloops-no-timeline-20261016.html"),
        };
        using var http = new HttpClient(new RoutingHandler(bodies));
        var provider = new BcHousingProjectsProvider(http, NullLogger<BcHousingProjectsProvider>.Instance);
        var source = new OpportunitySource { Id = Guid.NewGuid(), Name = "BCHousing_Projects", SourceType = OpportunitySourceType.BcHousingProjects, BaseUrl = "https://www.bchousing.org" };

        var rows = await provider.FetchAsync(source, new Dictionary<string, string> { ["bchousing.delayMs"] = "0" }, CancellationToken.None);

        Assert.Equal(new[] { "nanaimo-fifth-street-redevelopment", "kamloops-fortune-drive-and-leigh-road" }, rows.Select(r => r.ExternalReference));
    }

    [Fact]
    public async Task ASitemapWithNoProjectPagesFailsTheRunInsteadOfReturningNothing()
    {
        using var http = new HttpClient(new RoutingHandler(new Dictionary<string, string> { [BcHousingProjectsProvider.DefaultSitemapUrl] = "<urlset></urlset>" }));
        var provider = new BcHousingProjectsProvider(http, NullLogger<BcHousingProjectsProvider>.Instance);
        var source = new OpportunitySource { Id = Guid.NewGuid(), Name = "BCHousing_Projects", SourceType = OpportunitySourceType.BcHousingProjects, BaseUrl = "https://www.bchousing.org" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchAsync(source, new Dictionary<string, string>(), CancellationToken.None));
    }

    private static string Fixture(string name)
    {
        using var stream = typeof(BcHousingProjectsProviderTests).Assembly.GetManifestResourceStream("Kor.Opportunities.Data.Tests.Fixtures." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _bodies;
        public RoutingHandler(IReadOnlyDictionary<string, string> bodies) => _bodies = bodies;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_bodies.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
