using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A STRIP LONGER THAN TEN METRES IS A POUR STRIP, NOT A HOLE (intake step 108, 2026-09-16). 30933's L0 went out with two
/// 0.3 x 44 m slits down the middle of the plate and two 1 x 33 m bands along its edge cut as openings: pour strips drawn as
/// closed loops on the slab-edge layer, which the DXF route's ring rule cuts. Her own models judged the shape before the
/// rule was written (e2k-ask openings-shapes over 96 exported models, 4,967 openings): 504 strips ten times longer than
/// wide, 503 of them sub-metre sleeves under 10 m long, ONE longer than 10 m. So the composer refuses an opening that is
/// both - at least ten times longer than wide AND longer than 10 m - on the least box round it, and flags it.
/// WHAT THIS COVERS: the long thin strip is not cut, on the axes and at an angle; a short sleeve of the same aspect is;
/// a long opening of a modest aspect is; the flag names the sheet, the storey and the size; the thresholds are the rows'
/// (dxf.pour-strip-min-length-mm, dxf.pour-strip-aspect; migration 098). WHAT IT DOES NOT: the DXF classifier's own ring flags; a pour
/// strip shorter than 10 m (cut, as her sleeves are); the real sets (30933 in run 31).
/// </summary>
public sealed class AStripLongerThanTenMetresIsNoHoleTests
{
    private static readonly string[] Reference =
    {
        "$ PROGRAM INFORMATION",
        "  PROGRAM  \"ETABS\"  VERSION \"21.2.0\"",
        "",
        "$ CONTROLS",
        "  UNITS  \"KIP\"  \"IN\"  \"F\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 3\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 144",
        "  STORY \"Base\"  HEIGHT 0",
        "",
        "$ MATERIAL PROPERTIES",
        "  MATERIAL  \"65 MPa Walls\"    TYPE \"Concrete\"    GRADE \"x\"",
        "",
        "$ POINT COORDINATES",
        "  POINT \"1\"  0 0 0",
        "",
        "$ AREA CONNECTIVITIES",
        "",
    };

    private const double In = 1 / 25.4;   // model units (inches) per millimetre

    private static PlanLoop Box(double x0Mm, double y0Mm, double x1Mm, double y1Mm)
        => new("JBP_C_SLABEDG", new[] { new DxfPoint(x0Mm * In, y0Mm * In), new DxfPoint(x1Mm * In, y0Mm * In), new DxfPoint(x1Mm * In, y1Mm * In), new DxfPoint(x0Mm * In, y1Mm * In) }, true);

    private static PlanLoop Rotated(PlanLoop loop, double degrees, double aboutXMm, double aboutYMm)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r), ax = aboutXMm * In, ay = aboutYMm * In;
        return new PlanLoop(loop.Layer, loop.Points.Select(p => new DxfPoint(ax + (p.X - ax) * c - (p.Y - ay) * s, ay + (p.X - ax) * s + (p.Y - ay) * c)).ToList(), true);
    }

    private static (int Cut, List<string> Flags) Compose(params PlanLoop[] openings)
    {
        var doc = E2kDocument.Parse(Reference);
        var story = doc.ReadStories().Single(s => s.Name == "LEVEL 3");
        var plan = new PlanGeometrySet();
        plan.Slabs.Add(Box(0, 0, 60000, 60000));                       // a 60 x 60 m plate
        plan.Walls.Add(new WallAxis(new DxfPoint(0, 0), new DxfPoint(60000 * In, 0), 12, "JBP_V-WALL"));
        plan.Openings.AddRange(openings);
        var summary = E2kGeometryComposer.Compose(doc, new[] { new StoryPlacement(story, plan, "plan.dxf") });
        int cut = doc.LinesOf("AREA ASSIGNS").Count(l => l.Contains("OPENING \"Yes\"", StringComparison.Ordinal));
        return (cut, summary.Flags.Where(f => f.Contains("pour strip", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public void ALongThinStripIsNotCut_OnTheAxesOrAtAnAngle()
    {
        var (cut, flags) = Compose(
            Box(20000, 5000, 20300, 49000),                             // 0.3 x 44 m down the plate: 30933's slit
            Rotated(Box(30000, 5000, 31000, 38000), 30, 30500, 21500)); // 1 x 33 m at 30 degrees: the same strip, turned
        Assert.Equal(0, cut);
        Assert.Equal(2, flags.Count);
        Assert.Contains(flags, f => f.Contains("plan.dxf", StringComparison.Ordinal) && f.Contains("LEVEL 3", StringComparison.Ordinal) && f.Contains("0.3 x 44.0 m", StringComparison.Ordinal));
    }

    [Fact]
    public void AShortSleeveOfTheSameAspectIsCut_AndSoIsALongHoleOfAModestOne()
    {
        var (cut, flags) = Compose(
            Box(10000, 10000, 10500, 15000),                            // 0.5 x 5 m: a sleeve, one of her 503
            Box(40000, 10000, 42000, 22000));                           // 2 x 12 m: six times longer than wide, a void
        Assert.Equal(2, cut);
        Assert.Empty(flags);
    }

    [Fact]
    public void TheThresholdsAreTheRows()
    {
        // the numbers migration 098 banks as dxf.pour-strip-min-length-mm and dxf.pour-strip-aspect; CompiledDefaultsAreTheBankedRowsTests holds them to the rows
        var options = new ComposeOptions();
        Assert.Equal(10000.0, options.PourStripMinLengthMm);
        Assert.Equal(10.0, options.PourStripAspect);
        Assert.Contains("dxf.pour-strip-min-length-mm", DxfToEtabsService.RequiredRuleKeys);
        Assert.Contains("dxf.pour-strip-aspect", DxfToEtabsService.RequiredRuleKeys);
    }
}
