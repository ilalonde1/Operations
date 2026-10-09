using System.Net;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

public sealed class GenericJsonOpportunityProviderTests
{
    private const string Item = """
        {
          "title": " Development permit ",
          "buyer": " City of Calgary ",
          "location": " 123 Main Street ",
          "url": " https://example.test/permits/DP123 ",
          "description": " Original description. ",
          "details": { "text": " Custom description. " },
          "postedDate": "2026-10-01T12:00:00Z",
          "deadline": "2026-10-31T12:00:00Z",
          "city": " Calgary ",
          "province": " AB ",
          "value": "1,234.50",
          "reference": " DP123 ",
          "applicant": " Example Architect ",
          "parties": [{ "name": " Example Developer " }]
        }
        """;

    private static Dictionary<string, string> Config() => new()
    {
        ["json.cityPath"] = "city",
        ["json.provincePath"] = "province",
        ["json.estimatedValuePath"] = "value",
        ["json.externalReferencePath"] = "reference"
    };

    // Fixed pre-change result: record equality compares every candidate property,
    // including the untouched raw payload and null optional fields.
    private static OpportunityCandidate LegacyCandidate => new()
    {
        Title = "Development permit",
        Buyer = "City of Calgary",
        Location = "123 Main Street",
        Url = "https://example.test/permits/DP123",
        Description = "Original description.",
        PostedDateUtc = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        SubmissionDeadlineUtc = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
        ProjectCity = "Calgary",
        ProjectProvince = "AB",
        EstimatedValueCad = 1234.50m,
        ExternalReference = "DP123",
        RawJson = Item
    };

    [Theory]
    [InlineData(null, null, "Original description.")]
    [InlineData("", null, "Original description.")]
    [InlineData("   ", null, "Original description.")]
    [InlineData(null, "details.text", "Custom description.")]
    [InlineData(null, "missing", null)]
    public async Task FetchAsync_WithoutApplicantPath_MatchesLegacyCandidate(
        string? applicantPath, string? descriptionPath, string? description)
    {
        var config = Config();
        if (applicantPath is not null)
            config["json.applicantPath"] = applicantPath;
        if (descriptionPath is not null)
            config["json.descriptionPath"] = descriptionPath;

        Assert.Equal(LegacyCandidate with { Description = description }, await FetchAsync(Item, config));
    }

    [Theory]
    [InlineData("applicant", "description", "Example Architect", "Original description.")]
    [InlineData(" parties[0].name ", "details.text", "Example Developer", "Custom description.")]
    [InlineData("applicant", "missing", "Example Architect", null)]
    public async Task FetchAsync_WithApplicantPath_PopulatesContactAndPrefixesDescription(
        string applicantPath, string descriptionPath, string applicant, string? description)
    {
        var config = Config();
        config["json.applicantPath"] = applicantPath;
        config["json.descriptionPath"] = descriptionPath;

        Assert.Equal(LegacyCandidate with
        {
            BuyerContactName = applicant,
            Description = $"Applicant: {applicant}. {description}".Trim()
        }, await FetchAsync(Item, config));
    }

    [Fact]
    public async Task FetchAsync_WithLongApplicant_TruncatesContactTo200Characters()
    {
        var applicant = new string('A', 205);
        var item = JsonSerializer.Serialize(new
        {
            title = "Permit",
            url = "https://example.test/permit",
            applicant = $"  {applicant}  ",
            description = "Original description."
        });
        var config = Config();
        config["json.applicantPath"] = "applicant";

        var candidate = await FetchAsync(item, config);

        Assert.Equal(new string('A', 200), candidate.BuyerContactName);
        Assert.Equal($"Applicant: {applicant}. Original description.", candidate.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FetchAsync_WithEmptyApplicant_PreservesLegacyCandidate(string? applicant)
    {
        var item = Item.Replace("\" Example Architect \"", JsonSerializer.Serialize(applicant));
        var config = Config();
        config["json.applicantPath"] = "applicant";

        Assert.Equal(LegacyCandidate with { RawJson = item }, await FetchAsync(item, config));
    }

    [Fact]
    public async Task FetchAsync_WithMissingApplicant_PreservesLegacyCandidate()
    {
        var config = Config();
        config["json.applicantPath"] = "missing.name";

        Assert.Equal(LegacyCandidate, await FetchAsync(Item, config));
    }

    private static async Task<OpportunityCandidate> FetchAsync(string item, Dictionary<string, string> config)
    {
        using var handler = new StubHandler($"[{item}]");
        using var http = new HttpClient(handler);
        var provider = new GenericJsonOpportunityProvider(http, NullLogger<GenericJsonOpportunityProvider>.Instance);
        var source = new OpportunitySource
        {
            Name = "Test JSON feed",
            SourceType = OpportunitySourceType.GenericJson,
            BaseUrl = "https://example.test/feed",
            RequestTimeoutSeconds = 30
        };

        return Assert.Single(await provider.FetchAsync(source, config, CancellationToken.None));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
