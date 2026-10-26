using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.PdfToSafe;

/// <summary>
/// A FLOOR THE DRAWING ZONES BY THICKNESS IS MODELLED AS ITS ZONES (intake step 183, 2026-10-25). 31065's L1 is a transfer
/// floor: 900 over the west half, 500 north-centre, 600 in the east bays, 300 strips at the edges, each printed inside the
/// zone it names, the zones divided by the floor's own heavy pen and by dashed lines. The slab pass recovers the floor as one
/// ring and the classifier, finding four thicknesses printed in it, kept the 12" default - where the engineer's model carries
/// 37,540 sq ft of 900 mm. This cuts a finished floor into its zones; the floor itself is not re-derived.
/// </summary>
/// <remarks>
/// The floor's ring and the zone lines inside it (the strokes drawn with the floor outline's own pen, and dashed runs joined
/// into lines) are arranged into faces. A face takes the thickness printed inside it; a face that prints none takes the
/// call-out nearest it; a face that prints two takes the one printed more, then the one printed nearer its middle. Like
/// faces are united. A zone that would wrap another is cut through the inner one, above and below, so no zone
/// overlaps another. WHAT IT DOES NOT: a zone outlined by a curved dashed line (only horizontal and vertical dashes join), a
/// zone the drawing outlines with a third pen, a floor printing one thickness or none (no zones: the floor as it was). A
/// same-class fault it would not catch: a dimension line drawn with the outline's pen across the floor, which divides a
/// zone in two - harmless where both halves print the same thickness, and a wrong boundary where they do not.
/// </remarks>
public static class SlabZones
{
    /// <summary>One zone: its ring, and the thickness printed in it as the drawing prints it.</summary>
    public sealed record Zone(PlanLoop Ring, double Printed, bool IsMetric);

    /// <summary>The reader's trace (GeometryFilterService.FaceTrace), for the probe below.</summary>
    internal static Action<string>? Trace;

    /// <summary>A thickness call-out on the page: where it stands, and the number it prints.</summary>
    public readonly record struct Callout(DxfPoint At, double Printed, bool IsMetric);

