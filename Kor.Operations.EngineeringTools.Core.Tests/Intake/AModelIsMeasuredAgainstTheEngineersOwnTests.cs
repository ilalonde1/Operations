#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A model is measured against the engineer's own model of the job — the yardstick — column by
/// column, both ways, with the frames met by grid name where both name a grid and by column
/// registration where they do not (ModelYardstick, 2026-09-11: 68 of the 81 engineers' models
/// exported from KOR-210 carry no grid lines, and sit at survey coordinates).
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two models of one storey, theirs in inches at a 300 m survey offset with no
/// GRIDS, ours in mm — the frame found from the columns, the support stated (4 of 5: one of ours
/// has no partner), residuals both ways (theirs -> ours: 4 of 6, two of theirs never read); a
/// storey named LEVEL P2 by them and P2 by us meeting; the same pair with grids on both sides met
/// by name instead; a storey prefixed for one building meeting only that building's. WHAT IT DOES NOT: rotation between the frames (translation only); walls and
/// plates; the real 81 exports (the corpus run's ledger has those, one row per job).
/// </remarks>
public sealed class AModelIsMeasuredAgainstTheEngineersOwnTests
{
    private static string E2k(string unit, IReadOnlyList<(string Storey, double Height)> storeys, IReadOnlyList<(string Name, double X, double Y, string Storey)> columns,
        IReadOnlyList<(string Label, string Dir, double Coord)>? grids = null)
    {
        var l = new List<string> { "$ CONTROLS", $"  UNITS  \"LB\"  \"{unit}\"  \"F\"", "", "$ STORIES - IN SEQUENCE FROM TOP" };
        foreach (var (s, h) in storeys) l.Add($"  STORY \"{s}\"  HEIGHT {h.ToString(CultureInfo.InvariantCulture)}");
        l.Add("  STORY \"Base\"  ELEV 0");
        if (grids is not null)
        {
            l.Add(""); l.Add("$ GRIDS"); l.Add("  GRIDSYSTEM \"G1\"  TYPE \"CARTESIAN\"  BUBBLESIZE 60");
            foreach (var (label, dir, c) in grids) l.Add($"  GRID \"G1\"  LABEL \"{label}\"  DIR \"{dir}\"  COORD {c.ToString(CultureInfo.InvariantCulture)} VISIBLE \"Yes\"  BUBBLELOC \"End\"");
        }
        l.Add(""); l.Add("$ POINT COORDINATES");
        foreach (var (n, x, y, _) in columns) l.Add($"  POINT \"P{n}\"  {x.ToString(CultureInfo.InvariantCulture)}  {y.ToString(CultureInfo.InvariantCulture)}");
        l.Add(""); l.Add("$ LINE CONNECTIVITIES");
        foreach (var (n, _, _, _) in columns) l.Add($"  LINE \"{n}\"  COLUMN  \"P{n}\"  \"P{n}\"  1");
        l.Add(""); l.Add("$ LINE ASSIGNS");
        foreach (var (n, _, _, s) in columns) l.Add($"  LINEASSIGN  \"{n}\"  \"{s}\"  SECTION \"C1\"");
        l.Add("");
        return string.Join("\n", l);
    }

    [Fact]
    public void WithoutGridsTheFrameComesFromTheColumnsAndBothDirectionsAreCounted()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-yard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // ours, in mm, on P2: five columns on a 6 m grid; theirs, in inches, on LEVEL P2, 300 m away in
            // survey coordinates: four of ours are there (within 40 mm), one of ours is not, and they have two
            // columns we never read
            double sx = 300000, sy = 450000;
            var ours = new List<(string, double, double, string)> { ("C1", 0, 0, "P2"), ("C2", 6000, 0, "P2"), ("C3", 12000, 0, "P2"), ("C4", 0, 6000, "P2"), ("C5", 6000, 6000, "P2") };
            var theirs = new List<(string, double, double, string)>
            {
                ("E1", (0 + sx) / 25.4, (0 + sy) / 25.4, "LEVEL P2"), ("E2", (6030 + sx) / 25.4, (0 + sy) / 25.4, "LEVEL P2"),
                ("E3", (12000 + sx) / 25.4, (-20 + sy) / 25.4, "LEVEL P2"), ("E4", (0 + sx) / 25.4, (6000 + sy) / 25.4, "LEVEL P2"),
                ("E6", (12000 + sx) / 25.4, (6000 + sy) / 25.4, "LEVEL P2"), ("E7", (18000 + sx) / 25.4, (6000 + sy) / 25.4, "LEVEL P2"),
            };
            string m = Path.Combine(root, "ours.e2k"), y = Path.Combine(root, "theirs.e2k");
            File.WriteAllText(m, E2k("MM", [("P2", 3000)], ours));
            File.WriteAllText(y, E2k("IN", [("LEVEL P2", 118)], theirs));

