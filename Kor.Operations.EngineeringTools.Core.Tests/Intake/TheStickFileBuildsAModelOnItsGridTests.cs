#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The PDF route to ETABS, end to end (intake step 15): 31168's parkade plans read off the stick
/// file, written as named sheets on the office's layers, and built into a model on the reference's
/// grid by the names of their axes.
/// </summary>
/// <remarks>
/// SLOW: one PDF and one .e2k read off the share through DrawingMirror. WHAT THIS COVERS: the
/// three parkade sheets are found by sheet number, named as views, read as plans, placed on
/// storeys, and set on the grid by name — 3 of 3, none left in its page frame; the model carries
/// columns on P1 and P2 in the numbers the drawings carry. WHAT IT DOES NOT: the floor plates,
/// which a parkade plan does not draw as a filled region and this route does not yet read; walls
/// against Revit's, which pdf-vs-dxf measures; and any building the reference's grid does not name.
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]   // PlanSheetNaming.Vocabulary is a mutable static another class rewrites
public sealed class TheStickFileBuildsAModelOnItsGridTests
{
    [Fact]
    public void Langara31168ParkadePlansBuildOnTheReferencesGridByName()
    {
        if (!LiveProjects.ShareReachable) return;
        string stickFolder = LiveProjects.Folder("31168", "05 Stickfile");
        var dated = Directory.EnumerateFiles(stickFolder, "31168-01 - *.pdf", SearchOption.TopDirectoryOnly)
            .Select(f => (File: f, Date: System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^31168-01 - (\d{4}-\d{2}-\d{2})").Groups[1].Value))
            .Where(t => t.Date.Length > 0)
            .OrderByDescending(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.File, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(dated.Count > 0, $"no dated stick file PDF under {stickFolder}");
        string pdf = DrawingMirror.SingleFile(dated[0].File);
        string reference = DrawingMirror.SingleFile(LiveProjects.File("31168", "31168-reference.e2k"));

        string work = Path.Combine(Path.GetTempPath(), "kor-tests", "stickfile-model-31168");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        string dxfDir = Path.Combine(work, "dxf");
        Directory.CreateDirectory(dxfDir);

        // the parkade plans, by sheet number, wherever the issue puts them
        var wanted = new HashSet<string>(new[] { "S2.02", "S2.03.1", "S2.04.1" }, StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        using (var doc = PdfDocument.Open(pdf))
        {
            var facts = DocumentFacts.From(doc);
            var request = new IntakeRequest(96, PdfIntakeOptions.Default);
            for (int page = 1; page <= Math.Min(doc.NumberOfPages, 24) && written.Count < wanted.Count; page++)
            {
                var record = DrawingIntake.ReadSheet(doc, page, request, facts);
                if (record.SheetNumber is null || !wanted.Contains(record.SheetNumber)) continue;
                Assert.Equal("plan", record.SheetType);
                string name = SheetDxfName.For(record, $"31168-p{page:00}");
                Assert.StartsWith(record.SheetNumber + "_1_", name);
                Assert.True(PlanSheetNaming.Parse(name).ParkadeLevels.Count > 0, $"{name}: no parkade level read from the name");
                DxfExporter.Export(record.Geometry, Path.Combine(dxfDir, name), korLayers: true);
                written.Add(name);
            }
        }
        Assert.Equal(3, written.Count);

        var report = DxfToEtabsService.Run(new DxfToEtabsRequest
        {
            DxfFolder = dxfDir,
            ReferenceE2k = reference,
            OutputE2k = Path.Combine(work, "out.e2k"),
        });
        Assert.Equal(3, report.SheetsRead);
        Assert.Contains(report.Warnings, w => w.StartsWith("3 of 3 sheet(s) set on this model's grid by the names of their axes", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("could NOT be set on the grid", StringComparison.Ordinal));

        // AND THE TWO P2 HALVES ARE ONE PLAN, so three sheets are placed as two (intake step 23).
        // These drawings are millimetres and this model is inches: until 2026-09-09 the seams were
        // compared with a model-unit frame applied to millimetre linework, they missed each other by
        // a factor of 25, and the halves of the parkade she asked us to join stayed apart on this
        // route while joining on the PDF-only one. Read in the model's unit they land on each other.
        Assert.Equal(2, report.SheetsPlaced);
        Assert.Contains(report.Warnings, w => w.Contains("carry the same match line and were read as ONE plan", StringComparison.Ordinal)
                                              && w.Contains("S2.03.1", StringComparison.Ordinal) && w.Contains("S2.04.1", StringComparison.Ordinal));

        // the P3 plan's columns rise to P2 and the two P2 plans' to P1; banked 2026-09-08 at 70 and 112
        var model = File.ReadAllLines(report.OutputPath);
        int onP2 = model.Count(l => l.Contains("LINEASSIGN", StringComparison.Ordinal) && l.Contains("\"LEVEL P2\"", StringComparison.Ordinal));
        int onP1 = model.Count(l => l.Contains("LINEASSIGN", StringComparison.Ordinal) && l.Contains("\"LEVEL P1\"", StringComparison.Ordinal));
        Assert.True(onP2 >= 60 && onP1 >= 100, $"columns on P2 {onP2}, on P1 {onP1}");
    }
}
