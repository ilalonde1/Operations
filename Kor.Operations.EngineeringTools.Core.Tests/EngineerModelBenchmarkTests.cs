using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// How much of an engineer's own model this tool reproduces, storey by storey, on a building it
/// was never tuned against.
///
/// Everything else in this suite asks whether the model agrees with the drawings or with itself.
/// Neither can say whether the answer is RIGHT, because both are the tool marking its own work.
/// 31065 can: an engineer built that model, the drawings it was built from are on the share, and
/// nothing in this tool has ever been fitted to it.
///
/// Without this, a change is judged by whether counts moved, and three separate wrong turns on
/// 24 August came from exactly that -- including one where a fix was measured against a model
/// built three days earlier by different code, which flattered it by twelve points. A benchmark
/// re-derived by hand each time is not a benchmark.
///
/// RATCHETS, like ModelCoverageTests: these may only ever go up.
///
/// The reference model is used for its STOREY LIST ONLY. The tool skips a member the engineer
/// already has -- dxf.already-modelled-tolerance, 6 in -- which is right when it is filling gaps
/// in a real model and useless here: pointed at a finished model it builds the residue, 70 walls
/// against the 904 she drew, and scores itself on that. Turning the flag off in the request does
/// not help, because KorStandards overrides it and should. So the storeys are lifted out of her
/// model into a levels list and the model is built from that, with no members to skip.
///
/// Skipped when the project share is unreachable.
/// </summary>
/// <remarks>
/// SLOW: builds 31065 from its real drawings over SMB and reads a 1.4 MB reference model.
/// </remarks>
[Trait("Speed", "Slow")]
public class EngineerModelBenchmarkTests
{
    private readonly ITestOutputHelper _out;

    public EngineerModelBenchmarkTests(ITestOutputHelper output) => _out = output;

    private const string Job =
        @"\\Kor-fs01\Projects\Projects\03 Residential\31065-01 (5350 5430 Heather Street Vancouver)";

    private const string DxfFolder = Job + @"\02 Engineering\CAD Export\DXF Files\2025-06-18";

    private const string EngineerModel =
        Job + @"\02 Engineering\02 Lateral Design\01 ETABS Models\31065-01 Wind ULS_SG_Both Towers.e2k";

    /// <summary>
    /// Columns landing within 6 in of one of the engineer's, ON THE SAME STOREY. Storey matters:
    /// a whole-model best fit puts 1,077 of her 1,097 columns within 6 in of something we built
    /// and says nothing at all, because many of hers match the same few of ours.
    ///
    /// 929 of 1,097 (85%). It was 798 (73%) with members assigned to the storey whose sheet they
    /// were read from, before the engineer's rule that solid linework on the plan for storey N
    /// rises to N+1.
    /// </summary>
    private const int ColumnFloor = 929;

    /// <summary>
    /// The engineer's wall panels with one of ours running along them, within 12 in, on the same
    /// storey. 806 of 904, 89%.
    ///
    /// It read 63% until the measurement was fixed, and the gap was entirely in the ruler: her
    /// panel's midpoint was compared to OUR panel's midpoint, and she models one drawn wall as
    /// several stacked panels, so a single panel of ours lying exactly along her wall matched only
    /// whichever of hers shared its midpoint. Every other panel of hers on that same wall counted
    /// as missed. Nothing about the model changed to go from 63 to 89; the question did.
    ///
    /// Worth remembering before the next "the walls need work": measure what you think you are
    /// measuring first.
    /// </summary>
    private const int WallFloor = 806;

    /// <summary>
    /// Storeys whose floor area is within a fifth of the engineer's. A ratchet: up only.
    ///
    /// 19 of 23, up from 16. Two changes got it there: slab outlines now close by joining a
    /// chain's two loose ends, and a chain that will not close at the ordinary 6 in bridge is
    /// offered the interruption width, dxf.flood-fill-bridge at 36 in, which is what a slab edge
    /// cut by crossing linework actually carries. The second gave the YMCA mezzanine the second of
    /// the three slabs the engineer says are there.
    ///
    /// That change was nearly given back over a fear that measured out to nothing: 31138 appeared
    /// to go from 13 floor plates to 50. Those were different builds -- 13 is the residue added to
    /// a model the engineer had already built by hand, 50 is the whole building from a bare storey
    /// list -- and scored against HER model the two settings are identical, 11 of 27 storeys either
    /// way, 168,717 against 170,274 sq ft of a real 327,220. 31138 is not over-reading floors; it
    /// is finding half of them.
    /// </summary>
    // WALKED BACK from 19 to 16 on 25 August, deliberately.
    //
    // The three points came from chain-closing and a wider chaining pass, and the engineer opened
    // the model those produced: "on several levels (9, 3, mezz, 1) he inverted slab and opening".
    // Floor AREA cannot see a donut, which is how all three scored as gains. They are withdrawn
    // and this number goes with them. A ratchet exists to catch a regression nobody meant; it is
    // not a reason to keep shipping geometry an engineer has rejected.
    private const int FloorFloor = 16;

