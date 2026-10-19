#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// THE UNIT DIFFERENTIAL (rule 11, 2026-09-12): the same drawings composed into a model that counts
/// in inches, millimetres or feet are the same structure. Every length the composer
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
/// inch, millimetre and feet models, every joint's place (X, Y and Z) to a ten-thousandth of an inch,
/// every member's kind, storey, section dimensions and joints in their own order, every storey's
/// elevation and floor-below elevation, and the concrete's strength, modulus and weight per volume.
/// WHAT IT DOES NOT: plates' areas (not quantised); the reference-model route (an inch model, where every literal is right);
/// a same-class fault it would NOT catch: a literal applied identically wrong in both units.
/// #4-#8 are exercised only as far as this small two-storey plan reaches them.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]   // composes a model, which writes PlanSheetNaming.Vocabulary (Codex 2026-09-13, F23)
public sealed class AModelIsTheSameInInchesAndMillimetresTests
{
    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // Four decimal places in feet lose more than the comparison's 0.0001 in tolerance.
    private static string Coordinate(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Line(string layer, double x1, double y1, double x2, double y2)
        => ["0", "LINE", "8", layer, "10", Coordinate(x1), "20", Coordinate(y1), "11", Coordinate(x2), "21", Coordinate(y2)];

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

    private sealed record Shape(int Columns, int Walls, IReadOnlyList<string> SectionsInInches, int Piers, int Joints,
        IReadOnlyList<string> JointsInInches, IReadOnlyList<string> Placements,
        double FcPsi, double EPsi, double WeightPerCubicInch, IReadOnlyList<StoryLevel> StoriesInInches);

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
        double k = doc.LengthUnitInInches() ?? 1.0;
        double inch = 1.0 / k;
        var materialProperties = doc.LinesOf("MATERIAL PROPERTIES")
            .SelectMany(raw => Regex.Matches(raw, @"\b(FC|E|WEIGHTPERVOLUME)\s+([-+]?[\d.]+(?:[eE][-+]?\d+)?)"))
            .ToDictionary(m => m.Groups[1].Value,
                m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);
        int columns = doc.LinesOf("LINE CONNECTIVITIES").Count(l => l.Contains("  COLUMN  ", StringComparison.Ordinal));
        int walls = doc.LinesOf("AREA CONNECTIVITIES").Count(l => l.Contains("  PANEL  ", StringComparison.Ordinal));
        var sections = new List<string>();
        foreach (string raw in doc.LinesOf("FRAME SECTIONS").Concat(doc.LinesOf("WALL PROPERTIES")).Concat(doc.LinesOf("SLAB PROPERTIES")))
        {
            // every dimension the section states (audit F24: D AND B, not one of them), in inches to a ten-thousandth
            foreach (Match m in Regex.Matches(raw, @"\b(D|B|WALLTHICKNESS|SLABTHICKNESS)\s+(-?[\d.]+)"))
                sections.Add($"{m.Groups[1].Value} {N(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / inch)}");
        }
        var piers = doc.LinesOf("AREA ASSIGNS").Select(l => Regex.Match(l, @"PIER\s+""([^""]+)""")).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().Count();
        // every joint's place in inches, and every member's kind, storey and joints (F24: positions and connectivity, not a count)
        var joints = new List<string>();
        var pointAt = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^POINT\s+""([^""]+)""\s+(-?[\d.]+)\s+(-?[\d.]+)(?:\s+(-?[\d.]+))?");
            if (!m.Success) continue;
            // X, Y and the Z offset where one is written (the second audit's finding 5)
            string z = m.Groups[4].Success ? N(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) / inch) : "0";
            string at = $"{N(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / inch)},{N(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / inch)},{z}";
            joints.Add(at); pointAt[m.Groups[1].Value] = at;
        }
        // each section's own dimensions, in inches, by name (finding 6: a flat multiset of D and B loses which section owns which)
        var sectionDims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("FRAME SECTIONS").Concat(doc.LinesOf("WALL PROPERTIES")).Concat(doc.LinesOf("SLAB PROPERTIES")))
        {
            var name = Regex.Match(raw.TrimStart(), @"^\w+\s+""([^""]+)""");
            if (!name.Success) continue;
            sectionDims[name.Groups[1].Value] = string.Join(" ", Regex.Matches(raw, @"\b(D|B|WALLTHICKNESS|SLABTHICKNESS)\s+(-?[\d.]+)").Select(m => $"{m.Groups[1].Value} {N(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / inch)}"));
        }
        var sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("LINE ASSIGNS").Concat(doc.LinesOf("AREA ASSIGNS")))
        {
            var m = Regex.Match(raw.TrimStart(), @"^(?:LINE|AREA)ASSIGN\s+""([^""]+)""\s+""[^""]+""\s+SECTION\s+""([^""]+)""");
            if (m.Success) sectionOf[m.Groups[1].Value] = sectionDims.GetValueOrDefault(m.Groups[2].Value, "?");
        }
        var placements = new List<string>();
        var storeyOf = doc.StoreysByObject();
        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES").Concat(doc.LinesOf("AREA CONNECTIVITIES")))
        {
            var m = Regex.Match(raw.TrimStart(), @"^(?:LINE|AREA)\s+""([^""]+)""\s+(\w+)\s+(?:\d+\s+)?((?:""[^""]+""\s*)+)");
            if (!m.Success) continue;
            // the joints in the member's own order (finding 5: a panel's perimeter order is its topology), rotated to start at the least
            var ring = Regex.Matches(m.Groups[3].Value, @"""([^""]+)""").Select(x => pointAt.GetValueOrDefault(x.Groups[1].Value, "?")).ToList();
            int least = ring.IndexOf(ring.OrderBy(x => x, StringComparer.Ordinal).First());
            var at = ring.Skip(least).Concat(ring.Take(least));
            foreach (string storey in storeyOf.TryGetValue(m.Groups[1].Value, out var on) ? on : (IReadOnlyList<string>)["?"])
                placements.Add($"{m.Groups[2].Value} {storey} [{sectionOf.GetValueOrDefault(m.Groups[1].Value, "?")}] {string.Join(" ", at)}");
        }
        // and the storeys' elevations and the floors below them, in inches
        var stories = doc.ReadStories()
            .Select(st => new StoryLevel(st.Name, st.Elevation / inch, st.ElevationBelow / inch))
            .OrderBy(st => st.Name, StringComparer.Ordinal).ToList();
        foreach (var st in stories) placements.Add($"STOREY {st.Name} at {N(st.Elevation)}");
        return new Shape(columns, walls, sections.OrderBy(s => s, StringComparer.Ordinal).ToList(), piers, joints.Count,
            joints.OrderBy(j => j, StringComparer.Ordinal).ToList(), placements.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            materialProperties["FC"] / (k * k), materialProperties["E"] / (k * k),
            materialProperties["WEIGHTPERVOLUME"] / (k * k * k), stories);
    }

    private static void AssertRelative(double expected, double actual)
        => Assert.InRange(Math.Abs(actual - expected), 0.0, Math.Abs(expected) * 1e-6);

    [Fact]
    public void TheSameDrawingsMakeTheSameStructureInInchesAndInMillimetresAndInFeet()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-units-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var inches = Compose(root, "in", 1.0, 1, "in");
            var millimetres = Compose(root, "mm", 25.4, 4, "mm");
            var feet = Compose(root, "ft", 1.0 / 12.0, 2, "ft");

            // the inch model is the reference the composer was written for: one column (the 1.5 mm twin is
            // the same column), an 8 in wall, one pier up the building
            Assert.Equal(1, inches.Columns);
            Assert.Equal(1, inches.Piers);
            Assert.Contains("WALLTHICKNESS 8", inches.SectionsInInches);

            foreach (var model in new[] { inches, millimetres, feet })
            {
                AssertRelative(4000, model.FcPsi);
                AssertRelative(3604996.5, model.EPsi);
                AssertRelative(0.08680555, model.WeightPerCubicInch);
            }

            foreach (var model in new[] { millimetres, feet })
            {
                Assert.Equal(inches.Columns, model.Columns);
                Assert.Equal(inches.Walls, model.Walls);
                Assert.Equal(inches.Piers, model.Piers);
                Assert.Equal(inches.SectionsInInches, model.SectionsInInches);
                Assert.Equal(inches.Joints, model.Joints);
                Assert.Equal(inches.JointsInInches, model.JointsInInches);   // every joint at the same place, to a ten-thousandth of an inch
                Assert.Equal(inches.Placements, model.Placements);          // every member the same kind on the same storey on the same joints
                Assert.Equal(inches.StoriesInInches.Select(st => st.Name), model.StoriesInInches.Select(st => st.Name));
                foreach (var (expected, actual) in inches.StoriesInInches.Zip(model.StoriesInInches))
                {
                    Assert.InRange(Math.Abs(expected.Elevation - actual.Elevation), 0.0, 1e-4);
                    Assert.InRange(Math.Abs(expected.ElevationBelow - actual.ElevationBelow), 0.0, 1e-4);
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnUnsupportedLengthUnitHasNoInchesPerUnit()
        => Assert.Null(E2kDocument.InchesPerUnit("yd"));

    [Fact]
    public void AnInchShellKeepsItsExactMaterialLines()
    {
        var doc = E2kShellBuilder.FromLevels(E2kShellBuilder.ParseLevels(["L1, 0", "L2, 144"]), "in");

        Assert.Equal(new[]
        {
            "  MATERIAL  \"KOR-DEFAULT-CONCRETE\"  TYPE \"Concrete\"  GRADE \"f'c 4000 psi\"  WEIGHTPERVOLUME 0.08680555",
            "  MATERIAL  \"KOR-DEFAULT-CONCRETE\"  SYMTYPE \"Isotropic\"  E 3604996.5  U 0.2  A 5.5E-06",
            "  MATERIAL  \"KOR-DEFAULT-CONCRETE\"  FC 4000",
        }, doc.LinesOf("MATERIAL PROPERTIES").Where(line => line.TrimStart().StartsWith("MATERIAL", StringComparison.Ordinal)));
    }
}
