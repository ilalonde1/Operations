#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// WHAT THESE COVER: the two things that are DIFFERENT PER JURISDICTION on one
/// shared platform, and that fail silently rather than loudly.
///
///  1. The planning file number. The RDN writes PL2026-028; BC municipalities on
///     the same EngagementHQ platform write DP-2026-00529. That number becomes
///     the OpportunityKey, so getting it wrong does not error — it mints a
///     SECOND copy of an application already on file. That is not hypothetical:
///     it happened on 2026-09-10 and cost 20 duplicate Vancouver rows
///     (migration 321).
///  2. The applicant. Vancouver's descriptions open "Arcadis Architects has
///     applied to the City of Vancouver…", which is the only place in the whole
///     feed the design team is named, and the entire reason to read this source.
///     Reading the wrong span puts a wrong firm on a lead somebody will call.
///
/// The file-number tests are a DIFFERENTIAL: both jurisdictions are asserted in
/// the same run, so broadening the pattern for one and breaking the other fails
/// here rather than in production.
///
/// WHAT THEY DO NOT COVER: anything downstream — the relevance gate, dedup
/// against rows from other sources, or whether a given tenant's projects.json is
/// development applications at all rather than budget consultations. They also
/// do not check that the applicant we read is the CORRECT firm, only that it is
/// the span the description offers. A same-class fault they would NOT catch: a
/// tenant that writes "The applicant, Arcadis Architects, has applied…", where
/// the opening clause parses cleanly and yields "The applicant" — the stop-list
/// rejects that one, but a phrasing not on the list would yield prose. That is
/// caught only by looking at what a source actually delivered.
/// </summary>
public sealed class EngagementHqOpportunityProviderTests
{
    private static readonly Dictionary<string, string> VancouverConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["engagementhq.projectsUrl"] = "https://shapeyourcity.test/projects.json",
        ["engagementhq.buyerOverride"] = "City of Vancouver",
        ["engagementhq.cityOverride"] = "Vancouver",
        ["engagementhq.provinceOverride"] = "BC",
        ["engagementhq.includeArchived"] = "false",
    };

    private static readonly Dictionary<string, string> RdnConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["engagementhq.projectsUrl"] = "https://getinvolved.test/projects.json",
        ["engagementhq.buyerOverride"] = "Regional District of Nanaimo",
        ["engagementhq.provinceOverride"] = "BC",
        ["engagementhq.includeArchived"] = "false",
    };

    [Fact]
    public async Task ABcMunicipalDpNumberBecomesTheExternalReference()
    {
        var feed = Feed(
            Project(1378, "1378 E 26th Ave (DP-2026-00529) development application", "1378-e-26-ave",
                "Raj Home Design has applied to the City of Vancouver for permission to develop a laneway house."));

        var results = await FetchAsync(VancouverConfig, feed);

        var one = Assert.Single(results);
        Assert.Equal("DP-2026-00529", one.ExternalReference);
        Assert.Equal("City of Vancouver", one.Buyer);
        Assert.Equal("Vancouver", one.ProjectCity);
    }

    [Fact]
    public async Task TheRdnFileNumberStillWinsAfterTheBcMunicipalPatternWasAdded()
    {
        // The differential. Broadening the pattern for Vancouver must leave the
        // jurisdiction it was originally written for exactly as it was.
        var feed = Feed(
            Project(900, "PL2026-028 Zoning Amendment - 1234 Island Hwy", "pl2026-028",
                "An application to rezone the subject property."));

        var results = await FetchAsync(RdnConfig, feed);

        Assert.Equal("PL2026-028", Assert.Single(results).ExternalReference);
    }

    [Fact]
    public async Task WithNoFileNumberInTheTitleThePlatformIdIsTheReference()
    {
        // Vancouver rezonings carry no number in the title. They are still the
        // earliest signal there is, so they must ingest — just on a weaker key.
        var feed = Feed(
            Project(51019, "1166 W Pender St rezoning application", "1166-w-pender-st",
                "An application to rezone the subject site."));

        Assert.Equal("51019", Assert.Single(await FetchAsync(VancouverConfig, feed)).ExternalReference);
    }

    [Fact]
    public async Task TheOpeningClauseNamesTheApplicant()
    {
        var feed = Feed(
            Project(1394, "1394 Robson St (DP-2026-00432) development application", "1394-robson-st",
                "<p>Arcadis Architects has applied to the City of Vancouver for a development "
                + "application to develop a 19-storey, mixed-use building.</p>"));

        Assert.Equal("Arcadis Architects", Assert.Single(await FetchAsync(VancouverConfig, feed)).BuyerContactName);
    }

    [Theory]
    // The sentence subject is not always a party, and a wrong firm on a lead is
    // worse than no firm, because it is the field somebody will act on.
    [InlineData("The City of Vancouver has applied for permission to develop this site.")]
    [InlineData("Council has applied its policy to the subject site.")]
    [InlineData("The applicant has applied to the City of Vancouver for permission.")]
    [InlineData("An application to rezone the subject property was received.")]
    public async Task AClauseThatNamesNoPartyLeavesTheApplicantEmpty(string description)
    {
        var feed = Feed(
            Project(1, "123 Main St (DP-2026-00001) development application", "123-main-st", description));

        Assert.Null(Assert.Single(await FetchAsync(VancouverConfig, feed)).BuyerContactName);
    }

    [Fact]
    public async Task ArchivedProjectsAreDroppedUnlessAskedFor()
    {
        // Archived means decided, and by then the structural engineer is chosen.
        var feed = Feed(
            Project(1, "1 A St (DP-2026-00001) development application", "1-a-st", "X Architects has applied.",
                archived: true),
            Project(2, "2 B St (DP-2026-00002) development application", "2-b-st", "Y Architects has applied."));

        Assert.Equal("DP-2026-00002",
            Assert.Single(await FetchAsync(VancouverConfig, feed)).ExternalReference);

        var withArchived = new Dictionary<string, string>(VancouverConfig, StringComparer.OrdinalIgnoreCase)
        {
            ["engagementhq.includeArchived"] = "true",
        };
        Assert.Equal(2, (await FetchAsync(withArchived, feed)).Count);
    }

    [Fact]
    public async Task ThePlatformCarriesFarMoreThanApplicationsAndTheRestIsDropped()
    {
        var feed = Feed(
            Project(1, "1 A St (DP-2026-00001) development application", "1-a-st", "X has applied."),
            Project(2, "2026 Budget consultation", "budget-2026", "Tell us your priorities."),
            Project(3, "Stanley Park cycling route", "stanley-park", "Share your thoughts."),
            Project(4, "1166 W Pender St rezoning application", "1166-w-pender", "A rezoning."));

        var results = await FetchAsync(VancouverConfig, feed);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.DoesNotContain("Budget", r.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task APayloadWithoutAProjectsArrayFailsLoudly()
    {
        // The platform changing shape must stop the run, not quietly ingest zero
        // and let a source look healthy while it goes stale.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => FetchAsync(VancouverConfig, "{\"data\":[]}"));
    }

    private static string Feed(params object[] projects)
        => JsonSerializer.Serialize(new { projects });

    private static object Project(long id, string name, string permalink, string description,
        bool archived = false)
        => new
        {
            id,
            name,
            permalink,
            description,
            archived,
            created_at = "2026-08-12T15:19:39-07:00",
            published_at = "2026-08-12T15:19:39-07:00",
        };

    private static async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        IReadOnlyDictionary<string, string> cfg, string body)
    {
        var handler = new StubHandler(body);
        using var http = new HttpClient(handler);
        var provider = new EngagementHqOpportunityProvider(
            http, NullLogger<EngagementHqOpportunityProvider>.Instance);

        var source = new OpportunitySource
        {
            Id = Guid.NewGuid(),
            Name = "Test_DevelopmentApplications",
            SourceType = OpportunitySourceType.EngagementHq,
            BaseUrl = "https://shapeyourcity.test",
            RequestTimeoutSeconds = 30,
        };

        return await provider.FetchAsync(source, cfg, CancellationToken.None);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
