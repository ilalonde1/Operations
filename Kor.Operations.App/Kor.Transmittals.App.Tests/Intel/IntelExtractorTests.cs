#nullable enable
using System.Text.Json;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Worker.Jobs;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    // The "FirmNarrative" provider is served by CanonicalSchemaExtractor("FirmNarrative")
    // (IntelExtractorBootstrap) against the canonical decisionMakers/signals/actions/
    // narratives schema that AnthropicResearchExecutorService now emits. The old
    // paragraphCurrent/History/Action extractor and its three tests were deleted with it;
    // the canonical shape is covered by the CanonicalSchemaExtractor tests below.

    [Fact]
    public void CanonicalSchemaExtractor_pacnw_emitsAllBuckets()
    {
        using var doc = LoadFixture("canonical-pacnw-mithun.json");
        var ctx = new IntelExtractionContext(99, 1, "PacNWMarketResearch", doc, DateTimeOffset.UtcNow);

        var result = new CanonicalSchemaExtractor("PacNWMarketResearch").Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Signals);
        Assert.NotEmpty(result.Actions);
        Assert.NotEmpty(result.Works);
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void CanonicalSchemaExtractor_unknownProvider_stillExtracts()
    {
        using var doc = LoadFixture("canonical-pacnw-mithun.json");
        var ctx = new IntelExtractionContext(99, 1, "UnknownX", doc, DateTimeOffset.UtcNow);

        var result = new CanonicalSchemaExtractor("PacNWMarketResearch").Extract(ctx);

        Assert.NotEmpty(result.People);
    }

    [Fact]
    public void CanonicalSchemaExtractor_emptyArrays_returnsEmpty()
    {
        using var doc = JsonDocument.Parse("{\"decisionMakers\":[],\"signals\":[]}");

        var result = new CanonicalSchemaExtractor("PacNWMarketResearch")
            .Extract(new IntelExtractionContext(1, 1, "PacNWMarketResearch", doc, DateTimeOffset.UtcNow));

        Assert.Equal(
            0,
            result.People.Count
            + result.Affiliations.Count
            + result.Signals.Count
            + result.Actions.Count
            + result.Works.Count
            + result.Risks.Count
            + result.Narratives.Count);
    }

    [Fact]
    public void CompetitorProfileExtractor_constructedAsContractorResearch_handlesShape()
    {
        using var doc = LoadFixture("contractor-research.json");
        var ctx = new IntelExtractionContext(99, 1, "ContractorResearch", doc, DateTimeOffset.UtcNow);

        var result = new CompetitorProfileExtractor("ContractorResearch").Extract(ctx);

        Assert.Equal("ContractorResearch", new CompetitorProfileExtractor("ContractorResearch").ProviderName);
        Assert.NotEmpty(result.People);
    }

    [Fact]
    public void CompetitorProfileExtractor_defaultConstructor_keepsCompetitorProfileName()
    {
        Assert.Equal("CompetitorProfile", new CompetitorProfileExtractor().ProviderName);
    }

    [Fact]
    public void ArchitectPipelineResearchExtractor_skippedTrue_returnsEmpty()
    {
        using var doc = JsonDocument.Parse("{\"skipped\":true,\"skipReason\":\"no data\"}");
        var ctx = new IntelExtractionContext(1, 1, "ArchitectPipelineResearch", doc, DateTimeOffset.UtcNow);

        var result = new ArchitectPipelineResearchExtractor().Extract(ctx);

        Assert.Equal(
            0,
            result.People.Count
            + result.Affiliations.Count
            + result.Signals.Count
            + result.Actions.Count
            + result.Works.Count
            + result.Risks.Count
            + result.Narratives.Count);
    }

    [Fact]
    public void ArchitectPipelineResearchExtractor_wrappedResultJson_emitsAllBuckets()
    {
        using var doc = LoadFixture("architect-pipeline.json");
        var ctx = new IntelExtractionContext(99, 1, "ArchitectPipelineResearch", doc, DateTimeOffset.UtcNow);

        var result = new ArchitectPipelineResearchExtractor().Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Works);
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void ArchitectPipelineResearchExtractor_structuralPartnersStrings_emitNarratives()
    {
        var inner = "{\"structuralPartners\":[\"Fast + Epp\",\"RJC Engineers\"]}";
        var outer = "{\"skipped\":false,\"confidence\":0.8,\"resultJson\":" + JsonSerializer.Serialize(inner) + "}";
        using var doc = JsonDocument.Parse(outer);
        var ctx = new IntelExtractionContext(99, 1, "ArchitectPipelineResearch", doc, DateTimeOffset.UtcNow);

        var result = new ArchitectPipelineResearchExtractor().Extract(ctx);

        Assert.Equal(2, result.Narratives.Count);
        Assert.All(result.Narratives, n =>
        {
            Assert.Equal("Summary", n.NarrativeType);
            Assert.Contains("Recurring structural partner", n.ParagraphText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DeveloperPipelineResearchExtractor_snakeCase_emitsAllBuckets()
    {
        using var doc = LoadFixture("developer-pipeline-snake.json");
        var ctx = new IntelExtractionContext(99, 1, "DeveloperPipelineResearch", doc, DateTimeOffset.UtcNow);

        var result = new DeveloperPipelineResearchExtractor().Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Works);
        Assert.NotEmpty(result.Actions);
    }

    [Fact]
    public void DeveloperPipelineResearchExtractor_camelCase_alsoWorks()
    {
        using var doc = JsonDocument.Parse("{\"keyContacts\":[{\"name\":\"X\",\"title\":\"VP\"}],\"pipelineProjects\":[{\"projectName\":\"P1\"}],\"korRelationshipSignal\":\"engage X\"}");
        var ctx = new IntelExtractionContext(99, 1, "DeveloperPipelineResearch", doc, DateTimeOffset.UtcNow);

        var result = new DeveloperPipelineResearchExtractor().Extract(ctx);

        Assert.Equal(1, result.People.Count);
        Assert.Equal(1, result.Works.Count);
        Assert.Equal(1, result.Actions.Count);
    }

    [Fact]
    public void PersonListExtractor_decisionMakers_emitsPersonAndAffiliation()
    {
        using var doc = LoadFixture("personlist-decisionmakers.json");
        var ctx = new IntelExtractionContext(99, 1, "DecisionMakers", doc, DateTimeOffset.UtcNow);

        var result = new PersonListExtractor("DecisionMakers").Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Affiliations);
    }

    [Fact]
    public void PersonListExtractor_korConnection_emitsAction()
    {
        using var doc = JsonDocument.Parse("{\"people\":[{\"name\":\"X\",\"title\":\"VP\",\"korConnection\":\"Worked with KOR on Y project\"}]}");
        var ctx = new IntelExtractionContext(99, 1, "DecisionMakers", doc, DateTimeOffset.UtcNow);

        var result = new PersonListExtractor("DecisionMakers").Extract(ctx);

        var action = Assert.Single(result.Actions);
        Assert.Equal("ContactStrategy", action.ActionType);
    }

    [Fact]
    public void CompetitorSignalsExtractor_arrayFields_emitTypedSignals()
    {
        using var doc = JsonDocument.Parse("{\"hiringSignals\":[\"Hired 5\"],\"leadershipMoves\":[\"X promoted\"],\"officeChanges\":[\"New SEA office\"],\"ownershipMnA\":[\"Acquired Z\"],\"capacityRead\":\"Booked through 2027\"}");
        var ctx = new IntelExtractionContext(99, 1, "CompetitorSignals", doc, DateTimeOffset.UtcNow);

        var result = new CompetitorSignalsExtractor().Extract(ctx);

        Assert.Contains(result.Signals, s => s.SignalType == "HiringSurge");
        Assert.Contains(result.Signals, s => s.SignalType == "LeadershipChange");
        Assert.Contains(result.Signals, s => s.SignalType == "OfficeMove");
        Assert.Contains(result.Signals, s => s.SignalType == "OwnershipMnA");
        Assert.Contains(result.Signals, s => s.SignalType == "CapacityStrain");
        Assert.Contains(result.Risks, r => r.RiskType == "CapacityStrain");
    }

    [Fact]
    public void StructuralPartnerMapExtractor_richFixture_emitsMultipleBuckets()
    {
        using var doc = LoadFixture("structural-partner-map.json");
        var ctx = new IntelExtractionContext(99, 1, "StructuralPartnerMap", doc, DateTimeOffset.UtcNow);

        var result = new StructuralPartnerMapExtractor().Extract(ctx);

        Assert.NotEmpty(result.People);
        Assert.NotEmpty(result.Works);
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void IncumbentRostersExtractor_howToGetOnRoster_emitsAction()
    {
        using var doc = JsonDocument.Parse("{\"howToGetOnRoster\":\"Submit RFI in Q3\",\"incumbentFirms\":[\"A\",\"B\"],\"discipline\":\"Structural\"}");
        var ctx = new IntelExtractionContext(99, 1, "IncumbentRosters", doc, DateTimeOffset.UtcNow);

        var result = new IncumbentRostersExtractor().Extract(ctx);

        Assert.Contains(result.Actions, a => a.ActionType == "HowToGetOnRoster");
        Assert.True(result.Narratives.Count >= 2);
    }

    [Fact]
    public void InstitutionalOwnerResearchExtractor_korRelevanceReason_emitsAction()
    {
        using var doc = JsonDocument.Parse("{\"structuralRelevance\":\"primary structural buyer\",\"korRelevanceReason\":\"engage VP Facilities directly\"}");
        var ctx = new IntelExtractionContext(99, 1, "InstitutionalOwnerResearch", doc, DateTimeOffset.UtcNow);

        var result = new InstitutionalOwnerResearchExtractor().Extract(ctx);

        Assert.Contains(result.Actions, a => a.ActionType == "PursuitAngle");
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void PrimeTargetingExtractor_objectKorRelationship_extractsRationale()
    {
        using var doc = JsonDocument.Parse("{\"rationale\":\"Tier 1 fit\",\"korRelationship\":{\"rationale\":\"prior win with KOR\",\"score\":0.9},\"priorityRank\":\"High\"}");
        var ctx = new IntelExtractionContext(99, 1, "PrimeTargeting", doc, DateTimeOffset.UtcNow);

        var result = new PrimeTargetingExtractor().Extract(ctx);

        Assert.Contains(result.Actions, a => a.ActionType == "PursuitAngle");
        Assert.Contains(result.Actions, a => a.ActionType == "ContactStrategy");
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void SubConsultantExtractor_teamsWithAndProjects_emitsBoth()
    {
        using var doc = JsonDocument.Parse("{\"teamsWith\":[\"Firm A\",\"Firm B\"],\"notableProjects\":[\"Project X\"]}");
        var ctx = new IntelExtractionContext(99, 1, "SubConsultant", doc, DateTimeOffset.UtcNow);

        var result = new SubConsultantExtractor().Extract(ctx);

        Assert.True(result.Narratives.Count >= 2);
        Assert.True(result.Works.Count >= 1);
    }

    [Fact]
    public void MarketResearchExtractor_emitsPersonAndAction()
    {
        using var doc = JsonDocument.Parse("{\"keyLeadership\":[{\"name\":\"X\",\"title\":\"Principal\"}],\"korRelevance\":\"important target\",\"korRelevanceReason\":\"engage now\"}");
        var ctx = new IntelExtractionContext(99, 1, "AlbertaMarketResearch", doc, DateTimeOffset.UtcNow);

        var result = new MarketResearchExtractor("AlbertaMarketResearch").Extract(ctx);

        Assert.Equal(1, result.People.Count);
        Assert.Equal(1, result.Actions.Count);
        Assert.NotEmpty(result.Narratives);
    }

    [Fact]
    public void IndigenousResearchExtractor_nationAndKorSignal_emits()
    {
        using var doc = JsonDocument.Parse("{\"org_name\":\"Y\",\"nations\":[\"Squamish\",\"Tsleil-Waututh\"],\"kor_signal\":\"warm contact via Z\"}");
        var ctx = new IntelExtractionContext(99, 1, "IndigenousDevResearch", doc, DateTimeOffset.UtcNow);

        var result = new IndigenousResearchExtractor("IndigenousDevResearch").Extract(ctx);

        Assert.True(result.Narratives.Count >= 2);
        Assert.Equal(1, result.Actions.Count);
    }

    [Fact]
    public void MassTimberProjectsCatalogExtractor_buildsWorkPerProject()
    {
        using var doc = JsonDocument.Parse("{\"projects\":[{\"projectName\":\"P1\",\"architect\":\"A1\",\"structuralEngineer\":\"SE1\",\"owner\":\"O1\",\"completionYear\":2020,\"city\":\"X\"},{\"projectName\":\"P2\",\"architect\":\"A2\",\"structuralEngineer\":\"SE2\",\"completionYear\":2022}]}");
        var ctx = new IntelExtractionContext(99, 1, "MassTimberProjectsCatalog", doc, DateTimeOffset.UtcNow);

        var result = new MassTimberProjectsCatalogExtractor().Extract(ctx);

        Assert.Equal(2, result.Works.Count);
        Assert.Equal("P1", result.Works[0].ProjectName);
        Assert.NotNull(result.Works[0].Notes);
        Assert.Contains("Architect: A1", result.Works[0].Notes);
        Assert.Contains("SE: SE1", result.Works[0].Notes);
        Assert.Contains("Owner: O1", result.Works[0].Notes);
        Assert.Contains("City: X", result.Works[0].Notes);
        Assert.Contains("Year: 2020", result.Works[0].Notes);
    }

    /// <summary>
    /// Integration test requiring KOR_OPPORTUNITIES_OPPORTUNITIESDB to point at a live KorOpportunitiesDb.
    /// Returns without SQL work when the env var is missing.
    /// </summary>
    [Fact]
    public async Task SqlEnrichmentTrackingStore_recordAttempt_withOkStatusAndJson_persistsIntel()
    {
        var connectionString = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        const long canonicalOrgId = 38918;
        const string displayName = "R79TestPerson";

        await CleanupR79TestPersonAsync(connectionString, displayName);
        try
        {
            var before = await CountIntelPeopleAsync(connectionString, displayName);
            var fallback = new DefaultIntelExtractor();
            var registry = new IntelExtractorRegistry(
                new IIntelExtractor[] { new DataHoningExtractor() },
                fallback);
            var persistence = new IntelPersistenceService(connectionString);
            var store = new SqlEnrichmentTrackingStore(connectionString, registry, persistence);
            var result = new EnrichmentResult(
                EnrichmentStatuses.Ok,
                ErrorMessage: null,
                ResultJson: "{\"keyPeople\":[{\"name\":\"R79TestPerson\",\"title\":\"Test Title\"}]}",
                Notes: null);

            await store.RecordAttemptAsync(
                canonicalOrgId,
                "DataHoning",
                result,
                DateTimeOffset.UtcNow.AddDays(1),
                CancellationToken.None);

            var after = await CountIntelPeopleAsync(connectionString, displayName);
            Assert.True(before == 0);
            Assert.True(after >= 1);
        }
        finally
        {
            await CleanupR79TestPersonAsync(connectionString, displayName);
        }
    }

    /// <summary>
    /// Smoke test requiring KOR_OPPORTUNITIES_OPPORTUNITIESDB to point at a live KorOpportunitiesDb.
    /// Returns without SQL work when the env var is missing.
    /// </summary>
    [Fact]
    public async Task IntelExtractionCatchUpJob_smokeTest_runsWithoutError()
    {
        var connectionString = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var fallback = new DefaultIntelExtractor();
        var registry = new IntelExtractorRegistry(IntelExtractorBootstrap.GetDefaultExtractors(), fallback);
        var persistence = new IntelPersistenceService(connectionString);
        var job = new IntelExtractionCatchUpJob(
            Options.Create(new OpportunitiesWorkerOptions { OpportunitiesDb = connectionString }),
            registry,
            persistence,
            NullLogger<IntelExtractionCatchUpJob>.Instance);

        await job.Execute(null!);
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

    private static async Task<int> CountIntelPeopleAsync(string connectionString, string displayName)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.IntelPerson WHERE DisplayName = @name;";
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@name", displayName);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task CleanupR79TestPersonAsync(string connectionString, string displayName)
    {
        const string sql = @"
DELETE FROM opportunities.IntelPersonAffiliation
WHERE IntelPersonId IN (SELECT Id FROM opportunities.IntelPerson WHERE DisplayName = @name);

DELETE FROM opportunities.IntelPerson
WHERE DisplayName = @name;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@name", displayName);
        await cmd.ExecuteNonQueryAsync();
    }
}
