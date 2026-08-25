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
        int Columns, int ReferenceColumns, int Walls, int ReferenceWalls, int AnyStorey, string Report);

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
                        DxfFolder = DxfFolder,
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
        Dictionary<string, List<(DxfPoint A, DxfPoint B)>> Walls);

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
            if (w.Success) wallEnds[w.Groups[1].Value] = (w.Groups[2].Value, w.Groups[3].Value);
        }

        var columns = new Dictionary<string, List<DxfPoint>>(StringComparer.OrdinalIgnoreCase);
        var walls = new Dictionary<string, List<(DxfPoint A, DxfPoint B)>>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            var a = Regex.Match(line, @"^\s*(?:LINE|AREA)ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (!a.Success) continue;

            string name = a.Groups[1].Value, storey = a.Groups[2].Value;
            if (generated != name.StartsWith("K", StringComparison.Ordinal)) continue;

            if (columnAt.TryGetValue(name, out string? at) && points.TryGetValue(at, out var pt))
                Add(columns, storey, pt);
            else if (wallEnds.TryGetValue(name, out var ends)
                     && points.TryGetValue(ends.A, out var s)
                     && points.TryGetValue(ends.B, out var e))
                AddWall(walls, storey, (s, e));
        }

        return new Members(columns, walls);

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

            report.Add($"{storey,-9} {refCol.Count,6} {c,4}   {refWall.Count,7} {w,4}");
        }

        report.Add($"columns {columns}/{refColumns}, walls {walls}/{refWalls}");
        return new Score(columns, refColumns, walls, refWalls, anyStorey, string.Join("\n", report));
    }

    private static int Within(List<DxfPoint> reference, List<DxfPoint> ours, double tolerance)
        => reference.Count(p => ours.Any(q =>
            Math.Abs(p.X - q.X) <= tolerance && Math.Abs(p.Y - q.Y) <= tolerance));
}
