#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The drawings' own grid is written to the model (intake step 40). With no reference model, the
/// set's reference plan supplies the grid every sheet is placed on by axis name — and the branch
/// that writes GRIDS ran only when NO grid was known at all, so every PDF-only model shipped with
/// no GRIDS section: 31168's Revit-route model has 21 named axes, ours had 0 (2026-09-10). The
/// axes the sheets were placed by are exactly the grid to write.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a set of one sheet with named axes on its GRID layer and no reference model
/// producing GRIDS with those labels, at those coordinates in the model's unit, and the warning
/// saying so. WHAT IT DOES NOT: a set whose reference MODEL brings its own GRIDS (unchanged, and
/// covered by the reference-route tests); several sheets disagreeing about an axis (the median is
/// taken; not banked); the six sets (31170 27 axes, 31168 38, measured 2026-09-10).
/// </remarks>
public sealed class TheDrawingsOwnGridIsWrittenToTheModelTests
{
    private static string Line(string layer, double x1, double y1, double x2, double y2)
        => $"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}";
    private static string Text(string layer, string value, double x, double y)
        => $"0\nTEXT\n8\n{layer}\n10\n{x.ToString(CultureInfo.InvariantCulture)}\n20\n{y.ToString(CultureInfo.InvariantCulture)}\n40\n300\n1\n{value}";

    [Fact]
    public void ASetPlacedOnItsOwnReferencePlanWritesThatPlansAxesAsGrids()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-own-grid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // a plan in mm: axes 1, 2, 3 at x = 0, 6000, 12000 and A, B at y = 0, 8000, labelled at their ends;
            // a column at each of six intersections so the sheet has structure to place
            var entities = new List<string>
            {
                "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES",
            };
            foreach (var (name, x) in new[] { ("1", 0.0), ("2", 6000.0), ("3", 12000.0) })
            {
                entities.Add(Line("GRID", x, -1000, x, 9000));
                entities.Add(Text("GRID", name, x, 9000));
            }
            foreach (var (name, y) in new[] { ("A", 0.0), ("B", 8000.0) })
            {
                entities.Add(Line("GRID", -1000, y, 13000, y));
                entities.Add(Text("GRID", name, -1000, y));
            }
            foreach (double x in new[] { 0.0, 6000.0, 12000.0 })
                foreach (double y in new[] { 0.0, 8000.0 })
                    entities.Add($"0\nLWPOLYLINE\n8\nKOR_V_COL\n90\n4\n70\n1\n10\n{x - 200}\n20\n{y - 200}\n10\n{x + 200}\n20\n{y - 200}\n10\n{x + 200}\n20\n{y + 200}\n10\n{x - 200}\n20\n{y + 200}");
            entities.Add("0\nENDSEC\n0\nEOF");
            File.WriteAllText(Path.Combine(root, "A102_1_LEVEL 1 PLAN.dxf"), string.Join("\n", entities));
            string levels = Path.Combine(root, "levels.csv");
            File.WriteAllLines(levels, ["# unit: mm", "L1,0", "L2,3000"]);
            string output = Path.Combine(root, "out.e2k");

            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = root, LevelsFile = levels, LevelsUnit = "mm", OutputE2k = output, DeriveRulesFromReference = false,
            });

            var grids = File.ReadAllLines(output).Where(l => l.TrimStart().StartsWith("GRID ", StringComparison.Ordinal)).ToList();
            Assert.Equal(5, grids.Count);
            foreach (string label in new[] { "\"1\"", "\"2\"", "\"3\"", "\"A\"", "\"B\"" })
                Assert.Contains(grids, g => g.Contains($"LABEL {label}", StringComparison.Ordinal));
            var x2 = Assert.Single(grids, g => g.Contains("LABEL \"2\"", StringComparison.Ordinal));
            var x3 = Assert.Single(grids, g => g.Contains("LABEL \"3\"", StringComparison.Ordinal));
            Assert.Contains("DIR \"X\"", x2, StringComparison.Ordinal);
            double c2 = double.Parse(x2.Split("COORD")[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            double c3 = double.Parse(x3.Split("COORD")[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.Equal(6000, c3 - c2, 1);                                                    // the model is in mm: axes 2 and 3 are 6 m apart
            Assert.Contains(report.Warnings, w => w.Contains("The model's grid is the drawings' own: 5 named axis(es)", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
