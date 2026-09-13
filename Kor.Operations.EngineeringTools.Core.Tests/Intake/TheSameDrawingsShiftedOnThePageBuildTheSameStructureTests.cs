#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE SAME DRAWINGS SHIFTED ON THE PAGE BUILD THE SAME STRUCTURE (intake step 56, rule 11's
/// differential for the class §61 found): where a drawing set's views are put on the page is not
/// information about the building, so a set read once and composed twice — as it is, and with every
/// view moved by one vector — must give two models that differ by that vector alone. On 2026-09-12
/// the same 36 DXFs of 31168 translated 5 m x 3 m built a model whose columns matched to 100% and
/// whose walls differed by 25 lost / 11 gained: tower A's stair core read as 18 walls in one frame
/// and 8 in the other, because two of its sheets named no axis and stayed wherever the page put them
/// (step 55 places those on their members). This is the check that finds every such dependence at
/// once, on all six banked sets.
/// </summary>
/// <remarks>
/// SLOW: the six banked sets read once each (the PDF half, minutes) and composed twice.
/// WHAT THIS COVERS: every column's and wall's placement per storey and every plate's area, on the
/// six banked sets, under one translation of every view by (5,000.37, 3,000.61) mm - not a whole
/// number of millimetres, so sub-millimetre keys and hair-fine tolerances are exercised - registered by the
/// grid labels both models carry (<see cref="ModelDiff"/>), and the registration itself is asserted
/// to be the shift applied. WHAT IT DOES NOT: a rotation; a shift of one view alone (that is a
/// different drawing); sections and materials behind a name; the report's warnings; a dependence
/// on where the origin is that shows only under a different vector (one vector is one draw). A
/// SAME-CLASS FAULT IT WOULD NOT CATCH: a dependence on the frame that shows only under a vector this one
/// is not (a rotation; a shift that lands a coordinate exactly on a rounding boundary).
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests
{
    // NOT A WHOLE NUMBER OF MILLIMETRES. Under (5,000, 3,000) every fractional part survives the shift and
    // anything keyed below a millimetre - an exact-duplicate key at 0.1 mm, a fit's vote a hair either side
    // of its tolerance - reads the same in both frames; the page-frame change of step 54 shifted each sheet
    // by its own fraction and moved 31065's L1 plan 3 mm where this vector had seen nothing (section 64).
    private const double ShiftX = 5000.37, ShiftY = 3000.61;

    private readonly ITestOutputHelper _out;
    public TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryBankedSetBuildsTheSameStructureShifted()
    {
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the route reads its rules from KorStandards and never skips.");
        Assert.True(LiveProjects.ShareReachable, "the projects share is not reachable; the six sources are mirrored from it");
        var (options, source) = PdfIntakeOptions.For(conn);
        Assert.Equal("KorStandards", source);

        string results = Path.Combine(AppContext.BaseDirectory, "TestResults", "shifted");
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
                var moved = sheets.Views.Select(v => v.Shifted(ShiftX, ShiftY)).ToList();
                var asIs = PdfOnlyBuild.Compose(pdf, Path.Combine(work, "as-is"), pages, sheets, sheets.Views, options, conn);
                var shifted = PdfOnlyBuild.Compose(pdf, Path.Combine(work, "shifted"), pages, sheets, moved, options, conn);
                // both readings left on disk beside their models, so what moved can be opened (dxf-inspect, grid-names)
                foreach (var (dir, views) in new[] { ("as-is", sheets.Views), ("shifted", (IReadOnlyList<DxfSheet>)moved) })
                {
                    string dxfDir = Path.Combine(work, dir, "dxf");
                    Directory.CreateDirectory(dxfDir);
                    foreach (var v in views) File.WriteAllLines(Path.Combine(dxfDir, v.Name), v.Lines);
                }
                if (asIs.Model is null) { outcomes[i] = $"{set.Job}: no model as-is: {asIs.ModelError}"; return; }
                if (shifted.Model is null) { outcomes[i] = $"{set.Job}: no model shifted: {shifted.ModelError}"; return; }

                var diff = ModelDiff.Compare(asIs.OutputE2k, shifted.OutputE2k);
                double unit = E2kDocument.Load(asIs.OutputE2k).LengthUnitInInches() ?? 1.0;   // the shift in the model's unit: the views are millimetres
                var expected = ((long)Math.Round(ShiftX / 25.4 / unit), (long)Math.Round(ShiftY / 25.4 / unit));
                string line = $"{set.Job}: registered at ({diff.Shift.X}, {diff.Shift.Y}) {diff.ShiftHow}; {diff.OneLine}";
                _out.WriteLine(line);
                if (!diff.ByteIdentical && diff.Storeys.Any(s => s.Changed)) _out.WriteLine(ModelDiff.Report(diff));
                bool registered = Math.Abs(diff.Shift.X - expected.Item1) <= 1 && Math.Abs(diff.Shift.Y - expected.Item2) <= 1;
                bool same = diff.LostColumns + diff.GainedColumns + diff.LostWalls + diff.GainedWalls + diff.PlatesMoved == 0;
                string diffPath = Path.Combine(results, $"{set.Job}-diff.txt");   // this run's, or none (a stale one reads as a regression)
                if (!registered || !same)
                {
                    File.WriteAllText(diffPath, line + Environment.NewLine + ModelDiff.Report(diff));
                    outcomes[i] = line + (registered ? "" : $" - expected the shift ({expected.Item1}, {expected.Item2})");
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
            "The same drawings shifted on the page built a different structure - the model depends on where the origin is. " +
            "Look at what moved (TestResults/shifted/<job>-diff.txt, and render both models): " + string.Join(" | ", moved));
    }

    [Fact]
    public void AShiftedViewMovesEveryCoordinateAndNothingElse()
    {
        var view = new DxfSheet("LEVEL 2 PLAN.dxf",
        [
            "0", "SECTION", "2", "HEADER", "9", "$INSBASE", "10", "-100.0000", "20", "-50.0000", "30", "0.0000", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "8", "KOR_V-WALL", "10", "1.5000", "20", "2.5000", "30", "0.0000", "11", "11.5000", "21", "12.5000", "31", "0.0000",
            "0", "TEXT", "8", "GRID", "10", "0.0000", "20", "0.0000", "40", "300.0000", "1", "10",     // a grid LABELLED "10": a value, not a code
            "0", "ENDSEC", "0", "EOF",
        ]);
        var moved = view.Shifted(5000, 3000);
        Assert.Equal(view.Name, moved.Name);
        Assert.Equal(view.Lines.Count, moved.Lines.Count);
        Assert.Equal(["10", "4900.0000", "20", "2950.0000"], moved.Lines.Skip(6).Take(4));                      // the header's insertion base
        Assert.Equal(["10", "5001.5000", "20", "3002.5000", "30", "0.0000", "11", "5011.5000", "21", "3012.5000"], moved.Lines.Skip(22).Take(10));
        Assert.Equal(["40", "300.0000", "1", "10"], moved.Lines.Skip(42).Take(4));                              // the label "10" is still "10"
        Assert.Equal(view.Lines.Where((_, i) => i % 2 == 0), moved.Lines.Where((_, i) => i % 2 == 0));   // every code as it was
    }
}
