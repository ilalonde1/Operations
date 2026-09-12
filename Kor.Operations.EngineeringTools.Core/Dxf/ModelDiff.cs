#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// What a second model lost or gained against a first, storey by storey, with positions — the
/// differential behind every "byte-identical, or what moved" reading of the six banked sets. Ported
/// 2026-09-11 from <c>members_diff.py</c> and <c>plate_diff.py</c> (built 2026-09-10, corrected after
/// the Codex audit: F22 grid-label alignment, F24 counts not printed samples) so the six-set gate, the
/// <c>model-diff</c> verb and the corpus ledger read the same code.
/// </summary>
/// <remarks>
/// Columns are matched by their base joint (x, y) rounded to the model's unit; walls by the centroid
/// of their panel AND its length, so a wall turned through ninety degrees about its centre is a
/// change. Storeys are the UNION of both files' storeys, so a storey only the second file has is
/// reported. The frames are matched BY GRID LABEL when both carry GRIDS (the same label in the same
/// system is the same line); when either has none, the modal displacement between each column and
/// its nearest neighbour stands in and the result says GUESS — that guess reads a lone member's move
/// as a frame shift and a whole-model shift as member moves. A member within two units of one in the
/// other file is the same member (a joint moves a unit when the model's offset re-rounds). Plates are
/// compared by area per storey. WHAT THIS DOES NOT COMPARE: sections and materials behind a name,
/// openings, spandrels, joints on their own, the report's warnings.
/// </remarks>
public static class ModelDiff
{
    public sealed record Member(long X, long Y, long Length);   // Length 0 for a column

    public sealed record StoreyDiff(string Storey, int ColumnsBefore, int ColumnsAfter, int WallsBefore, int WallsAfter,
        IReadOnlyList<Member> LostColumns, IReadOnlyList<Member> GainedColumns, IReadOnlyList<Member> LostWalls, IReadOnlyList<Member> GainedWalls,
        IReadOnlyList<double> PlatesBefore, IReadOnlyList<double> PlatesAfter)
    {
        public bool Changed => LostColumns.Count + GainedColumns.Count + LostWalls.Count + GainedWalls.Count > 0 || PlatesMoved;
        public bool PlatesMoved => !PlatesBefore.Select(p => Math.Round(p)).SequenceEqual(PlatesAfter.Select(p => Math.Round(p)));
    }

    public sealed record Result(bool ByteIdentical, (long X, long Y) Shift, string ShiftHow, IReadOnlyList<StoreyDiff> Storeys)
    {
        public int LostColumns => Storeys.Sum(s => s.LostColumns.Count);
        public int GainedColumns => Storeys.Sum(s => s.GainedColumns.Count);
        public int LostWalls => Storeys.Sum(s => s.LostWalls.Count);
        public int GainedWalls => Storeys.Sum(s => s.GainedWalls.Count);
        public int PlatesMoved => Storeys.Count(s => s.PlatesMoved);
        public string OneLine => ByteIdentical ? "byte-identical"
            : $"plates moved {PlatesMoved}, columns lost {LostColumns} / gained {GainedColumns}, walls lost {LostWalls} / gained {GainedWalls}";
    }