    /// <summary>
    /// The floor's zones, or null where it has fewer than two (and why, in <paramref name="why"/>).
    /// </summary>
    public static IReadOnlyList<Zone>? Cut(
        PlanLoop floor,
        IReadOnlyList<(IReadOnlyList<DxfPoint> Line, double Width)> lines,
        IReadOnlyList<Callout> callouts,
        double joinMm, double bridgeMm, double extendMm, double dashGapMm,
        out string why,
        IEnumerable<DxfSegment>? wallEdges = null,
        IReadOnlyList<(string Text, double MinX, double MinY, double MaxX, double MaxY)>? words = null)
    {
        var inside = callouts.Where(c => LoopGeometry.PointInPolygon(c.At, floor.Points)).ToList();
        if (inside.Select(c => (c.Printed, c.IsMetric)).Distinct().Count() < 2) { why = "prints fewer than two thicknesses"; return null; }

        bool OnRing(DxfPoint p)
        {
            for (int i = 0; i < floor.Points.Count; i++)
                if (LoopGeometry.DistanceToSegment(p, floor.Points[i], floor.Points[(i + 1) % floor.Points.Count]) <= joinMm) return true;
            return false;
        }
        bool Inside(DxfPoint p) => LoopGeometry.PointInPolygon(p, floor.Points) && !OnRing(p);

        // A DASH is a short two-point stroke, whatever its pen; anything longer is drawn solid. The outline's pen is the pen
        // the floor's ring is drawn SOLID with, by length: counted by strokes, the dashes of an edge drawn dashed (31065 L1's
        // floor above) outnumbered the solid outline and named their own pen, and every hatch stroke became a zone line
        // A dash is up to four gaps long: 31065 L1's long-dash zone lines draw ~900 mm dashes at 1:100 (looked at, the 500/600
        // line at y 32 m), which a two-gap limit (711 mm) dropped, and the line with them
        static double Length(IReadOnlyList<DxfPoint> l) => Enumerable.Range(1, l.Count - 1).Sum(k => l[k - 1].DistanceTo(l[k]));
        bool IsDash(IReadOnlyList<DxfPoint> l) => l.Count == 2 && l[0].DistanceTo(l[1]) <= 4 * dashGapMm;
        var onRing = lines.Where(l => l.Line.Count >= 2 && !IsDash(l.Line) && l.Line.All(OnRing)).ToList();
        if (onRing.Count == 0) { why = "no solid stroke lies on the floor's ring, so its pen is not known"; return null; }
        double pen = onRing.GroupBy(l => Math.Round(l.Width, 2)).OrderByDescending(g => g.Sum(l => Length(l.Line))).ThenByDescending(g => g.Key).First().Key;

        var zoneLines = new List<DxfSegment>();
        var strokes = new List<(DxfPoint A, DxfPoint B)>();
        int shortOfThePen = 0;
        foreach (var (line, width) in lines)
        {
            bool ofThePen = Math.Abs(Math.Round(width, 2) - pen) <= 0.01;
            // A SHORT STROKE IN THE OUTLINE'S OWN PEN IS A PIECE OF A BOUNDARY, not only a dash: 31130 L16's tower-above
            // outline steps down 0.9 m twice, each step one short stroke of the outline's pen, and taken only as dashes -
            // no run to join - they were dropped and the outline stood open at each step (looked at, S2.19.1). Offered as both.
            // ⚠ A dash pattern drawn IN the outline's pen (some offices dash the tower-above line) would come in as many short
            // pieces - harmless where they stay in line, but counted, so a floor where the count jumps is looked at first
            if (IsDash(line))
            {
                if (Inside(new DxfPoint((line[0].X + line[1].X) / 2, (line[0].Y + line[1].Y) / 2))) strokes.Add((line[0], line[1]));
                if (!ofThePen) continue;
                shortOfThePen++;
            }
            if (!ofThePen) continue;
            for (int k = 1; k < line.Count; k++)
            {
                var (a, b) = (line[k - 1], line[k]);
                if (Inside(new DxfPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2))) zoneLines.Add(new DxfSegment("ZONE", a, b));
            }
        }
        // a wall's faces divide as the outline's pen does: the reader takes a wall's face lines out of the page's lines once
        // they are its wall, and on 31065 L1 the 500/600 boundary at y 32 m is wall SWA's face (looked at)
        int walls = 0;
        foreach (var w in wallEdges ?? [])
            if (Inside(new DxfPoint((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2))) { zoneLines.Add(new DxfSegment("ZONE", w.Start, w.End)); walls++; }
        int onPen = zoneLines.Count - walls;
        // A DASHED LINE RUNS TO THE LINE IT STOPS AT: its last dash falls where the pattern puts it, up to a dash short of that
        // line (four gaps, as a dash is long), so each end is carried along the line to the first solid line, wall or the
        // floor's ring within that reach - the arrangement joins a loose end only within its bridge, and a dashed boundary
        // left loose divides nothing
        var solid = zoneLines.Concat(Enumerable.Range(0, floor.Points.Count)
            .Select(i => new DxfSegment("RING", floor.Points[i], floor.Points[(i + 1) % floor.Points.Count]))).ToList();
        foreach (var (a, b) in DashedLines(strokes, dashGapMm, words ?? []))
            zoneLines.Add(new DxfSegment("ZONE", CarriedOn(b, a, solid, 4 * dashGapMm), CarriedOn(a, b, solid, 4 * dashGapMm)));
        string counts = $"pen {pen:0.00}, {onPen} stroke(s) on it inside ({shortOfThePen} short), {walls} wall edge(s),{zoneLines.Count - onPen - walls} dashed line(s) of {strokes.Count} short stroke(s)";
        var ring = Enumerable.Range(0, floor.Points.Count).Select(i => new DxfSegment("ZONE", floor.Points[i], floor.Points[(i + 1) % floor.Points.Count]));

        // KOR_STEP183_PROBE="x0,y0,x1,y1" (mm): every zone line and dash inside that box, to the trace - the instrument for a
        // boundary that did not divide (31065 L1's 500/600 line, 2026-10-25)
        if (Trace is not null && Environment.GetEnvironmentVariable("KOR_STEP183_PROBE") is { Length: > 0 } probe)
        {
            var q = probe.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            bool InBox(DxfPoint p) => p.X >= q[0] && p.X <= q[2] && p.Y >= q[1] && p.Y <= q[3];
            foreach (var c in inside) Trace($"slab pass: step 183 probe: call-out {c.Printed:0.#} at ({c.At.X:0},{c.At.Y:0})");
            foreach (var s in zoneLines.Where(s => InBox(s.Start) || InBox(s.End)))
                Trace($"slab pass: step 183 probe: zone line ({s.Start.X:0},{s.Start.Y:0})-({s.End.X:0},{s.End.Y:0})");
            foreach (var (a, b) in strokes.Where(s => InBox(s.A) || InBox(s.B)).Take(60))
                Trace($"slab pass: step 183 probe: dash ({a.X:0},{a.Y:0})-({b.X:0},{b.Y:0})");
            foreach (var w in (words ?? []).Where(w => InBox(new DxfPoint(w.MinX, w.MinY)) || InBox(new DxfPoint(w.MaxX, w.MaxY))))
                Trace($"slab pass: step 183 probe: word '{w.Text}' x {w.MinX:0}..{w.MaxX:0} y {w.MinY:0}..{w.MaxY:0}");
            foreach (var (line, width) in lines.Where(l => l.Line.Any(InBox)).Take(60))
                Trace($"slab pass: step 183 probe: page line w{width:0.00} {line.Count} pt ({line[0].X:0},{line[0].Y:0})-({line[^1].X:0},{line[^1].Y:0})");
        }

        PlanarRings.Result planar;
        try { planar = new PlanarRings(joinMm, bridgeMm, extendMm).Build(ring.Concat(zoneLines)); }
        catch (InvalidOperationException refused) { why = "the zone lines would not arrange: " + refused.Message; return null; }

        int n = planar.Faces.Count;
        var inFloor = new bool[n];
        var label = new (double Printed, bool IsMetric)?[n];
        for (int i = 0; i < n; i++)
        {
            var f = planar.Faces[i];
            inFloor[i] = Math.Abs(f.Outer.Area) > 0 && LoopGeometry.PointInPolygon(InteriorPoint(f.Outer), floor.Points);
            if (!inFloor[i]) continue;
            var says = inside.Where(c => LoopGeometry.PointInPolygon(c.At, f.Outer.Points) && !f.Holes.Any(h => LoopGeometry.PointInPolygon(c.At, h.Points))).ToList();
            if (says.Count == 0) continue;
            var centre = f.Outer.Centroid();
            label[i] = says.GroupBy(c => (c.Printed, c.IsMetric))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Min(c => c.At.DistanceTo(centre))).First().Key;
        }

