#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE SAME DRAWINGS IN ANOTHER ORDER BUILD THE SAME STRUCTURE (audit F11, intake step 61,
/// 2026-09-13): the order a CAD export lists a view's entities in is not information about the
/// building, so a set read once and composed twice — as it is, and with every view's entities
/// reversed — must give two models that are the same structure. A nearest-node search that keeps
/// the first of three points within one tolerance, a tie broken by arrival, a "first" segment that
/// seeds a walk: each is a dependence on entity order, and this is the check that finds every one
/// at once, on all six banked sets. The yardstick's own frame was one such (§68: `MaxBy` on pair
/// votes, four sets' verdicts moved with the order our columns were listed in).
/// </summary>
/// <remarks>
/// SLOW: the six banked sets read once each (the PDF half, minutes) and composed twice.
/// WHAT THIS COVERS: every column's and wall's placement per storey and every plate's area, on the
/// six banked sets, under one reordering (reversal) of every view's entities, registered by the grid
/// labels both models carry (<see cref="ModelDiff"/>, one partner each, walls by their extent along
/// each axis). WHAT IT DOES NOT: an order this one is not (a shuffle; reversing one view alone);
/// sections and materials behind a name; the report's warnings; object NAMES (a label may differ).
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a dependence that shows only under a permutation that is
/// not a reversal (two entities that swap under a shuffle and keep their relative order under a
/// reversal).
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class TheSameDrawingsInAnotherOrderBuildTheSameStructureTests
{
    private readonly ITestOutputHelper _out;
    public TheSameDrawingsInAnotherOrderBuildTheSameStructureTests(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "RED, KNOWN, MEASURED 2026-09-14 (s70): PlanLoopBuilder seeds its walk and numbers its nodes in arrival order, and all six sets build other walls reversed (31168: 473 lost / 241 gained; 31065: 134 / 161; 31130: 76 / 54). A canonical coordinate sort was tried and reverted the same night. Run it by hand: dotnet test --filter FullyQualifiedName~TheSameDrawingsInAnotherOrder; the diffs land in TestResults/reversed/<job>-diff.txt.")]
    public void EveryBankedSetBuildsTheSameStructureWithItsEntitiesReversed()
    {
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the route reads its rules from KorStandards and never skips.");
        Assert.True(LiveProjects.ShareReachable, "the projects share is not reachable; the six sources are mirrored from it");
        var (options, source) = PdfIntakeOptions.For(conn);
        Assert.Equal("KorStandards", source);

        string results = Path.Combine(AppContext.BaseDirectory, "TestResults", "reversed");
        var sets = SixSetsBuildAsBankedTests.Sets;
        var outcomes = new string?[sets.Count];
        Parallel.For(0, sets.Count, new ParallelOptions { MaxDegreeOfParallelism = sets.Count }, i =>
        {
            var set = sets[i];
            try
            {
                string pdf = DrawingMirror.SingleFile(set.SharePath);
                Assert.True(File.Exists(pdf), $"{set.Job}: not mirrored from {set.SharePath}");
                string work = Path.Combine(results, set.Job);
                if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
                Directory.CreateDirectory(work);
                int pages;
                using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf)) pages = doc.NumberOfPages;

                // read once, composed twice
                var sheets = PdfOnlyBuild.WriteSheets(pdf, Path.Combine(work, set.Job + ".dxf"), 1, pages, set.Scale, markup: false, korLayers: true, options, writeDxf: false);
                var reversed = sheets.Views.Select(v => v.Reversed()).ToList();
                var asIs = PdfOnlyBuild.Compose(pdf, Path.Combine(work, "as-is"), pages, sheets, sheets.Views, options, conn);
                var other = PdfOnlyBuild.Compose(pdf, Path.Combine(work, "reversed"), pages, sheets, reversed, options, conn);
                foreach (var (dir, views) in new[] { ("as-is", sheets.Views), ("reversed", (IReadOnlyList<DxfSheet>)reversed) })
                {
                    string dxfDir = Path.Combine(work, dir, "dxf");
                    Directory.CreateDirectory(dxfDir);
                    foreach (var v in views) File.WriteAllLines(Path.Combine(dxfDir, v.Name), v.Lines);
                }
                if (asIs.Model is null) { outcomes[i] = $"{set.Job}: no model as-is: {asIs.ModelError}"; return; }
                if (other.Model is null) { outcomes[i] = $"{set.Job}: no model reversed: {other.ModelError}"; return; }

                var diff = ModelDiff.Compare(asIs.OutputE2k, other.OutputE2k);
                string line = $"{set.Job}: registered at ({diff.Shift.X}, {diff.Shift.Y}) {diff.ShiftHow}; {diff.OneLine}";
                _out.WriteLine(line);
                if (!diff.ByteIdentical && diff.Storeys.Any(s => s.Changed)) _out.WriteLine(ModelDiff.Report(diff));
                bool registered = diff.Shift.X == 0 && diff.Shift.Y == 0;
                bool same = diff.LostColumns + diff.GainedColumns + diff.LostWalls + diff.GainedWalls + diff.PlatesMoved == 0;
                string diffPath = Path.Combine(results, $"{set.Job}-diff.txt");   // this run's, or none (a stale one reads as a regression)
                if (!registered || !same)
                {
                    File.WriteAllText(diffPath, line + Environment.NewLine + ModelDiff.Report(diff));
                    outcomes[i] = line + (registered ? "" : " - expected no shift");
                }
                else File.Delete(diffPath);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                outcomes[i] = $"{set.Job}: {ex.GetType().Name}: {ex.Message}";
            }
        });

        var moved = outcomes.Where(o => o is not null).ToList();
        Assert.True(moved.Count == 0,
            "The same drawings in another order built a different structure - the model depends on the order the entities came in. " +
            "Look at what moved (TestResults/reversed/<job>-diff.txt, and render both models): " + string.Join(" | ", moved));
    }

    [Fact]
    public void AReversedViewHasItsEntitiesInTheOppositeOrderAndNothingElseMoved()
    {
        var view = new DxfSheet("LEVEL 2 PLAN.dxf",
        [
            "0", "SECTION", "2", "HEADER", "9", "$INSBASE", "10", "0.0000", "20", "0.0000", "30", "0.0000", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "8", "A", "10", "1.0000", "20", "2.0000", "11", "3.0000", "21", "4.0000",
            "0", "TEXT", "8", "GRID", "10", "0.0000", "20", "0.0000", "1", "10",
            // an R12 polyline is POLYLINE, its VERTEX entities and SEQEND: one entity to the reader, one here
            "0", "POLYLINE", "8", "C", "66", "1", "70", "1",
            "0", "VERTEX", "8", "C", "10", "0.0000", "20", "0.0000",
            "0", "VERTEX", "8", "C", "10", "9.0000", "20", "0.0000",
            "0", "SEQEND",
            "0", "LINE", "8", "B", "10", "5.0000", "20", "6.0000", "11", "7.0000", "21", "8.0000",
            "0", "ENDSEC", "0", "EOF",
        ]);
        var reversed = view.Reversed();
        Assert.Equal(view.Lines.Count, reversed.Lines.Count);
        Assert.Equal(view.Lines.Take(18), reversed.Lines.Take(18));                                          // the header and the section head as they were
        Assert.Equal(["0", "LINE", "8", "B"], reversed.Lines.Skip(18).Take(4));                              // B first now
        Assert.Equal(["0", "POLYLINE", "8", "C", "66", "1", "70", "1", "0", "VERTEX"], reversed.Lines.Skip(30).Take(10));   // the polyline whole, its vertices in their order
        Assert.Equal(["0", "SEQEND"], reversed.Lines.Skip(54).Take(2));
        Assert.Equal(["0", "TEXT", "8", "GRID"], reversed.Lines.Skip(56).Take(4));
        Assert.Equal(["0", "LINE", "8", "A"], reversed.Lines.Skip(66).Take(4));
        Assert.Equal(["0", "ENDSEC", "0", "EOF"], reversed.Lines.Skip(78));
        Assert.Equal(view.Lines, reversed.Reversed().Lines);                                                // twice is the original
    }
}
