#nullable enable
using System.IO;
using System.Reflection;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.MajorProjects;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

/// <summary>
/// Brief 6: EXPECTED evidence-limited populations and claims versus ACTUAL shared
/// SQL wiring, urgency classification, project appointment text and model context.
/// Adversarial VALUES fixtures in BdIntegrityCheck feed the production predicates
/// dismissed/expired/completed/wrong-role/wrong-country rows (authored, not run).
/// Does not execute SQL, WPF, PDF or a model. Incorrect upstream FKs, source roles
/// or verdicts, deployment drift and UI clipping escape these checks.
/// No builds/tests/database access permitted for this change; no red/green claim.
/// </summary>
public sealed class BdEvidencePopulationRegressionTests
{
    [Theory]
    [InlineData("PURSUE", "No urgent action; wait for funding", false)]
    [InlineData("PURSUE", "URGENT", false)]
    [InlineData("MONITOR", "URGENT", false)]
    [InlineData(" pursue_urgent ", "No narrative supplied", true)]
    [InlineData(null, "urgent", false)]
    public void Urgency_RequiresStructuredEvidence(string? verdict, string? narrative, bool expected)
    {
        var actual = BdVerdicts.IsUrgent(verdict, narrative);
        Assert.True(actual == expected, $"EXPECTED urgent={expected}; ACTUAL {actual} for {verdict}: {narrative}");
    }

    [Fact]
    public void LiveReaders_UseTheSharedUndismissedPopulation()
    {
        var actual = Source("Kor.Opportunities.Data/BdReports/SqlBdReportService.cs");
        foreach (var method in new[] { "GetCallSheetPoolAsync", "GetPursuitDossiersAsync", "GetAttackCardsHtmlAsync" })
        {
            var section = actual[actual.IndexOf(" " + method + "(", StringComparison.Ordinal)..];
            section = section[section.IndexOf("var sql = $@\"", StringComparison.Ordinal)..];
            section = section[..section.IndexOf("\";", StringComparison.Ordinal)];
            Assert.True(section.Contains("BdReportPopulationSql.LivePursuit", StringComparison.Ordinal),
                $"EXPECTED {method} to use shared live population; ACTUAL:\n{section}");
        }
        Assert.Contains("m.DismissedAtUtc IS NULL", BdReportPopulationSql.LivePursuit);
        Assert.Contains("m.RetiredAtUtc IS NULL", BdReportPopulationSql.LivePursuit);
    }

    [Fact]
    public void SectorAndRegion_UseTheSameEligibilityWithQualifiedAliases()
    {
        Assert.Equal(BdReportPopulationSql.LiveRfp("", "@prov"), BdReportPopulationSql.RegionLiveRfp);
        Assert.Equal(BdReportPopulationSql.ForwardProject("", "@prov"), BdReportPopulationSql.RegionForward);
        var source = Source("Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs");
        Assert.Contains("BdReportPopulationSql.LiveRfp(\"o.\", \"@oppProvince\")", source);
        Assert.Contains("BdReportPopulationSql.ForwardProject(\"mpi.\", \"@province\")", source);
        Assert.Contains("BuildSectorOpportunityFilter(request, asOf)", source);
        Assert.Contains("Value = asOf", source);
    }

    [Theory]
    [InlineData("KOR", "KorStructural")]
    [InlineData("Other firm", "Structural")]
    public void ProjectAppointment_OverridesArchitectOpenPattern_AndRemainsSeparateInModelContext(string name, string kind)
    {
        var project = new PursuitBriefProject(1, "Project", null, null, null, null, null, "", null,
            name, 7, kind);
        var brief = new PursuitBrief(project, null, null,
            new PursuitBriefArchitect("Architect", 2, "OPEN", "HIGH", "General pattern", new[] { "Partner" }),
            new(null, null, null, null), Array.Empty<PursuitBriefPerson>(), Array.Empty<PursuitBriefPerson>(),
            Array.Empty<PursuitBriefCompetitorNote>(), null, null, null);
        var actual = project.StructuralAppointmentDisplay;
        Assert.Contains(name, actual);
        Assert.DoesNotContain("OPEN", actual);
        var context = (string)typeof(PursuitBriefWindow).GetMethod("BuildApproachContext",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { brief })!;
        Assert.Contains(actual, context);
        Assert.True(context.IndexOf(actual, StringComparison.Ordinal) < context.IndexOf("ARCHITECT-WIDE", StringComparison.Ordinal),
            $"EXPECTED project appointment before separate architect pattern; ACTUAL:\n{context}");
        Assert.DoesNotContain("incumbents to displace", context);
        Assert.Contains("not project appointment evidence", context);
    }

