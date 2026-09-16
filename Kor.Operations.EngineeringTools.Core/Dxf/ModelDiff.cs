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
    /// <summary>
    /// A member's place: its centre; for a wall its extent along X and along Y and the angle of its longest edge
    /// in whole degrees, 0 to 179 (audit F17, and the second audit's finding 4: the two diagonals of one box have
    /// one centre and one pair of extents - the angle tells them apart). Extents and angle are 0 for a column.
    /// </summary>
    public sealed record Member(long X, long Y, long AlongX, long AlongY, int Angle = 0)
    {
        public long Length => Math.Max(AlongX, AlongY);
    }

    public sealed record StoreyDiff(string Storey, int ColumnsBefore, int ColumnsAfter, int WallsBefore, int WallsAfter,
        IReadOnlyList<Member> LostColumns, IReadOnlyList<Member> GainedColumns, IReadOnlyList<Member> LostWalls, IReadOnlyList<Member> GainedWalls,
        IReadOnlyList<double> PlatesBefore, IReadOnlyList<double> PlatesAfter)
    {
        public bool Changed => LostColumns.Count + GainedColumns.Count + LostWalls.Count + GainedWalls.Count > 0 || PlatesMoved;
        public bool PlatesMoved => !PlatesBefore.Select(p => Math.Round(p)).SequenceEqual(PlatesAfter.Select(p => Math.Round(p)));
    }

    /// <summary>
    /// WHERE A MEMBER WENT (2026-09-16, the second instance of "members vanish when a plate appears" - step 97's
    /// 31130 P1 walls, step 98's 34 columns off 31138 L2): a member is one place on the plan and a SPAN of storeys
    /// it is assigned to; the storey diff counts it on each storey and cannot say whether a column left the model
    /// or only lost one storey of its stack. This pairs each place across the two models and names the span
    /// before and after. Kind: "column" or "wall"; Before/After: the storeys, bottom first, empty when absent.
    /// </summary>
    public sealed record SpanChange(string Kind, Member Place, IReadOnlyList<string> Before, IReadOnlyList<string> After)
    {
        public string Class => Before.Count == 0 ? "new" : After.Count == 0 ? "vanished"
            : Before.Except(After, StringComparer.OrdinalIgnoreCase).Any() && After.Except(Before, StringComparer.OrdinalIgnoreCase).Any() ? "moved"
            : After.Count < Before.Count ? "shortened" : "lengthened";
    }

    public sealed record Result(bool ByteIdentical, (long X, long Y) Shift, string ShiftHow, IReadOnlyList<StoreyDiff> Storeys)
    {
        public IReadOnlyList<SpanChange> Spans { get; init; } = [];
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
            var (lostColumns, gainedColumns) = Match(ma.Columns, mb.Columns);
            var (lostWalls, gainedWalls) = Match(ma.Walls, mb.Walls);
            storeys.Add(new StoreyDiff(storey, ma.Columns.Count, mb.Columns.Count, ma.Walls.Count, mb.Walls.Count,
                lostColumns, gainedColumns, lostWalls, gainedWalls,
                pa.OrderByDescending(v => v).ToList(), pb.OrderByDescending(v => v).ToList()));
        }
        return new Result(false, shift, how, storeys) { Spans = Spans(order, membersA, membersB) };
    }

    // each place's storeys in each model; a place is the member's key (centre, extents, angle) after the frame shift
    private static List<SpanChange> Spans(List<string> order,
        Dictionary<string, (IReadOnlyList<Member> Columns, IReadOnlyList<Member> Walls)> a,
        Dictionary<string, (IReadOnlyList<Member> Columns, IReadOnlyList<Member> Walls)> b)
    {
        var changes = new List<SpanChange>();
        foreach (var (kind, pick) in new (string, Func<(IReadOnlyList<Member> Columns, IReadOnlyList<Member> Walls), IReadOnlyList<Member>>)[]
                 { ("column", m => m.Columns), ("wall", m => m.Walls) })
        {
            var before = new Dictionary<Member, List<string>>();
            var after = new Dictionary<Member, List<string>>();
            foreach (var storey in order)
            {
                if (a.TryGetValue(storey, out var ma)) foreach (var m in pick(ma)) (before.TryGetValue(m, out var l) ? l : before[m] = new List<string>()).Add(storey);
                if (b.TryGetValue(storey, out var mb)) foreach (var m in pick(mb)) (after.TryGetValue(m, out var l) ? l : after[m] = new List<string>()).Add(storey);
            }
            foreach (var place in before.Keys.Union(after.Keys))
            {
                var x = before.TryGetValue(place, out var xb) ? xb : new List<string>();
                var y = after.TryGetValue(place, out var yb) ? yb : new List<string>();
                if (!x.SequenceEqual(y, StringComparer.OrdinalIgnoreCase)) changes.Add(new SpanChange(kind, place, x, y));
            }
        }
        return changes.OrderBy(c => c.Kind).ThenBy(c => c.Class).ThenBy(c => c.Place.X).ThenBy(c => c.Place.Y).ToList();
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
        if (r.Spans.Count > 0)
        {
            sb.AppendLine($"where the members went ({r.Spans.Count} place(s) whose storeys changed):");
            foreach (var g in r.Spans.GroupBy(c => (c.Kind, c.Class)).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Class))
            {
                sb.AppendLine($"  {g.Key.Kind} {g.Key.Class}: {g.Count()}");
                foreach (var c in g.Take(sample))
                    sb.AppendLine($"     at ({c.Place.X.ToString("N0", CultureInfo.InvariantCulture)}, {c.Place.Y.ToString("N0", CultureInfo.InvariantCulture)})" +
                                  (c.Place.Length > 0 ? $" {c.Place.Length.ToString("N0", CultureInfo.InvariantCulture)} long" : "") +
                                  $": {(c.Before.Count == 0 ? "-" : string.Join(" ", c.Before))} -> {(c.After.Count == 0 ? "-" : string.Join(" ", c.After))}");
                if (g.Count() > sample) sb.AppendLine(CultureInfo.InvariantCulture, $"     ... and {g.Count() - sample} more");
            }
        }
        sb.AppendLine(r.OneLine);
        return sb.ToString();

        static string Join(IReadOnlyList<double> plates) => plates.Count == 0 ? "-" : string.Join(" ", plates.Select(p => p.ToString("N0", CultureInfo.InvariantCulture)));
    }

    // ONE MATCHING, READ BOTH WAYS (audit F18, and the second audit's finding 1: two greedy passes, one from each
    // side, need not describe one matching - {0,2} against {-2,1} read as one lost and none gained where both pair).
    // What the maximum matching leaves on the first side is lost, on the second gained. A wall's partner has its
    // extent along each axis and its angle (F17).
    private static (List<Member> Lost, List<Member> Gained) Match(IReadOnlyList<Member> xs, IReadOnlyList<Member> ys)
    {
        var pairs = new List<(long D, int I, int J)>();
        for (int i = 0; i < xs.Count; i++)
            for (int j = 0; j < ys.Count; j++)
            {
                var p = xs[i]; var q = ys[j];
                if (Math.Abs(p.X - q.X) > 2 || Math.Abs(p.Y - q.Y) > 2 || Math.Abs(p.AlongX - q.AlongX) > 2 || Math.Abs(p.AlongY - q.AlongY) > 2 || AngleApart(p.Angle, q.Angle) > 1) continue;
                pairs.Add((Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y), i, j));
            }
        // a MAXIMUM matching, not a greedy one: nearest-first left {0, 2} against {-2, 1} with 2 unmatched (0 took 1),
        // where 0 <-> -2 and 2 <-> 1 pair everything. Kuhn's augmenting paths over the candidate pairs, tried
        // nearest first so the matching found is the nearest of the maximum ones the search reaches.
        var candidates = new List<int>[xs.Count];
        for (int i = 0; i < xs.Count; i++) candidates[i] = [];
        foreach (var (_, i, j) in pairs.OrderBy(t => t.D).ThenBy(t => t.I).ThenBy(t => t.J)) candidates[i].Add(j);
        var matchY = new int[ys.Count]; Array.Fill(matchY, -1);
        bool Augment(int i, bool[] seen)
        {
            foreach (int j in candidates[i])
            {
                if (seen[j]) continue;
                seen[j] = true;
                if (matchY[j] < 0 || Augment(matchY[j], seen)) { matchY[j] = i; return true; }
            }
            return false;
        }
        var matchedX = new bool[xs.Count];
        for (int i = 0; i < xs.Count; i++) matchedX[i] = Augment(i, new bool[ys.Count]);
        return (xs.Where((_, i) => !matchedX[i]).OrderBy(m => m.X).ThenBy(m => m.Y).ToList(),
                ys.Where((_, j) => matchY[j] < 0).OrderBy(m => m.X).ThenBy(m => m.Y).ToList());
    }

    private static int AngleApart(int a, int b) { int d = Math.Abs(a - b) % 180; return Math.Min(d, 180 - d); }

    /// <summary>The angle of a wall's longest edge, whole degrees 0 to 179.</summary>
    private static int AngleOf(IReadOnlyList<(double X, double Y)> pts)
    {
        double best = 0, bx = 1, by = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
            if (l > best) { best = l; bx = dx; by = dy; }
        }
        int deg = (int)Math.Round(Math.Atan2(by, bx) * 180 / Math.PI);
        return ((deg % 180) + 180) % 180;
    }

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
                    list.Columns.Add(new Member((long)Math.Round(pts[0].X), (long)Math.Round(pts[0].Y), 0, 0));
                else if (kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    list.Walls.Add(new Member((long)Math.Round(pts.Average(p => p.X)), (long)Math.Round(pts.Average(p => p.Y)), (long)Math.Round(maxX - minX), (long)Math.Round(maxY - minY), AngleOf(pts)));
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
