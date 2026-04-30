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
    private static readonly Assembly _asm = typeof(Kor.Operations.Services.AppAiService).Assembly;
    private static readonly System.Type _tExtracted = _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.ExtractedGeometry", throwOnError: true)!;
    private static readonly System.Type _tSettings  = _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.SlabColorSettings", throwOnError: true)!;
    private static readonly System.Type _tExtractor = _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.PdfGeometryExtractor", throwOnError: true)!;

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
            .First(m => m.Name == "ReclassifyByColor" && m.GetParameters().Length == 5);
        return mi.Invoke(null, new[] { original, colorSettings, null, null, null })!;
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