    [Fact]
    public void UnknownProjectSeat_IsNotInferredOpen_AndAllRenderersUseProjectEvidence()
    {
        var project = new PursuitBriefProject(1, "Project", null, null, null, null, null, "", null);
        Assert.Contains("unknown", project.StructuralAppointmentDisplay);
        Assert.Contains("not established", project.StructuralAppointmentDisplay);
        var named = project with { StructuralEngineerName = "Source firm" };
        Assert.Contains("unresolved", named.StructuralAppointmentDisplay);
        Assert.Contains("structuralOrg.Id AS StructuralEngineerOrgId",
            Source("Kor.Opportunities.Data/MajorProjects/SqlPursuitBriefStore.cs"));
        Assert.Contains("Brief?.Project.StructuralAppointmentDisplay",
            Source("Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefViewModel.cs"));
        Assert.Contains("brief.Project.StructuralAppointmentDisplay",
            Source("Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefPdfExporter.cs"));
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefWindow.xaml");
        Assert.DoesNotContain("Likely prime &amp; structural seat", xaml);
        Assert.DoesNotContain("Brief.Architect.SeatStatus, Converter", xaml);
        Assert.Contains("Architect-wide relationship pattern", xaml);
    }

    [Fact]
    public void ReportWording_DoesNotUpgradeRecordedRolesOrRegionMatchesToConfirmation()
    {
        var competitor = CompetitorIntelReportGenerator.Build(new[]
        {
            new CompetitorFootprintRow(1, "Firm", 1, 0, 0, 1, new[] { "Recorded project" })
        }, DateTimeOffset.UtcNow);
        var actual = HtmlPreviewBuilder.Render(competitor);
        Assert.Contains("Projects with a recorded structural role (may be historical)", actual);
        Assert.DoesNotContain("Projects confirmed as structural engineer", actual);
        var awards = AwardProgramsReportGenerator.Build(new[]
        {
            new AwardProgramReportRow(1, "Body", "Program", null, null, "Structural", "US",
                null, null, null, null, 7, "KOR project")
        }, DateTimeOffset.UtcNow);
        var table = Assert.Single(awards.Blocks.OfType<TableBlock>());
        Assert.Contains("Recorded KOR structural project — region match", table.Headers);
        Assert.DoesNotContain("Light KOR-project match", HtmlPreviewBuilder.Render(awards));
        Assert.Contains("application suitability still require review", HtmlPreviewBuilder.Render(awards));
    }

    [Fact]
    public void StructuralAndAwardReaders_UseEvidencePredicatesAndControlledFixtures()
    {
        var service = Source("Kor.Opportunities.Data/BdReports/SqlBdReportService.cs");
        Assert.Equal(2, service.Split("{BdReportPopulationSql.StructuralRole}").Length - 1);
        Assert.Contains("{BdReportPopulationSql.AwardProjectMatch}", service);
        Assert.DoesNotContain("LIKE N'%engineer%'", service);
        var fixtures = Source("tools/BdIntegrityCheck/Program.cs");
        foreach (var key in new[] { "bd_live_pursuit_fixture", "bd_sector_eligibility_fixture",
            "bd_structural_role_fixture", "bd_award_project_match_fixture" })
            Assert.True(fixtures.Contains(key, StringComparison.Ordinal), $"EXPECTED controlled SQL gate {key}; ACTUAL missing");
        Assert.Contains("Mechanical engineer", fixtures);
        Assert.Contains("No structural role", fixtures);
        Assert.Contains("Completed", fixtures);
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(
        XamlStaticResourceOrderTests.GetRepoRoot(), path));
}
