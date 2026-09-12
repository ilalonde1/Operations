#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The two halves of the PDF route hand over in memory (completion plan WP4, 2026-09-11): the
/// intake's views go to the composer as <see cref="DxfSheet"/>s and the level list as lines, and
/// the DXF folder is an outlet, not the transport. The gate was written before the change and
/// holds both routes to the same model.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the composer given views in memory and a DXF folder that does not exist
/// builds the same model as the composer given the same views as files (a synthetic set of two
/// storeys, one column, one wall, one plate); the six banked sets built through memory and
/// through the disk are byte-identical (Slow — the six-set gate's cost twice). WHAT IT DOES NOT:
/// a set whose text is outside ASCII (the file is Windows-1252 and the disk route decodes it as
/// UTF-8; memory carries the string — where that ever differs, memory is the right one and the
/// bank moves); the time saved (measured in the plan, not asserted).
/// </remarks>
public sealed class TheHandoffIsInMemoryAndTheModelIsTheSameTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Line(string layer, double x1, double y1, double x2, double y2)
        => string.Join("\n", "0", "LINE", "8", layer, "10", N(x1), "20", N(y1), "11", N(x2), "21", N(y2));

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string[] Rectangle(string layer, double x0, double y0, double x1, double y1) =>
        [Line(layer, x0, y0, x1, y0), Line(layer, x1, y0, x1, y1), Line(layer, x1, y1, x0, y1), Line(layer, x0, y1, x0, y0)];

    private static IReadOnlyList<string> PlanDxf()
    {
        var lines = new List<string> { "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "1", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES" };
        foreach (string block in Rectangle("JBP_C_SLABEDG", 0, 0, 900, 700)) lines.AddRange(block.Split('\n'));
        foreach (string block in Rectangle("JBP_V_COL", 100, 100, 124, 124)) lines.AddRange(block.Split('\n'));
        foreach (string block in Rectangle("JBP_V-WALL", 300, 100, 700, 112)) lines.AddRange(block.Split('\n'));
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        return lines;
    }

    [Fact]
    public void TheComposerGivenViewsInMemoryReadsNoFolderAndBuildsTheSameModel()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-handoff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            const string name = "--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf";
            string[] levels = ["LEVEL 1,0", "LEVEL 2,144"];
            var plan = PlanDxf();

            // the disk route: the view as a file, the levels as a file
            string diskDir = Path.Combine(root, "disk");
            Directory.CreateDirectory(diskDir);
            File.WriteAllLines(Path.Combine(diskDir, name), plan);
            File.WriteAllLines(Path.Combine(root, "levels.csv"), levels);
            string diskOut = Path.Combine(root, "disk.e2k");
            var fromDisk = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = diskDir, LevelsFile = Path.Combine(root, "levels.csv"), OutputE2k = diskOut, DeriveRulesFromReference = false,
            });

            // the memory route: the same view and levels as lines, and a folder that does not exist
            string memoryOut = Path.Combine(root, "memory.e2k");
            var fromMemory = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = Path.Combine(root, "no-such-folder"), Sheets = [new DxfSheet(name, plan)], LevelLines = levels,
                OutputE2k = memoryOut, DeriveRulesFromReference = false,
            });

            Assert.Equal(fromDisk.SavedModel.Columns, fromMemory.SavedModel.Columns);
            Assert.Equal(fromDisk.SavedModel.Walls, fromMemory.SavedModel.Walls);
            Assert.Equal(fromDisk.SavedModel.Floors, fromMemory.SavedModel.Floors);
            Assert.True(fromMemory.SavedModel.Columns >= 1 && fromMemory.SavedModel.Floors >= 1, "the synthetic plan has a column and a plate");
            Assert.Equal(File.ReadAllText(diskOut), File.ReadAllText(memoryOut));
            Assert.Equal(Path.GetFileName(name), Path.GetFileName(Assert.Single(fromMemory.Sheets).File));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TheSixSetsBuildTheSameModelThroughMemoryAsThroughTheDisk()
    {
        string? rules = Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");
        Assert.False(string.IsNullOrWhiteSpace(rules), "KOR_ENGINEERINGTOOLS_STANDARDSDB is the rules the six are built with");
        var (options, source) = PdfIntakeOptions.For(rules);
        Assert.Equal("KorStandards", source);

        string root = Path.Combine(Path.GetTempPath(), $"kor-handoff-six-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var verdicts = new List<string>();
        try
        {
            foreach (var set in SixSetsBuildAsBankedTests.Sets)
            {
                string pdf = DrawingMirror.SingleFile(set.SharePath);
                var memory = PdfOnlyBuild.Build(pdf, Path.Combine(root, set.Job + "-memory"), set.Scale, options, rules, stem: set.Job, handoff: PdfOnlyBuild.Handoff.Memory);
                var disk = PdfOnlyBuild.Build(pdf, Path.Combine(root, set.Job + "-disk"), set.Scale, options, rules, stem: set.Job, handoff: PdfOnlyBuild.Handoff.Disk);
                bool bothBuilt = memory.Model is not null && disk.Model is not null;
                bool identical = bothBuilt && File.ReadAllBytes(memory.OutputE2k).SequenceEqual(File.ReadAllBytes(disk.OutputE2k));
                verdicts.Add($"{set.Job}: {(identical ? "identical" : bothBuilt ? "DIFFERENT" : $"memory {(memory.Model is null ? "no model: " + memory.ModelError : "model")}, disk {(disk.Model is null ? "no model: " + disk.ModelError : "model")}")}"
                    + $"  ({memory.Elapsed.TotalSeconds:0} s in memory, {disk.Elapsed.TotalSeconds:0} s by disk)");
            }
            foreach (string v in verdicts) output.WriteLine(v);        // the per-route seconds are the measurement WP4 states
            Assert.True(verdicts.All(v => v.Contains(": identical", StringComparison.Ordinal)), string.Join("\n", verdicts));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
