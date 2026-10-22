#nullable enable
using System.IO;
using System.Reflection;
using DocumentFormat.OpenXml.Packaging;
using Kor.Operations.App.BusinessDevelopment.Briefs;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Core.Models;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Brief 3: EXPECTED evidence states versus ACTUAL user-visible strings, parsed
/// research and export content. Authored only (no build/test/DB execution allowed).
/// Each gate below states its limits. These are not production incident counts.
/// </summary>
public sealed class BdMissingEvidenceRegressionTests
{
    // Covers failed versus successful-empty Deltek reads, missing links and PDF
    // snapshot text. Does not render PDF or execute ODBC; source misattribution escapes.
    [Fact]
    public async Task PursuitBrief_FailedEmptyAndUnlinkedReadsHaveDifferentExportText()
    {
        var store = new Mock<IPursuitBriefStore>();
        store.Setup(x => x.GetBriefForProjectAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Brief("client"));
        var deltek = new Mock<IDeltekClientContextService>();
        deltek.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence());
        var bd = new Mock<IKorClientBdIntelligenceStore>();
        bd.Setup(x => x.LoadByClendorIdAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientBdIntelligence(1, "client", new KorPursuitSummary(0, 0, 0, 0, 0, null, null),
                Array.Empty<KorPursuitRecent>(), new ExternalAwardActivity(0, 0, null, 0),
                Array.Empty<ExternalAwardRecent>(), new CompetitorActivity(0, null, 0, null), Array.Empty<NewsMentionRecent>()));
        var vm = new PursuitBriefViewModel(store.Object, deltek.Object, bd.Object);
        await vm.LoadAsync(1, CancellationToken.None);
        var empty = vm.OwnerContactsDisplay;
        Assert.Contains("No KOR contacts", empty);
        Assert.Contains("No KOR pursuit history", vm.OwnerBdRecordDisplay);
        Assert.Contains("No KOR contacts", vm.ArchitectWarmthDisplay);
        Assert.DoesNotContain("unavailable", vm.StatusMessage);
        deltek.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("read failed"));
        bd.Setup(x => x.LoadByClendorIdAsync("client", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("history failed"));
        await vm.LoadAsync(1, CancellationToken.None);
        var snapshot = new PursuitBriefPdfSnapshot(vm.Brief!, vm.KorEdgeDisplay,
            vm.OwnerContactsDisplay, vm.OwnerBdRecordDisplay, vm.ArchitectWarmthDisplay,
            vm.ThePlayDisplay, vm.FitScoreDisplay);
        Assert.Contains("could not retrieve", snapshot.OwnerContactsDisplay);
        Assert.NotEqual(empty, snapshot.OwnerContactsDisplay);
        Assert.Contains("could not retrieve", snapshot.OwnerBdRecordDisplay);
        Assert.Contains("could not retrieve", snapshot.ArchitectWarmthDisplay);
        Assert.Contains("unavailable", vm.StatusMessage);
        deltek.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence { HasDegradedSections = true });
        await vm.LoadAsync(1, CancellationToken.None);
        Assert.Contains("Partial retrieval", vm.OwnerContactsDisplay);
        Assert.DoesNotContain("No KOR contacts", vm.OwnerContactsDisplay);
        store.Setup(x => x.GetBriefForProjectAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Brief(null));
        await vm.LoadAsync(1, CancellationToken.None);
        Assert.Contains("not linked", vm.OwnerContactsDisplay);
        Assert.DoesNotContain("No prior KOR work", vm.KorEdgeDisplay);
    }

    // Covers both export entry points' shared enrichment and actual DOCX failure
    // explanation versus successfully empty data. Does not drive WPF/PDF rendering;
    // incorrect Deltek financial source data would escape.
    [Fact]
    public async Task OrgExports_KeepUnavailableExplanationAndSuccessfulEmptySection()
    {
        var deltek = new Mock<IDeltekClientContextService>();
        var data = new OrgBriefData(1, "Owner", "Example", null, 0, null,
            Array.Empty<OrgRecentProject>(), 0, Array.Empty<OrgRecentProject>(), null, "client", null);
        var enricher = new OrgBriefDeltekEnricher(deltek.Object);
        deltek.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence());
        var empty = await enricher.EnrichAsync(data, CancellationToken.None);
        Assert.NotNull(empty.Deltek);
        Assert.Equal(0, empty.Deltek!.ProjectCount);
        Assert.Null(empty.DeltekNote);
        deltek.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"));
        var failed = await enricher.EnrichAsync(data, CancellationToken.None);
        Assert.Null(failed.Deltek);
        Assert.Contains("could not retrieve", failed.DeltekNote!);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        try
        {
            new BriefGenerator().WriteOrgBrief(failed, path);
            using var doc = WordprocessingDocument.Open(path, false);
            Assert.Contains("could not retrieve", doc.MainDocumentPart!.Document!.Body!.InnerText);
        }
        finally { File.Delete(path); }
        Assert.Contains("_orgDeltek.EnrichAsync(data", Source("Kor.Operations.App/BusinessDevelopment/Briefs/BriefsMakerWindow.xaml.cs"));
        Assert.Contains(".EnrichAsync(data", Source("Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsView.xaml.cs"));
    }

    // Covers six visual assertion templates, not Chromium layout or SQL contact
    // identity. A wrongly assigned org edge would escape.
    [Fact]
    public void Visuals_DoNotInferSeatOrKorRelationshipFromMissingEdgesOrResearchContacts()
    {
        var actual = BdVisualHtml.Cards("[]", 0) + BdVisualHtml.Graph("{}", 1, 0, 0) + BdVisualHtml.Treemap("[]", 0);
        Assert.Contains("seat status unknown", actual);
        Assert.Contains("research contacts", actual);
        Assert.DoesNotContain("open seat", actual);
        Assert.DoesNotContain("we know", actual);
    }

    // Covers unusable/partial payload and unknown relationship versus explicit
    // false. No live-row count is inferred; a plausible but wrong firm escapes.
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"primeConsultant\":{}}")]
    public void PrimeResearch_UnusableIsNotCompleted(string json)
    {
        var actual = PrimeConsultantParser.Parse(json);
        Assert.Null(actual.KnownToKor);
        Assert.False(actual.IsUsable);
    }

    // Covers boolean/string boolean/missing/invalid relationship assessment and
    // partial counts in rendered HTML. Does not validate real-world relationship
    // evidence; a model's incorrect explicit true would escape.
    [Fact]
    public void PrimeResearch_UnknownIsDistinctFromExplicitNo_AndRowsRemainVisible()
    {
        var unknown = PrimeConsultantParser.Parse("""{"primeConsultant":{"firmName":"Firm"}}""");
        var no = PrimeConsultantParser.Parse("""{"primeConsultant":{"firmName":"Firm","knownToKor":false}}""");
        var yes = PrimeConsultantParser.Parse("""{"primeConsultant":{"firmName":"Firm","knownToKor":"true"}}""");
        var invalid = PrimeConsultantParser.Parse("""{"primeConsultant":{"firmName":"Firm","knownToKor":7}}""");
        Assert.Null(unknown.KnownToKor);
        Assert.Null(invalid.KnownToKor);
        Assert.False(unknown.IsComplete);
        Assert.False(no.KnownToKor);
        Assert.True(no.IsComplete);
        Assert.True(yes.KnownToKor);
        var doc = PrimeConsultantReportGenerator.Build(new[]
        {
            new PrimeConsultantRow(1, "Incomplete", "BC", null, "TBD", 0, null, null, null, null, null, null, null),
            new PrimeConsultantRow(2, "Partial", "BC", null, "TBD", 0, "Firm", null, null, null, null, null, null)
        }, DateTimeOffset.UtcNow);
        var labels = doc.Blocks.OfType<LabelValueBlock>().ToDictionary(x => x.Label, x => x.Value);
        Assert.Equal("0", labels["Usable firm and relationship assessments: "]);
        Assert.Equal("2", labels["Partial or unusable research: "]);
        var actual = HtmlPreviewBuilder.Render(doc);
        Assert.Contains("Incomplete", actual);
        Assert.Contains("Unknown / not assessed", actual);
    }

    // Covers source mutation and fallback disclosures, not SQL/LLM execution.
    // A different provider falsely stamping a refresh timestamp would escape.
    [Fact]
    public void QuietCache_DoesNotStampVerification_AndReportsDiscloseFallback()
    {
        var source = Source("Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs");
        Assert.DoesNotContain("TouchVerifiedAsync", source);
        Assert.DoesNotContain("e.LastRefreshAtUtc = sysdatetimeoffset()", source);
        Assert.Equal(1, source.Split("e.LastRefreshAtUtc = @evidenceCutoffUtc").Length - 1);
        var service = Source("Kor.Opportunities.Data/BdReports/SqlBdReportService.cs");
        Assert.Contains("RefreshNote", service);
        Assert.Contains("could not be checked", service);
        var note = BdReportFormatting.EvidenceNote(Array.Empty<PursuitBriefRow>());
        Assert.Contains("as last researched", note);
        Assert.Contains("confirm status before acting", note);
        Assert.DoesNotContain("verified", note);
        var cached = new PursuitBriefRow(1, "Cached project", "BC", null, null, null, null,
            null, null, null, null, BdVerdicts.Pursue, null, null, null, null,
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"), false)
            { RefreshNote = "Recent news could not be checked for this report." };
        var doc = SectorReportGenerator.Build(new SectorReportDefinition("test", "Test", "Test", "1=1"),
            new SectorReportProse("Intro", Array.Empty<string>(), Array.Empty<SynthesisSection>(), Array.Empty<string>()),
            new[] { cached }, DateTimeOffset.UtcNow);
        var actual = HtmlPreviewBuilder.Render(doc);
        Assert.Contains("could not be checked", actual);
        Assert.Contains("1 Jan 2020", actual);
    }

    // Covers an empty executive report's generated text, not accuracy of supplied
    // evidence or dated background guidance; wrong upstream verdicts escape.
    [Fact]
    public void EmptyOverview_DoesNotClaimUniversalVerification()
    {
        var doc = ExecSummaryReportGenerator.Build(new BdExecHeadline(0, 0, 0),
            Array.Empty<SectorVerdictSummary>(), Array.Empty<PursuitBriefRow>(), DateTimeOffset.UtcNow);
        var actual = HtmlPreviewBuilder.Render(doc);
        Assert.DoesNotContain("structurally eliminated", actual);
        Assert.DoesNotContain("Every PURSUE has", actual);
        Assert.Contains("date from June 2026", actual);
        Assert.Contains("confirm amounts, contacts and status before acting", actual);
    }

    // Covers actual common opportunity assessment text for absent status/fit/seat
    // inputs, shared by DOCX/HTML/PDF. Does not verify classification quality;
    // an inaccurate supplied sector label would escape.
    [Fact]
    public void OpportunityBrief_AbsentLifecycleAndSeatEvidenceIsUnknown()
    {
        var data = new OpportunityBriefData(1, "Closed old opportunity", "Owner", null,
            DateTimeOffset.UtcNow.AddYears(-1), null, 0, null, null, null, 0, 0, null, null, 0, 0, null);
        var actual = string.Join(" ", BriefPdfGenerator.OppWarmthBullets(data));
        Assert.Contains("assessment unavailable", actual);
        Assert.Contains("structural seat availability are unknown", actual);
        Assert.DoesNotContain("Live in active procurement", actual);
        Assert.DoesNotContain("both match", actual);
        Assert.Contains("BriefPdfGenerator.OppWarmthBullets(data)", Source("Kor.Operations.App/BusinessDevelopment/Briefs/BriefGenerator.cs"));
    }

    // Covers Admin's actual mapping of running/succeeded/failed rows, not worker
    // liveness. An abandoned run with no end timestamp still appears running.
    [Theory]
    [InlineData(false, false, "running")]
    [InlineData(false, true, "running")]
    [InlineData(true, true, "ok")]
    [InlineData(true, false, "failed")]
    public void Admin_EndTimestampDeterminesWhetherOutcomeIsFinal(bool ended, bool success, string expected)
    {
        var run = new IngestionRun { StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
            EndedAtUtc = ended ? DateTimeOffset.UtcNow : null, Success = success };
        var actual = (AdminViewModel.IngestionRunRow)typeof(AdminViewModel)
            .GetMethod("ToRunRow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { run })!;
        Assert.Equal(expected, actual.Status);
    }

    // Covers the distinction brief 3 itself got wrong: a payload that parsed and records an
    // explicit NEGATIVE finding (no firm, knownToKor false, a note saying why) is a result,
    // not a partial payload. 99 of 148 live rows were exactly that and the report called
    // them "partial or unusable". Does not validate the note's truth; a wrong negative
    // ("none - paused" for a project that is live) would escape.
    [Fact]
    public void PrimeResearch_RecordedNegativeFindingIsAResultNotAGap()
    {
        var negative = PrimeConsultantParser.Parse(
            """{"overallConfidence":0.7,"description":"none - procurement is PAUSED","primeConsultant":{"firmName":null,"knownToKor":false}}""");
        Assert.True(negative.IsUsable);
        Assert.False(negative.HasFirm);
        Assert.False(negative.IsComplete);
        Assert.Equal("none - procurement is PAUSED", negative.ResearchNote);

        var doc = PrimeConsultantReportGenerator.Build(new[]
        {
            new PrimeConsultantRow(1, "Paused project", "BC", null, "TBD", 0, null, null, null, false, null, null, null)
                { ResearchNote = "none - procurement is PAUSED" },
            new PrimeConsultantRow(2, "Empty payload", "BC", null, "TBD", 0, null, null, null, null, null, null, null),
        }, DateTimeOffset.UtcNow);
        var labels = doc.Blocks.OfType<LabelValueBlock>().ToDictionary(x => x.Label, x => x.Value);
        Assert.Equal("1", labels["Negative findings (research found no prime; reason recorded where known): "]);
        Assert.Equal("1", labels["Partial or unusable research: "]);
        var html = HtmlPreviewBuilder.Render(doc);
        Assert.Contains("none - procurement is PAUSED", html);
        Assert.Contains("No usable finding recorded", html);
    }

    private static PursuitBrief Brief(string? client) => new(
        new PursuitBriefProject(1, "Project", "Owner", "BC", null, null, null, "TBD", null),
        client, client, new PursuitBriefArchitect("Architect", 2, null, null, null, Array.Empty<string>()),
        new PursuitBriefOwnerProcurement(null, null, null, null), Array.Empty<PursuitBriefPerson>(),
        Array.Empty<PursuitBriefPerson>(), Array.Empty<PursuitBriefCompetitorNote>(), null, null, null);

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
}
