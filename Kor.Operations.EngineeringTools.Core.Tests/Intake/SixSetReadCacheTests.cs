#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WHAT THIS COVERS: reader-source and options invalidation versus composer/test edits, PDF bytes
/// and scale, source additions/removals/renames, repository-root discovery, and a saved manifest/CSV
/// reloaded through the analyzer's typed reader. Missing/damaged cache artifacts force a miss.
/// WHAT IT DOES NOT: parse PDFs or DXFs, query rules, run the composer or prove the two-minute target;
/// the six-set gate's two real runs do that. A fault hidden by a shared full/recompose assumption
/// or by reader-side static state is outside these synthetic cache tests.
/// </summary>
public sealed class SixSetReadCacheTests
{
    [Fact]
    public void ReaderBytesAndOptionsInvalidateButComposerAndTestEditsDoNot()
    {
        InTempDirectory(root =>
        {
            const string core = "Kor.Operations.EngineeringTools.Core/";
            string[] included =
            [
                "PdfToSafe/GeometryFilterService.cs", "PdfToSafe/Nested/Reader.cs",
                "Intake/SheetViews.cs", "Intake/Nested/Reader.cs", "Intake/PdfOnlyBuild.cs",
                "Dxf/PlanSheetNaming.cs", "Dxf/DrawingVocabulary.cs", "Dxf/DxfSheet.cs", "Dxf/DxfModels.cs",
                "Dxf/LoopGeometry.cs", "Dxf/PlanLoopBuilder.cs", "Dxf/DashedLineJoiner.cs",
                "Dxf/MatchLineSheetJoin.cs", "Dxf/GridAlignment.cs", "Dxf/StructuralPlanClassifier.cs", "Dxf/RuleSettings.cs",
            ];
            string[] excluded =
            [
                "Intake/CorpusAnalyzer.cs", "Intake/CorpusDiff.cs", "Intake/SheetDiff.cs", "Intake/SetCheck.cs", "Intake/StoreysFromPlans.cs",
                "Dxf/E2kGeometryComposer.cs", "Dxf/DxfToEtabsService.cs", "Dxf/WallOutlineDecomposer.cs",
                "Dxf/WallNetwork.cs", "Dxf/ModelDiff.cs", "Dxf/ModelYardstick.cs",
            ];
            foreach (string file in included.Concat(excluded)) Write(root, core + file, "// source");
            const string testFile = "Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetsBuildAsBankedTests.cs";
            Write(root, testFile, "// test");
            var options = PdfIntakeOptions.Default;
            string original = SixSetReadCache.ReaderHash(root, options);
            foreach (string file in excluded.Select(f => core + f).Append(testFile))
            {
                File.AppendAllText(Path.Combine(root, file), " ");
                Assert.Equal(original, SixSetReadCache.ReaderHash(root, options));
            }
            foreach (string file in included)
            {
                string path = Path.Combine(root, core + file);
                File.AppendAllText(path, " ");
                Assert.NotEqual(original, SixSetReadCache.ReaderHash(root, options));
                File.WriteAllText(path, "// source");
            }
            string added = Write(root, core + "PdfToSafe/Added.cs", "// source");
            string withAdded = SixSetReadCache.ReaderHash(root, options);
            Assert.NotEqual(original, withAdded);
            string renamed = Path.Combine(root, core + "PdfToSafe/Renamed.cs");
            File.Move(added, renamed);
            Assert.NotEqual(withAdded, SixSetReadCache.ReaderHash(root, options));
            File.Delete(renamed);
            Assert.Equal(original, SixSetReadCache.ReaderHash(root, options));
            Assert.NotEqual(original, SixSetReadCache.ReaderHash(root, options with { ColumnMinDimMm = options.ColumnMinDimMm + 1 }));
            var wordOptions = options with { ForceWords = new[] { "KIP" } };
            Assert.NotEqual(SixSetReadCache.ReaderHash(root, wordOptions),
                SixSetReadCache.ReaderHash(root, wordOptions with { ForceWords = new[] { "KN" } }));
            // A missing required source must not produce a plausible hash over an incomplete list.
            File.Delete(Path.Combine(root, core + "Dxf/RuleSettings.cs"));
            Assert.Throws<FileNotFoundException>(() => SixSetReadCache.ReaderHash(root, options));
        });
    }

    [Fact]
    public void PdfFingerprintUsesBytesAndScaleAndTheRootIsFoundAboveTheTestOutput()
    {
        InTempDirectory(root =>
        {
            string project = Write(root, "Kor.Operations.EngineeringTools.Core.Tests/Kor.Operations.EngineeringTools.Core.Tests.csproj", "fixture");
            string output = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Debug", "net8.0");
            Directory.CreateDirectory(output);
            Assert.Equal(root, SixSetReadCache.RepositoryRoot(output));
            string input = Write(root, "input.bin", "first");
            var before = SixSetReadCache.Inputs(input, 96, "reader");
            Assert.Equal(before, SixSetReadCache.Inputs(input, 96, "reader"));
            var stamp = File.GetLastWriteTimeUtc(input);
            File.WriteAllText(input, "other"); // Same length and restored timestamp, different bytes.
            File.SetLastWriteTimeUtc(input, stamp);
            Assert.NotEqual(before.PdfSha256, SixSetReadCache.Inputs(input, 96, "reader").PdfSha256);
            Assert.NotEqual(before, before with { Scale = 100 });
        });
    }

