#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The differential and the render are code in the repo, not scripts in docs (completion plan WP2,
/// 2026-09-11): <see cref="ModelDiff"/> says what a second model lost or gained against a first, and
/// <see cref="ModelRender"/> draws every storey on one sheet.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a model against itself (byte-identical); against a copy shifted by the grid
/// with one column moved, one wall gone, one plate enlarged and one storey added — the frame taken
/// from the grid labels, the moved column lost and gained, the wall lost, the plate moved, the new
/// storey reported; a copy with no grids falling back to the modal displacement and saying GUESS;
/// the SVG carrying one cell per storey that holds something, a circle per column and a polyline
/// per wall. WHAT IT DOES NOT: Edge's PNG (a screenshot; the SVG is the artefact); the banked six
/// (SixSetsBuildAsBankedTests reads them through the same code).
/// </remarks>
public sealed class TheDifferentialAndTheRenderAreCodeTests
{
    private static string E2k(IReadOnlyList<string> storeys, IReadOnlyList<(string Name, double X, double Y, string Storey)> columns,
        IReadOnlyList<(string Name, IReadOnlyList<(double X, double Y)> Ring, string Storey, string Kind)> areas, double gridShift = 0, bool grids = true)
    {
        var l = new List<string> { "$ CONTROLS", "  UNITS  \"LB\"  \"MM\"  \"F\"", "", "$ STORIES - IN SEQUENCE FROM TOP" };
        foreach (var s in storeys) l.Add($"  STORY \"{s}\"  HEIGHT 3000");
        l.Add("  STORY \"Base\"  ELEV 0");
        if (grids)
        {
            l.Add(""); l.Add("$ GRIDS"); l.Add("  GRIDSYSTEM \"G1\"  TYPE \"CARTESIAN\"  BUBBLESIZE 60");
            foreach (var (label, dir, c) in new[] { ("1", "X", 0.0), ("2", "X", 6000.0), ("A", "Y", 0.0), ("B", "Y", 6000.0) })
                l.Add($"  GRID \"G1\"  LABEL \"{label}\"  DIR \"{dir}\"  COORD {(c + gridShift).ToString(CultureInfo.InvariantCulture)} VISIBLE \"Yes\"  BUBBLELOC \"End\"");
        }
        l.Add(""); l.Add("$ POINT COORDINATES");
        int n = 1;
        var pointNames = new Dictionary<(double, double), string>();
        string P(double x, double y)
        {
            if (!pointNames.TryGetValue((x, y), out var name)) { name = $"KP{n++}"; pointNames[(x, y)] = name; l.Add($"  POINT \"{name}\"  {(x + gridShift).ToString(CultureInfo.InvariantCulture)}  {(y + gridShift).ToString(CultureInfo.InvariantCulture)}"); }
            return name;
        }
        var lineConn = new List<string>(); var areaConn = new List<string>(); var assigns = new List<string>();
        foreach (var (name, x, y, storey) in columns) { string p = P(x, y); lineConn.Add($"  LINE \"{name}\"  COLUMN  \"{p}\"  \"{p}\"  1"); assigns.Add($"  LINEASSIGN  \"{name}\"  \"{storey}\"  SECTION \"C1\""); }
        foreach (var (name, ring, storey, kind) in areas)
        {
            var ps = ring.Select(r => P(r.X, r.Y)).ToList();
            areaConn.Add($"  AREA \"{name}\"  {kind}  {ps.Count}  {string.Join("  ", ps.Select(p => $"\"{p}\""))}  " + string.Join("  ", ps.Select(_ => "0")));
            assigns.Add($"  AREAASSIGN  \"{name}\"  \"{storey}\"  SECTION \"S1\"");
        }
        l.Add(""); l.Add("$ LINE CONNECTIVITIES"); l.AddRange(lineConn);
        l.Add(""); l.Add("$ AREA CONNECTIVITIES"); l.AddRange(areaConn);
        l.Add(""); l.Add("$ LINE ASSIGNS"); l.AddRange(assigns.Where(a => a.Contains("LINEASSIGN", StringComparison.Ordinal)));
        l.Add(""); l.Add("$ AREA ASSIGNS"); l.AddRange(assigns.Where(a => a.Contains("AREAASSIGN", StringComparison.Ordinal)));
        l.Add("");
        return string.Join("\n", l);
    }

    private static IReadOnlyList<(double, double)> Box(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    [Fact]
    public void TheDifferentialSaysWhatMovedInTheFirstModelsFrame()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var columns = new List<(string, double, double, string)> { ("KC1", 0, 0, "L2"), ("KC2", 6000, 0, "L2"), ("KC3", 0, 6000, "L2"), ("KC4", 6000, 6000, "L2") };
            var areas = new List<(string, IReadOnlyList<(double, double)>, string, string)>
            {
                ("KW1", Box(0, -100, 6000, 100), "L2", "PANEL"), ("KW2", Box(-100, 0, 100, 6000), "L2", "PANEL"),
                ("KF1", Box(0, 0, 6000, 6000), "L2", "FLOOR"),
            };
            string before = Path.Combine(root, "before.e2k"), after = Path.Combine(root, "after.e2k"), same = Path.Combine(root, "same.e2k"), noGrids = Path.Combine(root, "nogrids.e2k");
            File.WriteAllText(before, E2k(["L2"], columns, areas));
            File.WriteAllText(same, E2k(["L2"], columns, areas));
            // after: the whole model 10 m over (the grid says so), KC2 moved 500 mm, KW2 gone, the plate 2 m wider, a storey L3 with one column
            var after2 = new List<(string, double, double, string)> { ("KC1", 0, 0, "L2"), ("KC2", 6500, 0, "L2"), ("KC3", 0, 6000, "L2"), ("KC4", 6000, 6000, "L2"), ("KC5", 0, 0, "L3") };
            var afterAreas = new List<(string, IReadOnlyList<(double, double)>, string, string)> { ("KW1", Box(0, -100, 6000, 100), "L2", "PANEL"), ("KF1", Box(0, 0, 8000, 6000), "L2", "FLOOR") };
            File.WriteAllText(after, E2k(["L3", "L2"], after2, afterAreas, gridShift: 10000));
            File.WriteAllText(noGrids, E2k(["L3", "L2"], after2, afterAreas, gridShift: 10000, grids: false));

            Assert.True(ModelDiff.Compare(before, same).ByteIdentical);

            var d = ModelDiff.Compare(before, after);
            Assert.False(d.ByteIdentical);
            Assert.Equal((10000, 10000), d.Shift);
            Assert.Contains("grid labels", d.ShiftHow, StringComparison.Ordinal);
            var l2 = Assert.Single(d.Storeys, s => s.Storey == "L2");
            Assert.Single(l2.LostColumns); Assert.Single(l2.GainedColumns);             // KC2 at 6000 lost, at 6500 gained
            Assert.Equal(6500, l2.GainedColumns[0].X);
            Assert.Single(l2.LostWalls); Assert.Empty(l2.GainedWalls);
            Assert.True(l2.PlatesMoved);
            var l3 = Assert.Single(d.Storeys, s => s.Storey == "L3");
            Assert.Single(l3.GainedColumns);
            Assert.Equal("plates moved 1, columns lost 1 / gained 2, walls lost 1 / gained 0", d.OneLine);
            Assert.Contains("positions below are the first model's frame", ModelDiff.Report(d), StringComparison.Ordinal);

            var g = ModelDiff.Compare(before, noGrids);
            Assert.Contains("GUESS", g.ShiftHow, StringComparison.Ordinal);
            Assert.Equal((10000, 10000), g.Shift);                                    // three of four columns agree on it
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheRenderDrawsOneCellPerStoreyWithSomethingOnIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string model = Path.Combine(root, "m.e2k");
            File.WriteAllText(model, E2k(["ROOF", "L2", "L1"],
                [("KC1", 0, 0, "L1"), ("KC2", 6000, 0, "L1"), ("KC3", 0, 0, "L2")],
                [("KW1", Box(0, -100, 6000, 100), "L1", "PANEL"), ("KF1", Box(0, 0, 6000, 6000), "L1", "FLOOR")]));
            string? svg = ModelRender.Svg(model, "a title & more", out int drawn, columns: 2, cellPx: 300);
            Assert.NotNull(svg);
            Assert.Equal(2, drawn);                                                    // ROOF has nothing on it
            Assert.Contains("a title &amp; more", svg, StringComparison.Ordinal);
            Assert.Contains(">L1  1f 1w 2c</text>", svg, StringComparison.Ordinal);
            Assert.Contains(">L2  0f 0w 1c</text>", svg, StringComparison.Ordinal);
            Assert.DoesNotContain(">ROOF", svg, StringComparison.Ordinal);
            Assert.Equal(3, svg.Split("<circle").Length - 1);
            Assert.Equal(1, svg.Split("<polyline").Length - 1);
            Assert.Equal(1, svg.Split("<polygon").Length - 1);
            var (svgPath, _, drawnAgain) = ModelRender.Write(model, Path.Combine(root, "m.png"), "t", png: false);
            Assert.True(File.Exists(svgPath)); Assert.Equal(2, drawnAgain);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Audit F17 and F18 (step 61, 2026-09-13): a wall was its centre and its longest extent, so a wall
    /// turned ninety degrees in place was the same wall; and each member matched ANY partner within
    /// tolerance, so a second copy at the same place hid behind the first's partner. A wall carries its
    /// extent along each axis and every member takes one partner. WHAT THIS COVERS: the turned wall as lost
    /// and gained; the duplicated column as gained; the duplicated wall as gained. WHAT IT DOES NOT: a wall
    /// moved along its own axis by less than 2 units; two members swapping places.
    /// </summary>
    [Fact]
    public void ATurnedWallAndASecondCopyAreSeen()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var columns = new List<(string, double, double, string)> { ("KC1", 0, 0, "L2"), ("KC2", 6000, 0, "L2"), ("KC3", 0, 6000, "L2"), ("KC4", 6000, 6000, "L2") };
            var areas = new List<(string, IReadOnlyList<(double, double)>, string, string)> { ("KW1", Box(-3000, -100, 3000, 100), "L2", "PANEL"), ("KF1", Box(0, 0, 6000, 6000), "L2", "FLOOR") };
            string before = Path.Combine(root, "before.e2k"), turned = Path.Combine(root, "turned.e2k"), doubled = Path.Combine(root, "doubled.e2k");
            File.WriteAllText(before, E2k(["L2"], columns, areas));
            // the wall turned in place: the same centre, the same 6 m extent, along Y now
            File.WriteAllText(turned, E2k(["L2"], columns, [("KW1", Box(-100, -3000, 100, 3000), "L2", "PANEL"), ("KF1", Box(0, 0, 6000, 6000), "L2", "FLOOR")]));
            // a second copy of KC1 a millimetre away and a second copy of the wall
            var doubledColumns = columns.Append(("KC5", 1, 0, "L2")).ToList();
            File.WriteAllText(doubled, E2k(["L2"], doubledColumns, [("KW1", Box(-3000, -100, 3000, 100), "L2", "PANEL"), ("KW2", Box(-3000, -100, 3000, 100), "L2", "PANEL"), ("KF1", Box(0, 0, 6000, 6000), "L2", "FLOOR")]));

            var t = Assert.Single(ModelDiff.Compare(before, turned).Storeys);
            Assert.Single(t.LostWalls); Assert.Single(t.GainedWalls);
            Assert.Empty(t.LostColumns); Assert.Empty(t.GainedColumns);

            var d = Assert.Single(ModelDiff.Compare(before, doubled).Storeys);
            Assert.Empty(d.LostColumns); Assert.Single(d.GainedColumns);
            Assert.Empty(d.LostWalls); Assert.Single(d.GainedWalls);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
