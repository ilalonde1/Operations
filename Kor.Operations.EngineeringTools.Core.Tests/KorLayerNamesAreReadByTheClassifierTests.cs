#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A DXF written from a PDF must be readable by the same classifier that reads one exported from
/// Revit — and each layer must be read as ONE thing.
/// </summary>
/// <remarks>
/// StructuralPlanClassifier matches layers by SUBSTRING, against PlanClassificationOptions:
/// SLABEDG, _COL, WALL. So a PDF-derived plan joins the hardened DXF-to-ETABS pipeline for free if
/// its layer names contain those, and stays a special case needing --slab-layers/--column-layers
/// overrides forever if they do not.
///
/// The exporter therefore BUILDS each name out of the pattern it must match rather than spelling it
/// out a second time. This is the test that the construction actually works, and — the part that
/// matters more — that the three names stay mutually unambiguous.
///
/// Ambiguity is the live danger, not a typo. ModelQuestionnaire already records the trap in KOR's
/// own convention: "V_COL-WALL is a column layer, and testing walls first would take it for a
/// wall." A name carrying two patterns is read as whichever the classifier tests first, and which
/// one that is is not something the exporter controls.
///
/// WHAT THIS COVERS: that each emitted name matches its own pattern and NO other; that BEAM
/// deliberately matches none; that a future edit to any pattern which creates an overlap fails
/// here rather than silently turning slabs into walls somewhere downstream.
///
/// WHAT IT DOES NOT COVER: that the classifier, handed a real file, then recovers sensible plates
/// and members from it — that is geometry, not naming, and the sheet border and title block are
/// still in the file. It also does not prove the Revit bridge emits these exact names; it proves
/// both are read by one vocabulary, which is the property that matters.
/// </remarks>
public sealed class KorLayerNamesAreReadByTheClassifierTests
{
    private static readonly PlanClassificationOptions Patterns = new();

    private static string[] LayersIn(string dxf)
        => System.IO.File.ReadAllLines(dxf);

    // ── the vocabulary is a LIST, and the banked one has three column patterns ───────────────────

    /// <summary>KorStandards as it actually stands: dxf.column-layer-patterns = "_COL;-COL;S-COL".</summary>
    private static PlanClassificationOptions Banked => new()
    {
        SlabLayerPatterns   = ["SLABEDG"],
        ColumnLayerPatterns = ["_COL", "-COL", "S-COL"],
        WallLayerPatterns   = ["WALL"],
    };