    private const double ColumnTolerance = 6.0;
    private const double WallTolerance = 12.0;

    [Fact]
    public void ColumnsLandWhereTheEngineerPutThem()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        _out.WriteLine(score.Report);
        Assert.True(score.Columns >= ColumnFloor,
            $"Columns on their own storey within {ColumnTolerance:0}in fell to {score.Columns}/{score.ReferenceColumns}; " +
            $"the ratchet is {ColumnFloor}. This number may only go up.\n{score.Report}");
    }

    [Fact]
    public void WallsLandWhereTheEngineerPutThem()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        Assert.True(score.Walls >= WallFloor,
            $"Walls on their own storey within {WallTolerance:0}in fell to {score.Walls}/{score.ReferenceWalls}; " +
            $"the ratchet is {WallFloor}. This number may only go up.\n{score.Report}");
    }

    /// <summary>
    /// Floor plates the size the engineer made them, storey by storey, within a fifth.
    ///
    /// Nothing else in this file can see a plate. Walls and columns are unmoved by a change to how
    /// slab outlines are read, so a change that doubles a floor scores identically on both and
    /// looks free -- which is how C-LEVEL 3 on 31168 nearly shipped at 22,676 sq ft against the
    /// 12,830 it had, on the one storey the engineer had checked herself.
    ///
    /// The guard that was going to catch it would not have. The plan was "a closed outline may not
    /// enclose more ground than the structure standing on that storey", and that sheet's own walls
    /// and columns span the same 250 ft the wrong plate did, because the tower columns pass through
    /// it. Measuring the sheet before writing the rule is what showed that; it would otherwise have
    /// shipped as a guard that guards nothing.
    ///
    /// A fifth is loose on purpose. A plate traced off a raster has a stepped edge, and slabs
    /// cantilever past the structure by amounts a drawing states and this tool does not read.
    /// Being out by a FACTOR is the fault worth refusing.
    /// </summary>
    [Fact]
    public void FloorPlatesAreTheSizeTheEngineerMadeThem()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        _out.WriteLine(score.Report);
        Assert.True(score.FloorsWithin20Percent >= FloorFloor,
            $"Storeys whose floor area is within 20% of the engineer's fell to " +
            $"{score.FloorsWithin20Percent}/{score.ReferenceFloors}; the ratchet is {FloorFloor}. " +
            $"This number may only go up.\n{score.Report}");
    }

    /// <summary>
    /// Columns the size and shape the engineer made them, and walls her thickness.
    ///
    /// Position is not the whole job. A column in exactly the right place at the wrong size, or a
    /// 12 in wall where she has 24, is a member she retypes — and nothing else in this suite
    /// compares either against HER model. ModelCoverageTests checks sizes against the DRAWINGS,
    /// which is the tool marking its own reading of them.
    ///
    /// 133 of 139 columns and 58 of 62 walls, measured only where both models declare a section.
    /// These were unknown until 25 August and turned out to be the strong part.
    /// </summary>
    [Fact]
    public void MembersAreTheSizeTheEngineerMadeThem()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        _out.WriteLine(score.Report);
        Assert.True(score.SizedRight >= 133,
            $"Columns matching her size fell to {score.SizedRight}/{score.SizedCompared}; the ratchet is 133.");
        // 60 of 66, tightened from 58 on 25 August. The old figure was left behind by a change that
        // widened the comparison — 62 of her walls were being checked when it was set, 66 now — so
        // it had gone slack by two and would have let a real regression through. Read twice, on the
        // code before and after this session's change, identical both times.
        Assert.True(score.ThickRight >= 60,
            $"Walls matching her thickness fell to {score.ThickRight}/{score.ThickCompared}; the ratchet is 60.");
    }

    /// <summary>
    /// Openings — shafts, stairs, every penetration cut from a slab.
    ///
    /// THE DENOMINATOR WAS WRONG UNTIL 25 AUGUST, and it made this look four times worse than it
    /// is. "8 against her 359" was read as the tool finding six percent of the holes an engineer
    /// cuts, and it was the top item on the gap list on that basis. Her 359 are not 359 holes.
    /// Measured off her own model, 176 of them (49%) are under six inches across and come to
    /// 452 sq ft in the whole building; only 53 are at least 12in across and at least 10 sq ft.
    /// The rest is slab trimmed back off a wall face — see IsAHole, which has the geometry.
    ///
    /// So the score is 8 of 53, not 8 of 359. Still the largest gap in the tool, and still
    /// invisible to everything else here — nothing else in this file can see an opening, so the
    /// model looks healthy on every other number while missing shafts. But chasing 359 would mean
    /// inventing three hundred one-inch slivers, which would be worse than missing them.
    ///
    /// The reason for the real gap is structural rather than a tolerance. An opening is made from
    /// a closed ring lying inside a slab, so this tool finds the ones drawn on a slab-edge layer.
    /// Hers include every elevator and stair shaft, which are bounded by WALLS — a closed wall
    /// enclosure with no floor inside it — and nothing here reads those as holes. Reading every
    /// wall enclosure as a shaft was tried on 24 August and rejected by the engineer the next
    /// morning, because a wall enclosure is a ROOM at least as often as it is a shaft.
    ///
    /// A ratchet at the measured value, not a target. It exists so the next change is scored
    /// against it, and so this cannot quietly get worse while somebody works on floors.
    /// </summary>
    [Fact]
    public void OpeningsAreCutWhereTheEngineerCutsThem()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        _out.WriteLine(score.Report);
        Assert.True(score.Openings >= OpeningFloor,
            $"Openings fell to {score.Openings} against the engineer's {score.ReferenceOpenings} holes; " +
            $"the ratchet is {OpeningFloor}. This number may only go up.\n{score.Report}");
    }

    /// <summary>
    /// Holes this tool cuts where the engineer cut one. Measured, not chosen — and stated as one
    /// name so the assertion and the message it prints can never drift apart, which they had:
    /// this test asserted 8 while telling whoever read the failure that the ratchet was 22.
    /// </summary>
    private const int OpeningFloor = 8;

    /// <summary>
    /// The whole-model best fit, kept as a guard rather than as a score. It is the figure that got
    /// quoted as evidence this tool worked -- "1,077 of 1,097 columns within 6 inches" -- and it is
    /// storey-agnostic, so it stayed near-perfect through the entire period when every member was
    /// a storey too low. A number that cannot fail is not a test; this one asserts only that the
    /// building has not moved wholesale, and the storey-wise ratchets above do the real work.
    /// </summary>
    [Fact]
    public void TheBuildingIsInTheRightPlaceOnPlan()
    {
        var score = ScoreOrSkip();
        if (score is null) return;

        Assert.True(score.AnyStorey >= 940,
            $"Only {score.AnyStorey}/{score.ReferenceColumns} of the engineer's columns have one of ours within " +
            $"{ColumnTolerance:0}in anywhere in the model. The building itself has shifted.");
    }

    private sealed record Score(
        int Columns, int ReferenceColumns, int Walls, int ReferenceWalls, int AnyStorey,
        int FloorsWithin20Percent, int ReferenceFloors,
        int SizedRight, int SizedCompared, int ThickRight, int ThickCompared,
        int Openings, int ReferenceOpenings, string Report);

    private static Score? _cached;
    private static readonly object Gate = new();

    private Score? ScoreOrSkip()
    {
        lock (Gate)
        {
            if (_cached is not null) return _cached;

            // Say why, rather than passing in under a millisecond and looking like a green test.
            // A benchmark that quietly skips is worse than no benchmark: it reports success for a
            // measurement it never took.
            if (!Directory.Exists(DxfFolder))
            {
                _out.WriteLine($"SKIPPED: drawings not reachable at {DxfFolder}");
                return null;
            }
            if (!File.Exists(EngineerModel))
            {
                _out.WriteLine($"SKIPPED: reference model not reachable at {EngineerModel}");
                return null;
            }

            string output = Path.Combine(Path.GetTempPath(), $"kor-benchmark-{Guid.NewGuid():N}.e2k");
            try
            {
                string levels = Path.Combine(Path.GetTempPath(), $"kor-benchmark-{Guid.NewGuid():N}.csv");
                File.WriteAllLines(levels, StoreyList(File.ReadAllLines(EngineerModel)));

                try
                {
                    DxfToEtabsService.Run(new DxfToEtabsRequest
                    {
                        RequireRuleSettings = true,
                        DxfFolder = DrawingCache.Local(DxfFolder),
                        LevelsFile = levels,
                        OutputE2k = output,
                    });
                }
                finally
                {
                    if (File.Exists(levels)) File.Delete(levels);
                }

                var lines = File.ReadAllLines(output);
                var engineer = Read(File.ReadAllLines(EngineerModel), generated: false);
                var ours = Read(lines, generated: true);
                _cached = Compare(engineer, ours);
                return _cached;
            }
            finally
            {
                if (File.Exists(output)) File.Delete(output);
            }
        }
    }

    /// <summary>
    /// The engineer's storey names and elevations, as a levels list. ETABS writes storeys from the
    /// top down, each carrying the height of the storey below it, and the base carrying an
    /// absolute elevation -- so the elevations are accumulated upward from the base.
    /// </summary>
    private static IEnumerable<string> StoreyList(string[] lines)
    {
        var heights = new List<(string Name, double Height)>();
        double baseElevation = 0;
        bool inSection = false;

        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("$", StringComparison.Ordinal))
            {
                inSection = line.Contains("STORIES", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;

            var elev = Regex.Match(line, @"^\s*STORY\s+""([^""]+)""\s+ELEV\s+(-?[\d.]+)");
            if (elev.Success) { baseElevation = double.Parse(elev.Groups[2].Value); continue; }

            var story = Regex.Match(line, @"^\s*STORY\s+""([^""]+)""\s+HEIGHT\s+(-?[\d.]+)");
            if (story.Success) heights.Add((story.Groups[1].Value, double.Parse(story.Groups[2].Value)));
        }

        yield return "Level,Elevation";

        double at = baseElevation;
        foreach (var (name, height) in Enumerable.Reverse(heights))
        {
            at += height;
            yield return $"{name},{at.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    private sealed record Members(
        Dictionary<string, List<DxfPoint>> Columns,
        Dictionary<string, List<(DxfPoint A, DxfPoint B)>> Walls,
        Dictionary<string, double> FloorArea,
        List<(DxfPoint At, double Long, double Short, bool Round)> ColumnSizes,
        List<(DxfPoint At, double Thickness)> WallThicknesses,
        int Openings,
        int FlaggedOpenings);

    /// <summary>
    /// Columns as their plan point and walls as their midpoint, per storey. "K" is the prefix this
    /// tool stamps on everything it creates: without it the engineer's own objects, carried through
    /// into the output, would be matched against themselves and every score would read 100%.
    /// </summary>
    private static Members Read(string[] lines, bool generated)
    {
        var points = new Dictionary<string, DxfPoint>(StringComparer.OrdinalIgnoreCase);
        var columnAt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wallEnds = new Dictionary<string, (string A, string B)>(StringComparer.OrdinalIgnoreCase);
        var floorJoints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var openingJoints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Section tables: what a column's size is, and how thick a wall is. Compared member by
        // member, these are the properties an engineer would otherwise retype.
        var frame = new Dictionary<string, (double Long, double Short, bool Round)>(StringComparer.OrdinalIgnoreCase);
        var shell = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var sectionOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int openings = 0, flagged = 0;

        foreach (string line in lines)
        {
            var p = Regex.Match(line, @"^\s*POINT\s+""([^""]+)""\s+(-?[\d.]+)\s+(-?[\d.]+)");
            if (p.Success)
            {
                points[p.Groups[1].Value] = new DxfPoint(
                    double.Parse(p.Groups[2].Value), double.Parse(p.Groups[3].Value));
                continue;
            }

            var c = Regex.Match(line, @"^\s*LINE\s+""([^""]+)""\s+COLUMN\s+""([^""]+)""");
            if (c.Success) { columnAt[c.Groups[1].Value] = c.Groups[2].Value; continue; }

            var w = Regex.Match(line, @"^\s*AREA\s+""([^""]+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (w.Success) { wallEnds[w.Groups[1].Value] = (w.Groups[2].Value, w.Groups[3].Value); continue; }

            var fs = Regex.Match(line, @"^\s*FRAMESECTION\s+""(.+?)""\s+.*?SHAPE\s+""([^""]+)""");
            if (fs.Success)
            {
                var d = Regex.Match(line, @"\sD\s+([\d.]+)");
                var b2 = Regex.Match(line, @"\sB\s+([\d.]+)");
                if (d.Success)
                {
                    double dv = double.Parse(d.Groups[1].Value);
                    double bv = b2.Success ? double.Parse(b2.Groups[1].Value) : 0.0;
                    frame[fs.Groups[1].Value] = (Math.Max(dv, bv), Math.Min(dv, bv),
                        fs.Groups[2].Value.Contains("Circle", StringComparison.OrdinalIgnoreCase));
                }
                continue;
            }

            var sp = Regex.Match(line, @"^\s*SHELLPROP\s+""([^""]+)""\s+.*?WALLTHICKNESS\s+([\d.]+)");
            if (sp.Success) { shell[sp.Groups[1].Value] = double.Parse(sp.Groups[2].Value); continue; }

            var f = Regex.Match(line, @"^\s*AREA\s+""([^""]+)""\s+FLOOR\s+(\d+)\s+(.*)$");
            if (f.Success)
            {
                floorJoints[f.Groups[1].Value] = Regex.Matches(f.Groups[3].Value, @"""([^""]+)""")
                    .Select(x => x.Groups[1].Value)
                    .Take(int.Parse(f.Groups[2].Value))
                    .ToList();
                continue;
            }

            // An opening's own ring: AREA "A1" AREA 4 "1" "2" "3" "4" 0 0 0 0. Without this the
            // only thing known about an opening was that it existed, which is how a count of 359
            // stood as the target for two days -- see how it is filtered below.
            var op = Regex.Match(line, @"^\s*AREA\s+""([^""]+)""\s+AREA\s+(\d+)\s+(.*)$");
            if (op.Success)
                openingJoints[op.Groups[1].Value] = Regex.Matches(op.Groups[3].Value, @"""([^""]+)""")
                    .Select(x => x.Groups[1].Value)
                    .Take(int.Parse(op.Groups[2].Value))
                    .ToList();
        }

        var columns = new Dictionary<string, List<DxfPoint>>(StringComparer.OrdinalIgnoreCase);
        var walls = new Dictionary<string, List<(DxfPoint A, DxfPoint B)>>(StringComparer.OrdinalIgnoreCase);
        var floorArea = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            var a = Regex.Match(line, @"^\s*(?:LINE|AREA)ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (!a.Success) continue;

            string name = a.Groups[1].Value, storey = a.Groups[2].Value;
            if (generated != name.StartsWith("K", StringComparison.Ordinal)) continue;

            var sec = Regex.Match(line, @"SECTION\s+""(.+?)""");
            if (sec.Success) sectionOf.TryAdd(name, sec.Groups[1].Value);
            if (line.Contains("OPENING \"Yes\"", StringComparison.OrdinalIgnoreCase))
            {
                flagged++;
                if (IsAHole(name, openingJoints, points)) openings++;
            }

            if (columnAt.TryGetValue(name, out string? at) && points.TryGetValue(at, out var pt))
                Add(columns, storey, pt);
            else if (wallEnds.TryGetValue(name, out var ends)
                     && points.TryGetValue(ends.A, out var s)
                     && points.TryGetValue(ends.B, out var e))
                AddWall(walls, storey, (s, e));
            else if (floorJoints.TryGetValue(name, out var ring))
            {
                double area = Shoelace(ring.Where(points.ContainsKey).Select(x => points[x]).ToList());
                floorArea[storey] = floorArea.TryGetValue(storey, out double had) ? had + area : area;
            }
        }

        var columnSizes = new List<(DxfPoint, double, double, bool)>();
        foreach (var (name, at) in columnAt)
        {
            if (generated != name.StartsWith("K", StringComparison.Ordinal)) continue;
            if (!points.TryGetValue(at, out var where)) continue;
            if (!sectionOf.TryGetValue(name, out string? section)) continue;
            if (!frame.TryGetValue(section, out var box)) continue;
            columnSizes.Add((where, box.Long, box.Short, box.Round));
        }

        var wallThicknesses = new List<(DxfPoint, double)>();
        foreach (var (name, ends) in wallEnds)
        {
            if (generated != name.StartsWith("K", StringComparison.Ordinal)) continue;
            if (!points.TryGetValue(ends.A, out var s1) || !points.TryGetValue(ends.B, out var e1)) continue;
            if (!sectionOf.TryGetValue(name, out string? section)) continue;
            if (!shell.TryGetValue(section, out double t)) continue;
            wallThicknesses.Add((new DxfPoint((s1.X + e1.X) / 2, (s1.Y + e1.Y) / 2), t));
        }

        return new Members(columns, walls, floorArea, columnSizes, wallThicknesses, openings, flagged);

        static void Add(Dictionary<string, List<DxfPoint>> into, string storey, DxfPoint at)
        {
            if (!into.TryGetValue(storey, out var list)) into[storey] = list = new List<DxfPoint>();
            list.Add(at);
        }

        static void AddWall(Dictionary<string, List<(DxfPoint, DxfPoint)>> into, string storey,
            (DxfPoint, DxfPoint) run)
        {
            if (!into.TryGetValue(storey, out var list)) into[storey] = list = new List<(DxfPoint, DxfPoint)>();
            list.Add(run);
        }
    }

    /// <summary>
    /// A wall is a RUN, not a point.
    ///
    /// This compared her panel's midpoint to ours and scored 63%, which flattered nothing and
    /// measured nothing: she models one drawn wall as several stacked panels, so a single panel of
    /// ours lying exactly along her wall matches only whichever of hers happens to share its
    /// midpoint, and every other panel of hers on that same wall counts as missed. The wall is in
    /// the right place; the ruler was wrong.
    ///
    /// So the question asked here is the one an engineer would ask looking at the two models: does
    /// a wall of mine run along this wall of hers? Her panel's midpoint is measured to the nearest
    /// point on our panel's line, not to our midpoint.
    /// </summary>
    /// <summary>Signed area of a ring, in square inches, unsigned.</summary>
    private static double Shoelace(IReadOnlyList<DxfPoint> ring)
    {
        if (ring.Count < 3) return 0;
        double sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2.0;
    }

    private static double DistanceToRun(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double qx = a.X + t * dx, qy = a.Y + t * dy;
        return Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
    }

    private static Score Compare(Members engineer, Members ours)
    {
        int columns = 0, refColumns = 0, walls = 0, refWalls = 0, anyStorey = 0;
        int floorsClose = 0, refFloors = 0;
        var report = new List<string> { "storey    refCol  hit   refWall  hit" };

        var everyOurColumn = ours.Columns.Values.SelectMany(x => x).ToList();

        foreach (var storey in engineer.Columns.Keys.Concat(engineer.Walls.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var refCol = engineer.Columns.TryGetValue(storey, out var rc) ? rc : new List<DxfPoint>();
            var refWall = engineer.Walls.TryGetValue(storey, out var rw) ? rw : new List<(DxfPoint A, DxfPoint B)>();
            var ourCol = ours.Columns.TryGetValue(storey, out var oc) ? oc : new List<DxfPoint>();
            var ourWall = ours.Walls.TryGetValue(storey, out var ow) ? ow : new List<(DxfPoint A, DxfPoint B)>();

            int c = Within(refCol, ourCol, ColumnTolerance);
            int w = refWall.Count(r => ourWall.Any(o =>
                DistanceToRun(new DxfPoint((r.A.X + r.B.X) / 2, (r.A.Y + r.B.Y) / 2), o.A, o.B) <= WallTolerance));

            columns += c; refColumns += refCol.Count;
            walls += w; refWalls += refWall.Count;
            anyStorey += Within(refCol, everyOurColumn, ColumnTolerance);

            // Floor area, which is what a slab-reading change moves and what nothing else here
            // can see. Walls and columns are untouched by it, so a change that doubles a floor
            // scores identically on both and looks free.
            double refArea = engineer.FloorArea.TryGetValue(storey, out double ra) ? ra : 0;
            double ourArea = ours.FloorArea.TryGetValue(storey, out double oa) ? oa : 0;
            string floorNote = string.Empty;
            if (refArea > 0)
            {
                refFloors++;
                if (Math.Abs(ourArea - refArea) <= refArea * 0.20) floorsClose++;
                floorNote = $"   floor {refArea / 144,8:N0} vs {ourArea / 144,8:N0} sq ft";
            }

            report.Add($"{storey,-9} {refCol.Count,6} {c,4}   {refWall.Count,7} {w,4}{floorNote}");
        }

        // Column size, wall thickness and openings: the properties an engineer retypes if they
        // are wrong, and which position alone cannot see. Compared only where BOTH models declare
        // one -- a member with no section says nothing about whether this tool sized it right.
        int sized = 0, sizedRight = 0;
        foreach (var (at, wide, narrow, round) in engineer.ColumnSizes)
        {
            var near = ours.ColumnSizes
                .Where(o => Math.Abs(o.At.X - at.X) <= ColumnTolerance && Math.Abs(o.At.Y - at.Y) <= ColumnTolerance)
                .ToList();
            if (near.Count == 0) continue;
            sized++;
            if (near.Any(o => Math.Abs(o.Long - wide) <= 2 && Math.Abs(o.Short - narrow) <= 2)) sizedRight++;
        }

        int thick = 0, thickRight = 0;
        foreach (var (at, t) in engineer.WallThicknesses)
        {
            var near = ours.WallThicknesses
                .Where(o => Math.Abs(o.At.X - at.X) <= WallTolerance && Math.Abs(o.At.Y - at.Y) <= WallTolerance)
                .ToList();
            if (near.Count == 0) continue;
            thick++;
            if (near.Any(o => Math.Abs(o.Thickness - t) <= 1.0)) thickRight++;
        }

        report.Add($"columns {columns}/{refColumns}, walls {walls}/{refWalls}, " +
                   $"floors within 20% {floorsClose}/{refFloors}, " +
                   $"column size {sizedRight}/{sized}, wall thickness {thickRight}/{thick}, " +
                   $"openings {ours.Openings} of her {engineer.Openings} " +
                   $"(holes; she flags {engineer.FlaggedOpenings} in all, the rest slab trim)");

        return new Score(columns, refColumns, walls, refWalls, anyStorey, floorsClose, refFloors,
            sizedRight, sized, thickRight, thick, ours.Openings, engineer.Openings,
            string.Join("\n", report));
    }

    /// <summary>
    /// Whether an area flagged OPENING "Yes" is a HOLE THROUGH A FLOOR, or a sliver of slab
    /// trimmed off where it met a wall.
    ///
    /// Both are legitimate, and an engineer writes far more of the second than the first. On
    /// 31065 she flags 359 openings. Measured off her own model:
    ///
    ///     176 of the 359 (49%) are UNDER SIX INCHES across, and come to 452 sq ft in total
    ///      53 of the 359 (15%) are at least 12in across and at least 10 sq ft
    ///
    /// A typical tower floor of hers carries sixteen. Two are shafts -- 107 and 103 sq ft, one
    /// per tower core. The other fourteen are hairlines: 1-3in wide, 175in long, lying along the
    /// core walls. Each sliver's centre sits 3.7-11.4in from one of her wall centrelines, which
    /// is a wall face at her thicknesses; the two real shafts sit 41-42in away, out in the middle
    /// of the core where a lift goes. They are the slab edge trimmed back off the wall, not
    /// penetrations, and nothing should be trying to reproduce them from a drawing.
    ///
    /// So "openings 22 of her 359" was never six percent of the holes in this building. The
    /// denominator was mostly slab trim. Filtered to holes, the same models score against 53.
    /// The filter is applied to OUR openings by the same rule, so it cannot flatter us: a sliver
    /// this tool cut would not count either.
    /// </summary>
    private static bool IsAHole(
        string name,
        Dictionary<string, List<string>> openingJoints,
        Dictionary<string, DxfPoint> points)
    {
        if (!openingJoints.TryGetValue(name, out var ring)) return false;

        var ring2 = ring.Where(points.ContainsKey).Select(x => points[x]).ToList();
        if (ring2.Count < 3) return false;

        double wide = ring2.Max(p => p.X) - ring2.Min(p => p.X);
        double tall = ring2.Max(p => p.Y) - ring2.Min(p => p.Y);

        return Math.Min(wide, tall) >= HoleNarrowest && Shoelace(ring2) >= HoleSmallest;
    }

    /// <summary>Narrowest a penetration may be, in inches. Below this it is slab trim.</summary>
    private const double HoleNarrowest = 12.0;

    /// <summary>Smallest a penetration may be, in square inches — 10 sq ft.</summary>
    private const double HoleSmallest = 10.0 * 144.0;

    private static int Within(List<DxfPoint> reference, List<DxfPoint> ours, double tolerance)
        => reference.Count(p => ours.Any(q =>
            Math.Abs(p.X - q.X) <= tolerance && Math.Abs(p.Y - q.Y) <= tolerance));
}
