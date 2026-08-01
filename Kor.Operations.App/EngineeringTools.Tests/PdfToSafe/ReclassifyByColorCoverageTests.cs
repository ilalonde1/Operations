#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Augments the existing <see cref="ReclassifyByColorTests"/> with paths
    /// that currently have no explicit coverage:
    ///   - Slab→Beam (polyline preserved, null section hint)
    ///   - null colorSettings + no element overrides → returns original unchanged
    ///   - Color not in settings dict → falls through to default classification
    ///   - Per-element override wins over per-color setting
    /// Uses the same reflection pattern as the original file to match style.
    /// </summary>
    public class ReclassifyByColorCoverageTests
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
            var dictT = typeof(Dictionary<,>).MakeGenericType(typeof(System.ValueTuple<byte, byte, byte>), _tSettings);
            return (System.Collections.IDictionary)System.Activator.CreateInstance(dictT)!;
        }

        private static Dictionary<int, string> NewOverrides(params (int Index, string Type)[] entries)
        {
            var d = new Dictionary<int, string>();
            foreach (var (i, t) in entries) d[i] = t;
            return d;
        }

        private static object ReclassifyByColor(
            object original,
            System.Collections.IDictionary? colorSettings,
            Dictionary<int, string>? slabOverrides = null,
            Dictionary<int, string>? lineOverrides = null,
            Dictionary<int, string>? columnOverrides = null)
        {
            var mi = _tExtractor.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "ReclassifyByColor");
            var paras = mi.GetParameters();
            var args = new object?[paras.Length];
            args[0] = original;
            args[1] = colorSettings;
            args[2] = slabOverrides;
            args[3] = lineOverrides;
            args[4] = columnOverrides;
            for (int i = 5; i < paras.Length; i++) args[i] = paras[i].DefaultValue;
            return mi.Invoke(null, args)!;
        }

        private static void AddSlab(object geo, List<(double, double)> pts, (byte, byte, byte) color)
        {
            var slabs = (System.Collections.IList)_tExtracted.GetProperty("Slabs")!.GetValue(geo)!;
            var colors = (System.Collections.IList)_tExtracted.GetProperty("SlabColors")!.GetValue(geo)!;
            slabs.Add(pts);
            colors.Add(color);
        }

        private static void AddLine(object geo, List<(double, double)> pts, (byte, byte, byte) color)
        {
            var lines  = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(geo)!;
            var colors = (System.Collections.IList)_tExtracted.GetProperty("LineColors")!.GetValue(geo)!;
            var hints  = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(geo)!;
            lines.Add(pts);
            colors.Add(color);
            hints.Add((System.ValueTuple<double, double>?)null);
        }

        private static int Count(object geo, string listName)
        {
            var list = (System.Collections.IList)_tExtracted.GetProperty(listName)!.GetValue(geo)!;
            return list.Count;
        }

        // ─── Null / empty input fast path ─────────────────────────────────

        [Fact]
        public void NullColorSettings_NoOverrides_ReturnsOriginalUnchanged()
        {
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));

            var result = ReclassifyByColor(geo, colorSettings: null);
            // Fast-path should return the SAME instance.
            Assert.Same(geo, result);
        }

        [Fact]
        public void EmptyColorSettings_NoOverrides_ReturnsOriginalUnchanged()
        {
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)1, (byte)2, (byte)3));

            var result = ReclassifyByColor(geo, NewColorSettingsDict());
            Assert.Same(geo, result);
        }

        [Fact]
        public void ColorNotInSettings_DefaultsToSlab()
        {
            // Slab's color isn't in the settings dict → falls through to default
            // classification. Because ANOTHER color in settings maps to "Wall"
            // (a recognized change type), the fast-path no-op is skipped and
            // the slab is re-emitted. The slab still lands in the Slabs bucket
            // (not Lines, not Columns) because its own color has no mapping.
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)99, (byte)99, (byte)99));

            var settings = NewColorSettingsDict();
            settings[((byte)200, (byte)200, (byte)200)] = NewSettings("Wall"); // mapping for a DIFFERENT color

            var result = ReclassifyByColor(geo, settings);
            Assert.Equal(1, Count(result, "Slabs"));
            Assert.Equal(0, Count(result, "Lines"));
            Assert.Equal(0, Count(result, "Columns"));
        }

        // ─── Slab→Beam path ───────────────────────────────────────────────

        [Fact]
        public void SlabAsBeam_PreservesPolygonAsPolylineWithNullHint()
        {
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (3000, 0), (3000, 400), (0, 400) }, ((byte)7, (byte)7, (byte)7));

            var settings = NewColorSettingsDict();
            settings[((byte)7, (byte)7, (byte)7)] = NewSettings("Beam");

            var result = ReclassifyByColor(geo, settings);

            Assert.Equal(0, Count(result, "Slabs"));
            Assert.Equal(1, Count(result, "Lines"));
            // Beam reclassification preserves the polygon verbatim.
            var lines = (System.Collections.IList)_tExtracted.GetProperty("Lines")!.GetValue(result)!;
            var poly = (List<(double X, double Y)>)lines[0]!;
            Assert.Equal(4, poly.Count);
            // And leaves the section hint null — BeamSectionParser is the source of truth.
            var hints = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(result)!;
            Assert.Null(hints[0]);
        }

        // ─── Per-element override priority ───────────────────────────────

        [Fact]
        public void SlabOverride_WinsOverColorSetting()
        {
            // Color says "Slab", element override says "Wall" — override wins.
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (200, 0), (200, 5000), (0, 5000) }, ((byte)128, (byte)0, (byte)0));

            var settings = NewColorSettingsDict();
            settings[((byte)128, (byte)0, (byte)0)] = NewSettings("Slab"); // default

            var result = ReclassifyByColor(geo, settings,
                slabOverrides: NewOverrides((0, "Wall")));

            // Override routed the slab to Wall path → centerline + hint.
            Assert.Equal(0, Count(result, "Slabs"));
            Assert.Equal(1, Count(result, "Lines"));
            var hints = (System.Collections.IList)_tExtracted.GetProperty("LineSectionHints")!.GetValue(result)!;
            Assert.NotNull(hints[0]);
        }

        [Fact]
        public void SlabOverrideToSlab_WhenColorSaysWall_KeepsItAsSlab()
        {
            // Useful real case: engineer has burgundy default = Wall, but
            // element [3] is actually a real slab region → override back to Slab.
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }, ((byte)128, (byte)0, (byte)0));

            var settings = NewColorSettingsDict();
            settings[((byte)128, (byte)0, (byte)0)] = NewSettings("Wall");

            var result = ReclassifyByColor(geo, settings,
                slabOverrides: NewOverrides((0, "Slab")));

            Assert.Equal(1, Count(result, "Slabs"));
            Assert.Equal(0, Count(result, "Lines"));
        }

        // ─── Line→Slab orphan promotion (the Regent-PDF 4-balcony bug) ────

        [Fact]
        public void OrphanSlabTypedThreePointLine_BecomesIndividualSlab()
        {
            // The exact scenario that silently dropped 4 balcony extensions on
            // the Regent plan export: red color mapped to "Slab", a closed
            // 27-point outline (chains to a polygon) PLUS 4 open 3-point spurs
            // that don't connect to the outline's chain. Pre-fix, those spurs
            // became "orphan beams" and never made it to result.Slabs.
            // Post-fix, each ≥3-point orphan is its own mini-slab polygon.
            //
            // Geometry uses 4 m × 3 m balcony stubs (6 m² each — above the
            // 2 m² noise-filter threshold added in Batch 52a). The real Regent
            // spurs were 4–5 m²; the test mirrors that scale.
            var geo = NewExtracted();
            var red = ((byte)255, (byte)0, (byte)0);

            // Closed 4-point perimeter — chain-assembles into one slab polygon.
            AddLine(geo, new List<(double, double)>
            {
                (0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0)
            }, red);
            // Four isolated 3-point "balcony" polylines (4 m × 3 m = 6 m² each)
            // that share no vertices with the perimeter.
            AddLine(geo, new() { (12000, 2000), (16000, 2000), (16000, 5000) }, red);
            AddLine(geo, new() { (12000, 6000), (16000, 6000), (16000, 9000) }, red);
            AddLine(geo, new() { (-6000, 2000), (-6000, 5000), (-2000, 5000) }, red);
            AddLine(geo, new() { (-6000, 6000), (-6000, 9000), (-2000, 9000) }, red);

            var settings = NewColorSettingsDict();
            settings[red] = NewSettings("Slab");

            var result = ReclassifyByColor(geo, settings);

            // Expect 5 slabs: 1 from the closed perimeter + 4 from promoted orphans.
            Assert.Equal(5, Count(result, "Slabs"));
            // All colors tracked correctly.
            Assert.Equal(5, Count(result, "SlabColors"));
            // No residual Line entries for the red extensions.
            Assert.Equal(0, Count(result, "Lines"));
        }

        [Fact]
        public void OrphanSlabTypedTwoPointLine_StaysAsBeam()
        {
            // A 2-point polyline can't form a polygon; stays as a beam-style
            // Line. Defensive floor for the auto-close logic.
            var geo = NewExtracted();
            var red = ((byte)255, (byte)0, (byte)0);
            AddLine(geo, new() { (0, 0), (1000, 0) }, red);

            var settings = NewColorSettingsDict();
            settings[red] = NewSettings("Slab");

            var result = ReclassifyByColor(geo, settings);

            Assert.Equal(0, Count(result, "Slabs"));
            Assert.Equal(1, Count(result, "Lines"));
        }

        // ─── Parallel-list invariant under multiple slab types ────────────

        [Fact]
        public void MultipleSlabs_MixedTypes_MaintainParallelArrays()
        {
            var geo = NewExtracted();
            AddSlab(geo, new() { (0, 0), (200, 0), (200, 5000), (0, 5000) },     ((byte)1, (byte)0, (byte)0)); // → Wall
            AddSlab(geo, new() { (0, 0), (500, 0), (500, 500), (0, 500) },       ((byte)0, (byte)1, (byte)0)); // → Column
            AddSlab(geo, new() { (0, 0), (3000, 0), (3000, 400), (0, 400) },     ((byte)0, (byte)0, (byte)1)); // → Beam
            AddSlab(geo, new() { (0, 0), (8000, 0), (8000, 8000), (0, 8000) },   ((byte)1, (byte)1, (byte)0)); // → Slab (no mapping)

            var settings = NewColorSettingsDict();
            settings[((byte)1, (byte)0, (byte)0)] = NewSettings("Wall");
            settings[((byte)0, (byte)1, (byte)0)] = NewSettings("Column");
            settings[((byte)0, (byte)0, (byte)1)] = NewSettings("Beam");
            // (byte)1,1,0 is absent from settings → stays as Slab

            var result = ReclassifyByColor(geo, settings);

            // Every Lines entry has a matching LineSectionHints entry.
            Assert.Equal(Count(result, "Lines"), Count(result, "LineSectionHints"));
            // Every Slabs entry has a matching SlabColors entry.
            Assert.Equal(Count(result, "Slabs"), Count(result, "SlabColors"));
            // Every Columns entry has a matching ColumnColors + ColumnSizes.
            Assert.Equal(Count(result, "Columns"), Count(result, "ColumnColors"));
            Assert.Equal(Count(result, "Columns"), Count(result, "ColumnSizes"));
        }
    }
}