    public static Result Compare(string beforePath, string afterPath)
    {
        byte[] a = File.ReadAllBytes(beforePath), b = File.ReadAllBytes(afterPath);
        if (a.AsSpan().SequenceEqual(b)) return new Result(true, (0, 0), "byte-identical", []);

        var before = E2kDocument.Load(beforePath);
        var after = E2kDocument.Load(afterPath);
        var (orderA, membersA, platesA) = Members(before);
        var (orderB, membersB, platesB) = Members(after);
        var order = orderA.Concat(orderB.Where(s => !orderA.Contains(s, StringComparer.OrdinalIgnoreCase))).ToList();

        // the frame shift between the two models, by grid label first
        var ga = before.ReadGrids(); var gb = after.ReadGrids();
        var dx = ga.Keys.Where(k => k.Dir == "X" && gb.ContainsKey(k)).Select(k => gb[k] - ga[k]).ToList();
        var dy = ga.Keys.Where(k => k.Dir == "Y" && gb.ContainsKey(k)).Select(k => gb[k] - ga[k]).ToList();
        (long X, long Y) shift;
        string how;
        if (dx.Count >= 2 && dy.Count >= 2)
        {
            shift = ((long)Math.Round(Median(dx)), (long)Math.Round(Median(dy)));
            how = $"by {dx.Count} X and {dy.Count} Y grid labels both files carry";
        }
        else
        {
            var votes = new Dictionary<(long, long), int>();
            foreach (var storey in order)
            {
                if (!membersA.TryGetValue(storey, out var ma) || !membersB.TryGetValue(storey, out var mb) || mb.Columns.Count == 0) continue;
                foreach (var p in ma.Columns)
                {
                    var q = mb.Columns.MinBy(q => (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y))!;
                    var v = (q.X - p.X, q.Y - p.Y);
                    votes[v] = votes.TryGetValue(v, out int n) ? n + 1 : 1;
                }
            }
            shift = votes.Count == 0 ? (0, 0) : votes.MaxBy(kv => kv.Value).Key;
            how = "by the modal column displacement - a GUESS, one file has no GRIDS";
        }
        if (shift != (0, 0))
            membersB = membersB.ToDictionary(kv => kv.Key,
                kv => (Columns: (IReadOnlyList<Member>)kv.Value.Columns.Select(m => m with { X = m.X - shift.X, Y = m.Y - shift.Y }).ToList(),
                       Walls: (IReadOnlyList<Member>)kv.Value.Walls.Select(m => m with { X = m.X - shift.X, Y = m.Y - shift.Y }).ToList()),
                StringComparer.OrdinalIgnoreCase);

        var storeys = new List<StoreyDiff>();
        foreach (var storey in order)
        {
            var ma = membersA.TryGetValue(storey, out var xa) ? xa : (Columns: [], Walls: []);
            var mb = membersB.TryGetValue(storey, out var xb) ? xb : (Columns: [], Walls: []);
            var pa = platesA.TryGetValue(storey, out var ppa) ? ppa : [];
            var pb = platesB.TryGetValue(storey, out var ppb) ? ppb : [];
            storeys.Add(new StoreyDiff(storey, ma.Columns.Count, mb.Columns.Count, ma.Walls.Count, mb.Walls.Count,
                Unmatched(ma.Columns, mb.Columns), Unmatched(mb.Columns, ma.Columns), Unmatched(ma.Walls, mb.Walls), Unmatched(mb.Walls, ma.Walls),
                pa.OrderByDescending(v => v).ToList(), pb.OrderByDescending(v => v).ToList()));
        }
        return new Result(false, shift, how, storeys);
    }

