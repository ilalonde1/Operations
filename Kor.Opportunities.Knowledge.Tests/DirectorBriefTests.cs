using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Knowledge.Brief;
using Kor.Opportunities.Knowledge.Store;
using Xunit;

namespace Kor.Opportunities.Knowledge.Tests;

/// <summary>
/// Covers bounded facts, export queries/counts/JSON, file-wide rejection, retirement checks,
/// raw-byte provenance and duplicate/force behavior through the production import coordinator.
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
        var exporter = new DirectorBriefExport(facts.GetAsync, (query, take, ct) =>
        {
            Assert.Equal(12, take);
            queries.Add(query);
            return Task.FromResult<IReadOnlyList<KnowledgeClaimHit>>(query == DirectorQuestions.ClaimsQuery(DirectorSection.Asks)
                ? Array.Empty<KnowledgeClaimHit>() : Enumerable.Range(1, 15)
                    .Select(i => new KnowledgeClaimHit(i, 100, "Synthetic book", i + 1000, "Rule", "Synthetic advice.", 0.8f)).ToArray());
        });
        var export = await exporter.ExportAsync(Week);
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
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(DirectorBriefFiles.ParseExport(bytes), DirectorBriefFiles.JsonOptions));
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
        .Select((s, i) => new DirectorBriefSection(s, "Synthetic headline.", "Synthetic facts and reasoning.", "Synthetic action.", new long[] { i + 1 })).ToArray());
    private static DirectorBriefExportFile Export() => new(Week, DateTimeOffset.UnixEpoch, Enum.GetValues<DirectorSection>()
        .Select((s, i) => new ExportSection(s.ToString(), DirectorQuestions.Question(s), DirectorQuestions.ClaimsQuery(s), "Synthetic facts.",
            new[] { new OfferedClaim(i + 1, i + 101, 1, "Synthetic source", "Rule", "Offered statement.", 0.9f) })).ToArray());
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
        public Task AcknowledgeAsync(long id, string by, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class FactsStore : IBdDirectorStore, IBdDirectorLedgerStore, IBdDashboardStore
    {
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
}
