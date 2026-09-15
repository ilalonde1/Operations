#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A WALL THE DRAFTER DID NOT FILL IS A WALL ONLY AT A RETAINING WALL'S THICKNESS (step 67, 2026-09-14).
/// Thickness cannot tell a 2x6 stud wall (139.7 mm) from a six-inch wall drawn thin (31065: 142-150 mm),
/// but the drawing can: wood plans read ~80% of their walls from unfilled line pairs (31066 L2: 117 of
/// 145), concrete plans 0-35%. A sheet with two thirds and at least twenty of its walls as unfilled
/// pairs is a wood plan; on it an unfilled pair under 8 in and a filled band under the six-inch floor
/// with NO slack are stud walls - partitions on the layer the model does not read.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the sheet-kind decision by share and count; both kinds of stud wall on a wood plan;
/// the retaining wall (an unfilled pair at 12 in) and the filled six-inch wall left alone on it; a
/// concrete plan (few unfilled pairs) untouched even with a 140 mm filled band. WHAT IT DOES NOT: the
/// six sets (the gate: five byte-identical, 31170-arch loses two slab-outline line pairs read as
/// walls); a wood plan whose studs are drawn filled AND whose concrete outnumbers them.
/// </remarks>
public sealed class AWoodPlansStudWallsArePartitionsTests
{
    private static ExtractedGeometry Plan(int unfilledPairs, double unfilledMm, int filled, double filledMm)
    {
        var g = new ExtractedGeometry();
        int line = 0;
        for (int i = 0; i < unfilledPairs; i++)
        {
            g.Walls.Add(new WallPanel([(0, i * 1000.0), (5000, i * 1000.0), (5000, i * 1000.0 + unfilledMm), (0, i * 1000.0 + unfilledMm)], (0, i * 1000.0 + unfilledMm / 2), (5000, i * 1000.0 + unfilledMm / 2), unfilledMm));
            g.WallFaceLines[line++] = g.Walls.Count - 1;
            g.WallFaceLines[line++] = g.Walls.Count - 1;
        }
        for (int i = 0; i < filled; i++)
            g.Walls.Add(new WallPanel([(0, 50000 + i * 1000.0), (5000, 50000 + i * 1000.0), (5000, 50000 + i * 1000.0 + filledMm), (0, 50000 + i * 1000.0 + filledMm)], (0, 50000 + i * 1000.0 + filledMm / 2), (5000, 50000 + i * 1000.0 + filledMm / 2), filledMm));
        for (int i = 0; i < g.Walls.Count; i++) { g.WallTypeCodes.Add(null); g.WallIsPartition.Add(false); }
        return g;
    }

    [Fact]
    public void OnAWoodPlanTheStudWallsArePartitionsAndTheConcreteStays()
    {
        // 30 unfilled pairs at a 2x6 (139.7 mm), 6 filled bands at 140 (poche'd studs), 2 unfilled at 12 in
        // (retaining) and 2 filled at 152.4 (a six-inch wall): 30 + 2 unfilled of 40 = a wood plan
        var g = Plan(30, 139.7, 6, 140);
        g.Walls.Add(new WallPanel([(0, 90000), (5000, 90000), (5000, 90304.8), (0, 90304.8)], (0, 90152.4), (5000, 90152.4), 304.8)); g.WallFaceLines[100] = g.Walls.Count - 1; g.WallFaceLines[101] = g.Walls.Count - 1;
        g.Walls.Add(new WallPanel([(0, 91000), (5000, 91000), (5000, 91304.8), (0, 91304.8)], (0, 91152.4), (5000, 91152.4), 304.8)); g.WallFaceLines[102] = g.Walls.Count - 1; g.WallFaceLines[103] = g.Walls.Count - 1;
        g.Walls.Add(new WallPanel([(0, 92000), (5000, 92000), (5000, 92152.4), (0, 92152.4)], (0, 92076.2), (5000, 92076.2), 152.4));
        g.Walls.Add(new WallPanel([(0, 93000), (5000, 93000), (5000, 93152.4), (0, 93152.4)], (0, 93076.2), (5000, 93076.2), 152.4));
        while (g.WallIsPartition.Count < g.Walls.Count) { g.WallTypeCodes.Add(null); g.WallIsPartition.Add(false); }

        WallTypeTagging.StudWallsOfAWoodPlan(g, 152.4);

        Assert.Equal(36, g.WallIsPartition.Count(p => p));                       // 30 unfilled studs + 6 poche'd studs
        Assert.All(Enumerable.Range(0, 36), i => Assert.True(g.WallIsPartition[i]));
        Assert.All(Enumerable.Range(36, 4), i => Assert.False(g.WallIsPartition[i]));  // the retaining walls and the six-inch walls stay
    }

    [Fact]
    public void AConcretePlanIsLeftAloneWhateverItsBandsMeasure()
    {
        // 15 unfilled pairs of 43 walls (35%, the most any concrete plan measured) and a 140 mm filled band
        var g = Plan(15, 304.8, 28, 140);
        WallTypeTagging.StudWallsOfAWoodPlan(g, 152.4);
        Assert.DoesNotContain(true, g.WallIsPartition);
        // and a majority by share alone is not enough without the count: 4 unfilled pairs of 5 walls
        var small = Plan(4, 139.7, 1, 140);
        WallTypeTagging.StudWallsOfAWoodPlan(small, 152.4);
        Assert.DoesNotContain(true, small.WallIsPartition);
    }
}
