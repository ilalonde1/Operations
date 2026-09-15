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
[Collection(SheetNamingVocabularyCollection.Name)]   // composes a model, which writes PlanSheetNaming.Vocabulary (Codex 2026-09-13, F23)
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
            // every axis: its direction, and its coordinate relative to axis 1 / axis A in the model's unit (mm) —
            // the drawing's own spacing, whatever origin the model chose (the audit noted the earlier assertion
            // checked one direction and one spacing; a translated, corrupted or turned axis escaped it)
            static (string Dir, double Coord) Of(string line) =>
                (line.Split("DIR")[1].Trim().Split(' ')[0].Trim('"'), double.Parse(line.Split("COORD")[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture));
            var axes = new[] { "1", "2", "3", "A", "B" }.ToDictionary(l => l, l => Of(Assert.Single(grids, g => g.Contains($"LABEL \"{l}\"", StringComparison.Ordinal))));
            foreach (string l in new[] { "1", "2", "3" }) Assert.Equal("X", axes[l].Dir);
            foreach (string l in new[] { "A", "B" }) Assert.Equal("Y", axes[l].Dir);
            Assert.Equal(6000, axes["2"].Coord - axes["1"].Coord, 1);
            Assert.Equal(12000, axes["3"].Coord - axes["1"].Coord, 1);
            Assert.Equal(8000, axes["B"].Coord - axes["A"].Coord, 1);
            // and the columns stand on the axes they were drawn on: the column at (2, B) is at the axes' coordinates
            var points = File.ReadAllLines(output).Where(l => l.TrimStart().StartsWith("POINT ", StringComparison.Ordinal))
                .Select(l => l.Split('"')[2].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToList();
            Assert.Contains(points, p => Math.Abs(p[0] - axes["2"].Coord) <= 1 && Math.Abs(p[1] - axes["B"].Coord) <= 1);
            Assert.Contains(report.Warnings, w => w.Contains("The model's grid is the drawings' own: 5 named axis(es)", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// THE REFERENCE PLAN IS THE PLAN THE MOST OTHER PLANS CAN BE SET ON, not the plan naming the most
    /// axes (step 65, 2026-09-14). 30993's EARTHQUAKE SENSOR LAYOUT LEVEL 4 PARTIAL PLAN names 45 "axes"
    /// - its sensor bubbles read as grid labels - and once B1 read it as a plan of L4 it won the reference
    /// on count; the 28 structural plans named nothing it named and 28 of 78 placed views became 2. Here: a
    /// LEVEL 1 plan with axes 1-3 / A-B, a LEVEL 2 plan drawing the same five, and a SENSOR LAYOUT of level
    /// 1 with ten bubbles S1-S10 nobody else names. WHAT THIS COVERS: the choice, and that both structural
    /// plans then stand on one grid carrying the five shared names. WHAT IT DOES NOT: the sensor sheet
    /// itself - placed by its columns, it carries S1-S10 into the model's GRIDS under the standing rule
    /// that a placed sheet's axes join the grid (15 GRID lines here, not 5); the members it contributes (a
    /// sheet-type row's business); a set where no two plans share three names (the count of a plan's own
    /// names breaks that tie, as before).
    /// </summary>
    [Fact]
    public void TheReferencePlanIsTheOneTheMostOtherPlansShareTheirAxesWith()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-own-grid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            static List<string> Plan(IEnumerable<(string Name, double X)> xs, IEnumerable<(string Name, double Y)> ys, IEnumerable<(double X, double Y)> columns)
            {
                var e = new List<string> { "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES" };
                foreach (var (name, x) in xs) { e.Add(Line("GRID", x, -1000, x, 9000)); e.Add(Text("GRID", name, x, 9000)); }
                foreach (var (name, y) in ys) { e.Add(Line("GRID", -1000, y, 13000, y)); e.Add(Text("GRID", name, -1000, y)); }
                foreach (var (x, y) in columns)
                    e.Add($"0\nLWPOLYLINE\n8\nKOR_V_COL\n90\n4\n70\n1\n10\n{x - 200}\n20\n{y - 200}\n10\n{x + 200}\n20\n{y - 200}\n10\n{x + 200}\n20\n{y + 200}\n10\n{x - 200}\n20\n{y + 200}");
                e.Add("0\nENDSEC\n0\nEOF");
                return e;
            }
            var xs = new[] { ("1", 0.0), ("2", 6000.0), ("3", 12000.0) };
            var ys = new[] { ("A", 0.0), ("B", 8000.0) };
            var columns = xs.SelectMany(x => ys.Select(y => (x.Item2, y.Item2))).ToList();
            File.WriteAllText(Path.Combine(root, "S2.02_1_LEVEL 1 PLAN.dxf"), string.Join("\n", Plan(xs, ys, columns)));
            File.WriteAllText(Path.Combine(root, "S2.03_1_LEVEL 2 PLAN.dxf"), string.Join("\n", Plan(xs, ys, columns)));
            // the impostor: level 1 again, the same columns, and ten bubbles along its own lines that no other sheet names
            var sensorsX = Enumerable.Range(1, 6).Select(i => ($"S{i}", -500.0 + i * 2000)).ToArray();
            var sensorsY = Enumerable.Range(7, 4).Select(i => ($"S{i}", -500.0 + (i - 6) * 2000)).ToArray();
            File.WriteAllText(Path.Combine(root, "S7.02_1_SENSOR LAYOUT LEVEL 1 PARTIAL PLAN.dxf"), string.Join("\n", Plan(sensorsX, sensorsY, columns)));
            string levels = Path.Combine(root, "levels.csv");
            File.WriteAllLines(levels, ["# unit: mm", "L1,0", "L2,3000", "L3,6000"]);
            string output = Path.Combine(root, "out.e2k");

            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = root, LevelsFile = levels, LevelsUnit = "mm", OutputE2k = output, DeriveRulesFromReference = false,
            });

            var grids = File.ReadAllLines(output).Where(l => l.TrimStart().StartsWith("GRID ", StringComparison.Ordinal)).ToList();
            foreach (string label in new[] { "\"1\"", "\"2\"", "\"3\"", "\"A\"", "\"B\"" })
                Assert.Contains(grids, g => g.Contains($"LABEL {label}", StringComparison.Ordinal));      // the shared five are the grid
            Assert.Contains(report.Warnings, w => w.Contains("S2.02_1_LEVEL 1 PLAN.dxf: the set's reference plan", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Warnings, w => w.Contains("SENSOR LAYOUT LEVEL 1 PARTIAL PLAN.dxf: the set's reference plan", StringComparison.Ordinal));
            Assert.Contains(report.Warnings, w => w.Contains("S2.03_1_LEVEL 2 PLAN.dxf: 3 of 3 X and 2 of 2 Y grid lines matched by name", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
