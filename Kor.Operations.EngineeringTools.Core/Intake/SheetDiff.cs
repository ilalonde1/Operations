using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Frame = Kor.Operations.EngineeringTools.Dxf.AnnotationOverlay.Frame;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// REISSUE IMPACT: what changed on one sheet between two issues, as objects — columns moved, added,
/// removed or resized; walls lengthened or gone; footings re-marked; grid axes moved; storeys
/// changed; schedule cells rewritten. The new issue is set on the old one's grid by the names of
/// its axes first (the page may have shifted; the grid has not), so a move is a move of the member
/// and not of the title block.
/// </summary>
/// <remarks>
/// The rules, each a universal statement: a column found within five feet of where it was is the
/// same column, and has moved if it is more than two inches from there; a wall of the same orientation
/// and thickness whose axis lies within a foot and overlaps half the shorter is the same wall; a
/// footing found within two feet is the same footing; a grid axis is the same by name; a storey is
/// the same by its two level names; a schedule row is the same by its mark. Tolerances are drafting
/// tolerances, not geometry's: a plan read at 1:96 resolves to about an inch.
/// </remarks>
public static class SheetDiff
{
    /// <summary>
    /// A column found within five feet of where it was is that column, moved; farther, and it is a
    /// column removed here and one added there — which is what the engineer would say of it too.
    /// Within two inches is drafting, not a move.
    /// </summary>
    public const double ColumnMatchMm = 1524, ColumnMovedMm = 50, SizeChangedMm = 25;
    public const double WallAxisMm = 305, WallOverlapShare = 0.5, WallLengthChangedMm = 150, WallThicknessMm = 25, WallAngleDegrees = 5;
    public const double FootingMatchMm = 610, FootingSizeChangedMm = 25;
    public const double GridMovedMm = 25, StoreyHeightChangedMm = 25;

    public sealed record Moved((double X, double Y) From, (double X, double Y) To)
    {
        public double DistanceMm => Math.Sqrt(Math.Pow(To.X - From.X, 2) + Math.Pow(To.Y - From.Y, 2));
    }
    public sealed record Resized((double X, double Y) At, (double WidthMm, double DepthMm) From, (double WidthMm, double DepthMm) To);
    public sealed record WallChange(WallPanel From, WallPanel To, string What);
    public sealed record FootingChange(FootingOutline From, FootingOutline To, string What);
    public sealed record GridChange(string Name, bool Vertical, double FromMm, double ToMm);
    public sealed record StoreyChange(string Level, string LevelBelow, double FromMm, double ToMm);
    public sealed record ScheduleChange(string Heading, string Mark, string Column, string From, string To);
    /// <summary>One member at one place, a wall on one issue and a column on the other: re-read, not rebuilt.</summary>
    public sealed record KindChange((double X, double Y) At, string From, string To);

