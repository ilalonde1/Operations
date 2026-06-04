#nullable enable
using System.Text.Json;
using Kor.Opportunities.Data.Intel;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class IntelExtractorTests
{
    [Fact]
    public void DataHoningExtractor_AnthemFixture_emitsPersonAndWork()
    {
        using var doc = LoadFixture("datahoning-anthem.json");
        var ctx = new IntelExtractionContext(207, 1, "DataHoning", doc, DateTimeOffset.UtcNow);

        var result = new DataHoningExtractor().Extract(ctx);

        Assert.Contains(result.People, p => p.DisplayName == "Eric Carlson");
        Assert.Contains(
            result.Affiliations,
            a => a.IsCurrent && a.Title?.Contains("CEO", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotEmpty(result.Works);
    }

    [Fact]
    public void DataHoningExtractor_unknownShape_returnsEmpty()
    {
        using var doc = JsonDocument.Parse("{\"unrelated\":42}");

        var result = new DataHoningExtractor().Extract(new IntelExtractionContext(1, 1, "DataHoning", doc, DateTimeOffset.UtcNow));

        Assert.Equal(0, result.People.Count + result.Works.Count + result.Affiliations.Count);
    }

    [Fact]
    public void PublicSectorResearchExtractor_departure_emitsSignalAndPastAffiliation()
    {
        using var doc = LoadFixture("publicsector-ahs.json");
        var ctx = new IntelExtractionContext(999, 1, "PublicSectorResearch", doc, DateTimeOffset.UtcNow);

        var result = new PublicSectorResearchExtractor().Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.Contains(result.Affiliations, a => a.IsCurrent == false);
        Assert.Contains(result.Signals, s => s.SignalType == "LeadershipChange");
    }

    [Fact]
    public void PublicSectorResearchExtractor_currentRole_keepsIsCurrentTrue()
    {
        using var doc = JsonDocument.Parse("{\"decisionMakers\":[{\"name\":\"Jane Doe\",\"title\":\"Director\",\"notes\":\"Joined 2024.\"}]}");
        var ctx = new IntelExtractionContext(1, 1, "PublicSectorResearch", doc, DateTimeOffset.UtcNow);

        var result = new PublicSectorResearchExtractor().Extract(ctx);

        Assert.Contains(result.Affiliations, a => a.IsCurrent == true);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void CompetitorProfileExtractor_fixture_emitsAllBuckets()
    {
        using var doc = LoadFixture("competitor-trl.json");
        var ctx = new IntelExtractionContext(99, 1, "CompetitorProfile", doc, DateTimeOffset.UtcNow);

        var result = new CompetitorProfileExtractor().Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Works);
        Assert.NotEmpty(result.Signals);
        Assert.NotEmpty(result.Risks);
        Assert.NotEmpty(result.Narratives);
        Assert.NotEmpty(result.Actions);
    }

    [Fact]
    public void CompetitorProfileExtractor_signalKeywords_classifyCorrectly()
    {
        using var doc = JsonDocument.Parse("{\"recentSignals\":[\"Hired 5 structural engineers\",\"Won UBC tower contract\",\"Acquired Vancouver office of XYZ\"]}");
        var ctx = new IntelExtractionContext(1, 1, "CompetitorProfile", doc, DateTimeOffset.UtcNow);

        var result = new CompetitorProfileExtractor().Extract(ctx);

        Assert.Equal("HiringSurge", result.Signals[0].SignalType);
        Assert.Equal("RecentWin", result.Signals[1].SignalType);
        Assert.Equal("OwnershipMnA", result.Signals[2].SignalType);
    }

    [Fact]
    public void FirmNarrativeExtractor_threeParagraphs_yieldThreeNarrativesPlusAction()
    {
        using var doc = LoadFixture("firmnarrative-sfu.json");
        var ctx = new IntelExtractionContext(99, 1, "FirmNarrative", doc, DateTimeOffset.UtcNow);

        var result = new FirmNarrativeExtractor().Extract(ctx);

        Assert.Contains(result.Narratives, n => n.NarrativeType == "Current");
        Assert.Contains(result.Narratives, n => n.NarrativeType == "History");
        Assert.Contains(result.Narratives, n => n.NarrativeType == "Action");
        Assert.Contains(result.Actions, a => a.ActionType == "PursuitAngle");
    }

    [Fact]
    public void FirmNarrativeExtractor_lowConfidence_yieldsLow()
    {
        using var doc = JsonDocument.Parse("{\"paragraphCurrent\":\"text\",\"overallConfidence\":0.45}");

        var result = new FirmNarrativeExtractor().Extract(new IntelExtractionContext(1, 1, "FirmNarrative", doc, DateTimeOffset.UtcNow));

        Assert.All(result.Narratives, n => Assert.Equal(IntelConfidence.Low, n.Confidence));
        Assert.All(result.Actions, a => Assert.Equal(IntelConfidence.Low, a.Confidence));
    }

    [Fact]
    public void FirmNarrativeExtractor_dataDependenciesPresent_yieldsLow()
    {
        using var doc = JsonDocument.Parse("{\"paragraphCurrent\":\"text\",\"overallConfidence\":0.9,\"dataDependencies\":[\"needs verification\"]}");

        var result = new FirmNarrativeExtractor().Extract(new IntelExtractionContext(1, 1, "FirmNarrative", doc, DateTimeOffset.UtcNow));

        Assert.All(result.Narratives, n => Assert.Equal(IntelConfidence.Low, n.Confidence));
        Assert.All(result.Actions, a => Assert.Equal(IntelConfidence.Low, a.Confidence));
    }

    private static JsonDocument LoadFixture(string filename)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Intel", "Fixtures", filename);
        var text = System.IO.File.ReadAllText(path);
        if (text.TrimStart().StartsWith("// TODO", StringComparison.Ordinal))
        {
            throw new System.IO.InvalidDataException("Fixture not yet populated: " + filename);
        }

        return JsonDocument.Parse(text);
    }
}
