using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Rule-by-rule tests for ExportValidator. The validator is an internal
/// type with internal dependencies; reflection keeps the tests narrow
/// without widening the production API surface.
/// </summary>
public class ExportValidatorTests
{
    private static readonly Assembly _asm = typeof(Kor.Operations.Services.AppAiService).Assembly;
    private static readonly System.Type _tExtracted =
        _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.ExtractedGeometry", throwOnError: true)!;
    private static readonly System.Type _tSettings =
        _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.SlabColorSettings", throwOnError: true)!;
    private static readonly System.Type _tExport =
        _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.ExportSettings", throwOnError: true)!;
    private static readonly System.Type _tValidator =
        _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.ExportValidator", throwOnError: true)!;
    private static readonly System.Type _tResult =
        _asm.GetType("Kor.Operations.EngineeringTools.PdfToSafe.ValidationResult", throwOnError: true)!;

    private static object NewExtracted() =>
        System.Activator.CreateInstance(_tExtracted, nonPublic: true)!;

    private static object NewExportSettings() =>
        System.Activator.CreateInstance(_tExport, nonPublic: true)!;

    private static System.Collections.IDictionary NewColorSettingsDict()
    {
        var dictT = typeof(Dictionary<,>).MakeGenericType(
            typeof(System.ValueTuple<byte, byte, byte>), _tSettings);
        return (System.Collections.IDictionary)System.Activator.CreateInstance(dictT)!;
    }

    private static object NewSettings(double thicknessMm = 250, double live = 0)
    {
        var s = System.Activator.CreateInstance(_tSettings, nonPublic: true)!;
        _tSettings.GetProperty("ThicknessMm")!.SetValue(s, thicknessMm);
        _tSettings.GetProperty("LiveKPa")!.SetValue(s, live);
        return s;
    }

    private static object Validate(object extracted, System.Collections.IDictionary? colorSettings, object exportSettings)
    {
        var mi = _tValidator.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!;
        return mi.Invoke(null, new[] { extracted, colorSettings, exportSettings })!;
    }

    private static IEnumerable<object> IssuesOf(object result)
    {
        var issuesProp = _tResult.GetProperty("Issues")!;
        return ((System.Collections.IEnumerable)issuesProp.GetValue(result)!).Cast<object>();
    }

    private static string CategoryOf(object issue)
    {
        var cat = issue.GetType().GetProperty("Category")!.GetValue(issue);
        return (string)cat!;
    }

    private static int ErrorCount(object result) =>
        (int)_tResult.GetProperty("ErrorCount")!.GetValue(result)!;

    private static int WarningCount(object result) =>
        (int)_tResult.GetProperty("WarningCount")!.GetValue(result)!;

    private static void AddSlab(object geo, List<(double, double)> pts, (byte, byte, byte) color)
    {
        ((System.Collections.IList)_tExtracted.GetProperty("Slabs")!.GetValue(geo)!).Add(pts);
        ((System.Collections.IList)_tExtracted.GetProperty("SlabColors")!.GetValue(geo)!).Add(color);
    }

    private static void AddColumn(object geo, (double, double) pt, (byte, byte, byte) color)
    {
        ((System.Collections.IList)_tExtracted.GetProperty("Columns")!.GetValue(geo)!).Add(pt);
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnColors")!.GetValue(geo)!).Add(color);
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnSizes")!.GetValue(geo)!).Add((400d, 400d));
    }

    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_model_emits_one_error()
    {
        var geo = NewExtracted();
        var settings = NewExportSettings();
        var result = Validate(geo, null, settings);

        Assert.Equal(1, ErrorCount(result));
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "model-empty");
    }

    [Fact]
    public void Healthy_model_emits_no_errors_and_no_warnings()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (5000, 0), (5000, 5000), (0, 5000) }, ((byte)255, (byte)0, (byte)0));
        AddColumn(geo, (1000, 1000), ((byte)255, (byte)0, (byte)0)); // inside the slab

        var colorSettings = NewColorSettingsDict();
        colorSettings[((byte)255, (byte)0, (byte)0)] = NewSettings(thicknessMm: 250, live: 2.0);

        var es = NewExportSettings();
        _tExport.GetProperty("LoadCombCode")!.SetValue(es, "NBC");

        var result = Validate(geo, colorSettings, es);
        Assert.Equal(0, ErrorCount(result));
        Assert.Equal(0, WarningCount(result));
    }

    [Fact]
    public void Column_outside_every_slab_emits_warning()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)255, (byte)0, (byte)0));
        AddColumn(geo, (5000, 5000), ((byte)255, (byte)0, (byte)0)); // far outside

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "column-unsupported");
    }

    [Fact]
    public void Zero_thickness_slab_emits_error()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));
        var cs = NewColorSettingsDict();
        cs[((byte)1, (byte)2, (byte)3)] = NewSettings(thicknessMm: 0);

        var result = Validate(geo, cs, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-thickness");
    }

    [Fact]
    public void Combo_without_live_load_emits_warning()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));
        var cs = NewColorSettingsDict();
        cs[((byte)1, (byte)2, (byte)3)] = NewSettings(thicknessMm: 250, live: 0); // no LIVE

        var es = NewExportSettings();
        _tExport.GetProperty("LoadCombCode")!.SetValue(es, "NBC");

        var result = Validate(geo, cs, es);
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "live-load-missing");
    }

    [Fact]
    public void Duplicate_columns_emit_warning()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));
        AddColumn(geo, (1000, 1000), ((byte)1, (byte)2, (byte)3));
        AddColumn(geo, (1000.5, 1001), ((byte)1, (byte)2, (byte)3)); // within 10mm

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "column-duplicate");
    }

    [Fact]
    public void Degenerate_slab_polygon_emits_error()
    {
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (100, 100) }, ((byte)1, (byte)2, (byte)3)); // only 2 points

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-degenerate");
    }
}
