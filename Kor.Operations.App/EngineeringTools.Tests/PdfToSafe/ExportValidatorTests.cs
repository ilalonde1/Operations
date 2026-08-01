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

    // ─── Additional coverage added 2026-04 ────────────────────────────────

    [Fact]
    public void Collinear_three_point_slab_with_zero_area_emits_error()
    {
        // Three points but on a straight line — polygon has ≥3 vertices so
        // passes Rule 2's degenerate-count gate, but its PolygonAreaMm2 is 0.
        // Rule 2 second branch ("slab-zero-area") must fire.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (2000, 0) }, ((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-zero-area");
    }

    [Fact]
    public void Negative_thickness_emits_error()
    {
        // -1 mm is invalid for a slab; same rule as 0 thickness but exercises
        // the <= 0 branch end of the condition.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)7, (byte)7, (byte)7));
        var cs = NewColorSettingsDict();
        cs[((byte)7, (byte)7, (byte)7)] = NewSettings(thicknessMm: -1);

        var result = Validate(geo, cs, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-thickness");
    }

    [Fact]
    public void Short_wall_centerline_emits_warning()
    {
        // Wall centerline <10 mm is degenerate — Rule 5 fires (currently had
        // no positive-case coverage).
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));

        var lines    = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var hints    = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        var colors   = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (100, 100), (105, 100) }); // 5 mm long
        hints.Add((System.ValueTuple<double, double>?)((200.0, 300.0))); // non-null hint marks it a wall
        colors.Add(((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "wall-zero-length");
    }

    [Fact]
    public void Long_wall_centerline_does_not_emit_warning()
    {
        // 5000 mm wall → no wall-zero-length issue. Negative case for Rule 5.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));

        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var hints  = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        var colors = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (5000, 0) });
        hints.Add((System.ValueTuple<double, double>?)((200.0, 300.0)));
        colors.Add(((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "wall-zero-length");
    }

    [Fact]
    public void Line_without_hint_skipped_by_wall_rule()
    {
        // Lines with null hint aren't walls; Rule 5 must ignore them.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));

        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var hints  = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        var colors = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (1, 0) }); // 1 mm, sub-threshold
        hints.Add((System.ValueTuple<double, double>?)null);       // NO section hint → not a wall
        colors.Add(((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "wall-zero-length");
    }

    [Fact]
    public void Null_reclassified_arg_throws_ArgumentNullException()
    {
        var ex = Record.Exception(() => Validate(null!, null, NewExportSettings()));
        Assert.IsType<System.Reflection.TargetInvocationException>(ex);
        Assert.IsType<System.ArgumentNullException>(ex!.InnerException);
    }

    [Fact]
    public void Null_settings_arg_throws_ArgumentNullException()
    {
        var ex = Record.Exception(() => Validate(NewExtracted(), null, null!));
        Assert.IsType<System.Reflection.TargetInvocationException>(ex);
        Assert.IsType<System.ArgumentNullException>(ex!.InnerException);
    }

    [Fact]
    public void Multiple_issues_in_one_model_all_surface()
    {
        // Real models hit several rules at once. Assert we don't stop on the
        // first issue — ALL of these should be reported.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (5000, 0), (5000, 5000), (0, 5000) }, ((byte)1, (byte)2, (byte)3));
        AddColumn(geo, (5e5, 5e5), ((byte)1, (byte)2, (byte)3));       // way outside slab
        AddColumn(geo, (5e5 + 1, 5e5 + 1), ((byte)1, (byte)2, (byte)3)); // duplicate of above

        var cs = NewColorSettingsDict();
        cs[((byte)1, (byte)2, (byte)3)] = NewSettings(thicknessMm: 0); // zero thickness

        var es = NewExportSettings();
        _tExport.GetProperty("LoadCombCode")!.SetValue(es, "NBC");

        var result = Validate(geo, cs, es);
        var cats = IssuesOf(result).Select(CategoryOf).ToList();

        Assert.Contains("slab-thickness",    cats);
        Assert.Contains("column-unsupported",cats);
        Assert.Contains("column-duplicate",  cats);
        Assert.Contains("live-load-missing", cats);
    }

    [Fact]
    public void Suspicious_thick_wall_emits_warning()
    {
        // A "wall" 1850 mm thick is almost certainly a shaft outline that
        // the shaft-decomposition heuristic missed (e.g. aspect just under
        // the threshold) — surface as a warning so the engineer can override.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));
        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var hints  = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        var colors = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (5000, 0) });
        hints.Add((System.ValueTuple<double, double>?)((1850.0, 1000.0)));
        colors.Add(((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "wall-suspicious-thickness");
    }

    [Fact]
    public void Realistic_wall_thickness_does_not_emit_warning()
    {
        // 300 mm wall is realistic — no warning.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));
        var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
        var hints  = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
        var colors = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
        lines.Add(new List<(double, double)> { (0, 0), (5000, 0) });
        hints.Add((System.ValueTuple<double, double>?)((300.0, 1000.0)));
        colors.Add(((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "wall-suspicious-thickness");
    }

    [Fact]
    public void Suspicious_oversized_column_emits_warning()
    {
        // 1600 × 1600 mm "column" is past the columnDimWarn threshold (1500).
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));
        ((System.Collections.IList)_tExtracted.GetProperty("Columns")!.GetValue(geo)!).Add((5000d, 5000d));
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnColors")!.GetValue(geo)!).Add(((byte)1, (byte)2, (byte)3));
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnSizes")!.GetValue(geo)!).Add((1600d, 1600d));

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "column-suspicious-size");
    }

    [Fact]
    public void Normal_column_size_does_not_emit_warning()
    {
        // 500 × 800 mm — typical KOR concrete column. No warning.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }, ((byte)1, (byte)2, (byte)3));
        ((System.Collections.IList)_tExtracted.GetProperty("Columns")!.GetValue(geo)!).Add((5000d, 5000d));
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnColors")!.GetValue(geo)!).Add(((byte)1, (byte)2, (byte)3));
        ((System.Collections.IList)_tExtracted.GetProperty("ColumnSizes")!.GetValue(geo)!).Add((500d, 800d));

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "column-suspicious-size");
    }

    [Fact]
    public void Three_vertex_slab_emits_triangle_warning()
    {
        // Slab from chain-assembled balcony stub: 3 vertices, > 1 mm² area.
        // Rule 2 (slab-degenerate) requires < 3 vertices and Rule 2's
        // zero-area branch only fires on < 1 mm². Rule 10 fills the gap
        // for legitimate-but-fragmenty 3-vertex triangles.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (4000, 0), (2000, 1500) }, ((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.Contains(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-triangle");
    }

    [Fact]
    public void Four_vertex_slab_does_not_emit_triangle_warning()
    {
        // Standard rectangular slab — no warning.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (4000, 0), (4000, 3000), (0, 3000) }, ((byte)1, (byte)2, (byte)3));

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "slab-triangle");
    }

    [Fact]
    public void Column_inside_second_slab_is_supported()
    {
        // Rule 4 must consider EVERY slab. A column inside slab #2 but not
        // slab #1 is still supported.
        var geo = NewExtracted();
        AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));
        AddSlab(geo, new() { (5000, 5000), (6000, 5000), (6000, 6000), (5000, 6000) }, ((byte)1, (byte)2, (byte)3));
        AddColumn(geo, (5500, 5500), ((byte)1, (byte)2, (byte)3)); // inside slab 2

        var result = Validate(geo, null, NewExportSettings());
        Assert.DoesNotContain(IssuesOf(result).Cast<object>(), i => CategoryOf(i) == "column-unsupported");
    }
}
