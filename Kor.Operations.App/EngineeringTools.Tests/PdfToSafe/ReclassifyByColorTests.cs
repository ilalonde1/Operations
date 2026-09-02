using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Tests for PdfGeometryExtractor.ReclassifyByColor. Because ExtractedGeometry
/// and SlabColorSettings are internal, the tests drive the API via reflection
/// on the internal types, mirroring the existing AiTools test pattern.
/// </summary>
public class ReclassifyByColorTests
{
    private static readonly System.Type _tExtracted = PdfToSafeTestTypes.Resolve("Kor.Operations.EngineeringTools.PdfToSafe.ExtractedGeometry");
    private static readonly System.Type _tSettings  = PdfToSafeTestTypes.Resolve("Kor.Operations.EngineeringTools.PdfToSafe.SlabColorSettings");
    private static readonly System.Type _tExtractor = PdfToSafeTestTypes.Resolve("Kor.Operations.EngineeringTools.PdfToSafe.PdfGeometryExtractor");

    private static object NewExtracted() => System.Activator.CreateInstance(_tExtracted, nonPublic: true)!;
    private static object NewSettings(string type)
    {
        var s = System.Activator.CreateInstance(_tSettings, nonPublic: true)!;
        _tSettings.GetProperty("ElementType")!.SetValue(s, type);
        return s;
    }

    private static System.Collections.IDictionary NewColorSettingsDict()
    {
        // Dictionary<(byte, byte, byte), SlabColorSettings>
        var keyT = typeof(System.ValueTuple<byte, byte, byte>);
        var dictT = typeof(Dictionary<,>).MakeGenericType(keyT, _tSettings);
        return (System.Collections.IDictionary)System.Activator.CreateInstance(dictT)!;
    }