    public sealed record SheetDelta(
        string? SheetNumber, int OldPage, int NewPage, string FrameNote,
        IReadOnlyList<(double X, double Y)> ColumnsAdded, IReadOnlyList<(double X, double Y)> ColumnsRemoved,
        IReadOnlyList<Moved> ColumnsMoved, IReadOnlyList<Resized> ColumnsResized, int ColumnsSame,
        IReadOnlyList<WallPanel> WallsAdded, IReadOnlyList<WallPanel> WallsRemoved, IReadOnlyList<WallChange> WallsChanged, int WallsSame,
        IReadOnlyList<FootingOutline> FootingsAdded, IReadOnlyList<FootingOutline> FootingsRemoved, IReadOnlyList<FootingChange> FootingsChanged, int FootingsSame,
        IReadOnlyList<GridAxis> GridAdded, IReadOnlyList<GridAxis> GridRemoved, IReadOnlyList<GridChange> GridMoved,
        IReadOnlyList<StoreyLadder.Storey> StoreysAdded, IReadOnlyList<StoreyLadder.Storey> StoreysRemoved, IReadOnlyList<StoreyChange> StoreysChanged,
        IReadOnlyList<ScheduleChange> ScheduleChanges, IReadOnlyList<string> ScheduleRowsAdded, IReadOnlyList<string> ScheduleRowsRemoved)
    {
        /// <summary>Members read as a wall on one issue and a column on the other at one place; not counted as changes.</summary>
        public IReadOnlyList<KindChange> KindChanges { get; init; } = Array.Empty<KindChange>();

        public int Changes =>
            ColumnsAdded.Count + ColumnsRemoved.Count + ColumnsMoved.Count + ColumnsResized.Count
            + WallsAdded.Count + WallsRemoved.Count + WallsChanged.Count
            + FootingsAdded.Count + FootingsRemoved.Count + FootingsChanged.Count
            + GridAdded.Count + GridRemoved.Count + GridMoved.Count
            + StoreysAdded.Count + StoreysRemoved.Count + StoreysChanged.Count
            + ScheduleChanges.Count + ScheduleRowsAdded.Count + ScheduleRowsRemoved.Count;

        /// <summary>One line per change, the way the engineer would say it; positions in metres on the sheet.</summary>
        public IEnumerable<string> Lines()
        {
            static string P((double X, double Y) p) => $"({p.X / 1000:0.00}, {p.Y / 1000:0.00}) m";
            foreach (var c in ColumnsMoved) yield return $"column moved {c.DistanceMm:0} mm, {P(c.From)} -> {P(c.To)}";
            foreach (var c in ColumnsResized) yield return $"column at {P(c.At)} resized {c.From.WidthMm / 25.4:0}x{c.From.DepthMm / 25.4:0} -> {c.To.WidthMm / 25.4:0}x{c.To.DepthMm / 25.4:0} in";
            foreach (var c in ColumnsAdded) yield return $"column added at {P(c)}";
            foreach (var c in ColumnsRemoved) yield return $"column removed at {P(c)}";
            foreach (var w in WallsChanged) yield return $"wall at {P(Mid(w.From))}: {w.What}";
            foreach (var w in WallsAdded) yield return $"wall added at {P(Mid(w))}, {Length(w) / 25.4:0} x {w.ThicknessMm / 25.4:0} in";
            foreach (var w in WallsRemoved) yield return $"wall removed at {P(Mid(w))}, {Length(w) / 25.4:0} x {w.ThicknessMm / 25.4:0} in";
            foreach (var f in FootingsChanged) yield return $"footing {f.From.Mark} at {P(f.From.Centre)}: {f.What}";
            foreach (var f in FootingsAdded) yield return $"footing {f.Mark} added at {P(f.Centre)}";
            foreach (var f in FootingsRemoved) yield return $"footing {f.Mark} removed at {P(f.Centre)}";
            foreach (var g in GridMoved) yield return $"grid {g.Name} moved {g.ToMm - g.FromMm:+0;-0} mm";
            foreach (var g in GridAdded) yield return $"grid {g.Name} added";
            foreach (var g in GridRemoved) yield return $"grid {g.Name} removed";
            foreach (var s in StoreysChanged) yield return $"storey {s.Level} -> {s.LevelBelow}: {s.FromMm:0} -> {s.ToMm:0} mm";
            foreach (var s in StoreysAdded) yield return $"storey {s.Level} -> {s.LevelBelow} added ({s.HeightMm:0} mm)";
            foreach (var s in StoreysRemoved) yield return $"storey {s.Level} -> {s.LevelBelow} removed";
            foreach (var s in ScheduleChanges) yield return $"{s.Heading} {s.Mark} {s.Column}: \"{s.From}\" -> \"{s.To}\"";
            foreach (var s in ScheduleRowsAdded) yield return $"schedule row added: {s}";
            foreach (var s in ScheduleRowsRemoved) yield return $"schedule row removed: {s}";
            foreach (var k in KindChanges) yield return $"at {P(k.At)}: read as {k.From} on the old issue and {k.To} on the new — the same member, not a change";
        }
    }