    [Fact]
    public void AKeptReadingRoundTripsAndEveryMissingArtifactRequiresReadingAgain()
    {
        InTempDirectory(work =>
        {
            string[] files = ["S1_1_LEVEL 1; BLDG A.dxf", "S1_2_LEVEL 2, BLDG B.dxf"];
            foreach (string file in files) Write(work, "dxf/" + file, "placeholder, never parsed");
            var sheet = new PdfOnlyBuild.SheetOutcome(1, "S1", "plan", "Plan, \"two views\"", "L1", "1/8\"", 96,
                0, 0, 1, 2, 3, 0, 4, files, "a, \"quoted\" check", null);
            var detail = new PdfOnlyBuild.SheetOutcome(2, null, "detail", null, null, null, null,
                0, 0, 0, 0, 0, 0, 0, [], "", null);
            var sheets = new PdfOnlyBuild.SheetsResult([sheet, detail], [], 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0);
            var built = new PdfOnlyBuild.BuildOutcome("fixture", 2, sheets, null, null, null, null, "unused", TimeSpan.Zero);
            var inputs = new SixSetReadCache.Fingerprint("pdf-hash", 96, "reader-hash");
            Assert.Null(SixSetReadCache.Read(work, inputs, out string missing));
            Assert.Equal("manifest missing", missing);
            SixSetReadCache.Save(work, "job", inputs, built);
            var kept = SixSetReadCache.Read(work, inputs, out string hit);
            Assert.NotNull(kept);
            Assert.Equal("manifest matched", hit);
            Assert.Equal(inputs, kept.Manifest.Inputs);
            Assert.Equal(2, kept.Manifest.Pages);
            Assert.Equal(DateTimeKind.Utc, kept.Manifest.ReadAtUtc.Kind);
            Assert.Equal(1, kept.Sheets.Written);
            Assert.Equal(1, kept.Sheets.NotPlan);
            Assert.Equal(2, kept.Sheets.Sheets.Count);
            var restored = kept.Sheets.Sheets[0];
            Assert.Equal(files, restored.DxfFiles);
            Assert.Equal(sheet with { DxfFiles = restored.DxfFiles }, restored);
            var restoredDetail = kept.Sheets.Sheets[1];
            Assert.Empty(restoredDetail.DxfFiles);
            Assert.Equal(detail with { DxfFiles = restoredDetail.DxfFiles }, restoredDetail);
            var rows = CorpusAnalyzer.ReadSheetRows(Path.Combine(work, "sheets.csv"), Guid.Empty).ToList();
            Assert.Equal("job", rows[0].Job);
            Assert.Null(rows[0].Placed);
            Assert.Equal(sheet.Title, rows[0].Title);
            Assert.Equal(sheet.SelfCheck, rows[0].SelfCheck);
            Assert.Equal(kept.Manifest, SixSetReadCache.Read(work, inputs, out _)!.Manifest); // Hits keep the read time.

            foreach (var (changed, expectedReason) in new[]
            {
                (inputs with { PdfSha256 = "other" }, "manifest changed: PDF"),
                (inputs with { Scale = 100 }, "manifest changed: scale"),
                (inputs with { ReaderSha256 = "other" }, "manifest changed: reader source or options"),
            })
            {
                Assert.Null(SixSetReadCache.Read(work, changed, out string why));
                Assert.Equal(expectedReason, why);
            }
            File.Delete(Path.Combine(work, "sheets.csv"));
            Assert.Null(SixSetReadCache.Read(work, inputs, out string noCsv));
            Assert.Equal("sheets.csv missing", noCsv);
            SixSetReadCache.Save(work, "job", inputs, built);
            File.AppendAllText(Path.Combine(work, "sheets.csv"), "damaged");
            Assert.Null(SixSetReadCache.Read(work, inputs, out string damaged));
            Assert.Equal("sheets.csv changed", damaged);
            SixSetReadCache.Save(work, "job", inputs, built);
            string dxf = Path.Combine(work, "dxf"), away = Path.Combine(work, "dxf-away");
            Directory.Move(dxf, away);
            Assert.Null(SixSetReadCache.Read(work, inputs, out string noDirectory));
            Assert.Equal("dxf directory missing", noDirectory);
            Directory.Move(away, dxf);
            File.Delete(Path.Combine(dxf, files[0]));
            Assert.Null(SixSetReadCache.Read(work, inputs, out string noView));
            Assert.Equal("dxf views missing or changed file list", noView);
            Write(work, "dxf/" + files[0], "placeholder, never parsed");
            string extra = Write(work, "dxf/extra.dxf", "placeholder, never parsed");
            Assert.Null(SixSetReadCache.Read(work, inputs, out _));
            File.Delete(extra);
            Assert.NotNull(SixSetReadCache.Read(work, inputs, out _));
            File.WriteAllText(Path.Combine(work, SixSetReadCache.ManifestName), "{");
            Assert.Null(SixSetReadCache.Read(work, inputs, out string malformed));
            Assert.Equal("read cache invalid: JsonException", malformed);
        });
    }

    private static string Write(string root, string relative, string contents)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void InTempDirectory(Action<string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-read-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { check(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
