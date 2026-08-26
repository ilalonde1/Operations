using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class StickFileSlabThicknessReaderTests
{
    [Fact]
    public void NoStickFileSupplied_KeepsLegacyAssumedReportWording()
    {
        var doc = Model("LEVEL 3,0", "LEVEL 4,144");
        var level3 = doc.ReadStories().Single(s => s.Name == "LEVEL 3");

        var summary = E2kGeometryComposer.Compose(
            doc,
            new[] { new StoryPlacement(level3, FloorGeometry(), "level3.dxf") },
            new ComposeOptions { MembersRiseToStoreyAbove = false });

        string flag = Assert.Single(summary.Flags, f => f.Contains("Slab thickness is ASSUMED"));
        Assert.Contains("every one of the 1 floor plate(s) in this model is 12\" thick", flag);
        Assert.DoesNotContain("stick-file", flag);
    }

    [Fact]
    public void ARepeatedPageThicknessSuppliesTheMatchedSheet()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf");
        var page = Page(
            16,
            "LEVEL 1 PLAN - CONCRETE OUTLINE",
            "14\" SLAB",
            "14\" SLAB",
            "14\" SLAB",
            "30\" SLAB");

        var matches = StickFileSlabThicknessReader.MatchSheetsToPages(new[] { sheet }, new[] { page });

        var match = Assert.Single(matches).Value;
        Assert.Equal(14, match.ThicknessInches);

        var doc = Model("LEVEL 1,0", "LEVEL 2,144");
        var level1 = doc.ReadStories().Single(s => s.Name == "LEVEL 1");

        var summary = E2kGeometryComposer.Compose(
            doc,
            new[]
            {
                new StoryPlacement(level1, FloorGeometry(), sheet.FileName)
                {
                    SlabThickness = match.ThicknessInches,
                    SlabThicknessInches = match.ThicknessInches,
                    SlabThicknessPage = match.PageNumber,
                },
            },
            new ComposeOptions { DefaultSlabThickness = 12, MembersRiseToStoreyAbove = false, StickFileSlabThicknessAttempted = true });

        Assert.Contains("KOR-S14", summary.Sections);
        Assert.DoesNotContain("KOR-S12", summary.Sections);
        Assert.Contains(summary.Flags, f => f.Contains("LEVEL 1: 14\" from PDF page 16"));
    }

    [Fact]
    public void UnmatchedSheetKeepsDefaultAndReportSaysAssumed()
    {
        var doc = Model("LEVEL 9,0", "LEVEL 10,144");
        var level9 = doc.ReadStories().Single(s => s.Name == "LEVEL 9");

        var summary = E2kGeometryComposer.Compose(
            doc,
            new[] { new StoryPlacement(level9, FloorGeometry(), "level9.dxf") },
            new ComposeOptions { DefaultSlabThickness = 12, MembersRiseToStoreyAbove = false, StickFileSlabThicknessAttempted = true });

        Assert.Contains("KOR-S12", summary.Sections);
        string flag = Assert.Single(summary.Flags, f => f.Contains("Slab thickness still ASSUMED"));
        Assert.Contains("LEVEL 9", flag);
        Assert.Contains("did not match a stick-file PDF page with a readable field slab thickness", flag);
    }

    [Fact]
    public void OnePageWithTwoPlanTitlesSuppliesBothSheets()
    {
        var level3 = PlanSheetNaming.Parse("--Structural Plan - LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C.dxf");
        var level4 = PlanSheetNaming.Parse("--Structural Plan - LEVEL 4 PLAN - CONCRETE OUTLINE - BLDG C.dxf");
        var page = new StickFileSlabThicknessPage(
            30,
            new[]
            {
                "LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C",
                "LEVEL 4 PLAN - CONCRETE OUTLINE - BLDG C",
            },
            new[] { "14\" SLAB", "14\" SLAB", "24\" SLAB" });

        var matches = StickFileSlabThicknessReader.MatchSheetsToPages(new[] { level3, level4 }, new[] { page });

        Assert.Equal(2, matches.Count);
        Assert.Equal(30, matches[level3.FileName].PageNumber);
        Assert.Equal(14, matches[level3.FileName].ThicknessInches);
        Assert.Equal(30, matches[level4.FileName].PageNumber);
        Assert.Equal(14, matches[level4.FileName].ThicknessInches);
    }

    [Fact]
    public void BuildingPrefixTitleMatchesTheSameSheetTitle()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C.dxf");
        var page = Page(30, "BLDG C LEVEL 3 PLAN - CONCRETE OUTLINE", "14\" SLAB", "14\" SLAB");

        var matches = StickFileSlabThicknessReader.MatchSheetsToPages(new[] { sheet }, new[] { page });

        Assert.Equal(14, matches[sheet.FileName].ThicknessInches);
    }

    [Fact]
    public void StructuralDuplicateBeatsWithArchitectureDuplicateDeterministically()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C.dxf");
        var structural = Page(30, "LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C", "14\" SLAB", "14\" SLAB");
        var withArchitecture = Page(
            62,
            "LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C WITH ARCHITECTURAL BACKGROUND",
            "ARCHITECTURAL BACKGROUND",
            "8\" SLAB",
            "8\" SLAB");

        var matches = StickFileSlabThicknessReader.MatchSheetsToPages(
            new[] { sheet },
            new[] { withArchitecture, structural });

        var match = matches[sheet.FileName];
        Assert.Equal(30, match.PageNumber);
        Assert.Equal(14, match.ThicknessInches);
    }

    [Fact]
    public void ReportDistinguishesReadFromAssumedAndNamesLocalThickeningLimit()
    {
        var doc = Model("LEVEL 1,0", "LEVEL 2,144", "LEVEL 3,288");
        var level1 = doc.ReadStories().Single(s => s.Name == "LEVEL 1");
        var level2 = doc.ReadStories().Single(s => s.Name == "LEVEL 2");

        var summary = E2kGeometryComposer.Compose(
            doc,
            new[]
            {
                new StoryPlacement(level1, FloorGeometry(), "level1.dxf")
                {
                    SlabThickness = 14,
                    SlabThicknessInches = 14,
                    SlabThicknessPage = 16,
                },
                new StoryPlacement(level2, FloorGeometry(400), "level2.dxf"),
            },
            new ComposeOptions { DefaultSlabThickness = 12, MembersRiseToStoreyAbove = false, StickFileSlabThicknessAttempted = true });

        string read = Assert.Single(summary.Flags, f => f.Contains("Slab thickness read from the stick file PDF"));
        Assert.Contains("LEVEL 1: 14\" from PDF page 16", read);
        Assert.Contains("Local thickenings, drop bands and transfer bands", read);
        Assert.Contains("30\" or 36\" bands", read);

        string assumed = Assert.Single(summary.Flags, f => f.Contains("Slab thickness still ASSUMED"));
        Assert.Contains("LEVEL 2", assumed);
    }

    private static StickFileSlabThicknessPage Page(int number, string title, params string[] lines)
        => new(number, new[] { title }, new[] { title }.Concat(lines).ToList());

    private static E2kDocument Model(params string[] levels)
        => E2kShellBuilder.FromLevels(E2kShellBuilder.ParseLevels(levels), "in");

    private static PlanGeometrySet FloorGeometry(double x0 = 0)
    {
        var g = new PlanGeometrySet();
        g.Walls.Add(new WallAxis(new DxfPoint(x0, 0), new DxfPoint(x0 + 240, 0), 12, "JBP_V-WALL"));
        g.Slabs.Add(new PlanLoop("JBP_C_SLABEDG", new[]
        {
            new DxfPoint(x0, 0),
            new DxfPoint(x0 + 240, 0),
            new DxfPoint(x0 + 240, 240),
            new DxfPoint(x0, 240),
        }, true));
        return g;
    }
}
