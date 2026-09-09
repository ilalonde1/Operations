#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// A storey's plate is what its wall panels enclose, at the walls' OUTER face (Andrea Neuviale,
/// 25 Aug 2026: "it should always follow the outer edge of the walls"), where the drawing closes
/// no slab edge. The paint is closed across gaps up to a doorway, so a wall that stops at a door
/// still bounds the floor; a ring that does not close gives nothing.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: four panels round a rectangle giving the outer rectangle's area within 2%;
/// the same ring with a 48" doorway in one wall, still the plate; a 10 ft gap, no plate; too few
/// panels, no plate; an L-shaped ring, the L's area; a turned ring. WHAT IT DOES NOT: the
/// classifier's own gate (only where no slab edge closes, and never on a FOUNDATION sheet —
/// `StructuralPlanClassifier` keeps those); a real sheet (the PDF-only build of 31168's P2 against
/// Revit's 76,967 sq ft, the engineer models on 31065 and 31138 through the benchmark); a ring
/// whose gap is a ramp wider than a doorway, which leaks by design.
/// </remarks>
public sealed class AStoreysPlateIsWhatItsWallsEncloseTests
{
    private static readonly PlanClassificationOptions Options = new();
    private const double T = 12.0;                               // inches, the DXF side's unit in these tests

    private static WallAxis Wall(double x0, double y0, double x1, double y1, double t = T)
        => new(new DxfPoint(x0, y0), new DxfPoint(x1, y1), t, "WALL");

    /// <summary>A ring of four panels whose OUTER faces are the rectangle (0,0)–(w,h); axes run T/2 inside.</summary>
    private static List<WallAxis> Ring(double w, double h, double x = 0, double y = 0) =>
    [
        Wall(x, y + T / 2, x + w, y + T / 2),                     // south
        Wall(x + w - T / 2, y, x + w - T / 2, y + h),             // east
        Wall(x + w, y + h - T / 2, x, y + h - T / 2),             // north
        Wall(x + T / 2, y + h, x + T / 2, y),                     // west
    ];

    [Fact]
    public void FourPanelsRoundARectangleGiveTheOuterRectangle()
    {
        double w = 100 * 12, h = 60 * 12;
        var plate = DxfFloodFillPlateDetector.EnclosedByWallPanels(Ring(w, h), Options, out string note);
        Assert.NotNull(plate);
        Assert.InRange(plate!.Area, w * h * 0.98, w * h * 1.02);
        Assert.InRange(plate.Points.Min(p => p.X), -6, 6);        // the outer face, not the inner one
        Assert.InRange(plate.Points.Max(p => p.X), w - 6, w + 6);
        Assert.Contains("OUTER face", note);
    }

    [Fact]
    public void ADoorwayInOneWallDoesNotOpenTheFloor()
    {
        double w = 100 * 12, h = 60 * 12, door = 48;
        var ring = Ring(w, h);
        ring[0] = Wall(0, T / 2, w / 2 - door / 2, T / 2);                       // south wall stops at the door
        ring.Add(Wall(w / 2 + door / 2, T / 2, w, T / 2));                       // and resumes past it
        var plate = DxfFloodFillPlateDetector.EnclosedByWallPanels(ring, Options, out _);
        Assert.NotNull(plate);
        Assert.InRange(plate!.Area, w * h * 0.98, w * h * 1.02);
    }

    [Fact]
    public void AGapWiderThanADoorwayLeaksAndGivesNoPlate()
    {
        double w = 100 * 12, h = 60 * 12, gap = 120;
        var ring = Ring(w, h);
        ring[0] = Wall(0, T / 2, w / 2 - gap / 2, T / 2);
        ring.Add(Wall(w / 2 + gap / 2, T / 2, w, T / 2));
        Assert.Null(DxfFloodFillPlateDetector.EnclosedByWallPanels(ring, Options, out _));
    }

    [Fact]
    public void TwoPanelsAreNotARing()
    {
        Assert.Null(DxfFloodFillPlateDetector.EnclosedByWallPanels(Ring(1200, 720).Take(2).ToList(), Options, out _));
    }

    [Fact]
    public void AnLShapedRingGivesTheL()
    {
        // an L: a 100x60 ft rectangle with its top-right 40x30 ft corner missing
        double w = 1200, h = 720, cw = 480, ch = 360;
        var ring = new List<WallAxis>
        {
            Wall(0, T / 2, w, T / 2),                                  // south
            Wall(w - T / 2, 0, w - T / 2, h - ch),                     // east, lower part
            Wall(w, h - ch - T / 2, w - cw, h - ch - T / 2),           // the notch's bottom
            Wall(w - cw + T / 2, h - ch, w - cw + T / 2, h),           // the notch's side
            Wall(w - cw, h - T / 2, 0, h - T / 2),                     // north, left part
            Wall(T / 2, h, T / 2, 0),                                  // west
        };
        var plate = DxfFloodFillPlateDetector.EnclosedByWallPanels(ring, Options, out _);
        Assert.NotNull(plate);
        // the closing that bridges doorways also fills a re-entrant corner by about the doorway's
        // radius: on this L that is 2.3% (measured 707,076 against 691,200 sq in), a known limit
        double expected = w * h - cw * ch;
        Assert.InRange(plate!.Area, expected * 0.98, expected * 1.03);
    }

    [Fact]
    public void ATurnedRingIsATurnedPlate()
    {
        double c = Math.Cos(Math.PI / 7), s = Math.Sin(Math.PI / 7), w = 1200, h = 720;
        DxfPoint P(DxfPoint p) => new(5000 + p.X * c - p.Y * s, 5000 + p.X * s + p.Y * c);
        var turned = Ring(w, h).Select(a => new WallAxis(P(a.Start), P(a.End), a.Thickness, a.Layer)).ToList();
        var plate = DxfFloodFillPlateDetector.EnclosedByWallPanels(turned, Options, out _);
        Assert.NotNull(plate);
        Assert.InRange(plate!.Area, w * h * 0.97, w * h * 1.03);
    }
}