        // A FACE THAT PRINTS NOTHING TAKES THE CALL-OUT NEAREST IT (step 182's ground, face by face). It first took the
        // neighbour it shared the most edge with, and a part printing nothing is no evidence for its neighbour: 31130's P2
        // east field, 23,000 sq ft with no call-out of its own, took the 12" of the ramp zone it bordered - the engineer's P2
        // is a 10" field, and the 10" call-outs are the nearer
        for (int i = 0; i < n; i++)
        {
            if (!inFloor[i] || label[i] is not null) continue;
            var at = InteriorPoint(planar.Faces[i].Outer);
            var near = inside.MinBy(c => c.At.DistanceTo(at));
            label[i] = (near.Printed, near.IsMetric);
        }
        // AN ISLAND IN ONE CELL IS THAT CELL'S: a face standing in a hole of a single other face - a box, a detail drawn
        // inside one region - is not a zone boundary, and one cell cannot be cut round it (31065 L1 north stood down whole,
        // "a zone wraps another in a single cell", the moment a box inside the 900 field took a nearer call-out). It takes the
        // zone of the cell it stands in; one that prints a thickness of its own is taken in too, and said
        var islandsTakenIn = new List<string>();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < n; i++)
            {
                if (!inFloor[i]) continue;
                var at = InteriorPoint(planar.Faces[i].Outer);
                int around = Enumerable.Range(0, n).FirstOrDefault(j => j != i && inFloor[j]
                    && planar.Faces[j].Holes.Any(h => LoopGeometry.PointInPolygon(at, h.Points)), -1);
                if (around < 0 || label[around] is null || label[i] == label[around]) continue;
                if (inside.Any(c => LoopGeometry.PointInPolygon(c.At, planar.Faces[i].Outer.Points)))
                    islandsTakenIn.Add($"{Math.Abs(planar.Faces[i].Outer.Area) / 92903.04:0} sq ft printing {label[i]!.Value.Printed:0.#}");
                label[i] = label[around];
                changed = true;
            }
        }
        var labels = Enumerable.Range(0, n).Where(i => inFloor[i] && label[i] is not null).Select(i => label[i]!.Value).Distinct().ToList();
        if (labels.Count < 2)
        {
            why = $"its zone lines divide nothing between the thicknesses it prints ({counts}; {n} face(s), {inFloor.Count(f => f)} in the floor, " +
                  $"{Enumerable.Range(0, n).Count(i => inFloor[i] && label[i] is not null)} labelled)";
            return null;
        }

        // like faces united; a union that wraps another zone is cut through the zone it wraps, above and below. WHERE IT
        // CANNOT BE CUT - one cell round it, or no cut parting it - WHAT IT WRAPS IS TAKEN INTO IT, as an island in one cell
        // is, and the union is made again: 31168 L1 (building C) stood down whole, 36,356 sq ft, for one such island the
        // moment the outline's short steps came in (2026-10-26), where its eighteen zones had been read the build before
        var zones = new List<Zone>();
        for (int attempt = 0; ; attempt++)
        {
            zones.Clear();
            (double Printed, bool IsMetric)? takeInto = null; PlanLoop? wrappedHole = null;
            foreach (var l in labels)
            {
                var pending = new List<HashSet<int>> { Enumerable.Range(0, n).Where(i => inFloor[i] && label[i] == l).ToHashSet() };
                for (int pass = 0; pending.Count > 0 && takeInto is null; pass++)
                {
                    var group = pending[^1]; pending.RemoveAt(pending.Count - 1);
                    foreach (var region in planar.RecoverSurfaces(_ => false, (i, _) => group.Contains(i)).Slabs)
                    {
                        var wrapped = region.Holes.FirstOrDefault(h => Enumerable.Range(0, n).Any(i => inFloor[i] && label[i] != l
                            && LoopGeometry.PointInPolygon(InteriorPoint(planar.Faces[i].Outer), h.Points)));
                        // a sliver of no area between two lines a hair apart is no zone (31130 L14: "16" 0 sq ft")
                        if (wrapped is null) { if (Math.Abs(region.Outer.Area) >= 92903.04) zones.Add(new Zone(region.Outer, l.Printed, l.IsMetric)); continue; }
                        double y = wrapped.Centroid().Y;
                        var mine = group.Where(i => LoopGeometry.PointInPolygon(InteriorPoint(planar.Faces[i].Outer), region.Outer.Points)).ToList();
                        var above = mine.Where(i => InteriorPoint(planar.Faces[i].Outer).Y >= y).ToHashSet();
                        var below = mine.Where(i => !above.Contains(i)).ToHashSet();
                        if (pass > 8 || above.Count == 0 || below.Count == 0) { takeInto = l; wrappedHole = wrapped; break; }
                        pending.Add(above); pending.Add(below);
                    }
                }
                if (takeInto is not null) break;
            }
            // AND NO ZONE ENDS INSIDE ANOTHER, WHATEVER THE ARRANGEMENT DID: 30993 L2 (south) wrote ten zones of 2-8 sq ft -
            // column boxes printing nothing, given the nearest call-out - lying wholly inside a 12,307 sq ft zone, 30 sq ft of
            // plate written twice (and an opening of the engineer's covered). They joined other lines, so no face held them
            // as a hole and the wrap test above did not see them. A zone standing inside another is taken into it, as a
            // wrapped one is, and the union made again
            // (measured: all ten were the SAME thickness as the zone round them - its own cells standing in a hole of it;
            // written as an outer ring, the big zone already covers them, so they are dropped. One of ANOTHER thickness inside
            // another was never seen on the witness floors - 30993 L2, 31168 L1 C, 31065 L1N, 31130 L16 all tile without
            // handling it - so it is not handled here: the tiling check below refuses such a floor and it is written whole.)
            if (takeInto is null)
                foreach (var small in zones.OrderBy(z => z.Ring.Area).ToList())
                {
                    var at = InteriorPoint(small.Ring);
                    if (zones.Any(z => !ReferenceEquals(z, small) && z.Ring.Area > small.Ring.Area
                                       && (z.Printed, z.IsMetric) == (small.Printed, small.IsMetric) && LoopGeometry.PointInPolygon(at, z.Ring.Points)))
                        zones.Remove(small);
                }
            if (takeInto is null) break;
            if (attempt >= 60) { why = "zones wrap others and would not come apart"; return null; }
            var inHole = Enumerable.Range(0, n).Where(i => inFloor[i] && label[i] != takeInto
                && LoopGeometry.PointInPolygon(InteriorPoint(planar.Faces[i].Outer), wrappedHole!.Points)).ToList();
            if (inHole.Count == 0) { why = "a zone stands inside another and no cell of it could be found to take in"; return null; }
            islandsTakenIn.Add($"{inHole.Sum(i => Math.Abs(planar.Faces[i].Outer.Area)) / 92903.04:0} sq ft of {label[inHole[0]]!.Value.Printed:0.#} wrapped");
            foreach (int i in inHole) label[i] = takeInto;
            labels = Enumerable.Range(0, n).Where(i => inFloor[i] && label[i] is not null).Select(i => label[i]!.Value).Distinct().ToList();
            if (labels.Count < 2) { why = "taking in what its zones wrapped left one thickness"; return null; }
        }
        // THE ZONES TILE THE FLOOR, OR THERE ARE NO ZONES: every fault this step has had broke this one property - zones
        // overlapping (30993 L2, 30 sq ft written twice), a whole floor's zones gone, a zone past the ring. Checked here,
        // so a later change that breaks the take-in or the cut fails closed - the floor stays whole and the reason says why -
        // instead of writing a plate twice. Within 0.05%: the ring's own rounding (measured: 31065 L1N and 30993 L2 exact,
        // 31130 L16 1 sq ft on 9,082) - and NOT 0.2%, which the fault it is for (30993's 30 sq ft on 26,424, 0.11%) passed
        double tiled = zones.Sum(z => z.Ring.Area);
        if (Math.Abs(tiled - floor.Area) > 0.0005 * floor.Area)
        {
            why = $"its zones came to {tiled / 92903.04:0} sq ft of the floor's {floor.Area / 92903.04:0} - they do not tile it, so the floor is kept whole";
            return null;
        }
        why = $"{zones.Count} zone(s), {labels.Count} thickness(es), {tiled / 92903.04:0} of the floor's {floor.Area / 92903.04:0} sq ft ({counts}; {n} face(s))"
              + (islandsTakenIn.Count == 0 ? "" : $"; {islandsTakenIn.Count} island(s) or zone(s) taken into the zone round them:{string.Join(", ", islandsTakenIn.Distinct().Take(6))}");
        return zones;
    }

    /// <summary>
    /// The dashed lines among these strokes, each as one line: pieces of one direction joined by the footing reader's
    /// rule (collinear within <see cref="Intake.FootingOutlines.CollinearMm"/>, gaps no wider than the dash-join gap, at least
    /// <see cref="Intake.FootingOutlines.MinPieces"/> pieces) and ACROSS A GAP A WORD STANDS IN: a drafter breaks a dashed line
    /// for the text written over it, and 31065 L1's 500/600 line is broken twice that way (1,389 and 791 mm, a dimension and
    /// a column tag), so it stopped 2.7 m short of the edge it runs to and divided nothing. Each line is carried half a gap
    /// past its last dash at each end, where the line it stops at is drawn.
    /// </summary>
    internal static List<(DxfPoint A, DxfPoint B)> DashedLines(IReadOnlyList<(DxfPoint A, DxfPoint B)> strokes, double gapMm,
        IReadOnlyList<(string Text, double MinX, double MinY, double MaxX, double MaxY)> words)
    {
        double collinear = Intake.FootingOutlines.CollinearMm;
        // IN ANY DIRECTION: 31130 L14 outlines its 16" areas with long dashes whose left and bottom sides run parallel to the
        // slab's slanted edges, and a join of horizontal and vertical runs only left those zones open on two sides
        // (looked at, S2.17.1). Each dash is taken in the frame of its own direction - its offset across that direction and
        // its span along it - and dashes of one direction (within a degree) and one offset are one line's
        var withAngle = strokes.Where(s => s.A.DistanceTo(s.B) > collinear)
            .Select(s => (s.A, s.B, Angle: (Math.Atan2(s.B.Y - s.A.Y, s.B.X - s.A.X) * 180 / Math.PI + 180) % 180))
            .Select(s => s with { Angle = s.Angle >= 179.5 ? s.Angle - 180 : s.Angle })
            .OrderBy(s => s.Angle).ToList();
        var directions = new List<List<(DxfPoint A, DxfPoint B, double Angle)>>();
        foreach (var s in withAngle)
            if (directions.Count > 0 && s.Angle - directions[^1][^1].Angle <= 1.0) directions[^1].Add(s);
            else directions.Add([s]);
        var runs = new List<(DxfPoint A, DxfPoint B)>();
        foreach (var direction in directions)
        {
            double theta = direction.Average(s => s.Angle) * Math.PI / 180;
            double ux = Math.Cos(theta), uy = Math.Sin(theta);
            double Along(DxfPoint p) => p.X * ux + p.Y * uy;
            double Across(DxfPoint p) => -p.X * uy + p.Y * ux;
            DxfPoint Back(double t, double at) => new(t * ux - at * uy, t * uy + at * ux);
            var pieces = direction.Select(s => (At: (Across(s.A) + Across(s.B)) / 2, From: Math.Min(Along(s.A), Along(s.B)), To: Math.Max(Along(s.A), Along(s.B))))
                .Where(p => p.To - p.From > collinear).ToList();
            // a word's box in the same frame: its span along the line, and how far across it reaches
            var wordSpans = words.Select(w =>
            {
                var corners = new[] { new DxfPoint(w.MinX, w.MinY), new DxfPoint(w.MaxX, w.MinY), new DxfPoint(w.MaxX, w.MaxY), new DxfPoint(w.MinX, w.MaxY) };
                return (Lo: corners.Min(Along), Hi: corners.Max(Along), AcrossLo: corners.Min(Across), AcrossHi: corners.Max(Across));
            }).ToList();
            // the gap is the words': what of it no word on the line covers is no wider than a dash gap anywhere along it
            bool AWordStandsIn(double at, double from, double to)
            {
                var over = wordSpans.Where(w => at >= w.AcrossLo - gapMm / 2 && at <= w.AcrossHi + gapMm / 2)
                    .Select(w => (w.Lo, w.Hi))
                    .Where(s => s.Item2 >= from && s.Item1 <= to).OrderBy(s => s.Item1).ToList();
                if (over.Count == 0) return false;
                double reached = from;
                foreach (var (lo, hi) in over)
                {
                    if (lo - reached > gapMm) return false;
                    reached = Math.Max(reached, hi);
                }
                return to - reached <= gapMm;
            }
            pieces.Sort((p, q) => p.At.CompareTo(q.At));
            for (int start = 0, i = 1; i <= pieces.Count; i++)
            {
                if (i < pieces.Count && pieces[i].At - pieces[i - 1].At <= collinear) continue;
                var side = pieces.GetRange(start, i - start).OrderBy(p => p.From).ToList();
                start = i;
                // the line's groups: pieces joined across a dash gap, or a gap the words stand in
                var groups = new List<(double From, double To, double At, int Count)> { (side[0].From, side[0].To, side[0].At, 1) };
                foreach (var p in side.Skip(1))
                {
                    var g = groups[^1];
                    if (p.From - g.To <= gapMm || AWordStandsIn(g.At, g.To, p.From)) groups[^1] = (g.From, Math.Max(g.To, p.To), g.At, g.Count + 1);
                    else groups.Add((p.From, p.To, p.At, 1));
                }
                // A STRAY DASH PAST A BREAK IS THE LINE'S: a group too short to be a line on its own, within four dash gaps of a
                // run on the same line, ends that run (31065 L1's 500/600 line: one dash at the field's edge, then 1,389 mm
                // broken round a column's tags, then the run - the run stopped 1.55 m short of the edge it runs to)
                for (int k = 0; k + 1 < groups.Count; k++)
                {
                    var (a, b) = (groups[k], groups[k + 1]);
                    bool oneIsARun = a.Count >= Intake.FootingOutlines.MinPieces || b.Count >= Intake.FootingOutlines.MinPieces;
                    bool oneIsStray = a.Count < Intake.FootingOutlines.MinPieces || b.Count < Intake.FootingOutlines.MinPieces;
                    if (!oneIsARun || !oneIsStray || b.From - a.To > 4 * gapMm) continue;
                    groups[k] = (a.From, Math.Max(a.To, b.To), a.At, a.Count + b.Count);
                    groups.RemoveAt(k + 1); k--;
                }
                foreach (var (from, to, at, count) in groups.Where(g => g.Count >= Intake.FootingOutlines.MinPieces))
                    runs.Add((Back(from - gapMm / 2, at), Back(to + gapMm / 2, at)));
            }
        }
        return runs;
    }

    /// <summary>
    /// The end <paramref name="end"/> of the line from <paramref name="from"/>, carried on along the line to the first of
    /// <paramref name="onto"/> it reaches within <paramref name="reach"/>, a little past it so the two cross; the end as it
    /// is where nothing is within reach. A line already crossing one at its end stays as it is.
    /// </summary>
    internal static DxfPoint CarriedOn(DxfPoint from, DxfPoint end, IReadOnlyList<DxfSegment> onto, double reach)
    {
        double len = from.DistanceTo(end);
        if (len <= 0) return end;
        double ux = (end.X - from.X) / len, uy = (end.Y - from.Y) / len;
        double best = double.MaxValue;
        foreach (var s in onto)
        {
            // the ray end + t*u meets the segment s.Start + v*(s.End - s.Start)
            double sx = s.End.X - s.Start.X, sy = s.End.Y - s.Start.Y;
            double den = ux * sy - uy * sx;
            if (Math.Abs(den) < 1e-9) continue;
            double dx = s.Start.X - end.X, dy = s.Start.Y - end.Y;
            double t = (dx * sy - dy * sx) / den, v = (dx * uy - dy * ux) / den;
            if (v < 0 || v > 1 || t < -1e-6 || t > reach) continue;
            if (t < best) best = t;
        }
        if (best == double.MaxValue || best <= 1e-6) return end;
        return new DxfPoint(end.X + ux * (best + 1.0), end.Y + uy * (best + 1.0));
    }

    /// <summary>A point inside the ring: its centroid where that is inside, else the middle of the widest horizontal chord.</summary>
    internal static DxfPoint InteriorPoint(PlanLoop ring)
    {
        var c = ring.Centroid();
        if (LoopGeometry.PointInPolygon(c, ring.Points)) return c;
        var (minX, minY, maxX, maxY) = ring.Bounds();
        DxfPoint best = c; double widest = -1;
        for (int k = 1; k < 8; k++)
        {
            double y = minY + (maxY - minY) * k / 8;
            var xs = new List<double>();
            for (int i = 0; i < ring.Points.Count; i++)
            {
                var (a, b) = (ring.Points[i], ring.Points[(i + 1) % ring.Points.Count]);
                if ((a.Y > y) == (b.Y > y)) continue;
                xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            xs.Sort();
            for (int j = 0; j + 1 < xs.Count; j += 2)
                if (xs[j + 1] - xs[j] > widest) { widest = xs[j + 1] - xs[j]; best = new DxfPoint((xs[j] + xs[j + 1]) / 2, y); }
        }
        return best;
    }
}
