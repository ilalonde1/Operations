#nullable enable
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

/// <summary>
/// Brief 2 gates: EXPECTED report meaning versus ACTUAL parsed verdicts, report
/// blocks/exported DOCX text, synthesis write acknowledgements and SQL wiring.
/// Covers the six audited boundaries, not live SQL execution, model quality,
/// browser layout, PDF output or identity resolution. Same-class misses include
/// another report clipping outside Safe(), or an incorrect cost supplied upstream.
/// Authored only: the brief forbids builds, tests and DB access. No red/green claim.
/// DB population comparisons are also authored in BdIntegrityCheck.
/// </summary>
public sealed class BdReportMeaningRegressionTests
{
    private static readonly DateTimeOffset Generated = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    // Covers legacy case/whitespace/vocabulary in both JSON shapes, rank and urgency.
    // Excludes factual verdict validity; a well-spelled but wrong verdict escapes.
    [Theory]
    [InlineData("pursue", "PURSUE", 1)]
    [InlineData("pursue_urgent", "PURSUE_URGENT", 0)]
    [InlineData(" monitor ", "MONITOR", 2)]
    [InlineData("banana", null, 6)]
    [InlineData("discover", "DISCOVER", 3)]
    [InlineData("dead", "DEAD", 4)]
    [InlineData("duplicate", "DUPLICATE", 5)]
    public void Verdict_ReadBoundary_ClassifiesLegacyCaseAndRejectsUnknown(string raw, string? expected, int rank)
    {
        var actual = HoningResultParser.Parse(new JsonObject { ["verdict"] = raw }.ToJsonString());
        Assert.Equal(expected, actual.Verdict);
        var nested = HoningResultParser.Parse(new JsonObject
            { ["honingPass"] = new JsonObject { ["verdict"] = raw } }.ToJsonString());
        Assert.Equal(expected, nested.Verdict);
        Assert.Equal(rank, BdVerdicts.Rank(raw));
        Assert.Equal(expected is "PURSUE" or "PURSUE_URGENT", BdVerdicts.IsUrgent(raw, "URGENT"));
    }

    // Covers actual sector buckets after legacy JSON hydration, not SQL grouping.
    // A wrong SQL sector filter would escape this no-DB gate.
    [Fact]
    public void LegacyVerdicts_ReachSectorSectionsRatherThanOnlyTheHonedTotal()
    {
        var rows = new[] { "pursue_urgent", "pursue", "monitor", "discover", "dead", "duplicate" }
            .Select((raw, i) =>
            {
                var parsed = HoningResultParser.Parse(new JsonObject { ["verdict"] = raw }.ToJsonString());
                return Row(i + 1, null) with
                {
                    Verdict = parsed.Verdict,
                    IsUrgent = BdVerdicts.IsUrgent(parsed.Verdict, null)
                };
            }).ToArray();
        var definition = new SectorReportDefinition("test", "Test", "Test report", "1=1");
        var prose = new SectorReportProse("Intro", Array.Empty<string>(),
            Array.Empty<SynthesisSection>(), Array.Empty<string>());
        var doc = SectorReportGenerator.Build(definition, prose, rows, Generated);
        var kpis = doc.Blocks.OfType<KpiStripBlock>().Single().Items.ToDictionary(x => x.Label, x => x.Value);
        Assert.Equal("6", kpis["Projects honed"]);
        foreach (var label in new[] { "Urgent", "Pursue", "Monitor", "Discover" }) Assert.Equal("1", kpis[label]);
        var actual = ExportText(doc);
        foreach (var row in rows) Assert.Contains(row.ProjectName, actual);
    }

    // Covers response-to-write selection/counting; no SQL or HTTP execution.
    // A persistence callback lying about affected rows would escape this gate.
    [Fact]
    public async Task Synthesis_CountsOnlyAcknowledgedDistinctRequestedWrites()
    {
        var results = JsonNode.Parse("""
            [{"id":1,"verdict":"pursue_urgent"}, {"id":1,"verdict":"PURSUE"},
             {"id":2,"verdict":"banana"}, {"id":3,"verdict":"MONITOR"},
             {"id":999,"verdict":"PURSUE"}]
            """)!.AsArray();
        var writes = new List<(long Id, string Verdict)>();
        var actual = await OnDemandHoningSynthesizer.ApplySynthesisResultsAsync(
            results, new long[] { 1, 2, 3 }, (id, verdict, _, _) =>
            {
                writes.Add((id, verdict));
                return Task.FromResult(id == 1); // id 3 no longer has an update target
            }, CancellationToken.None);
        Assert.Equal(1, actual);
        Assert.Equal(new[] { (1L, "PURSUE_URGENT"), (3L, "MONITOR") }, writes);
    }

