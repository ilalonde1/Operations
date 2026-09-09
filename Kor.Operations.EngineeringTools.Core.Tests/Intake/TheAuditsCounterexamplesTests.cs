#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The smallest inputs Codex's audit of 2026-09-08 (docs/codex/CODEX-24-AUDIT-RESPONSE.md) gave for
/// each defect it found in steps 1–11, each as the failing test that came before the fix. One test
/// per finding, named for it.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: F1 a four-point taper is not a wall; F3 a label chooses between two scheduled
/// types that fit one interrupted outline; F4 two grid rules 1.5 pt apart are two axes and an axis
/// sits on its rule; F5 PLAN NOTES and KEY PLAN are not plans; F6 a footing-only page still writes
/// a DXF with a FOOTING layer entry; F7 a path decided twice or never decided fails loudly; F8 two
/// different SCALE fields are a conflict the field must not undo; F11 a declared column turned 30°
/// is a column, a stroked white fill is not a wall, and a label in the next column does not cut a
/// title-block field off. WHAT IT DOES NOT: F2 (the footing tests), F9/F10 (ledger wording) and the
/// prose corrections, which are in the doc and the ledger, not here; and real drawings, whose numbers
/// the banked tests hold.
/// </remarks>
public sealed class TheAuditsCounterexamplesTests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);
    private static GP Circle(double cx, double cy, double r) => new(
        new List<(double X, double Y)> { (cx, cy + r), (cx + r, cy), (cx, cy - r), (cx - r, cy) }, true, false, true, cx - r, cy - r, cx + r, cy + r);
    private static GP VRule(double x, double y0, double y1) => new(new List<(double X, double Y)> { (x, y0), (x, y1) }, false, false, true, x, y0, x, y1);
    private static RawSubpath Filled(params (double X, double Y)[] pts) => new(pts.ToList(), true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false);

    [Fact]
    public void F1_AFourPointTaperIsNotAWall()
    {
        var fates = new List<PathFate>();
        var geometry = FateFixture.Classify([Filled((0, 0), (6000, 0), (5800, 300), (200, 300))], fates);
        Assert.NotEqual(PathReason.BecameWall, Assert.Single(fates).Reason);
        Assert.Empty(geometry.Walls);
        // and the rectangle it tapers from still is one
        fates.Clear();
        FateFixture.Classify([Filled((0, 0), (6000, 0), (6000, 300), (0, 300))], fates);
        Assert.Equal(PathReason.BecameWall, Assert.Single(fates).Reason);
    }

    [Fact]
    public void F11_ADeclaredColumnTurnedThirtyDegreesIsAColumnNotAWall()
    {
        // FateFixture declares 1800 × 400; turned 30° its world box is 1759 × 1246 and matched nothing
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        (double X, double Y) P(double x, double y) => (x * c - y * s, x * s + y * c);
        var fates = new List<PathFate>();
        var geometry = FateFixture.Classify([Filled(P(0, 0), P(1800, 0), P(1800, 400), P(0, 400))], fates);
        Assert.Equal(PathReason.BecameColumnByDeclaredSize, Assert.Single(fates).Reason);
        Assert.Empty(geometry.Walls);
        var (w, d) = Assert.Single(geometry.ColumnSizes);
        Assert.Equal(1800, w, 0); Assert.Equal(400, d, 0);
    }

    [Fact]
    public void F11_AStrokedWhiteFillOfWallProportionsIsNotAWall()
    {
        var white = FateFixture.Rect(2000, 1000) with { Color = (0xFF, 0xFF, 0xFF), IsStroked = true };
        var fates = new List<PathFate>();
        FateFixture.Classify([white], fates);
        Assert.NotEqual(PathReason.BecameWall, Assert.Single(fates).Reason);
    }

    [Fact]
    public void F3_ALabelChoosesBetweenTwoScheduledTypesThatFitOneInterruptedOutline()
    {
        static RawSubpath Line(double x0, double y0, double x1, double y1) => new([(x0, y0), (x1, y1)], false, (0, 0, 0), false, true, 0.5, false);
        // one full dashed side of 1,500 at x = 10,000 and two 300 mm stubs running +x
        var raw = new List<RawSubpath>();
        double dash = 1500 / 9.0;
        for (int k = 0; k < 5; k++) raw.Add(Line(10000, 20000 + k * 2 * dash, 10000, 20000 + k * 2 * dash + dash));
        raw.Add(Line(10000, 20000, 10300, 20000));
        raw.Add(Line(10000, 21500, 10300, 21500));
        var f1 = new FootingScheduleReader.FootingType("F1", 1500, 1500, 600);
        var f2 = new FootingScheduleReader.FootingType("F2", 1500, 1500, 900);
        var label = new FootingOutlines.MarkLabel("F2", 10700, 20700);

        var (footings, _) = FootingOutlines.Read(raw, [f1, f2], labels: [label]);
        var f = Assert.Single(footings);
        Assert.Equal("F2", f.Mark); Assert.Equal(900, f.DepthMm); Assert.True(f.LabelledOnThePlan);
        // schedule order does not decide it
        var (reversed, _) = FootingOutlines.Read(raw, [f2, f1], labels: [label]);
        Assert.Equal("F2", Assert.Single(reversed).Mark);
    }

    /// <summary>
    /// Two grid rules 1.5 pt apart (50 mm at 1:96) are two axes, each on its own rule — the audit's
    /// construction — and an axis sits on its rule, not on the circle drawn a point off it. The
    /// floor is the rule reader's <see cref="ScheduleTableBorder.StraightPts"/> (0.6 pt): closer than
    /// that, two lines are one rule to it.
    /// </summary>
    [Fact]
    public void F4_TwoRulesAPointAndAHalfApartAreTwoAxesAndAnAxisSitsOnItsRule()
    {
        var words = new List<TT> { Tok("C", 1000, 1900), Tok("C.2", 1001.5, 300), Tok("7", 1500, 1900) };
        var paths = new List<GP>
        {
            Circle(1000, 1900, 14), VRule(1000, 200, 1886),      // C on its own rule
            Circle(1001.5, 300, 14), VRule(1001.5, 314, 1800),   // C.2 on a second rule 1.5 pt away
            Circle(1500, 1900, 14), VRule(1501, 200, 1886),      // a bubble drawn 1 pt off its rule
        };
        var grid = GridBubbles.On(new PC(1, W, H, words, paths));
        Assert.Equal(3, grid.Axes.Count);
        Assert.Contains(grid.Axes, a => a.Name == "C" && Math.Abs(a.At - 1000) < 0.01 && !a.LabelsDisagree);
        Assert.Contains(grid.Axes, a => a.Name == "C.2" && Math.Abs(a.At - 1001.5) < 0.01 && !a.LabelsDisagree);
        var seven = Assert.Single(grid.Axes, a => a.Name == "7");
        Assert.Equal(1501, seven.At, 2);   // the rule, not the circle
        Assert.Equal(new[] { 1000.0, 1001.5, 1501.0 }, grid.VerticalAxesX.Select(x => Math.Round(x, 2)));
        // the two ends of ONE rule, labelled differently, are one axis that says so
        var slip = GridBubbles.On(new PC(1, W, H,
            new List<TT> { Tok("5", 1400, 1900), Tok("6", 1400, 200) },
            new List<GP> { Circle(1400, 1900, 14), Circle(1400, 200, 14), VRule(1400, 214, 1886) }));
        var one = Assert.Single(slip.Axes);
        Assert.True(one.LabelsDisagree); Assert.Equal("5|6", one.Name);
    }

    [Theory]
    [InlineData("FOUNDATION PLAN NOTES", "notes/general")]
    [InlineData("KEY PLAN", "other")]
    [InlineData("LEVEL P1 FOUNDATION PLAN - WEST", "plan")]
    [InlineData("DESIGN LOAD PLANS", "plan")]
    [InlineData("FOUNDATION PLAN", "plan")]
    [InlineData("FOUNDATION SCHEDULE", "schedule")]
    [InlineData("FOUNDATION PLAN SCHEDULE", "schedule")]
    [InlineData("SHEAR WALL ELEVATIONS - BLDG B", "section/elevation")]
    public void F5_APlanFollowedByNotesOrScheduleIsThatKindAndAKeyPlanIsAnInset(string title, string expected)
    {
        Assert.Equal(expected, DrawingIntake.FirstTyped(title));
    }

    [Fact]
    public void F6_AFootingOnlyPageWritesADxfWithAFootingLayerEntry()
    {
        var g = new ExtractedGeometry();
        g.Footings.Add(new FootingOutline("F2", new[] { (10000.0, 20000.0), (11524.0, 20000.0), (11524.0, 21524.0), (10000.0, 21524.0) }, (10762, 20762), 1524, 1524, 813) { LabelledOnThePlan = true });
        string path = Path.Combine(Path.GetTempPath(), $"footing-only-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(g, path);
            Assert.True(File.Exists(path), "a footing-only page wrote no DXF");
            var lines = File.ReadAllLines(path).Select(l => l.Trim()).ToList();
            int tables = lines.IndexOf("TABLES"), entities = lines.IndexOf("ENTITIES");
            Assert.True(tables >= 0 && entities > tables);
            // a LAYER record named FOOTING in the table, and a POLYLINE on it among the entities
            bool layerDeclared = Enumerable.Range(tables, entities - tables - 1).Any(i => lines[i] == "LAYER" && lines[i + 1] == "2" && lines[i + 2] == "FOOTING");
            Assert.True(layerDeclared, "FOOTING is written as an entity layer but not declared in the LAYER table");
            bool polyline = Enumerable.Range(entities, lines.Count - entities - 3).Any(i => lines[i] == "POLYLINE" && lines[i + 1] == "8" && lines[i + 2] == "FOOTING");
            Assert.True(polyline);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void F7_APathDecidedTwiceOrNeverDecidedFailsLoudly()
    {
        var twice = new List<PathFate> { new(0, Disposition.Read, PathReason.BecameSlab, 0), new(0, Disposition.Discarded, PathReason.TooShort, null) };
        Assert.Throws<InvalidOperationException>(() => DrawingIntake.RemapToPopulation(twice, [0], [0], 1, 1));
        Assert.Throws<InvalidOperationException>(() => DrawingIntake.RemapToPopulation([], [0], [0], 1, 1));
        // a path the thinned read dropped is collapsed, as before
        var one = DrawingIntake.RemapToPopulation([new(0, Disposition.Read, PathReason.BecameSlab, 0)], [1], [0, 1], 1, 2);
        Assert.Equal(PathReason.CollapsedByThinning, one[0].Reason);
        Assert.Equal(PathReason.BecameSlab, one[1].Reason);
    }

    [Fact]
    public void F8_TwoDifferentScaleFieldsAreAConflictTheFieldMustNotUndo()
    {
        // the bottom-right of a 1000 × 700 sheet: two SCALE fields, 1 : 100 and 1 : 50
        var conflicting = new PC(1, 1000, 700, new List<TT>
        {
            Tok("SCALE:", 850, 100), Tok("1", 880, 100), Tok(":", 892, 100), Tok("100", 910, 100),
            Tok("SCALE:", 850, 60), Tok("1", 880, 60), Tok(":", 892, 60), Tok("50", 910, 60),
        }, new List<GP>());
        Assert.True(SheetScaleReader.StatesConflictingScales(conflicting));
        Assert.Null(SheetScaleReader.FromPage(conflicting));
        var one = new PC(1, 1000, 700, new List<TT> { Tok("SCALE:", 850, 100), Tok("1", 880, 100), Tok(":", 892, 100), Tok("100", 910, 100) }, new List<GP>());
        Assert.False(SheetScaleReader.StatesConflictingScales(one));
        Assert.Equal("1 : 100", SheetScaleReader.FromPage(one));
    }

    /// <summary>The level reader took "LEVEL 1 - CONCRETE" as a level named CONCRETE and "LEVEL 22 / MECH." as MECH. (doc §16, §19): the value is the level-shaped token on the label's own line.</summary>
    [Fact]
    public void TheLevelValueIsTheNumberOnTheLabelsLineNotTheWordWrappedUnderIt()
    {
        var page = new PC(1, W, H, new List<TT>
        {
            Tok("LEVEL", 100, 1000), Tok("1", 130, 1000), Tok("-", 145, 1000), Tok("CONCRETE", 118, 992),   // CONCRETE is nearer in x, on the line below
            Tok("LEVEL", 100, 900), Tok("22", 132, 900), Tok("MECH.", 122, 892),
            Tok("LEVEL", 100, 800), Tok("L0/P1", 136, 800),
        }, new List<GP>());
        var ladder = ScheduleGridReader.ReadLevelLadder(page);
        Assert.Equal(new[] { "L1", "L22", "L0/P1" }, ladder.Select(r => r.Normalized));
    }

    [Fact]
    public void F11_ALabelInTheNextColumnDoesNotCutATitleBlockFieldOff()
    {
        // a 1,500-wide sheet; the right fifth starts at 1,200. SHEET TITLE's column runs 260 pt; the REV
        // label sits beyond it, one line down, and used to end the field above it
        var page = new PC(1, 1500, 700, new List<TT>
        {
            Tok("SHEET", 1220, 300), Tok("TITLE", 1260, 300),
            Tok("REV", 1490, 280),
            Tok("LEVEL", 1220, 260), Tok("P1", 1260, 260), Tok("FOUNDATION", 1300, 260), Tok("PLAN", 1350, 260),
        }, new List<GP>());
        var fields = TitleBlockFields.Read(page);
        Assert.Equal("LEVEL P1 FOUNDATION PLAN", fields["SHEET TITLE"]);
    }
}