            var c = ModelYardstick.Compare(m, y);

            Assert.False(c.ShiftFromGrids);
            Assert.NotNull(c.ShiftMm);
            Assert.InRange(c.ShiftMm!.Value.X, sx - 30, sx + 30);
            Assert.InRange(c.ShiftMm.Value.Y, sy - 30, sy + 30);
            var storey = Assert.Single(c.Storeys);                             // P2 met LEVEL P2
            Assert.Equal("LEVEL P2", storey.YardstickStorey);
            Assert.Equal(5, c.OursCompared); Assert.Equal(4, c.OursWithin100); Assert.Equal(4, c.FrameSupport);
            Assert.Equal(6, c.TheirsCompared); Assert.Equal(4, c.TheirsWithin100);   // E6 and E7 are theirs alone
            Assert.Contains(c.Notes, n => n.Contains("column registration", StringComparison.Ordinal) && n.Contains("4 of 5", StringComparison.Ordinal));
            Assert.Contains("4 of 5", ModelYardstick.Summary(c), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WithGridsOnBothSidesTheFrameIsTheirNamesAndSaysSo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-yard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            double sx = 8000, sy = 70000;
            // the same building's second floor under the two routes' spellings: A-L2 and A-LEVEL 2
            var ours = new List<(string, double, double, string)> { ("C1", 0, 0, "A-L2"), ("C2", 6000, 0, "A-L2"), ("C3", 0, 6000, "A-L2") };
            var theirs = new List<(string, double, double, string)> { ("E1", sx, sy, "A-LEVEL 2"), ("E2", 6000 + sx, sy, "A-LEVEL 2"), ("E3", sx, 6000 + sy, "A-LEVEL 2") };
            var gridsOurs = new List<(string, string, double)> { ("1", "X", 0), ("2", "X", 6000), ("A", "Y", 0), ("B", "Y", 6000) };
            var gridsTheirs = new List<(string, string, double)> { ("1", "X", sx), ("2", "X", 6000 + sx), ("A", "Y", sy), ("B", "Y", 6000 + sy) };
            string m = Path.Combine(root, "ours.e2k"), y = Path.Combine(root, "theirs.e2k");
            File.WriteAllText(m, E2k("MM", [("A-L2", 3000)], ours, gridsOurs));
            File.WriteAllText(y, E2k("MM", [("A-LEVEL 2", 3000)], theirs, gridsTheirs));

            var c = ModelYardstick.Compare(m, y);

            Assert.True(c.ShiftFromGrids);
            Assert.Equal(2, c.SharedXLabels); Assert.Equal(2, c.SharedYLabels);
            Assert.Equal(sx, c.ShiftMm!.Value.X, 0.5); Assert.Equal(sy, c.ShiftMm.Value.Y, 0.5);
            Assert.Equal(3, c.OursCompared); Assert.Equal(3, c.OursWithin100); Assert.Equal(0, c.OursMedianMm, 0.5);
            Assert.Equal(3, c.TheirsWithin100);
            Assert.StartsWith("frames matched on 2 X and 2 Y grid labels", ModelYardstick.Summary(c), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StoreyNamesMeetAcrossTheTwoRoutesSpellings()
    {
        Assert.Equal(ModelYardstick.Stripped("A-LEVEL 1"), ModelYardstick.Stripped("A-L1"));
        Assert.Equal("P2", ModelYardstick.Stripped("LEVEL P2"));
        Assert.Equal("P2", ModelYardstick.Stripped("P2"));
        Assert.Equal("L1 MEZZ", ModelYardstick.Stripped("LEVEL 1 MEZZ"));
        Assert.NotEqual(ModelYardstick.Stripped("L1"), ModelYardstick.Stripped("L1 MEZZ"));
        // and a storey named for one building never meets another's, nor the whole job's: our L4 met
        // their C-LEVEL 4 at 33 m on 31168 before this (2026-09-11)
        Assert.Equal("C", ModelYardstick.Building("C-LEVEL 4"));
        Assert.Null(ModelYardstick.Building("L4"));
        Assert.NotEqual(ModelYardstick.Building("L4"), ModelYardstick.Building("C-LEVEL 4"));
    }


    /// <summary>
    /// Step 59 (2026-09-13): run 7 against run 8 found four sets whose models were identical to the member
    /// and whose yardstick verdict had moved (31174-01: 8 of 64 supported, then 5). The frame was the fullest
    /// bin of PAIR votes, a tie to whichever bin our columns voted first - the order the model lists them.
    /// The frame is judged by its support now, over every bin within a vote of the fullest, a tie in support
    /// to the tighter cluster, then to the lower bin. WHAT THIS COVERS: two frames with equal votes and
    /// equal support give the same frame whatever order our columns come in; support beats votes; the
    /// tie is the same correspondence wherever either model sits.
    /// WHAT IT DOES NOT: rotation; a genuine second structure under one job number (support says "weak",
    /// the note says so, and that is all it says).
    /// </summary>
    [Fact]
    public void TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns()
    {
        // theirs: two columns 6 m apart and a stray 30 m out; ours: the same two, and a stray 24 m out.
        // Pair votes: shift 0 gets two (0->0, 6000->6000); shift +6000 gets two (0->6000, 24000->30000). A tie,
        // and each frame supports two of our three readings. Listed stray-first, our stray's pairs vote first
        // and the old MaxBy took +6000; listed stray-last it took 0.
        var theirs = new List<(double X, double Y)> { (0, 0), (6000, 0), (30000, 0) };
        var strayLast = new List<(double X, double Y)> { (0, 0), (6000, 0), (24000, 0) };
        var strayFirst = new List<(double X, double Y)> { (24000, 0), (6000, 0), (0, 0) };

        var a = ModelYardstick.Register([("L1", "L1", strayLast, theirs)]);
        var b = ModelYardstick.Register([("L1", "L1", strayFirst, theirs)]);

        Assert.Equal(a.Shift, b.Shift);
        Assert.Equal(a.Support, b.Support);
        Assert.Equal((0.0, 0.0), a.Shift);                                      // equal support, equal spread: the lower bin
        Assert.Equal(2, a.Support);

        // SUPPORT BEATS VOTES (the second audit's finding 12): four of hers clustered under one of ours give the
        // rival bin four pair votes and one supported column; the true frame has three votes and three
        var trueTheirs = new List<(double X, double Y)> { (0, 0), (6000, 0), (12000, 0), (26950, 0), (26980, 0), (27010, 0), (27040, 0) };
        var trueOurs = new List<(double X, double Y)> { (0, 0), (6000, 0), (12000, 0), (20000, 0) };
        var t = ModelYardstick.Register([("L1", "L1", trueOurs, trueTheirs)]);
        Assert.Equal(3, t.Support);
        Assert.InRange(t.Shift.X, -1, 1);

        // THE TIE DOES NOT DEPEND ON WHERE EITHER MODEL SITS (finding 11): one of ours between two of hers, then
        // the same one of ours 1,500 mm over - the same correspondence both times, not the smaller move
        var two = new List<(double X, double Y)> { (-1000, 0), (1000, 0) };
        var at0 = ModelYardstick.Register([("L1", "L1", [(0.0, 0.0)], two)]);
        var at1500 = ModelYardstick.Register([("L1", "L1", [(1500.0, 0.0)], two)]);
        Assert.Equal(at0.Shift.X - 0, at1500.Shift.X - (-1500), 1e-9);          // the displacement to the same column of hers
    }

    /// <summary>
    /// WHAT KIND OF ERROR A RESIDUAL IS (2026-09-14). Two storeys under one frame: on L2 every column of ours
    /// sits 300 mm west of hers - a rigid part of (300, 0) that a placement fix would remove, after which the
    /// median is nil; on L3 they scatter about hers by 300 mm in four directions - a rigid part near zero
    /// and a median that stays. And against the plans' grid: hers stand on the intersections, ours do not.
    /// WHAT THIS DOES NOT COVER: a rotation between the models (the rigid part is a translation); the
    /// 600 mm window that keeps an unmatched column out of the median.
    /// </summary>
    [Fact]
    public void AStoreysResidualIsReadAsItsRigidPartAndWhatIsLeftAndEachModelIsPlacedAgainstTheGrid()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-yard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // L1 anchors the frame at zero: eight columns exactly on hers (more votes than L2's four 300 mm over
            // and L3's one - the frame is the fullest bin, and a fixture that lets L2 outvote L1 moves it)
            var ours = new List<(string, double, double, string)>();
            var theirs = new List<(string, double, double, string)>();
            int n = 0;
            foreach (var (x, y) in new[] { (0.0, 0.0), (6000.0, 0.0), (0.0, 6000.0), (6000.0, 6000.0), (12000.0, 0.0), (12000.0, 6000.0), (0.0, 12000.0), (6000.0, 12000.0) })
            {
                ours.Add(($"C{++n}", x, y, "L1")); theirs.Add(($"E{n}", x, y, "L1"));
            }
            foreach (var (x, y) in new[] { (0.0, 0.0), (6000.0, 0.0), (0.0, 6000.0), (6000.0, 6000.0) })
            {
                ours.Add(($"C{++n}", x - 300, y, "L2")); theirs.Add(($"E{n}", x, y, "L2"));               // ours 300 west, every one
            }
            var scatter = new[] { (300.0, 0.0), (-300.0, 0.0), (0.0, 300.0), (0.0, -300.0) };
            int k = 0;
            foreach (var (x, y) in new[] { (0.0, 0.0), (6000.0, 0.0), (0.0, 6000.0), (6000.0, 6000.0) })
            {
                var (dx, dy) = scatter[k++];
                ours.Add(($"C{++n}", x + dx, y + dy, "L3")); theirs.Add(($"E{n}", x, y, "L3"));            // ours about hers, four ways
            }
            var grids = new List<(string, string, double)> { ("1", "X", 0), ("2", "X", 6000), ("3", "X", 12000), ("A", "Y", 0), ("B", "Y", 6000), ("C", "Y", 12000) };
            string m = Path.Combine(root, "ours.e2k"), y2 = Path.Combine(root, "theirs.e2k");
            File.WriteAllText(m, E2k("MM", [("L3", 3000), ("L2", 3000), ("L1", 3000)], ours, grids));
            File.WriteAllText(y2, E2k("MM", [("L3", 3000), ("L2", 3000), ("L1", 3000)], theirs));   // her export carries no grid lines, as 85 of 104 do not

            var c = ModelYardstick.Compare(m, y2);

            Assert.False(c.ShiftFromGrids);
            var l2 = Assert.Single(c.Storeys, f => f.Storey == "L2");
            Assert.Equal(300, l2.RigidMm.X, 0.5); Assert.Equal(0, l2.RigidMm.Y, 0.5);
            Assert.Equal(300, l2.MedianMm, 0.5); Assert.Equal(0, l2.MedianAfterRigidMm, 0.5);
            Assert.Equal(0, l2.OursWithin100); Assert.Equal(4, l2.OursWithin100AfterRigid);
            var l3 = Assert.Single(c.Storeys, f => f.Storey == "L3");
            Assert.Equal(0, l3.RigidMm.X, 0.5); Assert.Equal(0, l3.RigidMm.Y, 0.5);
            Assert.Equal(300, l3.MedianAfterRigidMm, 0.5);                                          // the scatter stays
            // against the plans' grid (ours): hers all on an intersection, ours only L1's four
            Assert.Equal(16, c.OnOurGrid.TheirsN); Assert.Equal(16, c.OnOurGrid.TheirsOnBoth);
            Assert.Equal(16, c.OnOurGrid.OursN); Assert.Equal(8, c.OnOurGrid.OursOnBoth);
            Assert.Contains("rigid (  300,     0) ->     0 mm 100%", ModelYardstick.Summary(c), StringComparison.Ordinal);
            Assert.Contains("hers 16 / 16 / 16 of 16 (100%)", ModelYardstick.Summary(c), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