    // Covers retained known subtotal and mixed/all-unknown DOCX labels.
    // Does not verify source costs; an upstream fabricated zero would escape.
    [Theory]
    [InlineData(10_000_000, "1/2 projects costed")]
    [InlineData(null, "0/2 projects costed")]
    public void CallSheet_KeepsSubtotalAndDisclosesUnknownCosts(int? known, string coverage)
    {
        var pool = new[] { Row(1, known), Row(2, null) };
        var doc = CallSheetReportGenerator.Build(pool, Array.Empty<SectorVerdictSummary>(), Generated);
        var actual = ExportText(doc);
        Assert.Contains("Known-cost subtotal (CAD)", actual);
        Assert.Contains(coverage, actual);
        var kpi = doc.Blocks.OfType<KpiStripBlock>().SelectMany(x => x.Items)
            .Single(x => x.Label == "Known-cost subtotal (CAD)");
        Assert.Equal(known.HasValue ? "$10M" : "$0", kpi.Value);
    }

    // Covers historical portfolio wording and retained numbers in exported DOCX.
    // Does not execute membership SQL; duplicate project membership would escape.
    [Fact]
    public void Architect_HistoricalLinksRemainVisibleWithoutClaimingActiveSeats()
    {
        var doc = ArchitectFrequencyReportGenerator.Build(new[]
        {
            new ArchitectLeverageRow(1, "Example architect", 1, 10_000_000m, 0, 1, 0,
                new[] { "Delivered (2019) project" }, 1)
        }, Generated);
        var actual = ExportText(doc);
        Assert.Contains("Linked portfolio projects", actual);
        Assert.Contains("historical", actual);
        Assert.Contains("Delivered (2019) project", actual);
        Assert.DoesNotContain("linked active projects", actual);
        Assert.DoesNotContain("Active projects", actual);
        Assert.Contains("1/1 projects costed", actual);
    }

    // Covers exec/sector/dossier DOCX and visual HTML coverage disclosure.
    // Does not execute browser layout or cost SQL; hidden CSS text would escape.
    [Fact]
    public void MonetaryRenderers_DiscloseCoverageForTheirOwnPopulations()
    {
        var headline = new BdExecHeadline(8, 6, 10_000_000m, 1, 4);
        var sector = new SectorVerdictSummary("test", "Test sector", 0, 2, 1, 0, 0, 0, 0,
            3, 3, 0, 0, 10_000_000m, 1, 3);
        var actual = ExportText(ExecSummaryReportGenerator.Build(headline, new[] { sector },
            Array.Empty<PursuitBriefRow>(), Generated));
        Assert.Contains("1/4 projects costed", actual);
        Assert.Contains("1/3 projects costed", actual);
        Assert.Contains("$10M", actual);

        var dossier = new PursuitDossierRow(1, "Project", "BC", null, null, null, null,
            null, "TBD", BdVerdicts.Pursue, null, false, null, null, 0, 0, 0,
            null, null, false, null, null);
        var dossierText = ExportText(PursuitDossierReportGenerator.Build(new[] { dossier }, Generated));
        Assert.Contains("Known-cost subtotal (CAD)", dossierText);
        Assert.Contains("0/1 projects costed", dossierText);

        foreach (var html in new[]
        {
            BdVisualHtml.Graph("{\"nodes\":[],\"links\":[]}", 1, 0, 0, 1, 2),
            BdVisualHtml.Treemap("[]", 0, 1, 2)
        })
        {
            Assert.Contains("1/2 projects costed", html);
            Assert.Contains("Unknown costs contribute no heat", html);
            Assert.Contains("approximate ranking", html);
        }
    }