    public static SheetDelta Compare(SheetObjects old, SheetObjects @new)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);

        // THE NEW ISSUE ON THE OLD ONE'S GRID, BY NAME. Nothing else on the sheet is fixed between
        // issues: the plan may sit elsewhere on the page, the page may be another size.
        var reference = old.GridAxes.Select(a => new GridAlignment.ReferenceGrid(a.Name, a.Vertical, a.AtMm)).ToList();
        var axes = @new.GridAxes.Select(a => new GridAlignment.NamedAxis(a.Name, a.Vertical, a.AtMm)).ToList();
        var fit = reference.Count > 0 && axes.Count > 0 ? GridAlignment.SolveByName(axes, reference) : null;
        Frame frame = fit?.Frame ?? new Frame(0, 0, 0);
        string frameNote = fit is null
            ? (old.GridAxes.Count == 0 || @new.GridAxes.Count == 0
                ? "no named grid on one issue; compared in page coordinates"
                : "the issues share too few grid names to set one on the other; compared in page coordinates")
            : $"new issue set on the old one's grid by name: {fit.Note}";
        (double X, double Y) Map((double X, double Y) p)
        {
            var q = frame.Apply(new DxfPoint(p.X, p.Y));
            return (q.X, q.Y);
        }

        // ── columns ──
        var newCols = @new.Columns.Select(Map).ToList();
        var colPairs = MatchByDistance(old.Columns, newCols, ColumnMatchMm);
        var colsMoved = new List<Moved>(); var colsResized = new List<Resized>(); int colsSame = 0;
        foreach (var (i, j, d) in colPairs)
        {
            bool moved = d > ColumnMovedMm;
            if (moved) colsMoved.Add(new Moved(old.Columns[i], newCols[j]));
            bool resized = i < old.ColumnSizes.Count && j < @new.ColumnSizes.Count
                           && (Math.Abs(old.ColumnSizes[i].WidthMm - @new.ColumnSizes[j].WidthMm) > SizeChangedMm
                               || Math.Abs(old.ColumnSizes[i].DepthMm - @new.ColumnSizes[j].DepthMm) > SizeChangedMm);
            if (resized) colsResized.Add(new Resized(old.Columns[i], old.ColumnSizes[i], @new.ColumnSizes[j]));
            if (!moved && !resized) colsSame++;
        }
        var colsRemoved = old.Columns.Where((_, i) => colPairs.All(p => p.I != i)).ToList();
        var colsAdded = newCols.Where((_, j) => colPairs.All(p => p.J != j)).ToList();

        // ── walls ──
        var newWalls = @new.Walls.Select(w => Move(w, Map)).ToList();
        var wallPairs = MatchWalls(old.Walls, newWalls);
        var wallsChanged = new List<WallChange>(); int wallsSame = 0;
        foreach (var (i, j) in wallPairs)
        {
            var a = old.Walls[i]; var b = newWalls[j];
            var what = new List<string>();
            if (Math.Abs(Length(a) - Length(b)) > WallLengthChangedMm) what.Add($"length {Length(a) / 25.4:0} -> {Length(b) / 25.4:0} in");
            if (Math.Abs(a.ThicknessMm - b.ThicknessMm) > WallThicknessMm) what.Add($"thickness {a.ThicknessMm / 25.4:0} -> {b.ThicknessMm / 25.4:0} in");
            if (what.Count > 0) wallsChanged.Add(new WallChange(a, b, string.Join(", ", what))); else wallsSame++;
        }
        var wallsRemoved = old.Walls.Where((_, i) => wallPairs.All(p => p.I != i)).ToList();
        var wallsAdded = newWalls.Where((_, j) => wallPairs.All(p => p.J != j)).ToList();

        // ONE MEMBER, TWO READINGS. A 48" x 24" pier sits on the wall-or-column boundary, and one
        // issue's read may fall either side of it: a column added where a wall was removed, at the
        // same place, is the same member re-read (31130 S2.03.1, 2026-09-08), not a change.
        var kindChanges = new List<KindChange>();
        foreach (var (columns, walls, from, to) in new[]
                 {
                     (colsAdded, wallsRemoved, "a wall", "a column"),
                     (colsRemoved, wallsAdded, "a column", "a wall"),
                 })
        {
            foreach (var wall in walls.ToList())
            {
                var at = Mid(wall);
                var column = columns.FirstOrDefault(c => Math.Sqrt(Math.Pow(c.X - at.X, 2) + Math.Pow(c.Y - at.Y, 2)) <= ColumnMatchMm);
                if (column == default) continue;
                columns.Remove(column);
                walls.Remove(wall);
                kindChanges.Add(new KindChange(at, $"{from} {Length(wall) / 25.4:0} x {wall.ThicknessMm / 25.4:0} in", to));
            }
        }

        // ── footings ──
        var newFootings = @new.Footings.Select(f => f with { Centre = Map(f.Centre), Outline = f.Outline.Select(Map).ToList() }).ToList();
        var footingPairs = MatchByDistance(old.Footings.Select(f => f.Centre).ToList(), newFootings.Select(f => f.Centre).ToList(), FootingMatchMm);
        var footingsChanged = new List<FootingChange>(); int footingsSame = 0;
        foreach (var (i, j, _) in footingPairs)
        {
            var a = old.Footings[i]; var b = newFootings[j];
            var what = new List<string>();
            if (!a.Mark.Equals(b.Mark, StringComparison.OrdinalIgnoreCase)) what.Add($"mark {a.Mark} -> {b.Mark}");
            if (Math.Abs(a.LengthMm - b.LengthMm) > FootingSizeChangedMm || Math.Abs(a.WidthMm - b.WidthMm) > FootingSizeChangedMm)
                what.Add($"size {a.LengthMm:0}x{a.WidthMm:0} -> {b.LengthMm:0}x{b.WidthMm:0} mm");
            if (what.Count > 0) footingsChanged.Add(new FootingChange(a, b, string.Join(", ", what))); else footingsSame++;
        }
        var footingsRemoved = old.Footings.Where((_, i) => footingPairs.All(p => p.I != i)).ToList();
        var footingsAdded = newFootings.Where((_, j) => footingPairs.All(p => p.J != j)).ToList();

        // ── grid, by name ──
        var newGrid = @new.GridAxes.Select(a =>
        {
            var q0 = Map(a.Vertical ? (a.AtMm, 0.0) : (0.0, a.AtMm));
            var q1 = Map(a.Vertical ? (a.AtMm, 1000.0) : (1000.0, a.AtMm));
            bool vertical = Math.Abs(q1.X - q0.X) <= 1e-6;
            return new GridAxis(a.Name, vertical, vertical ? q0.X : q0.Y);
        }).ToList();
        // by name, and where a sheet carries a name more than once (two views, a key plan), each
        // old axis takes the nearest new one of its name, once
        var gridMoved = new List<GridChange>(); var gridAdded = new List<GridAxis>(); var gridRemoved = new List<GridAxis>();
        var usedNew = new HashSet<int>();
        foreach (var a in old.GridAxes)
        {
            int bestJ = -1; double bestD = double.MaxValue;
            for (int j = 0; j < newGrid.Count; j++)
            {
                var n = newGrid[j];
                if (usedNew.Contains(j) || n.Vertical != a.Vertical || !n.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase)) continue;
                double dist = Math.Abs(n.AtMm - a.AtMm);
                if (dist < bestD) { bestD = dist; bestJ = j; }
            }
            if (bestJ < 0) { gridRemoved.Add(a); continue; }
            usedNew.Add(bestJ);
            if (bestD > GridMovedMm) gridMoved.Add(new GridChange(a.Name, a.Vertical, a.AtMm, newGrid[bestJ].AtMm));
        }
        for (int j = 0; j < newGrid.Count; j++)
            if (!usedNew.Contains(j)) gridAdded.Add(newGrid[j]);

        // ── storeys, by their two level names ──
        var storeysChanged = new List<StoreyChange>(); var storeysAdded = new List<StoreyLadder.Storey>(); var storeysRemoved = new List<StoreyLadder.Storey>();
        foreach (var a in old.Storeys)
        {
            var b = @new.Storeys.FirstOrDefault(s => SameStorey(s, a));
            if (b is null) storeysRemoved.Add(a);
            else if (Math.Abs(b.HeightMm - a.HeightMm) > StoreyHeightChangedMm) storeysChanged.Add(new StoreyChange(a.Level, a.LevelBelow, a.HeightMm, b.HeightMm));
        }
        foreach (var b in @new.Storeys)
            if (!old.Storeys.Any(a => SameStorey(a, b))) storeysAdded.Add(b);

        // ── schedules, by heading and mark ──
        var scheduleChanges = new List<ScheduleChange>(); var rowsAdded = new List<string>(); var rowsRemoved = new List<string>();
        foreach (var table in old.Schedules)
        {
            var counterpart = @new.Schedules.FirstOrDefault(t => t.Kind.Equals(table.Kind, StringComparison.OrdinalIgnoreCase)
                                                                   && t.Heading.Equals(table.Heading, StringComparison.OrdinalIgnoreCase))
                              ?? @new.Schedules.FirstOrDefault(t => t.Kind.Equals(table.Kind, StringComparison.OrdinalIgnoreCase));
            if (counterpart is null) { rowsRemoved.AddRange(table.Rows.Select(r => $"{table.Heading} {r.Mark}")); continue; }
            foreach (var row in table.Rows)
            {
                var other = counterpart.Rows.FirstOrDefault(r => r.Mark.Equals(row.Mark, StringComparison.OrdinalIgnoreCase));
                if (other is null) { rowsRemoved.Add($"{table.Heading} {row.Mark}"); continue; }
                foreach (var (column, value) in row.Cells)
                {
                    other.Cells.TryGetValue(column, out string? theirs);
                    if (!Same(value, theirs)) scheduleChanges.Add(new ScheduleChange(table.Heading, row.Mark, column, value, theirs ?? ""));
                }
            }
            foreach (var row in counterpart.Rows)
                if (!table.Rows.Any(r => r.Mark.Equals(row.Mark, StringComparison.OrdinalIgnoreCase))) rowsAdded.Add($"{counterpart.Heading} {row.Mark}");
        }
        foreach (var table in @new.Schedules)
            if (!old.Schedules.Any(t => t.Kind.Equals(table.Kind, StringComparison.OrdinalIgnoreCase)))
                rowsAdded.AddRange(table.Rows.Select(r => $"{table.Heading} {r.Mark}"));

        return new SheetDelta(old.SheetNumber ?? @new.SheetNumber, old.PageNumber, @new.PageNumber, frameNote,
            colsAdded, colsRemoved, colsMoved, colsResized, colsSame,
            wallsAdded, wallsRemoved, wallsChanged, wallsSame,
            footingsAdded, footingsRemoved, footingsChanged, footingsSame,
            gridAdded, gridRemoved, gridMoved,
            storeysAdded, storeysRemoved, storeysChanged,
            scheduleChanges, rowsAdded, rowsRemoved) { KindChanges = kindChanges };
    }

    private static bool SameStorey(StoreyLadder.Storey a, StoreyLadder.Storey b) =>
        a.Level.Equals(b.Level, StringComparison.OrdinalIgnoreCase) && a.LevelBelow.Equals(b.LevelBelow, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? a, string? b) =>
        string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Nearest-first pairing within a reach; each point used once.</summary>
    internal static List<(int I, int J, double D)> MatchByDistance(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b, double reachMm)
    {
        var candidates = new List<(int I, int J, double D)>();
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
            {
                double d = Math.Sqrt(Math.Pow(a[i].X - b[j].X, 2) + Math.Pow(a[i].Y - b[j].Y, 2));
                if (d <= reachMm) candidates.Add((i, j, d));
            }
        var usedA = new HashSet<int>(); var usedB = new HashSet<int>();
        var pairs = new List<(int I, int J, double D)>();
        foreach (var c in candidates.OrderBy(c => c.D))
        {
            if (usedA.Contains(c.I) || usedB.Contains(c.J)) continue;
            usedA.Add(c.I); usedB.Add(c.J); pairs.Add(c);
        }
        return pairs;
    }

    /// <summary>Same orientation and thickness, axis within reach, overlapping half the shorter: the same wall.</summary>
    internal static List<(int I, int J)> MatchWalls(IReadOnlyList<WallPanel> a, IReadOnlyList<WallPanel> b)
    {
        var candidates = new List<(int I, int J, double Score)>();
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
            {
                if (Math.Abs(a[i].ThicknessMm - b[j].ThicknessMm) > 2 * WallThicknessMm) continue;
                double angle = Math.Abs(AngleDegrees(a[i]) - AngleDegrees(b[j])) % 180;
                if (Math.Min(angle, 180 - angle) > WallAngleDegrees) continue;
                var (ux, uy) = Unit(a[i]);
                double nx = -uy, ny = ux;
                double offB0 = (b[j].Start.X - a[i].Start.X) * nx + (b[j].Start.Y - a[i].Start.Y) * ny;
                double offB1 = (b[j].End.X - a[i].Start.X) * nx + (b[j].End.Y - a[i].Start.Y) * ny;
                if (Math.Abs((offB0 + offB1) / 2) > WallAxisMm) continue;
                double la = Length(a[i]);
                double t0 = (b[j].Start.X - a[i].Start.X) * ux + (b[j].Start.Y - a[i].Start.Y) * uy;
                double t1 = (b[j].End.X - a[i].Start.X) * ux + (b[j].End.Y - a[i].Start.Y) * uy;
                if (t0 > t1) (t0, t1) = (t1, t0);
                double overlap = Math.Min(la, t1) - Math.Max(0, t0);
                double shorter = Math.Min(la, Length(b[j]));
                if (shorter <= 0 || overlap < WallOverlapShare * shorter) continue;
                candidates.Add((i, j, overlap / shorter));
            }
        var usedA = new HashSet<int>(); var usedB = new HashSet<int>();
        var pairs = new List<(int I, int J)>();
        foreach (var c in candidates.OrderByDescending(c => c.Score))
        {
            if (usedA.Contains(c.I) || usedB.Contains(c.J)) continue;
            usedA.Add(c.I); usedB.Add(c.J); pairs.Add((c.I, c.J));
        }
        return pairs;
    }

    private static WallPanel Move(WallPanel w, Func<(double X, double Y), (double X, double Y)> map) =>
        new(w.Outline.Select(map).ToList(), map(w.Start), map(w.End), w.ThicknessMm);

    private static double Length(WallPanel w) => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
    private static (double X, double Y) Mid(WallPanel w) => ((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2);
    private static double AngleDegrees(WallPanel w) => Math.Atan2(w.End.Y - w.Start.Y, w.End.X - w.Start.X) * 180 / Math.PI;
    private static (double, double) Unit(WallPanel w)
    {
        double l = Length(w);
        return l < 1e-9 ? (1, 0) : ((w.End.X - w.Start.X) / l, (w.End.Y - w.Start.Y) / l);
    }
}
