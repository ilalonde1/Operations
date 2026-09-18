using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// TWO RINGS THAT SHARE AN EDGE ARE TWO RINGS (intake step 119, 2026-09-18). 31065's elevator shaft is drawn as two cabs
/// side by side, each X'd, each a closed rectangle on the slab-edge layer of the view's DXF, with the divider beam's
/// line between them. The loop builder walked both as ONE eight-point loop - round the first cab, along the shared
/// edge, round the second, back along the shared edge - and the walk's signed area cancelled to nothing, so the loop
/// fell under the minimum slab area and was thrown away without a word: 22 storeys with no elevator opening, her 22
/// A3 openings we "had not". The self-touch split that already turns a figure of eight into its two rings ran only
/// AFTER the size test; a loop that touches itself is split into its rings BEFORE anything judges its size.
/// WHAT THIS COVERS: the two cabs as the sheet draws them (its own millimetres, the shared edge meeting at one end and
/// 12.7 mm apart at the other), inside a floor, cut as two openings; and the same two cabs drawn as loose lines, where
/// the dash joiner makes one edge of the shared two, the walker takes the union and the middle is an open chain - one
/// opening the size of both, which is what she models (A3, 1.8 x 5.4 m). Either way no slab stands over either cab. WHAT IT DOES NOT: a cab under the
/// minimum (still dropped, as any small ring is), the wall and column layers (their own paths), the PDF reader that
/// wrote the two rings (the DXF is the fixture here).
/// </summary>
public sealed class TwoRingsSharingAnEdgeAreTwoRingsTests
{
    /// <summary>A closed polyline's edges, as the reader lifts them: OfClosedOutline, which the dash joiner leaves alone.</summary>
    private static IEnumerable<DxfSegment> Ring(params DxfPoint[] c)
    {
        for (int i = 0; i < c.Length; i++) yield return new DxfSegment("KOR_C_SLABEDG", c[i], c[(i + 1) % c.Length]) { OfClosedOutline = true };
    }

    /// <summary>The same edges drawn loose - lines, not a closed polyline - which the dash joiner may join end to end.</summary>
    private static IEnumerable<DxfSegment> Loose(params DxfPoint[] c)
    {
        for (int i = 0; i < c.Length; i++) yield return new DxfSegment("KOR_C_SLABEDG", c[i], c[(i + 1) % c.Length]);
    }

    private static string Made(PlanGeometrySet set) =>
        $"openings: {string.Join(" | ", set.Openings.Select(o => $"{o.Area:0} {o.Points.Count} pts"))}; flags: {string.Join(" | ", set.Flags)}";

    [Fact]
    public void TheTwoCabsAsTheSheetDrawsThemAreTwoOpenings()
    {
        // 31065 S2.10.1 view 2 (LEVEL 6, 8, 10 ...): the view's whole slab layer, its five rings in the DXF's own order
        // and millimetres - the 6,980 sq ft floor, the two cabs, a sleeve, the stair. The walker's path depends on
        // what else is on the layer (three rings alone come out as the union and an open chain), so the fixture is
        // the layer as written, not a sketch of it.
        var options = new PlanClassificationOptions { PairOpenFaces = false }.InUnitOf(1.0 / 25.4);
        var set = StructuralPlanClassifier.Classify(
            Ring(new(71128.4667, 24604.1333), new(73033.4667, 24604.1333), new(74523.6, 33739.6667), new(73740.4333, 42235.9667), new(75226.3333, 42235.9667), new(75226.3333, 44750.5667), new(73511.8333, 44750.5667), new(73003.8333, 50253.9), new(65383.8333, 50253.9), new(65383.8333, 51515.4333), new(61705.0667, 51515.4333), new(61705.0667, 50253.9), new(56125.5333, 50253.9), new(56125.5333, 51540.8333), new(52442.5333, 51540.8333), new(52442.5333, 50253.9), new(50351.2667, 50253.9), new(48992.3667, 41918.4667), new(49703.5667, 34124.9), new(48399.7, 34124.9), new(48696.0333, 30729.7667), new(49940.6333, 30844.0667), new(50512.1333, 24604.1333), new(52535.6667, 24604.1333), new(52535.6667, 23338.3667), new(56112.8333, 23334.1333), new(56112.8333, 24604.1333), new(57247.3667, 24604.1333), new(57247.3667, 23334.1333), new(61214.0, 23334.1333), new(61214.0, 24604.1333), new(67564.0, 24604.1333), new(67564.0, 23334.1333), new(71128.4667, 23334.1333))
                .Concat(Ring(new(60744.1, 35555.7667), new(62568.6667, 35555.7667), new(62568.6667, 38265.1), new(60744.1, 38273.5667)))
                .Concat(Ring(new(60744.1, 38273.5667), new(62568.6667, 38277.8), new(62568.6667, 40987.1333), new(60744.1, 40987.1333)))
                .Concat(Ring(new(61840.5333, 30632.4), new(62420.5, 30632.4), new(62382.4, 30945.6667), new(61810.9, 30945.6667)))
                .Concat(Ring(new(58060.1667, 33934.4), new(60443.5333, 33934.4), new(60443.5333, 40991.3667), new(58403.0667, 40991.3667), new(58403.0667, 40737.3667), new(58060.1667, 40737.3667))),
            options);

        Assert.Single(set.Slabs);
        // the stair (17 m²), the sleeve and the two cabs (4.9 m² each) - four openings, or three with the cabs as one
        var cabs = set.Openings.Where(o => o.Points.Min(p => p.X) > 60000 && o.Points.Max(p => p.X) < 63000 && o.Points.Min(p => p.Y) > 35000 && o.Points.Max(p => p.Y) < 41500).ToList();
        Assert.True(cabs.Count == 2, Made(set));
        Assert.All(cabs, o => Assert.InRange(o.Area / 1_000_000, 4.8, 5.1));   // 1.8 x 2.7 m each
        Assert.Contains(set.Flags, f => f.Contains("crossed itself", StringComparison.Ordinal) && f.Contains("2 separate ring(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTwoCabsDrawnAsLooseLinesAreOneOpeningTheSizeOfBoth()
    {
        // the same two cabs drawn as loose lines rather than closed polylines: the dash joiner makes one edge of the two
        // shared ones, the walker takes the union and leaves the middle as an open chain - one opening of both cabs,
        // no slab over either (and what she models: A3, 1.8 x 5.4 m)
        var set = StructuralPlanClassifier.Classify(
            Loose(new(0, 0), new(1200, 0), new(1200, 900), new(0, 900))
                .Concat(Loose(new(500, 300), new(572, 300), new(572, 407.5), new(500, 408)))
                .Concat(Loose(new(500, 408), new(572, 408.5), new(572, 516), new(500, 516))));

        Assert.Single(set.Slabs);
        Assert.InRange(set.Openings.Sum(o => o.Area), 72 * 216 - 80, 72 * 216 + 80);
        foreach (var centre in new[] { new DxfPoint(536, 354), new DxfPoint(536, 462) })
            Assert.True(set.Openings.Any(o => LoopGeometry.PointInPolygon(centre, o.Points)), Made(set));
    }
}
