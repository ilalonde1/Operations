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

    /// <summary>
    /// Halifax LEVEL 1's core (31180, VD's read 2026-10-27 13:58), the twelve wall outlines as drawn in millimetres, moved near
    /// the origin and stated in inches: a 24 in wall broken by a 969 mm and a 1195 mm door on two sides, a 12 in cross wall
    /// whose end stands inside it, a 6 in wall, and two L corners whose short pieces run on to the far face of the wall
    /// they turn into - the centrelines cross 275 mm in from the short piece's end.
    /// </summary>
    private static List<string> HalifaxCore(double unit, int insunits)
    {
        double U(double inches) => inches * unit;
        double X(double mm) => U(mm / 25.4 - 1400);
        double Y(double mm) => U(mm / 25.4 - 1000);
        var lines = new List<string> { "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", insunits.ToString(CultureInfo.InvariantCulture), "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES" };
        lines.AddRange(Rectangle("JBP_C_SLABEDG", U(0), U(0), U(600), U(500)));
        foreach (var (x0, y0, x1, y1) in HalifaxWalls)
            lines.AddRange(Rectangle("JBP_V-WALL", X(x0), Y(y0), X(x1), Y(y1)));
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        return lines;
    }

    /// <summary>The twelve outlines' extents in mm, as ezdxf printed them from LEVEL 1.dxf at 14:03 on 2026-10-27.</summary>
    private static readonly (double X0, double Y0, double X1, double Y1)[] HalifaxWalls =
    [
        (45576, 27981, 46084, 35905), (40369, 35748, 43173, 36358), (44142, 35748, 46116, 36358), (40369, 27596, 43173, 28206),
        (44142, 27596, 46116, 28206), (38168, 30341, 38676, 33582), (42845, 27849, 43150, 35990), (44286, 29349, 44438, 34517),
        (38117, 28082, 38726, 29383), (38147, 35748, 39174, 36358), (38168, 34612, 38676, 35912), (38147, 27596, 39174, 28206),
    ];

    private static Shape Compose(string root, string tag, double unit, int insunits, string levelsUnit,
        Func<double, int, List<string>>? plan = null)
    {
        string dir = Path.Combine(root, tag);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf"), plan?.Invoke(unit, insunits) ?? Plan(unit, insunits, 0, secondColumn: true));
        File.WriteAllLines(Path.Combine(dir, "--Structural Plan - LEVEL 2 PLAN - CONCRETE OUTLINE.dxf"), plan?.Invoke(unit, insunits) ?? Plan(unit, insunits, 2, secondColumn: false));
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

    /// <summary>
    /// THE WALL NETWORK IN THREE UNITS (2026-10-27). WallNetwork's snap (2 in), reach (+ 2 in) and door finder (3, 4 and 6 in)
    /// were inch literals run in the model's unit: Halifax LEVEL 1, a feet model, came out with its 969 mm doors closed and its
    /// four corner pieces tilted (VD, from its own 1.e2k), and every millimetre model had them 25.4 times too tight.
    /// COVERS: Halifax's core composed in inches, millimetres and feet - every joint, every member's kind, storey, section and
    /// joints compared - and, in the inch model, the answer itself: both doors on both sides carry a header, both L corners
    /// meet at their centrelines' crossing, no wall panel is off the drawing's two axes, and no wall became a column.
    /// DOES NOT: a reference-model build, more than this one plan's corners and doors, walls drawn at an angle.
    /// A same-class fault it would NOT catch: an inch literal on a path this plan never reaches (a pier, a curved wall,
    /// the slab reader), or one that happens to give the same topology in all three units at these sizes.
    /// </summary>
    [Fact]
    public void HalifaxsCoreIsTheSameWallNetworkInInchesMillimetresAndFeet()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-units-core-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var inches = Compose(root, "in", 1.0, 1, "in", HalifaxCore);
            var millimetres = Compose(root, "mm", 25.4, 4, "mm", HalifaxCore);
            var feet = Compose(root, "ft", 1.0 / 12.0, 2, "ft", HalifaxCore);

            // The inch model is the answer, checked outright: a differential cannot see a fault every unit shares.
            static (double X, double Y, double Z) P(string at)
            {
                var v = at.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                return (v[0], v[1], v[2]);
            }
            var panels = inches.Placements.Where(p => p.StartsWith("PANEL ", StringComparison.Ordinal))
                .Select(p => p[(p.IndexOf(']') + 2)..].Split(' ').Select(P).ToList()).ToList();
            var headers = panels.Where(js => js.Any(j => Math.Abs(j.Z) > 1e-6)).ToList();
            var walls = panels.Where(js => js.All(j => Math.Abs(j.Z) <= 1e-6)).ToList();
            double X(double mm) => mm / 25.4 - 1400;
            double Y(double mm) => mm / 25.4 - 1000;
            bool Near((double X, double Y, double Z) j, double x, double y) => Math.Abs(j.X - x) <= 0.5 && Math.Abs(j.Y - y) <= 0.5;
            bool HeaderBetween(double xa, double xb, double y) => headers.Any(js => js.Any(j => Near(j, xa, y)) && js.Any(j => Near(j, xb, y)));

            Assert.Equal(0, inches.Columns);                                  // the corner pieces are walls joined into an L, not columns
            foreach (double y in new[] { Y(36053), Y(27901) })
            {
                Assert.True(HeaderBetween(X(42997.5), X(44142), y), $"no header over the 969 mm door at y {y:0.##} in");
                Assert.True(HeaderBetween(X(39174), X(40369), y), $"no header over the 1195 mm door at y {y:0.##} in");
            }
            foreach (var (x, y) in new[] { (X(38421.5), Y(27901)), (X(38422), Y(36053)) })
                Assert.True(walls.Count(js => js.Any(j => Near(j, x, y))) >= 2, $"the L corner at ({x:0.##}, {y:0.##}) in is not one joint of two walls");
            var tilted = walls.Where(js => js.Any(a => js.Any(b => Math.Abs(a.X - b.X) > 0.01 && Math.Abs(a.Y - b.Y) > 0.01))).ToList();
            Assert.True(tilted.Count == 0, $"{tilted.Count} wall panel(s) off the drawing's axes: " + string.Join("; ", tilted.Select(js => string.Join(" ", js.Select(j => $"{j.X:0.##},{j.Y:0.##}")))));

            foreach (var (unit, model) in new[] { ("mm", millimetres), ("ft", feet) })
            {
                Assert.Equal(inches.Columns, model.Columns);
                Assert.Equal(inches.Walls, model.Walls);
                Assert.Equal(inches.Joints, model.Joints);
                Assert.True(inches.JointsInInches.SequenceEqual(model.JointsInInches),
                    $"{unit}: joints differ from the inch model - only in inches: {string.Join(" ", inches.JointsInInches.Except(model.JointsInInches))}; only in {unit}: {string.Join(" ", model.JointsInInches.Except(inches.JointsInInches))}");
                Assert.True(inches.Placements.SequenceEqual(model.Placements),
                    $"{unit}: members differ from the inch model - only in inches: {string.Join(" | ", inches.Placements.Except(model.Placements))}; only in {unit}: {string.Join(" | ", model.Placements.Except(inches.Placements))}");
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
