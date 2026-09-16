#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// TWO EDGES THAT MEET AT A COLUMN ARE JOINED THROUGH IT (intake step 97, 2026-09-16). The drafter draws the slab
/// edge from column to column and the column's box over the corner; the box is read as the column and leaves the
/// line set, so every edge ends short of an empty corner and the ring never closes. 31130's west tower plan
/// (p22, LEVEL 3-16) carried no plate for that reason on fourteen storeys - 17 of its 58 chain ends within a
/// foot of a column's footprint - and three attempts to blame the tendons (steps 80, 96, 96B) each emptied more.
/// WHAT THIS COVERS: a floor whose four edges stop short of its four corner columns closing at the column
/// centres to its full area; the same lines with no columns staying open (the columns are the rule, not a
/// wider bridge); columns BESIDE the edges' ends, off their line by more than half a column, joining nothing;
/// a lone line running into a column changing nothing. WHAT IT DOES NOT: an edge stopping more than the
/// corner-carry reach short (4 ft, the existing bound); a corner column read as a wall; an outline piece
/// drawn as a filled rectangle rather than a line (31130's east tower, p35); the real sheets, which are the
/// six-set gate. A same-class fault it would not catch: two beams meeting at an interior column and, with a
/// third line, closing a cell that holds a column of its own and joins the floor - the union would take it.
/// </summary>
public sealed class TwoEdgesMeetingAtAColumnAreJoinedThroughItTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 48000, Y1 = 26000;   // 8 x 6 m, 48 sq m: over the 400 sq ft a floor must have
    private const double HalfW = 300, HalfD = 400;                            // the fixture's column is 600 x 800
    private const double Short = 150;                                          // the edge stops this far short of the column's box

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    // the four edges, each stopping short of the corner columns' boxes at both ends, and a tendon from inside the
    // floor whose anchor sits 100 mm from the south edge's west end - the third end at that corner (section 109:
    // the anchor ON the edge). Without a column to meet at, the south edge's end bridges to the anchor, its
    // cheapest partner, the west edge's end is left alone, and the corner-carry that closes an empty corner never runs.
    private static RawSubpath[] EdgesStoppingAtTheCorners() =>
    [
        Line(X0 + HalfW + Short, Y0, X1 - HalfW - Short, Y0),   // south
        Line(X1, Y0 + HalfD + Short, X1, Y1 - HalfD - Short),   // east
        Line(X1 - HalfW - Short, Y1, X0 + HalfW + Short, Y1),   // north
        Line(X0, Y1 - HalfD - Short, X0, Y0 + HalfD + Short),   // west
        Line(44000, 23000, X0 + HalfW + Short + 100, Y0 + 100), // the tendon, anchored at the south-west corner
    ];

    [Fact]
    public void AFloorWhoseEdgesStopAtItsCornerColumnsClosesThroughThem()
    {
        var g = Read([.. EdgesStoppingAtTheCorners(), Column(X0, Y0), Column(X1, Y0), Column(X1, Y1), Column(X0, Y1), Column(44000, 23000)]);
        var plate = Assert.Single(g.Slabs);
        Assert.Equal((X1 - X0) * (Y1 - Y0), Area(plate), 1);
    }

    [Fact]
    public void TheSameEdgesWithNoCornerColumnsStayOpen()
    {
        // the gaps are the same size; without a column at the corner there is nothing for the edges to meet at
        var g = Read([.. EdgesStoppingAtTheCorners(), Column(44000, 23000)]);
        Assert.Empty(g.Slabs);
    }

    [Fact]
    public void AColumnBesideAnEdgesEndJoinsNothing()
    {
        // the corner columns stand 1.2 m off both edges' lines, outside the corner: not ahead of any end
        double off = 1200;
        var g = Read([.. EdgesStoppingAtTheCorners(), Column(X0 - off, Y0 - off), Column(X1 + off, Y0 - off), Column(X1 + off, Y1 + off), Column(X0 - off, Y1 + off), Column(44000, 23000)]);
        Assert.Empty(g.Slabs);
    }

    [Fact]
    public void ALoneLineRunningIntoAColumnChangesNothing()
    {
        // a closed floor with a column on its south edge and one beam line from inside the floor running into it:
        // one end at the column, so no join, and the floor is the same plate at the same area
        var g = Read(Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
                     Column(44000, Y0), Line(44000, 23000, 44000, Y0 + HalfD + Short), Column(42000, 23000), Column(46000, 23000));
        var plate = Assert.Single(g.Slabs);
        Assert.Equal((X1 - X0) * (Y1 - Y0), Area(plate), 1);
    }
}
