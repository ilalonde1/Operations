#nullable enable

using System;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlanGeometryTests
{
    // ── enclosed area (flood-fill) ──────────────────────────────────────────────────────────

    [Fact]
    public void EnclosedArea_ClosedRectangle_CountsExactInterior()
    {
        const int w = 200, h = 200;
        var lum = White(w, h);
        // closed rectangle outline: x in {50,150}, y in {40,160}
        DrawRectOutline(lum, w, 50, 40, 150, 160);

        var area = PlanGeometry.MeasureEnclosedArea(lum, w, h, darkThreshold: 110, sealHairlineGaps: false);

        // interior light = 51..149 (99) × 41..159 (119); the outline band itself is enclosed-dark
        // (it straddles the true edge), which is what keeps the light estimator conservative.
        const int outlinePerimeter = 2 * 101 + 2 * 121 - 4; // x:50..150 (101) × y:40..160 (121)
        Assert.Equal(99 * 119, area.InteriorLightPx);
        Assert.Equal(outlinePerimeter, area.InteriorDarkPx);
    }

    [Fact]
    public void EnclosedArea_HatchedCoreVoid_ExcludedFromLightArea()
    {
        // A dark (hatched/filled) core inside the plate is counted as interior-DARK, so the
        // conservative light area excludes it — exactly how the Coronation elevator core fell out.
        const int w = 200, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160);
        FillRect(lum, w, 90, 90, 110, 110, value: 0); // 21×21 solid-dark core

        var area = PlanGeometry.MeasureEnclosedArea(lum, w, h, darkThreshold: 110, sealHairlineGaps: false);

        const int outlinePerimeter = 2 * 101 + 2 * 121 - 4;
        Assert.Equal(99 * 119 - 21 * 21, area.InteriorLightPx);     // core void excluded from light
        Assert.Equal(outlinePerimeter + 21 * 21, area.InteriorDarkPx); // outline band + the dark core
    }

    [Fact]
    public void EnclosedArea_GridLineCrossingMargin_DoesNotLeakIntoInterior()
    {
        // A dark "grid line" splits the left margin into a compartment that no corner seed can
        // reach. Whole-border seeding must still fill it; otherwise it would be miscounted as
        // interior. Correct interior count (unchanged) proves the fix.
        const int w = 200, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160);
        DrawHLine(lum, w, x0: 0, x1: 50, y: 100); // border → plate, partitions the left margin

        var area = PlanGeometry.MeasureEnclosedArea(lum, w, h, darkThreshold: 110, sealHairlineGaps: false);

        Assert.Equal(99 * 119, area.InteriorLightPx);
    }

    [Fact]
    public void EnclosedArea_SealHairlineGaps_PreventsLeakThroughOnePixelGap()
    {
        const int w = 200, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160);
        // punch a 1px hole in the top edge -> without sealing, the fill leaks in
        lum[40 * w + 100] = 255;

        var leaked = PlanGeometry.MeasureEnclosedArea(lum, w, h, sealHairlineGaps: false);
        var sealed_ = PlanGeometry.MeasureEnclosedArea(lum, w, h, sealHairlineGaps: true);

        Assert.True(leaked.InteriorLightPx < 1000, "expected a leak through the 1px gap");
        // sealed should recover ~the full interior (minus the 1px dilation rind)
        Assert.True(sealed_.InteriorLightPx > 11000, $"sealing should recover interior, got {sealed_.InteriorLightPx}");
    }

    // ── enclosed regions (plate segmentation) ───────────────────────────────────────────────

    [Fact]
    public void EnclosedRegions_TwoPlates_ReturnedLargestFirst()
    {
        const int w = 300, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 120, 160);   // A: 69×119 = 8211
        DrawRectOutline(lum, w, 160, 60, 280, 140);  // B: 119×79 = 9401

        var regions = PlanGeometry.MeasureEnclosedRegions(lum, w, h, sealHairlineGaps: false);

        Assert.Equal(2, regions.Count);
        Assert.Equal(9401, regions[0].LightPx);   // larger first
        Assert.Equal(8211, regions[1].LightPx);
        Assert.InRange(regions[0].MinX, 160, 162); // bbox tracks plate B
    }

    [Fact]
    public void EnclosedRegions_MinPixels_DropsSmallPlate()
    {
        const int w = 300, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 120, 160);
        DrawRectOutline(lum, w, 160, 60, 280, 140);

        var regions = PlanGeometry.MeasureEnclosedRegions(lum, w, h, sealHairlineGaps: false, minPixels: 9000);

        Assert.Single(regions);
        Assert.Equal(9401, regions[0].LightPx);
    }

    [Fact]
    public void EnclosedRegions_CoreVoid_DoesNotSplitItsPlate()
    {
        const int w = 200, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160);
        FillRect(lum, w, 90, 90, 110, 110, value: 0); // hatched core hole

        var regions = PlanGeometry.MeasureEnclosedRegions(lum, w, h, sealHairlineGaps: false);

        Assert.Single(regions);                         // one plate, not two
        Assert.Equal(99 * 119 - 21 * 21, regions[0].LightPx);
    }

    // ── enclosed clusters (plate reconstruction from grid bays) ─────────────────────────────

    [Fact]
    public void EnclosedClusters_GridSplitPlate_MergesBaysAndOutClustersIntactNeighbour()
    {
        // Plate A is split by an interior grid line into two 49×119 bays (5831 px each). Plate B is a
        // smaller, intact 79×79 plate (6241 px) separated by a wide gap. The NAIVE largest-region
        // would pick B (6241 > a single 5831 bay) — the undercount bug. Clustering must re-unite A's
        // bays (11662) so the LARGEST cluster is A, and B stays its own separate cluster.
        const int w = 360, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160);   // plate A
        DrawVLine(lum, w, x: 100, y0: 40, y1: 160);  // grid line splits A into two bays
        DrawRectOutline(lum, w, 220, 60, 300, 140);  // plate B, intact, far to the right

        var regions = PlanGeometry.MeasureEnclosedRegions(lum, w, h, sealHairlineGaps: false);
        var clusters = PlanGeometry.MeasureEnclosedClusters(lum, w, h, sealHairlineGaps: false);

        // naive segmentation: 3 regions (2 bays of A + B), and its largest is B — the wrong plate
        Assert.Equal(3, regions.Count);
        Assert.Equal(79 * 79, regions[0].LightPx);

        // clustered: A's two bays re-unite into the largest cluster; B is a second, separate cluster
        Assert.Equal(2, clusters.Count);
        Assert.Equal(2 * (49 * 119), clusters[0].LightPx); // 11662 = both bays of A
        Assert.Equal(2, clusters[0].RegionCount);
        Assert.Equal(79 * 79, clusters[1].LightPx);        // B, on its own
        Assert.True(clusters[0].LightPx > clusters[1].LightPx, "the split plate must out-cluster the intact neighbour");
    }

    [Fact]
    public void EnclosedClusters_WideGapBetweenPlates_KeepsThemSeparate()
    {
        // Two equal plates with a wide exterior gap must NOT merge — this is what stops a loose
        // vision box that clips a neighbouring plate from double-counting it into the target.
        const int w = 360, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 30, 40, 110, 160);    // 79×119
        DrawRectOutline(lum, w, 240, 40, 320, 160);   // 79×119, far apart

        var clusters = PlanGeometry.MeasureEnclosedClusters(lum, w, h, sealHairlineGaps: false);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(79 * 119, clusters[0].LightPx);  // largest is ONE plate, never the sum
        Assert.Equal(79 * 119, clusters[1].LightPx);
    }

    // ── hatched-footing detection ───────────────────────────────────────────────────────────

    [Fact]
    public void HatchedRegions_FindsDiagonalHatch_IgnoresOrthogonalGridLines()
    {
        // A diagonally-hatched square (deep footing) plus long orthogonal grid lines. The orientation
        // filter must keep the short-run diagonal hatch and drop the long-run grid lines, so the
        // detected region sits on the hatched square, not the lines that cross the whole sheet.
        const int w = 400, h = 300;
        var lum = White(w, h);
        for (int y = 40; y < 200; y++)
            for (int x = 40; x < 200; x++)
                if ((x + y) % 8 < 2) lum[y * w + x] = 0;       // 25%-dense diagonal hatch
        for (int x = 0; x < w; x++) lum[150 * w + x] = 0;       // long horizontal grid line
        for (int y = 0; y < h; y++) lum[y * w + 300] = 0;       // long vertical grid line

        var regions = PlanGeometry.MeasureHatchedRegions(
            lum, w, h, windowRadius: 8, densityPercent: 8, minPixels: 500, maxRun: 14);

        Assert.NotEmpty(regions);
        Assert.InRange(regions[0].CentroidX, 40, 200);          // on the hatched square
        Assert.InRange(regions[0].CentroidY, 40, 200);
    }

    // ── gray footprint ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GrayFootprint_CountsNeutralGray_RejectsBlackWhiteAndColour()
    {
        const int w = 100, h = 100;
        var (r, g, b) = WhiteRgb(w, h);
        FillRectRgb(r, g, b, w, 30, 30, 60, 60, 210, 210, 210); // 31×31 neutral gray  → counted
        FillRectRgb(r, g, b, w, 70, 10, 80, 20, 0, 0, 0);       // black               → rejected (too dark)
        FillRectRgb(r, g, b, w, 70, 70, 80, 80, 210, 210, 150); // colour-tinted "gray" → rejected (not neutral)

        long gray = PlanGeometry.MeasureGrayFootprint(r, g, b, w, h);

        Assert.Equal(31 * 31, gray);
    }

    [Fact]
    public void GrayComponents_SeparatesBlobs_AndClassifiesWallVsColumn()
    {
        const int w = 400, h = 200;
        var (r, g, b) = WhiteRgb(w, h);
        FillRectRgb(r, g, b, w, 20, 20, 49, 49, 210, 210, 210);   // 30×30 compact  → column
        FillRectRgb(r, g, b, w, 80, 20, 99, 109, 210, 210, 210);  // 20×90 elongated → wall (elong 4.5)
        FillRectRgb(r, g, b, w, 150, 20, 179, 51, 210, 210, 210); // 30×32 compact  → column

        var comps = PlanGeometry.MeasureGrayComponents(r, g, b, w, h, minPixels: 30);

        Assert.Equal(3, comps.Count);
        // largest by area is the 20×90 wall (1800), then the two ~30×30 columns
        Assert.Equal(20 * 90, comps[0].AreaPx);
        long maxColPx = 25 * 25 * 100; // generous column cap so size never forces the call here
        Assert.Equal(PlanGeometry.VerticalKind.Wall, PlanGeometry.ClassifyVertical(comps[0], maxColPx));
        Assert.Equal(PlanGeometry.VerticalKind.Column, PlanGeometry.ClassifyVertical(comps[1], maxColPx));
        Assert.Equal(PlanGeometry.VerticalKind.Column, PlanGeometry.ClassifyVertical(comps[2], maxColPx));
    }

    [Fact]
    public void ClassifyVertical_StockyButOversizePier_IsWallBySize()
    {
        // A compact (elong ~1) blob that is simply too big to be a column is a wall by the size rule.
        var pier = new PlanGeometry.GrayComponent(AreaPx: 10_000, MinX: 0, MinY: 0, MaxX: 99, MaxY: 99);
        Assert.Equal(PlanGeometry.VerticalKind.Wall, PlanGeometry.ClassifyVertical(pier, maxColumnAreaPx: 5_000));
    }

    // ── scale resolution ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1/8\"=1'-0\"", 110, 0.022167272727)]
    [InlineData("3/16\" = 1'-0\"", 100, 0.016256)]
    [InlineData("1/4\"=1'-0\"", 100, 0.012192)]
    [InlineData("1\"=20'-0\"", 100, 0.06096)]   // whole-inch engineering/site scale
    [InlineData("1:100", 100, 0.0254)]
    [InlineData("1:50", 100, 0.0127)]
    public void MetresPerPixel_ParsesCommonScaleNotes(string note, double dpi, double expected)
    {
        double? mpp = PlanGeometry.MetresPerPixel(note, dpi);
        Assert.NotNull(mpp);
        Assert.Equal(expected, mpp!.Value, 6);
    }

    [Theory]
    [InlineData("AS NOTED")]
    [InlineData("")]
    [InlineData(null)]
    public void MetresPerPixel_ReturnsNullForUnparseableNotes(string? note)
    {
        Assert.Null(PlanGeometry.MetresPerPixel(note, 100));
    }

    [Fact]
    public void SquareMetresAndFeet_ConvertConsistently()
    {
        // 1/8"=1'-0" at 110 dpi: 9,597 sq.ft typical plate ≈ 1,814,499 interior px (Coronation)
        double mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110)!.Value;
        double sqft = PlanGeometry.SquareFeet(1_814_499, mpp);
        Assert.Equal(9597, sqft, 0); // matches the validated field measurement
    }

    // ── thickness zoning (Voronoi by callout) ───────────────────────────────────────────────

    [Fact]
    public void ThicknessZoneFractions_SplitsEnclosedAreaByNearestCallout()
    {
        const int w = 200, h = 200;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 50, 40, 150, 160); // enclosed interior 51..149 × 41..159

        // Two callouts: an 8" on the left third, a 12" on the right third of the plate.
        var callouts = new[]
        {
            new PlanGeometry.CalloutPx(75, 100, 8),
            new PlanGeometry.CalloutPx(125, 100, 12),
        };
        var zones = PlanGeometry.ThicknessZoneFractions(lum, w, h, callouts,
            minX: 50, minY: 40, maxX: 150, maxY: 160, darkThreshold: 110, sealHairlineGaps: false);

        long total = 0; foreach (var v in zones.Values) total += v;
        Assert.Equal(99 * 119, total);                 // every interior-light pixel is assigned
        Assert.True(zones.ContainsKey(8) && zones.ContainsKey(12));
        // The dividing line sits midway (x≈100), so the split is near 50/50.
        Assert.InRange((double)zones[8] / total, 0.45, 0.55);
    }

    [Fact]
    public void ThicknessZoneFractions_NoCallouts_ReturnsEmpty()
    {
        const int w = 60, h = 60;
        var lum = White(w, h);
        DrawRectOutline(lum, w, 10, 10, 50, 50);
        Assert.Empty(PlanGeometry.ThicknessZoneFractions(lum, w, h,
            Array.Empty<PlanGeometry.CalloutPx>(), 10, 10, 50, 50));
    }

    // ── synthetic-image helpers ─────────────────────────────────────────────────────────────

    private static byte[] White(int w, int h)
    {
        var lum = new byte[w * h];
        Array.Fill(lum, (byte)255);
        return lum;
    }

    private static (byte[] r, byte[] g, byte[] b) WhiteRgb(int w, int h)
        => (White(w, h), White(w, h), White(w, h));

    private static void DrawRectOutline(byte[] lum, int w, int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++) { lum[y0 * w + x] = 0; lum[y1 * w + x] = 0; }
        for (int y = y0; y <= y1; y++) { lum[y * w + x0] = 0; lum[y * w + x1] = 0; }
    }

    private static void DrawHLine(byte[] lum, int w, int x0, int x1, int y)
    {
        for (int x = x0; x <= x1; x++) lum[y * w + x] = 0;
    }

    private static void DrawVLine(byte[] lum, int w, int x, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++) lum[y * w + x] = 0;
    }

    private static void FillRect(byte[] lum, int w, int x0, int y0, int x1, int y1, byte value)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                lum[y * w + x] = value;
    }

    private static void FillRectRgb(
        byte[] r, byte[] g, byte[] b, int w,
        int x0, int y0, int x1, int y1, byte rv, byte gv, byte bv)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * w + x;
                r[i] = rv; g[i] = gv; b[i] = bv;
            }
    }
}