    private static object ReclassifyByColor(object original, System.Collections.IDictionary? colorSettings)
    {
        var mi = _tExtractor.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "ReclassifyByColor");
        var paras = mi.GetParameters();
        var args = new object?[paras.Length];
        args[0] = original;
        args[1] = colorSettings;
        // Remaining params all have defaults — pass each parameter's
        // declared default value explicitly. Keeps the test invariant to
        // new optional params being added later.
        for (int i = 2; i < paras.Length; i++) args[i] = paras[i].DefaultValue;
        return mi.Invoke(null, args)!;
    }

    private static void AddSlab(object geo, List<(double, double)> pts, (byte, byte, byte) color)
    {
        var slabs = _tExtracted.GetProperty("Slabs")!.GetValue(geo) as System.Collections.IList;
        var colors = _tExtracted.GetProperty("SlabColors")!.GetValue(geo) as System.Collections.IList;
        slabs!.Add(pts);
        colors!.Add(color);
    }

    private static int Count(object geo, string listName)
    {
        var list = _tExtracted.GetProperty(listName)!.GetValue(geo) as System.Collections.IList;
        return list!.Count;
    }

    [Fact]
    public void SlabAsWall_ReducesPolygonToCenterlineWithSectionHint()
    {
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (200, 0), (200, 5000), (0, 5000) // 200 x 5000mm wall footprint
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(0, Count(result, "Slabs"));
        Assert.Equal(1, Count(result, "Lines"));
        // Centerline is a 2-point segment.
        var lines = _tExtracted.GetProperty("Lines")!.GetValue(result) as System.Collections.IList;
        var centerline = (List<(double X, double Y)>)lines![0]!;
        Assert.Equal(2, centerline.Count);
        // Parallel section hint should be populated (200mm width, default 1000mm depth).
        var hints = _tExtracted.GetProperty("LineSectionHints")!.GetValue(result) as System.Collections.IList;
        Assert.NotNull(hints![0]);
        var hint = ((double WidthMm, double DepthMm))hints[0]!;
        Assert.Equal(200, hint.WidthMm, 3);
        Assert.Equal(1000, hint.DepthMm, 3);
    }

    [Fact]
    public void SlabAsColumn_ElongatedPolygon_RedirectsToWallPath()
    {
        // 200 x 5000mm is too elongated to be a column — guardrail should
        // route it to Wall path rather than produce a C200x5000 point section.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (200, 0), (200, 5000), (0, 5000)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Column");

        var result = ReclassifyByColor(geo, settings);

        // Guardrail diverts: no Columns, one Line with section hint.
        Assert.Equal(0, Count(result, "Columns"));
        Assert.Equal(1, Count(result, "Lines"));
        var hints = _tExtracted.GetProperty("LineSectionHints")!.GetValue(result) as System.Collections.IList;
        Assert.NotNull(hints![0]);
    }

    [Fact]
    public void SlabAsColumn_SmallSquare_StaysAsColumn()
    {
        // 500 x 500mm square — passes guardrail, becomes a column.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (500, 0), (500, 500), (0, 500)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Column");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Columns"));
        Assert.Equal(0, Count(result, "Lines"));
    }

    private static void AddColumn(object geo, (double X, double Y) centroid, (byte, byte, byte) color, (double, double) size)
    {
        var cols    = _tExtracted.GetProperty("Columns")!.GetValue(geo) as System.Collections.IList;
        var colors  = _tExtracted.GetProperty("ColumnColors")!.GetValue(geo) as System.Collections.IList;
        var sizes   = _tExtracted.GetProperty("ColumnSizes")!.GetValue(geo) as System.Collections.IList;
        cols!.Add(centroid);
        colors!.Add(color);
        sizes!.Add(size);
    }

    [Fact]
    public void SharedColor_AcrossSlabAndColumnBuckets_DoesNotShortCircuitReclassification()
    {
        // Regression for Batch 45: the fast-path optimisation used to return
        // `original` unchanged when every per-colour ElementType matched the
        // colour's natural bucket. The check ignored that a single colour can
        // appear in multiple buckets simultaneously — at KOR the burgundy
        // markup is in ColumnColors (small columns) AND SlabColors (large core
        // / shear-wall polygons). With type=Column the fast path saw burgundy
        // in ColumnColors, marked the whole reclassification as no-op, and
        // the wall-sized burgundy slab polygons silently stayed in the slab
        // bucket — vanishing from the export after a downstream slab-merge.
        var geo = NewExtracted();
        var burgundy = ((byte)128, (byte)0, (byte)0);

        // 500 x 500mm burgundy column.
        AddColumn(geo, (10000, 10000), burgundy, (500, 500));

        // 200 x 9000mm burgundy slab-bucket polygon — must route to wall.
        AddSlab(geo, new List<(double, double)>
        {
            (0, 0), (200, 0), (200, 9000), (0, 9000)
        }, burgundy);

        var settings = NewColorSettingsDict();
        settings[burgundy] = NewSettings("Column");

        var result = ReclassifyByColor(geo, settings);

        // Column stays as a column.
        Assert.Equal(1, Count(result, "Columns"));

        // The wall-sized slab polygon must have been routed out of the slab
        // bucket via the wall-reduction guardrail — NOT left behind by a
        // fast-path that incorrectly assumed "burgundy in ColumnColors → done".
        Assert.Equal(0, Count(result, "Slabs"));
        Assert.Equal(1, Count(result, "Lines"));

        // The resulting wall line has a section hint (centerline + W×D).
        var hints = _tExtracted.GetProperty("LineSectionHints")!.GetValue(result) as System.Collections.IList;
        Assert.NotNull(hints![0]);
    }

    [Fact]
    public void SlabAsWall_ShaftOutlinePolygon_DecomposesIntoFourWallCenterlines()
    {
        // A 2.8m × 9.7m closed rectangle is a stair-core / elevator-shaft outline,
        // not a 2.8m-thick wall (no such thing in real construction). The reducer
        // must decompose this into 4 centerlines on the 4 sides so the opening
        // detector can find the rectangle and cut the slab.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (2832, 0), (2832, 9732), (0, 9732)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        // 4 centerlines, one per side.
        Assert.Equal(4, Count(result, "Lines"));

        // Each gets the default shaft-wall hint (300mm × 1000mm).
        var hints = _tExtracted.GetProperty("LineSectionHints")!.GetValue(result) as System.Collections.IList;
        for (int i = 0; i < hints!.Count; i++)
        {
            Assert.NotNull(hints[i]);
            var hint = ((double WidthMm, double DepthMm))hints[i]!;
            Assert.Equal(300, hint.WidthMm, 3);
            Assert.Equal(1000, hint.DepthMm, 3);
        }
    }

    [Fact]
    public void SlabAsWall_ThinElongatedWall_StillReducesToSingleCenterline()
    {
        // Regression: a real 200mm × 5000mm wall (aspect 0.04, minor dim 200mm)
        // must NOT be misdetected as a shaft. Aspect threshold is 0.25,
        // minor-dim threshold is 1000mm — this polygon fails both.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (200, 0), (200, 5000), (0, 5000)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        // Original Batch 45 behaviour preserved: single centerline.
        Assert.Equal(1, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_ChunkyButElongated_DoesNotDecompose()
    {
        // A 1m × 8m thick shear wall: minor dim 1000 >= threshold, BUT aspect
        // 1000/8000 = 0.125 < 0.15. Stay as single centerline (treat as wall).
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (1000, 0), (1000, 8000), (0, 8000)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_BorderlineShaft_W1850Case_Decomposes()
    {
        // Calibration case from the reference KOR drawing: a 1.85m × 9.74m
        // closed polygon (aspect 0.19) is too square to be a 1.85m-thick wall
        // and too thick to be a real wall. Threshold 0.15 catches this as a
        // shaft outline while the 0.95m × 9.73m thick-shear-wall case below
        // (aspect 0.098) stays a wall.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (1850, 0), (1850, 9737), (0, 9737)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(4, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_ThickShearWall_W952Case_StaysAsWall()
    {
        // Calibration case from the reference KOR drawing: a 0.95m × 9.73m
        // closed polygon (aspect 0.098) IS a real (thick) shear wall, not a
        // shaft. Must stay as a single centerline so the engineer's intent
        // is preserved.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (952, 0), (952, 9732), (0, 9732)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_FrameOutlinePolygon_DecomposesIntoFourWallCenterlines()
    {
        // Engineer drew a 1.85 m × 9.74 m shaft as a FRAME outline — outer
        // perimeter (4 vertices) + inner perimeter (4 vertices) connected
        // into one closed polygon. PolygonAreaMm2 gives wall material area
        // (~6.6 m² for 300 mm walls) vs bbox 18 m² → fill ratio ~0.37.
        // Below the 0.85 filled-rectangle threshold; new branch B catches
        // it via vertex count ≥ 6.
        var geo = NewExtracted();
        // Outer rect 1850×9737 CCW + inner rect 1250×9137 CW (300mm walls).
        // Self-intersecting at the bridge, but shoelace area gives outer-inner.
        var pts = new List<(double, double)>
        {
            (0, 0), (1850, 0), (1850, 9737), (0, 9737), (0, 0),
            (300, 300), (300, 9437), (1550, 9437), (1550, 300), (300, 300)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        // 4 centerlines emitted, one per bbox side.
        Assert.Equal(4, Count(result, "Lines"));
        var hints = _tExtracted.GetProperty("LineSectionHints")!.GetValue(result) as System.Collections.IList;
        for (int i = 0; i < hints!.Count; i++)
        {
            Assert.NotNull(hints[i]);
            var h = ((double WidthMm, double DepthMm))hints[i]!;
            Assert.Equal(300, h.WidthMm, 3);
        }
    }

    [Fact]
    public void SlabAsWall_CShapePolygon_DecomposesIntoFourWallCenterlines()
    {
        // 6-vertex C-shape (3-walled stair core, open on the right):
        // outer rect minus a notch on the right. Fill ratio ~0.49.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (1850, 0), (1850, 300),
            (300, 300), (300, 9437), (1850, 9437), (1850, 9737), (0, 9737)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        // 4 walls — one is a phantom on the open side; engineer deletes if
        // they care. Trade is acceptable vs the current "1 wall, 1.85 m
        // thick, no opening" output that's far harder to repair.
        Assert.Equal(4, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_LShapeBuildingCorner_StaysAsSingleCenterline()
    {
        // L-shape with 6 vertices and 300 mm arms — what you'd get for an
        // actual building corner, not a shaft. Fill 0.116 < 0.15 → both
        // shaft branches fail and the polygon falls back to a single
        // centerline via ReducePolygonToWallCenterline.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (5000, 0), (5000, 300),
            (300, 300), (300, 5000), (0, 5000)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Lines"));
    }

    [Fact]
    public void SlabAsWall_ThickLShapeBuildingCorner_StaysAsSingleCenterline()
    {
        // Regression for L-shape false positive: 400 mm arms give fill
        // 0.154 — just barely above the 0.15 threshold. Without the
        // side-coverage check this would be decomposed into 4 walls (3
        // phantom), polluting the SAFE model with imaginary walls at a
        // legitimate building corner. The polygon's edges only fully
        // cover 2 of 4 bbox sides (bottom + left) — should reject.
        var geo = NewExtracted();
        var pts = new List<(double, double)>
        {
            (0, 0), (5000, 0), (5000, 400),
            (400, 400), (400, 5000), (0, 5000)
        };
        var color = ((byte)128, (byte)0, (byte)0);
        AddSlab(geo, pts, color);

        var settings = NewColorSettingsDict();
        settings[color] = NewSettings("Wall");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Lines"));
    }

    [Fact]
    public void OrphanSlab_BelowAreaThreshold_IsDroppedSilently()
    {
        // 3-point triangle, ~5000 mm² (0.005 m²) — pen-thickness artifact.
        // Must be dropped, not emitted as a phantom floor object.
        var geo = NewExtracted();
        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var lc     = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        var lh     = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (100, 0), (50, 100) });
        lc.Add(((byte)200, (byte)0, (byte)0));
        lh.Add((System.ValueTuple<double, double>?)null);

        var settings = NewColorSettingsDict();
        settings[((byte)200, (byte)0, (byte)0)] = NewSettings("Slab");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(0, Count(result, "Slabs"));
        Assert.Equal(0, Count(result, "Lines"));
    }

    [Fact]
    public void OrphanSlab_AboveAreaThreshold_IsKept()
    {
        // 3-point triangle, 3 m² — a real balcony stub. Must survive.
        var geo = NewExtracted();
        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var lc     = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        var lh     = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (4000, 0), (2000, 1500) });
        lc.Add(((byte)200, (byte)0, (byte)0));
        lh.Add((System.ValueTuple<double, double>?)null);

        var settings = NewColorSettingsDict();
        settings[((byte)200, (byte)0, (byte)0)] = NewSettings("Slab");

        var result = ReclassifyByColor(geo, settings);

        Assert.Equal(1, Count(result, "Slabs"));
    }

    [Fact]
    public void LinesAndHints_StayParallelAcrossReclassification()
    {
        // Invariant: Lines.Count == LineSectionHints.Count after every
        // reclassification. One of each type exercised.
        var geo = NewExtracted();
        AddSlab(geo, new List<(double, double)> { (0,0),(200,0),(200,5000),(0,5000) }, ((byte)1,(byte)1,(byte)1)); // Wall
        AddSlab(geo, new List<(double, double)> { (0,0),(500,0),(500,500),(0,500) },   ((byte)2,(byte)2,(byte)2)); // Column
        AddSlab(geo, new List<(double, double)> { (0,0),(3000,0),(3000,400),(0,400) }, ((byte)3,(byte)3,(byte)3)); // Beam

        var settings = NewColorSettingsDict();
        settings[((byte)1,(byte)1,(byte)1)] = NewSettings("Wall");
        settings[((byte)2,(byte)2,(byte)2)] = NewSettings("Column");
        settings[((byte)3,(byte)3,(byte)3)] = NewSettings("Beam");

        var result = ReclassifyByColor(geo, settings);

        int lines = Count(result, "Lines");
        int hints = Count(result, "LineSectionHints");
        Assert.Equal(lines, hints);
    }
}
