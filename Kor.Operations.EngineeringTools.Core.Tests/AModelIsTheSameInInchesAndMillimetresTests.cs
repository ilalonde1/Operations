#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// THE UNIT DIFFERENTIAL (rule 11, 2026-09-12): the same drawings composed into a model that counts
/// in inches and one that counts in millimetres are the same structure. Every length the composer
/// quantises by — the inch a column is placed to, the half inch a thickness snaps to, the six inches
/// a pier label is shared within, the foot a plate is keyed by — is a length, and a length written
/// as a literal in an inch-thinking composer is applied in whatever unit the model counts in. The
/// PDF route writes millimetre models, so "to the nearest inch" was to the nearest millimetre there:
/// 31138's columns, read from two sheets 2 mm apart, stood twice on every storey both sheets covered
/// (yardstick, 2026-09-12), and a wall's thickness came out at 198 mm where the drawing says 8 in.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two column outlines 1.5 mm apart on one storey (one column, read twice); a
/// wall drawn 7.8 in thick (an 8 in wall); the same wall on two storeys drawn 2 in apart (one pier);
/// the counts of column objects, wall objects, distinct sections and pier labels agree between the
/// two models, and every section's size agrees once converted. WHAT IT DOES NOT: plates (their area
/// is not quantised); the reference-model route (an inch model, where every literal is right);
/// a same-class fault it would NOT catch: a literal applied identically wrong in both units.
/// </remarks>
public sealed class AModelIsTheSameInInchesAndMillimetresTests
{
    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Line(string layer, double x1, double y1, double x2, double y2)
        => ["0", "LINE", "8", layer, "10", N(x1), "20", N(y1), "11", N(x2), "21", N(y2)];

    private static IEnumerable<string> Rectangle(string layer, double x0, double y0, double x1, double y1)
        => Line(layer, x0, y0, x1, y0).Concat(Line(layer, x1, y0, x1, y1)).Concat(Line(layer, x1, y1, x0, y1)).Concat(Line(layer, x0, y1, x0, y0));

    /// <summary>One storey's plan in inches: a plate, a column, and a wall 7.8 in thick; a second copy of the column 0.06 in (1.5 mm) over; the wall shifted by <paramref name="wallShiftIn"/>.</summary>
    private static List<string> Plan(double unit, int insunits, double wallShiftIn, bool secondColumn)
    {
        double U(double inches) => inches * unit;
        var lines = new List<string> { "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", insunits.ToString(CultureInfo.InvariantCulture), "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES" };
        lines.AddRange(Rectangle("JBP_C_SLABEDG", U(0), U(0), U(900), U(700)));
        lines.AddRange(Rectangle("JBP_V_COL", U(100), U(100), U(124), U(124)));
        if (secondColumn) lines.AddRange(Rectangle("JBP_V_COL", U(100.06), U(100), U(124.06), U(124)));
        lines.AddRange(Rectangle("JBP_V-WALL", U(300 + wallShiftIn), U(100), U(700 + wallShiftIn), U(107.8)));
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        return lines;
    }

    private sealed record Shape(int Columns, int Walls, IReadOnlyList<string> SectionsInInches, int Piers, int Joints);

    private static Shape Compose(string root, string tag, double unit, int insunits, string levelsUnit)
    {
        string dir = Path.Combine(root, tag);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf"), Plan(unit, insunits, 0, secondColumn: true));
        File.WriteAllLines(Path.Combine(dir, "--Structural Plan - LEVEL 2 PLAN - CONCRETE OUTLINE.dxf"), Plan(unit, insunits, 2, secondColumn: false));
        string levels = Path.Combine(root, tag + "-levels.csv");
        File.WriteAllLines(levels, [$"LEVEL 1,0", $"LEVEL 2,{N(144 * unit)}", $"LEVEL 3,{N(288 * unit)}"]);
        string output = Path.Combine(root, tag + ".e2k");
        DxfToEtabsService.Run(new DxfToEtabsRequest { DxfFolder = dir, LevelsFile = levels, LevelsUnit = levelsUnit, OutputE2k = output, DeriveRulesFromReference = false });

        var doc = E2kDocument.Load(output);
        double inch = 1.0 / (doc.LengthUnitInInches() ?? 1.0);
        int columns = doc.LinesOf("LINE CONNECTIVITIES").Count(l => l.Contains("  COLUMN  ", StringComparison.Ordinal));
        int walls = doc.LinesOf("AREA CONNECTIVITIES").Count(l => l.Contains("  PANEL  ", StringComparison.Ordinal));
        var sections = new List<string>();
        foreach (string raw in doc.LinesOf("FRAME SECTIONS").Concat(doc.LinesOf("WALL PROPERTIES")).Concat(doc.LinesOf("SLAB PROPERTIES")))
        {
            var m = Regex.Match(raw, @"\b(?:D|WALLTHICKNESS|SLABTHICKNESS)\s+(-?[\d.]+)");
            if (m.Success) sections.Add(N(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / inch));
        }
        var piers = doc.LinesOf("AREA ASSIGNS").Select(l => Regex.Match(l, @"PIER\s+""([^""]+)""")).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().Count();
        int joints = doc.LinesOf("POINT COORDINATES").Count(l => l.TrimStart().StartsWith("POINT", StringComparison.Ordinal));
        return new Shape(columns, walls, sections.OrderBy(s => s, StringComparer.Ordinal).ToList(), piers, joints);
    }

    [Fact]
    public void TheSameDrawingsMakeTheSameStructureInInchesAndInMillimetres()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-units-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var inches = Compose(root, "in", 1.0, 1, "in");
            var millimetres = Compose(root, "mm", 25.4, 4, "mm");

            // the inch model is the reference the composer was written for: one column (the 1.5 mm twin is
            // the same column), an 8 in wall, one pier up the building
            Assert.Equal(1, inches.Columns);
            Assert.Equal(1, inches.Piers);
            Assert.Contains("8", inches.SectionsInInches);

            Assert.Equal(inches.Columns, millimetres.Columns);
            Assert.Equal(inches.Walls, millimetres.Walls);
            Assert.Equal(inches.Piers, millimetres.Piers);
            Assert.Equal(inches.SectionsInInches, millimetres.SectionsInInches);
            Assert.Equal(inches.Joints, millimetres.Joints);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