    /// <summary>The report, as the scripts printed it: storeys that changed, counts on the storey line, a sample of positions under it.</summary>
    public static string Report(Result r, int sample = 12)
    {
        var sb = new StringBuilder();
        if (r.ByteIdentical) { sb.AppendLine("byte-identical"); return sb.ToString(); }
        if (r.Shift != (0, 0))
            sb.AppendLine($"(the second model sits {r.Shift.X.ToString("+#,0;-#,0", CultureInfo.InvariantCulture)}, {r.Shift.Y.ToString("+#,0;-#,0", CultureInfo.InvariantCulture)} from the first, {r.ShiftHow}; positions below are the first model's frame)");
        foreach (var s in r.Storeys.Where(s => s.Changed))
        {
            string line = $"{s.Storey}: columns {s.ColumnsBefore} -> {s.ColumnsAfter}, walls {s.WallsBefore} -> {s.WallsAfter}" +
                $"  [lost columns {s.LostColumns.Count}, gained columns {s.GainedColumns.Count}, lost walls {s.LostWalls.Count}, gained walls {s.GainedWalls.Count}]" +
                (s.PlatesMoved ? $"  plates {Join(s.PlatesBefore)} -> {Join(s.PlatesAfter)} sq ft" : "");
            sb.AppendLine(line);
            foreach (var (tag, items) in new[] { ("LOST column", s.LostColumns), ("GAINED column", s.GainedColumns), ("LOST wall", s.LostWalls), ("GAINED wall", s.GainedWalls) })
            {
                foreach (var m in items.Take(sample))
                    sb.AppendLine($"   {tag,-14} at ({m.X.ToString("N0", CultureInfo.InvariantCulture)}, {m.Y.ToString("N0", CultureInfo.InvariantCulture)})" + (m.Length > 0 ? $" {m.Length.ToString("N0", CultureInfo.InvariantCulture)} long" : ""));
                if (items.Count > sample) sb.AppendLine(CultureInfo.InvariantCulture, $"   ... and {items.Count - sample} more (a sample is printed; the count is in the storey line)");
            }
        }
        sb.AppendLine(r.OneLine);
        return sb.ToString();

        static string Join(IReadOnlyList<double> plates) => plates.Count == 0 ? "-" : string.Join(" ", plates.Select(p => p.ToString("N0", CultureInfo.InvariantCulture)));
    }

    private static List<Member> Unmatched(IReadOnlyList<Member> xs, IReadOnlyList<Member> ys)
        => xs.Where(p => !ys.Any(q => Math.Abs(p.X - q.X) <= 2 && Math.Abs(p.Y - q.Y) <= 2 && Math.Abs(p.Length - q.Length) <= 2))
             .OrderBy(m => m.X).ThenBy(m => m.Y).ToList();

    private static (List<string> Order, Dictionary<string, (IReadOnlyList<Member> Columns, IReadOnlyList<Member> Walls)>, Dictionary<string, List<double>> Plates) Members(E2kDocument doc)
    {
        var order = doc.ReadStories().Select(s => s.Name).ToList();
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^LINE\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREA\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        double perSqFt = (doc.LengthUnitInInches() ?? 1.0) is double u ? 144.0 / (u * u) : 144.0;   // model units² per sq ft
        var members = new Dictionary<string, (List<Member> Columns, List<Member> Walls)>(StringComparer.OrdinalIgnoreCase);
        var plates = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, kind) in kinds)
        {
            if (!points.TryGetValue(name, out var pts) || pts.Count == 0 || !storeysOf.TryGetValue(name, out var on)) continue;
            foreach (var storey in on)
            {
                if (!members.TryGetValue(storey, out var list)) members[storey] = list = (new List<Member>(), new List<Member>());
                if (kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase))
                    list.Columns.Add(new Member((long)Math.Round(pts[0].X), (long)Math.Round(pts[0].Y), 0));
                else if (kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    list.Walls.Add(new Member((long)Math.Round(pts.Average(p => p.X)), (long)Math.Round(pts.Average(p => p.Y)), (long)Math.Round(Math.Max(maxX - minX, maxY - minY))));
                }
                else if (kind.Equals("FLOOR", StringComparison.OrdinalIgnoreCase) && pts.Count >= 3)
                {
                    double area = 0;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                        area += p.X * q.Y - q.X * p.Y;
                    }
                    (plates.TryGetValue(storey, out var pl) ? pl : plates[storey] = new List<double>()).Add(Math.Abs(area) / 2 / perSqFt);
                }
            }
        }
        var dict = members.ToDictionary(kv => kv.Key, kv => ((IReadOnlyList<Member>)kv.Value.Columns, (IReadOnlyList<Member>)kv.Value.Walls), StringComparer.OrdinalIgnoreCase);
        return (order, dict, plates);
    }

    private static double Median(List<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
