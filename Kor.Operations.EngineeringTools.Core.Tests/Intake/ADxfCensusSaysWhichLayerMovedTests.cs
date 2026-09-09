#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The census counts entities per layer in the ENTITIES section and says which layers moved between
/// two folders — the differential every intake step is judged by, now compiled.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: entity types counted and not (VERTEX, the LAYER table), the layer of each,
/// the per-file diff and its summary, files present on one side only, and the baseline's file names.
/// WHAT IT DOES NOT: real DXFs — `takeoff intake-baseline` writes them and `takeoff dxf-census`
/// compares them by hand at each step — and a change inside an entity that leaves the count alone.
/// </remarks>
public sealed class ADxfCensusSaysWhichLayerMovedTests
{
    private static string Dxf(params (string Kind, string Layer)[] entities)
    {
        var lines = new List<string> { "0", "SECTION", "2", "TABLES", "0", "TABLE", "2", "LAYER", "0", "LAYER", "2", "GRID", "0", "ENDTAB", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES" };
        foreach (var (kind, layer) in entities)
        {
            lines.AddRange(new[] { "0", kind, "8", layer, "10", "0", "20", "0" });
            if (kind == "POLYLINE")
                lines.AddRange(new[] { "0", "VERTEX", "8", layer, "10", "1", "20", "1", "0", "VERTEX", "8", layer, "10", "2", "20", "2", "0", "SEQEND", "8", layer });
        }
        lines.AddRange(new[] { "0", "ENDSEC", "0", "EOF" });
        return string.Join("\n", lines);
    }

    [Fact]
    public void EntitiesAreCountedByLayerAndAPolylineCountsOnce()
    {
        var counts = DxfLayerCensus.Of(Dxf(("LINE", "GRID"), ("LINE", "GRID"), ("TEXT", "GRID"), ("POLYLINE", "SLAB"), ("POLYLINE", "FOOTING")).Split('\n'));
        Assert.Equal(3, counts["GRID"]);
        Assert.Equal(1, counts["SLAB"]);
        Assert.Equal(1, counts["FOOTING"]);
        Assert.Equal(3, counts.Count);   // the LAYER table's "GRID" entry is not an entity
    }

    [Fact]
    public void TheDiffNamesTheLayerThatMovedAndNothingElse()
    {
        string root = Path.Combine(Path.GetTempPath(), $"census-{Guid.NewGuid():N}");
        string before = Path.Combine(root, "before"), after = Path.Combine(root, "after");
        Directory.CreateDirectory(before); Directory.CreateDirectory(after);
        try
        {
            File.WriteAllText(Path.Combine(before, "a.dxf"), Dxf(("POLYLINE", "SLAB"), ("LINE", "BEAM"), ("LINE", "BEAM")));
            File.WriteAllText(Path.Combine(after, "a.dxf"), Dxf(("POLYLINE", "SLAB"), ("LINE", "BEAM"), ("POLYLINE", "FOOTING")));
            File.WriteAllText(Path.Combine(before, "b.dxf"), Dxf(("LINE", "BEAM")));
            File.WriteAllText(Path.Combine(after, "b.dxf"), Dxf(("LINE", "BEAM")));
            File.WriteAllText(Path.Combine(after, "c.dxf"), Dxf(("LINE", "BEAM")));

            var diffs = DxfLayerCensus.Compare(before, after);
            Assert.Equal(3, diffs.Count);
            var a = diffs.Single(d => d.Name == "a.dxf");
            Assert.Equal(new[] { "BEAM", "FOOTING" }, a.Changed.Keys.OrderBy(k => k));
            Assert.Equal((2, 1), a.Changed["BEAM"]);
            Assert.Equal((0, 1), a.Changed["FOOTING"]);
            Assert.True(diffs.Single(d => d.Name == "b.dxf").Identical);
            Assert.True(diffs.Single(d => d.Name == "c.dxf").MissingBefore);

            var report = DxfLayerCensus.Report(diffs);
            Assert.Contains(report, l => l.StartsWith("b.dxf") && l.Contains("identical"));
            Assert.Contains(report, l => l.StartsWith("c.dxf") && l.Contains("only in AFTER"));
            Assert.Equal("1 of 3 identical; layers that moved: BEAM, FOOTING", report[^1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TheBaselineNamesItsFilesAsTheStepFoldersDo()
    {
        var multi = IntakeBaseline.Jobs.Single(j => j.Number == "31130-01");
        var single = IntakeBaseline.Jobs.Single(j => j.Number == "31202-01");
        Assert.Equal("31130-01-p11.dxf", IntakeBaseline.DxfName(multi, 11));
        Assert.Equal("31202-01.dxf", IntakeBaseline.DxfName(single, 17));
        Assert.Equal(13, IntakeBaseline.Jobs.Sum(j => j.LastPage - j.FirstPage + 1));
    }
}