    // Covers the six audited Safe helpers, preserving complete text and marking clips.
    // Does not reconstruct omitted qualifications; a clip outside Safe would escape.
    [Theory]
    [InlineData(typeof(SectorReportGenerator))]
    [InlineData(typeof(CallSheetReportGenerator))]
    [InlineData(typeof(ExecSummaryReportGenerator))]
    [InlineData(typeof(PrimeConsultantReportGenerator))]
    [InlineData(typeof(PursuitDossierReportGenerator))]
    [InlineData(typeof(AwardProgramsReportGenerator))]
    public void RecommendationClips_AlwaysMarkAbridgement(Type generator)
    {
        var safe = generator.GetMethod("Safe", BindingFlags.NonPublic | BindingFlags.Static)!;
        var dangerous = new string('A', 600) + " Do not approach yet; structural scope is already awarded.";
        var actual = (string)safe.Invoke(null, new object[] { dangerous, 110 })!;
        Assert.EndsWith("[abridged]", actual);
        Assert.Equal("Complete text", safe.Invoke(null, new object[] { "Complete text", 110 }));
    }

    // Covers strategic summary/sample cap disclosure with retained count in DOCX.
    // Does not validate keyword matches; incorrect matching would escape.
    [Fact]
    public void StrategicSummary_LabelsRetrievedSampleAndDetailCap()
    {
        var projects = Enumerable.Range(1, 12).Select(i => $"Project {i}").ToArray();
        var doc = StrategicRelationshipsReportGenerator.Build(new[]
        {
            (StrategicTargetCatalog.All[0], (IReadOnlyList<string>)projects)
        }, Generated);
        var actual = ExportText(doc);
        Assert.Contains("Retrieved sample matches", actual);
        Assert.Contains("not total matches", actual);
        Assert.Contains("10 of 12 retrieved", actual);
        Assert.Equal("12", doc.Blocks.OfType<TableBlock>().Single().Rows[0][2]);
    }

    // Covers source wiring and fixed SQL predicates; runtime comparisons live in
    // BdIntegrityCheck. Wrong parameter binding or an added query could escape.
    [Fact]
    public void SqlBoundaries_UseSharedPopulationRulesAndAcknowledgements()
    {
        // Source wiring only. The DB invariants exercise the shared SQL against
        // independent EXISTS-based expectations; neither executes in this suite.
        var service = Source("Kor.Opportunities.Data/BdReports/SqlBdReportService.cs");
        Assert.DoesNotContain("COUNT(*) FROM opportunities.IntelPersonAffiliation", service);
        Assert.Contains("BdReportPopulationSql.CurrentPeopleFrom", service);
        var brief = Source("Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs");
        Assert.Equal(2, brief.Split("{BdReportPopulationSql.RegionLiveRfp}").Length - 1);
        Assert.Equal(2, brief.Split("{BdReportPopulationSql.RegionForward}").Length - 1);
        Assert.Contains("BdReportPopulationSql.CurrentPeopleFrom", brief);
        Assert.Contains("DismissedAtUtc IS NULL", BdReportPopulationSql.RegionLiveRfp);
        Assert.Contains("SubmissionDeadlineUtc > @asOf", BdReportPopulationSql.RegionLiveRfp);
        Assert.Contains("ProjectStage IN (N'CapitalPlan', N'FacilityRenewal')", BdReportPopulationSql.RegionForward);
        Assert.Contains("pa.IsCurrent = 1", BdReportPopulationSql.CurrentPeopleFrom);
        Assert.Contains("p.RetiredAtUtc IS NULL", BdReportPopulationSql.CurrentPeopleFrom);
        Assert.Contains("pa.RetiredAtUtc IS NULL", BdReportPopulationSql.CurrentPeopleFrom);
        var synthesis = Source("Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs");
        Assert.Contains("return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;", synthesis);
        Assert.DoesNotContain("ToLowerInvariant()", synthesis);
    }

    private static PursuitBriefRow Row(long id, decimal? cost) => new(
        id, $"Project {id}", "BC", "Healthcare", null, "RFP", null, cost, null,
        null, null, BdVerdicts.Pursue, "Angle", null, null, null, Generated, false);

    private static string ExportText(BdReportDocument doc)
    {
        using var stream = new MemoryStream(DocxBuilder.Render(doc));
        using var word = WordprocessingDocument.Open(stream, false);
        return word.MainDocumentPart!.Document!.Body!.InnerText;
    }

    private static string Source(string relative)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Operations.sln"))
               && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
