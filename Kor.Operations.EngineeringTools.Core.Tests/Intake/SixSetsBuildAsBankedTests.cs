#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE SIX-SET GATE, AS A TEST: the six drawing sets every intake rule is measured on, each built
/// from its stick file alone by the one route in Core (<see cref="PdfOnlyBuild"/>) and compared
/// byte for byte with the model banked beside this test (<c>Baselines/pdf-only-&lt;job&gt;.e2k</c>).
/// Banking a step is replacing a baseline in the commit that changes the rule, so the diff is
/// reviewable in git; the shell harness and its hand-typed <c>cp</c> did this until 2026-09-11
/// (completion plan WP1).
/// </summary>
/// <remarks>
/// SLOW: six PDFs (124 MB, mirrored from the share once) built in parallel; matching reader manifests
/// reuse the DXF views and run the ladder and composer again. A miss runs the original full build.
/// WHAT THIS COVERS: the whole PDF-only route on five KOR sets and one architect's set, every
/// storey, member and plate of the finished file — a change that moves any byte fails here, and the
/// failure prints what moved (<see cref="ModelDiff"/>: plates by area, columns and walls by
/// position, frames matched by grid label) and leaves both models in TestResults. WHAT IT DOES NOT:
/// the other 286 sets (the corpus analyzer's ledger); whether a change is RIGHT — it says only that
/// the models changed, and the person who banks the new baseline is the one who looked.
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a change that is wrong in the same way on the banked
/// model and the new one — the yardsticks are the second gate for that.
/// The sources are named by their share paths: 31130's is its 2026-05-20 issue (now under "06 Old
/// Structural Stickfiles"), 31168's its 2026-04-21 issue (moved there by the office on 2026-09-18) — the bank is a fixed drawing, not the
/// newest one; the corpus run reads the newest.
/// WHAT THE CACHE COVERS: PDF SHA-256, scale, the specified reader source files and serialised
/// PdfIntakeOptions; sheets.csv and the DXF file list must still stand. The final byte comparison
/// runs on both paths. WHAT THE CACHE DOES NOT INVALIDATE ON: E2kGeometryComposer.cs,
/// DxfToEtabsService.cs, WallOutlineDecomposer.cs, WallNetwork.cs, ModelDiff.cs, ModelYardstick.cs
/// or the Tests project. It can hide a composer relying on reader-side static state set only by
/// reading: PlanSheetNaming.Vocabulary is such a dependency. PdfOnlyBuild.WriteLevels initialises
/// it from the rows before the ladder; hashing options does not replace that recompose-time work.
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class SixSetsBuildAsBankedTests
{
    public sealed record Banked(string Job, string SharePath, int Scale);

    public static readonly IReadOnlyList<Banked> Sets =
    [
        new("31130-01", @"\\Kor-fs01\Projects\Projects\03 Residential\31130-01 (Uplands Area 6 Lot 12 West Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31130-01 UPLANDS LOT 12 2026-05-20 Stick file.pdf", 96),
        new("31138-01", @"\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\05 Stickfile\31138-01 2026-09-01_2170 W 1st Ave_Str Set.pdf", 96),
        new("31065-01", @"\\Kor-fs01\Projects\Projects\03 Residential\31065-01 (5350 5430 Heather Street Vancouver)\05 Stickfile\31065-01 - 2026-07-08 - 5380-Heather Street - Stickfile (up to SSI-04)-OAP.pdf", 100),
        new("31202-01", @"\\Kor-fs01\Projects\Projects\03 Residential\31202-01 (1650 N Hotel Circle San Diego)\05 Stickfile\31202-01 2026-09-04 Hotel Circle North StickSet  - FULL SET.pdf", 96),
        new("31168-01", @"\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31168-01 - 2026-04-21- YMCA Langara - Stickfile.pdf", 96),
        new("31170-01-arch", @"\\Kor-fs01\Projects\Projects\03 Residential\31170-01 (2005-2045 West 49 Ave Vancouer)\05 Stickfile\01 Architectural\260723_W49th_75% BP Draft.pdf", 96),
    ];

    private readonly ITestOutputHelper _out;
    public SixSetsBuildAsBankedTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryBankedSetBuildsByteIdenticalToItsBaseline()
    {
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the route reads its rules from KorStandards and never skips.");
        Assert.True(LiveProjects.ShareReachable, "the projects share is not reachable; the six sources are mirrored from it");

        string baselines = Path.Combine(AppContext.BaseDirectory, "Baselines");
        string results = Path.Combine(AppContext.BaseDirectory, "TestResults", "six-sets");
        Directory.CreateDirectory(results);
        // THE BANK IS BUILT THE WAY PRODUCTION BUILDS: with the rows, and it says so. Until 2026-09-12
        // this read PdfIntakeOptions.Default and passed no connection; the composer read the rows anyway
        // (through the environment variable) and every dxf.pdf row equals its compiled default, so the six
        // were byte-identical either way - but a gate that asserts the connection and then does not use it
        // is one row away from banking the wrong model. Now it uses it.
        var (options, source) = PdfIntakeOptions.For(conn);
        Assert.Equal("KorStandards", source);
        string readerHash = SixSetReadCache.ReaderHash(SixSetReadCache.RepositoryRoot(AppContext.BaseDirectory), options);

        var outcomes = new (Banked Set, string? Failure, ModelDiff.Result? Diff)[Sets.Count];
        var cachePaths = new string?[Sets.Count];
        Parallel.For(0, Sets.Count, new ParallelOptions { MaxDegreeOfParallelism = Sets.Count }, i =>
        {
            var set = Sets[i];
            try
            {
                string pdf = DrawingMirror.SingleFile(set.SharePath);
                Assert.True(File.Exists(pdf), $"{set.Job}: not mirrored from {set.SharePath}");
                string work = Path.Combine(results, set.Job);
                var inputs = SixSetReadCache.Inputs(pdf, set.Scale, readerHash);
                var cached = SixSetReadCache.Read(work, inputs, out string reason);
                PdfOnlyBuild.BuildOutcome built;
                if (cached is not null)
                {
                    cachePaths[i] = $"recomposed (read cache from {cached.Manifest.ReadAtUtc:yyyy-MM-dd HH:mm} UTC)";
                    built = PdfOnlyBuild.Recompose(pdf, work, cached.Manifest.Pages, cached.Sheets, options, conn);
                }
                else
                {
                    cachePaths[i] = $"read ({reason})";
                    built = PdfOnlyBuild.Build(pdf, work, set.Scale, options, rulesConnection: conn, stem: set.Job);
                    SixSetReadCache.Save(work, set.Job, inputs, built);
                }
                if (built.Model is null) { outcomes[i] = (set, $"no model: {built.ModelError}", null); return; }
                string baseline = Path.Combine(baselines, $"pdf-only-{set.Job}.e2k");
                Assert.True(File.Exists(baseline), $"{set.Job}: no baseline at {baseline}");
                outcomes[i] = (set, null, ModelDiff.Compare(baseline, built.OutputE2k));
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                outcomes[i] = (set, $"{ex.GetType().Name}: {ex.Message}", null);
            }
        });

        var moved = new List<string>();
        for (int i = 0; i < outcomes.Length; i++)
        {
            var (set, failure, diff) = outcomes[i];
            if (cachePaths[i] is { } cachePath) _out.WriteLine($"{set.Job}: {cachePath}");
            string line = failure ?? diff!.OneLine;
            _out.WriteLine($"{set.Job}: {line}");
            // a diff on disk is THIS run's, or none: a set that built identical leaves no stale one from an
            // earlier run to be read as tonight's (2026-09-12: three were, and read as three regressions)
            string diffPath = Path.Combine(results, $"{set.Job}-diff.txt");
            if (failure is not null) moved.Add($"{set.Job}: {failure}");
            else if (!diff!.ByteIdentical)
            {
                string report = ModelDiff.Report(diff);
                _out.WriteLine(report);
                File.WriteAllText(diffPath, report);
                moved.Add($"{set.Job}: {line}");
            }
            else File.Delete(diffPath);
        }
        Assert.True(moved.Count == 0,
            "The six-set gate: these sets no longer build byte-identical to their baselines. Look at what moved (TestResults/six-sets/<job>-diff.txt, and render both models), " +
            "then either fix the rule or bank the new models by replacing Baselines/pdf-only-<job>.e2k in the same commit:\n  " + string.Join("\n  ", moved));
    }
}