    private static IReadOnlyList<string> EmittedLayers(bool korLayers, PlanClassificationOptions? rules)
    {
        var geo = new ExtractedGeometry();
        geo.Columns.Add((100, 100));
        geo.ColumnColors.Add((200, 16, 16));
        geo.ColumnIsAnnotation.Add(false);
        geo.ColumnSizes.Add((600, 600));

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"kor-vocab-{System.Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(geo, path, korLayers: korLayers, classification: rules);
            var lines = System.IO.File.ReadAllLines(path);
            var names = new List<string>();
            for (int i = 0; i < lines.Length - 1; i++)
                if (lines[i].Trim() == "8") names.Add(lines[i + 1].Trim());
            return names.Distinct().ToList();
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    /// <summary>
    /// The banked list has three column patterns. Any of them builds a workable name, but the
    /// exporter must not depend on WHICH is first — the first version indexed [0] and was correct
    /// only by luck of ordering.
    /// </summary>
    [Fact]
    public void AReorderedVocabularyStillProducesAReadableLayer()
    {
        var forward = Banked;
        var reversed = Banked with { ColumnLayerPatterns = ["S-COL", "-COL", "_COL"] };

        string a = Assert.Single(EmittedLayers(true, forward),
            n => Matches(n, forward.ColumnLayerPatterns));
        string b = Assert.Single(EmittedLayers(true, reversed),
            n => Matches(n, reversed.ColumnLayerPatterns));

        Assert.StartsWith("KOR_", a, StringComparison.Ordinal);
        Assert.StartsWith("KOR_", b, StringComparison.Ordinal);
    }

    /// <summary>An empty vocabulary must not throw — indexing [0] did.</summary>
    [Fact]
    public void AnEmptyVocabularyEmitsThePlainNameRatherThanThrowing()
    {
        var none = Banked with { ColumnLayerPatterns = [] };

        var names = EmittedLayers(true, none);

        Assert.Contains("COLUMN", names);
    }

    /// <summary>
    /// A pattern that would build a name readable as two elements must be passed over, not emitted.
    /// KOR's own convention carries this trap: "V_COL-WALL is a column layer, and testing walls
    /// first would take it for a wall."
    /// </summary>
    [Fact]
    public void APatternThatWouldBuildAnAmbiguousNameIsSkipped()
    {
        // "_COL-WALL" first would give KOR_V_COL-WALL — column AND wall
        var trap = Banked with { ColumnLayerPatterns = ["_COL-WALL", "_COL"] };

        string column = Assert.Single(EmittedLayers(true, trap),
            n => n.StartsWith("KOR_V", StringComparison.Ordinal)
              && !n.Contains("MARKUP", StringComparison.Ordinal)
              && !n.Contains("SLABEDG", StringComparison.Ordinal)
              && !n.EndsWith("WALL", StringComparison.Ordinal));

        Assert.Equal("KOR_V_COL", column);
    }

    /// <summary>Write one tiny geometry both ways and read the layer names back out of the file.</summary>
    private static IReadOnlyList<string> EmittedLayers(bool korLayers)
    {
        var geo = new ExtractedGeometry();
        geo.Slabs.Add(new List<(double X, double Y)> { (0, 0), (5000, 0), (5000, 4000), (0, 4000) });
        geo.SlabColors.Add((200, 16, 16));
        geo.SlabIsAnnotation.Add(false);
        geo.Columns.Add((100, 100));
        geo.ColumnColors.Add((200, 16, 16));
        geo.ColumnIsAnnotation.Add(false);
        geo.ColumnSizes.Add((600, 600));
        geo.Lines.Add(new List<(double X, double Y)> { (0, 0), (3000, 0) });
        geo.LineColors.Add((200, 16, 16));
        geo.LineIsAnnotation.Add(false);

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"kor-layers-{korLayers}-{System.Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(geo, path, korLayers: korLayers);

            // group code 8 is the layer name; the value is the line after it
            var lines = LayersIn(path);
            var names = new List<string>();
            for (int i = 0; i < lines.Length - 1; i++)
                if (lines[i].Trim() == "8")
                    names.Add(lines[i + 1].Trim());
            return names.Distinct().ToList();
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private static bool Matches(string layer, IReadOnlyList<string> patterns)
        => patterns.Any(p => layer.Contains(p, System.StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void WithoutTheFlagTheLayersAreUnchanged()
    {
        var names = EmittedLayers(korLayers: false);

        Assert.Contains("SLAB", names);
        Assert.Contains("COLUMN", names);
        // and the classifier cannot read the two that matter, which is exactly why the flag exists
        Assert.False(Matches("SLAB", Patterns.SlabLayerPatterns));
        Assert.False(Matches("COLUMN", Patterns.ColumnLayerPatterns));
    }

    [Fact]
    public void WithTheFlagEveryStructuralLayerMatchesItsOwnPattern()
    {
        var names = EmittedLayers(korLayers: true);

        string slab   = Assert.Single(names, n => Matches(n, Patterns.SlabLayerPatterns));
        string column = Assert.Single(names, n => Matches(n, Patterns.ColumnLayerPatterns));

        Assert.StartsWith("KOR_", slab);
        Assert.StartsWith("KOR_", column);
    }

    /// <summary>
    /// The one that guards the real hazard: no emitted name may carry two patterns, or which
    /// element it is depends on the order the classifier happens to test in.
    /// </summary>
    [Fact]
    public void NoEmittedLayerIsReadableAsTwoDifferentElements()
    {
        foreach (string layer in EmittedLayers(korLayers: true))
        {
            int hits = 0;
            if (Matches(layer, Patterns.SlabLayerPatterns))   hits++;
            if (Matches(layer, Patterns.ColumnLayerPatterns)) hits++;
            if (Matches(layer, Patterns.WallLayerPatterns))   hits++;

            Assert.True(hits <= 1,
                $"layer '{layer}' matches {hits} of the classifier's vocabularies; which element it " +
                "becomes then depends on the order they are tested in, which the exporter does not control");
        }
    }

    /// <summary>
    /// BEAM must stay unmatched. Lines is whatever did not close — 2,420 subpaths on 31130's page
    /// 12, mostly grid and dimension work. If this starts matching, those become walls.
    /// </summary>
    [Fact]
    public void TheLineLayerIsNotReadAsStructure()
    {
        var names = EmittedLayers(korLayers: true);
        string beam = Assert.Single(names, n => n.StartsWith("BEAM", System.StringComparison.Ordinal));

        Assert.False(Matches(beam, Patterns.SlabLayerPatterns));
        Assert.False(Matches(beam, Patterns.ColumnLayerPatterns));
        Assert.False(Matches(beam, Patterns.WallLayerPatterns));
    }
}
