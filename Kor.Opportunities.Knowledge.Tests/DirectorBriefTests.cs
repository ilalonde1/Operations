using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Knowledge.Brief;
using Kor.Opportunities.Knowledge.Claims;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;
using Xunit;

namespace Kor.Opportunities.Knowledge.Tests;

/// <summary>
/// Covers bounded facts, export queries/counts/JSON, file-wide rejection, retirement checks,
/// raw-byte provenance, expected outcomes/review dates, prior-call JSON and bounded verdict
/// demotion, and duplicate/force behavior through the production import coordinator.
/// No SQL, embedding, book content or application is executed. Fake import storage does not
/// prove transaction isolation, rollback or SQL syntax. Same-class miss: an incorrect SQL lock
/// could allow two concurrent imports even though the sequential fake duplicate gate passes.
/// </summary>
public sealed class DirectorBriefTests
{
    private static readonly DateOnly Week = new(2026, 10, 19);

    [Fact]
    public async Task Facts_AreBoundedFor101PursuitsAnd40Accounts()
    {
        var store = new FactsStore();
        var facts = new DirectorFacts(store, store, store);
        foreach (var section in Enum.GetValues<DirectorSection>())
        {
            var block = await facts.GetAsync(section, CancellationToken.None);
            Assert.InRange(block.Length, 1, 6000);
            Assert.DoesNotContain("NaN", block, StringComparison.Ordinal);
        }
        Assert.Contains("Additional rows omitted", await facts.GetAsync(DirectorSection.Accounts, CancellationToken.None), StringComparison.Ordinal);
        Assert.Contains("101", await facts.GetAsync(DirectorSection.Pursuits, CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_CarriesFiveBoundedSections_AndRoundTripsCamelCase()
    {
        var store = new FactsStore();
        var facts = new DirectorFacts(store, store, store);
        var queries = new List<string>();
        var priorReads = new List<string>();
        var prior = new PriorCall(Week.AddDays(-7), "Previous headline", "Previous recommendation", "A meeting",
            "Wrong", "No meeting", new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var exporter = new DirectorBriefExport(facts.GetAsync, (query, take, ct) =>
        {
            Assert.Equal(12, take);
            queries.Add(query);
            return Task.FromResult<IReadOnlyList<KnowledgeClaimHit>>(query == DirectorQuestions.ClaimsQuery(DirectorSection.Asks)
                ? Array.Empty<KnowledgeClaimHit>() : Enumerable.Range(1, 15)
                    .Select(i => new KnowledgeClaimHit(i, 100, "Synthetic book", i + 1000, "Rule", "Synthetic advice.", 0.8f)).ToArray());
        }, (section, since, ct) =>
        {
            Assert.Equal(Week.AddDays(-28), since);
            priorReads.Add(section);
            return Task.FromResult<IReadOnlyList<PriorCall>>(section == "Asks" ? Array.Empty<PriorCall>() : new[] { prior });
        });
        var export = await exporter.ExportAsync(Week);
        Assert.Equal(DirectorBriefFile.SectionNames, priorReads);
        Assert.Equal(prior, Assert.Single(export.Sections[0].PriorCalls));
        Assert.Empty(export.Sections[^1].PriorCalls);
        Assert.Equal(DirectorBriefFile.SectionNames, export.Sections.Select(s => s.Section));
        Assert.Equal(Enum.GetValues<DirectorSection>().Select(DirectorQuestions.ClaimsQuery), queries);
        foreach (var section in export.Sections)
        {
            Assert.Equal(DirectorQuestions.Question(Enum.Parse<DirectorSection>(section.Section)), section.Question);
            Assert.False(string.IsNullOrWhiteSpace(section.ClaimsQuery));
            Assert.InRange(section.Facts.Length, 1, 6000);
            Assert.InRange(section.Claims.Count, 0, 12);
        }
        Assert.Empty(export.Sections[^1].Claims);
        Assert.Equal(new OfferedClaim(1, 1001, 100, "Synthetic book", "Rule", "Synthetic advice.", 0.8f), export.Sections[0].Claims[0]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(export, DirectorBriefFiles.JsonOptions);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("2026-10-19", json.RootElement.GetProperty("weekOf").GetString());
        Assert.True(json.RootElement.GetProperty("sections")[0].TryGetProperty("claimsQuery", out _));
        Assert.True(json.RootElement.GetProperty("sections")[0].GetProperty("claims")[0].TryGetProperty("sourceTitle", out _));
        Assert.Equal("Wrong", json.RootElement.GetProperty("sections")[0].GetProperty("priorCalls")[0].GetProperty("outcome").GetString());
        var parsed = DirectorBriefFiles.ParseExport(bytes);
        Assert.Equal(prior, Assert.Single(parsed.Sections[0].PriorCalls));
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(parsed, DirectorBriefFiles.JsonOptions));
    }

    [Theory]
    [InlineData(null, "expected must contain")]
    [InlineData("", "expected must contain")]
    [InlineData(" ", "expected must contain")]
    [InlineData("overlong", "expected must contain")]
    [InlineData("one\ntwo", "expected must be one line")]
    [InlineData("one\rtwo", "expected must be one line")]
    [InlineData("one\u0085two", "expected must be one line")]
    [InlineData("one\u2028two", "expected must be one line")]
    [InlineData("one\u2029two", "expected must be one line")]
    public async Task Recommendation_RequiresOneLineExpectedOutcome_AndRejectsBeforeWriting(string? expected, string reason)
    {
        var file = Sections();
        file = file with { Sections = file.Sections.Select(s => s with { Expected = expected == "overlong" ? new string('x', 501) : expected }).ToArray() };
        var store = new BriefStore();
        var options = new JsonSerializerOptions(DirectorBriefFiles.JsonOptions)
            { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, options);
        if (expected is null) Assert.DoesNotContain("\"expected\"", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        var result = await DirectorBriefFiles.ImportAsync(JsonSerializer.SerializeToUtf8Bytes(Export(), DirectorBriefFiles.JsonOptions),
            bytes, "sections.json", "Importer", false, store);
        Assert.Contains(result.Rejected, e => e.Contains(reason, StringComparison.Ordinal));
        Assert.Equal(0, store.ImportCalls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void ReviewDays_AreBounded_EvenWithoutRecommendation(int? days, bool valid)
    {
        var file = Sections() with { Sections = Sections().Sections.Select(s => s with
            { Recommendation = null, Expected = null, ReviewAfterDays = days }).ToArray() };
        Assert.Equal(valid, DirectorBriefValidation.Validate(file, Export()).Count == 0);
    }

    [Theory]
    [InlineData("2026-10-19", null, "2026-11-17T00:00:00+00:00")]
    [InlineData("2026-10-19", 7, "2026-10-26T23:00:00+00:00")]
    [InlineData("2026-03-02", 7, "2026-03-09T23:00:00+00:00")]
    public void ReviewDate_IsSixteenPacificOnTheReviewDay(string week, int? days, string expected)
    {
        var section = Sections().Sections[0] with { ReviewAfterDays = days, Expected = new string('x', 500) };
        Assert.Empty(DirectorBriefValidation.Validate(Sections() with { Sections = Sections().Sections.Skip(1).Prepend(section).ToArray() }, Export()));
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            section.ReviewAtUtc(DateOnly.Parse(week, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Search_DemotesWrongClaims_AndCapsThePenaltyAtPointOneFive()
    {
        var verdicts = new Dictionary<long, ClaimVerdict>
        {
            [1] = new(1, 0, 2, 0, 0, new long[] { 105, 106 }),
            [3] = new(3, 0, 100, 0, 0, Array.Empty<long>()),
            [4] = new(4, 1, 1, 0, 0, new long[] { 107 }),
            [5] = new(5, 1, 2, 0, 0, new long[] { 108, 109 }),
            [6] = new(6, 3, 1, 0, 0, new long[] { 110 }),
        };
        using var search = new KnowledgeSearch(new SearchStore(), new UnitEmbedder(), verdicts);
        var hits = await search.SearchClaimsAsync("synthetic", 6, perSource: 0);
        Assert.Equal(new long[] { 2, 4, 6, 5, 1, 3 }, hits.Select(h => h.ClaimId));
        Assert.Equal(0.90f, hits.Single(h => h.ClaimId == 1).Score, 5);
        Assert.Equal(0.85f, hits.Single(h => h.ClaimId == 3).Score, 5);
        Assert.Equal(0.95f, hits.Single(h => h.ClaimId == 5).Score, 5);
        Assert.All(hits, h => Assert.InRange(h.Score, 0.85f, 1f));
        using var baseline = new KnowledgeSearch(new SearchStore(), new UnitEmbedder());
        Assert.Equal(Enumerable.Range(1, 6).Select(i => (long)i), (await baseline.SearchClaimsAsync("synthetic", 6, perSource: 0)).Select(h => h.ClaimId));
    }

    [Theory]
    [InlineData("missing", "Missing section: Asks")]
    [InlineData("sixth", "exactly five")]
    [InlineData("duplicate", "Duplicate section: ThisWeek")]
    [InlineData("long headline", "headline must contain")]
    [InlineData("empty headline", "headline must contain")]
    [InlineData("line break", "line break")]
    [InlineData("unicode line break", "line break")]
    [InlineData("empty body", "body must contain")]
    [InlineData("long body", "body must contain")]
    [InlineData("week", "weekOf must equal")]
    [InlineData("unoffered", "not offered for this section")]
    public void Validation_RejectsEachInvalidFileWithItsOwnReason(string change, string reason)
    {
        var file = Sections();
        var first = file.Sections[0];
        file = change switch
        {
            "missing" => file with { Sections = file.Sections.Take(4).ToArray() },
            "sixth" => file with { Sections = file.Sections.Append(first with { Section = "Extra" }).ToArray() },
            "duplicate" => file with { Sections = file.Sections.Take(4).Append(first).ToArray() },
            "week" => file with { WeekOf = Week.AddDays(1) },
            _ => file with { Sections = file.Sections.Skip(1).Prepend(change switch
            {
                "long headline" => first with { Headline = new string('x', 513) },
                "empty headline" => first with { Headline = " " },
                "line break" => first with { Headline = "first\nsecond" },
                "unicode line break" => first with { Headline = "first\u2028second" },
                "empty body" => first with { Body = "" },
                "long body" => first with { Body = new string('x', 20001) },
                // Claim 2 is offered to Pursuits, but never to ThisWeek.
                _ => first with { Citations = new long[] { 2 } },
            }).ToArray() },
        };
        Assert.Contains(DirectorBriefValidation.Validate(file, Export()), e => e.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_RejectsRetiredOrMissingCitation_AndAllowsUncitedRecommendationsWithoutOfferedClaims()
    {
        var file = Sections();
        var current = file.Sections.SelectMany(s => s.Citations).ToDictionary(id => id, id => (KnowledgeClaimRow?)Claim(id));
        current[1] = Claim(1) with { RetiredAtUtc = DateTimeOffset.UnixEpoch };
        Assert.Contains(DirectorBriefValidation.ValidateCurrentCitations(file, current), e => e.Contains("citation 1 is retired", StringComparison.Ordinal));
        current[1] = null;
        Assert.Contains(DirectorBriefValidation.ValidateCurrentCitations(file, current), e => e.Contains("citation 1 no longer exists", StringComparison.Ordinal));
        file = file with { Sections = file.Sections.Select(s => s with { Citations = Array.Empty<long>(), Recommendation = "Uncited recommendation." }).ToArray() };
        var export = Export() with { Sections = Export().Sections.Select(s => s with { Claims = Array.Empty<OfferedClaim>() }).ToArray() };
        Assert.Empty(DirectorBriefValidation.Validate(file, export));
        Assert.Empty(DirectorBriefValidation.ValidateCurrentCitations(file, new Dictionary<long, KnowledgeClaimRow?>()));
    }

    [Fact]
    public async Task Import_ValidatesBeforeWriting_AndSkipsDuplicateHashUnlessForced()
    {
        var store = new BriefStore();
        var export = JsonSerializer.SerializeToUtf8Bytes(Export(), DirectorBriefFiles.JsonOptions);
        // The BOM remains part of provenance even though it is removed for JSON parsing.
        var sections = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(JsonSerializer.SerializeToUtf8Bytes(Sections(), DirectorBriefFiles.JsonOptions)).ToArray();
        var first = await DirectorBriefFiles.ImportAsync(export, sections, "sections.json", "Importer", false, store);
        Assert.Equal(5, first.SectionsWritten);
        Assert.Empty(first.Rejected);
        var repeat = await DirectorBriefFiles.ImportAsync(export, sections, "renamed.json", "Importer", false, store);
        Assert.Equal(0, repeat.SectionsWritten);
        Assert.Empty(repeat.Rejected);
        var forced = await DirectorBriefFiles.ImportAsync(export, sections, "sections.json", "Importer", true, store);
        Assert.Equal(5, forced.SectionsWritten);
        Assert.Equal(10, store.RowsWritten);
        Assert.True(store.Ledger.Count == 2, "EXPECTED two committed imports, including the forced import.");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(export)), store.Ledger[0].ExportHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(sections)), store.Ledger[0].FileHash);
        Assert.False(store.Ledger[0].Forced);
        Assert.True(store.Ledger[1].Forced);

        var invalid = Sections() with { Sections = Sections().Sections.Skip(1).ToArray() };
        var rejected = await DirectorBriefFiles.ImportAsync(export, JsonSerializer.SerializeToUtf8Bytes(invalid, DirectorBriefFiles.JsonOptions), "bad.json", "Importer", true, store);
        Assert.Equal(0, rejected.SectionsWritten);
        Assert.NotEmpty(rejected.Rejected);
        Assert.Equal(3, store.ImportCalls);
        store.Retired = true;
        var retiredRepeat = await DirectorBriefFiles.ImportAsync(export, sections, "sections.json", "Importer", false, store);
        Assert.Equal(0, retiredRepeat.SectionsWritten);
        Assert.Empty(retiredRepeat.Rejected);
        rejected = await DirectorBriefFiles.ImportAsync(export, sections, "sections.json", "Importer", true, store);
        Assert.Contains(rejected.Rejected, e => e.Contains("is retired", StringComparison.Ordinal));
        Assert.Equal(5, store.ImportCalls);
        Assert.Equal(10, store.RowsWritten);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"sections\":null}")]
    [InlineData("{\"sections\":[null]}")]
    [InlineData("{\"weekOf\":\"not a date\"}")]
    public async Task Import_MalformedFilesReturnReasonsAndNeverReachStore(string json)
    {
        var store = new BriefStore();
        var result = await DirectorBriefFiles.ImportAsync(JsonSerializer.SerializeToUtf8Bytes(Export(), DirectorBriefFiles.JsonOptions),
            Encoding.UTF8.GetBytes(json), "bad.json", "Importer", false, store);
        Assert.Equal(0, result.SectionsWritten);
        Assert.NotEmpty(result.Rejected);
        Assert.Equal(0, store.ImportCalls);
    }

    [Theory]
    [InlineData("2026-10-15T12:00:00-07:00", "2026-10-18")]
    [InlineData("2026-10-18T12:00:00-07:00", "2026-10-25")]
    [InlineData("2026-10-18T01:00:00+00:00", "2026-10-18")]
    public void DefaultWeek_IsStrictlyNextPacificSunday(string now, string expected)
        => Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            DirectorBriefExport.NextSunday(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

    private static DirectorBriefFile Sections() => new(Week, "Session writer", DirectorBriefFile.SectionNames
        .Select((s, i) => new DirectorBriefSection(s, "Synthetic headline.", "Synthetic facts and reasoning.", "Synthetic action.", new long[] { i + 1 }, "Synthetic expected result.")).ToArray());
    private static DirectorBriefExportFile Export() => new(Week, DateTimeOffset.UnixEpoch, Enum.GetValues<DirectorSection>()
        .Select((s, i) => new ExportSection(s.ToString(), DirectorQuestions.Question(s), DirectorQuestions.ClaimsQuery(s), "Synthetic facts.",
            new[] { new OfferedClaim(i + 1, i + 101, 1, "Synthetic source", "Rule", "Offered statement.", 0.9f) }, Array.Empty<PriorCall>())).ToArray());
    private static KnowledgeClaimRow Claim(long id) => new(id, 1, "Current source title", id + 100, null, "Rule", "Current statement.", 0.9m,
        Array.Empty<string>(), null, null, null);

    private sealed class BriefStore : IDirectorBriefStore
    {
        internal bool Retired { get; set; }
        internal int ImportCalls { get; private set; }
        internal int RowsWritten { get; private set; }
        internal List<(string FileHash, string ExportHash, bool Forced)> Ledger { get; } = new();
        public Task<(int SectionsWritten, IReadOnlyList<string> Rejected)> ImportAsync(DirectorBriefFile file, string exportSha256,
            string fileName, string fileSha256, string by, bool force, CancellationToken ct = default)
        {
            ImportCalls++;
            if (!force && Ledger.Any(l => l.FileHash == fileSha256))
                return Task.FromResult<(int, IReadOnlyList<string>)>((0, Array.Empty<string>()));
            var current = file.Sections.SelectMany(s => s.Citations).Distinct().ToDictionary(id => id,
                id => (KnowledgeClaimRow?)(Claim(id) with { RetiredAtUtc = Retired ? DateTimeOffset.UnixEpoch : null }));
            var rejected = DirectorBriefValidation.ValidateCurrentCitations(file, current);
            if (rejected.Count != 0) return Task.FromResult((0, rejected));
            RowsWritten += file.Sections.Count;
            Ledger.Add((fileSha256, exportSha256, force));
            return Task.FromResult<(int, IReadOnlyList<string>)>((file.Sections.Count, Array.Empty<string>()));
        }
        public Task<IReadOnlyList<DirectorBriefRow>> GetLatestAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DirectorBriefRow>> GetHistoryAsync(int weeks, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PriorCall>> ListPriorCallsAsync(string section, DateOnly since, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AcknowledgeAsync(long id, string by, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class FactsStore : IBdDirectorStore, IBdDirectorLedgerStore, IBdDashboardStore
    {
        public Task<IReadOnlyList<CallSheetItem>> GetCallSheetAsync(string actorUpn, DateTimeOffset nowUtc, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<PendingBidRow>> ListPendingBidsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountRelationshipPursuitsAsync(CancellationToken ct) => throw new NotSupportedException();
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorPerson>>(new[]
            { new BdDirectorPerson("engineer", "Synthetic engineer", new[] { "engineer" }, 101, 24, 74, 90, 5, 9, 3, 4, 1) });
        public Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct) => Task.FromResult(new BdDirectorFunnel(new Dictionary<int, double> { [1] = 90 },
            Enumerable.Range(1, 101).Select(i => new BdDirectorUndecidedPursuit(i, "Synthetic pursuit " + i, "Buyer", "engineer", Now)).ToArray(), 0, 0, 0, 0));
        public Task<IReadOnlyList<BdDirectorAccount>> GetAccountsAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorAccount>>(
            Enumerable.Range(1, Math.Min(take, 40)).Select(i => new BdDirectorAccount(i, "Synthetic " + new string('a', 250), "Client", "client" + i, i, true, null, 2, Now, 1)).ToArray());
        public Task<BdTargetActuals> GetTargetActualsAsync(IReadOnlyList<BdTargetKey> keys, CancellationToken ct) => Task.FromResult(new BdTargetActuals(keys.ToDictionary(k => k, _ => 101m), 101, 0));
        public Task<IReadOnlyList<BdDirectorSignal>> GetRecentSignalsAsync(int days, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorSignal>>(new[] { new BdDirectorSignal(1, "Synthetic firm", "RecentWin", "Synthetic signal", Now, 1) });
        public Task<IReadOnlyList<BdTarget>> ListTargetsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdTarget>>(new[] { new BdTarget(1, "LivePursuits", "Firm", null, Now, Now.AddDays(90), 120, null, "Synthetic", Now) });
        public Task<IReadOnlyList<BdAsk>> ListAsksAsync(string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdAsk>>(new[] { new BdAsk(1, "engineer", 1, null, "Synthetic ask", null, 30, Now, "Synthetic", Now, "Open", null, null, null) });
        public Task<IReadOnlyList<BdDecision>> ListDecisionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDecision>>(Array.Empty<BdDecision>());
        public Task<BriefTrackRecord> GetBriefTrackRecordAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BdForwardPipelineRow>> GetForwardPipelineAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdForwardPipelineRow>>(new[] { new BdForwardPipelineRow(1, "Synthetic project", "Proponent", "BC", "School", 100m, "Planning") });
        public Task<PipelineFunnel> GetPipelineFunnelAsync(CancellationToken ct) => Task.FromResult(new PipelineFunnel(3, 2, 1));
        public Task<IReadOnlyList<BdOpenStructuralSeatRow>> GetOpenStructuralSeatsAsync(int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BdCompetitorWatchRow>> GetCompetitorWatchAsync(int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DataHealthRow>> GetDataHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SetTargetAsync(BdTarget target, CancellationToken ct) => throw new NotSupportedException();
        public Task RetireTargetAsync(long id, string by, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BdAsk>> ListOpenAsksForAsync(string toStaffKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CreateAskAsync(BdAsk ask, CancellationToken ct) => throw new NotSupportedException();
        public Task CloseAskAsync(long id, string status, string? note, string by, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> RecordDecisionAsync(BdDecision decision, CancellationToken ct) => throw new NotSupportedException();
        public Task ScoreDecisionAsync(long id, string outcome, string? note, string by, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnitEmbedder : IKnowledgeEmbedder
    {
        public float[] Embed(string text) { var vector = new float[384]; vector[0] = 1; return vector; }
    }

    // Only vector/claim reads are implemented. An unexpected write or passage search fails.
    private sealed class SearchStore : IKnowledgeStore
    {
        public Task<IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)>> LoadClaimVectorsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(long, long, float[])>>(Enumerable.Range(1, 6).Select(i => ((long)i, 1L, new UnitEmbedder().Embed("synthetic"))).ToArray());
        public Task<IReadOnlyList<KnowledgeClaim>> GetClaimsAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgeClaim>>(ids.Select(id => new KnowledgeClaim(id, 1, "Synthetic", id + 100, "Rule", "Synthetic statement", 1)).ToArray());
        public Task<IReadOnlyList<(long Id, string Statement)>> ListUnembeddedClaimsAsync(int take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetClaimEmbeddingsAsync(IReadOnlyList<(long ClaimId, byte[] Embedding)> embeddings, string model, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(int Written, int Rejected)> ImportRelationsAsync(RelationFile file, string by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ClaimFile> ExportAsync(long sourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(int PassagesCovered, int ClaimsWritten, int ClaimsRejected)> ImportClaimsAsync(ClaimFile file, bool force, string by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeSource?> FindBySha256Async(string sha256, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> IngestAsync(KnowledgeSource source, IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages, bool force, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgePassage>> ListUnembeddedAsync(long? sourceId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetEmbeddingsAsync(IReadOnlyList<(long PassageId, byte[] Embedding)> embeddings, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>> LoadVectorsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgePassage>> GetPassagesAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
