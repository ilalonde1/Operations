namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Rebuilds lines that the drawing renders as dashes.
///
/// A hidden or dashed edge arrives from the CAD export as a run of short segments lying on
/// one line, separated by the dash gap — on KOR's plans, a constant 11". Treated as separate
/// pieces they can never close into an outline. Joining is safe because only segments that
/// are collinear are joined: the gap is filled along the line the drawing already draws, so
/// no shape is invented and no corner is cut.
/// </summary>
public static class DashedLineJoiner
{
    public static IReadOnlyList<DxfSegment> Join(
        IEnumerable<DxfSegment> segments,
        double maxGap = 24.0,
        double angleToleranceDegrees = 0.5,
        double offsetTolerance = 0.15)
    {
        var result = new List<DxfSegment>();

        foreach (var layerGroup in segments.GroupBy(s => s.Layer))
        {
            // Group by the infinite line a segment sits on: its direction, and its perpendicular
            // distance from the origin. BY DISTANCE, NOT BY CELL (intake step 56, 2026-09-12): these
            // were cells of the tolerance, so two dashes of one line either side of a cell boundary
            // were two lines, and where the boundaries fell depended on where the drawing's origin
            // was — the same drawings shifted on the page joined their dashes differently. Sorted by
            // angle, a run of segments within the angle tolerance of its first is one direction;
            // within it, sorted by offset, each segment within the offset tolerance of the one
            // before continues the line. A dash gap along the line is the next loop's business.
            var placed = new List<(double Angle, double Ux, double Uy, DxfSegment Seg)>();
            foreach (var seg in layerGroup)
            {
                // an edge of a closed outline is a finished shape's edge, not a dash: left as drawn
                if (seg.OfClosedOutline) { result.Add(seg); continue; }
                double dx = seg.End.X - seg.Start.X, dy = seg.End.Y - seg.Start.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-9) continue;

                double ux = dx / length, uy = dy / length;
                if (ux < 0 || (Math.Abs(ux) < 1e-12 && uy < 0)) { ux = -ux; uy = -uy; }

                double angle = Math.Atan2(uy, ux) * 180.0 / Math.PI;
                placed.Add((angle, ux, uy, seg));
            }

            var lines = new List<List<DxfSegment>>();
            var byAngle = placed.OrderBy(p => p.Angle).ThenBy(p => p.Seg.Start.X).ThenBy(p => p.Seg.Start.Y).ToList();
            for (int i = 0; i < byAngle.Count;)
            {
                int j = i + 1;
                while (j < byAngle.Count && byAngle[j].Angle - byAngle[i].Angle <= angleToleranceDegrees) j++;
                // OFFSETS ARE MEASURED AGAINST THE DIRECTION'S FIRST SEGMENT (audit F8, step 61): each segment's offset
                // was taken along its own normal from the page's origin, so two dashes half a degree apart and fifty
                // metres from the origin sat 436 mm apart in offset - two lines - and where the origin was decided
                // it. From the first segment's start, along the first segment's normal, the offset is the distance
                // between the dashes' lines and nothing else.
                var first = byAngle[i];
                var direction = byAngle.GetRange(i, j - i)
                    .Select(p => (Offset: first.Ux * (p.Seg.Start.Y - first.Seg.Start.Y) - first.Uy * (p.Seg.Start.X - first.Seg.Start.X), p.Seg))
                    .OrderBy(p => p.Offset).ToList();
                var line = new List<DxfSegment> { direction[0].Seg };
                for (int k = 1; k < direction.Count; k++)
                {
                    if (direction[k].Offset - direction[k - 1].Offset > offsetTolerance) { lines.Add(line); line = new List<DxfSegment>(); }
                    line.Add(direction[k].Seg);
                }
                lines.Add(line);
                i = j;
            }

            foreach (var line in lines)
            {
                if (line.Count == 1) { result.Add(line[0]); continue; }

                double dx = line[0].End.X - line[0].Start.X, dy = line[0].End.Y - line[0].Start.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                double ux = dx / len, uy = dy / len;
                if (ux < 0 || (Math.Abs(ux) < 1e-12 && uy < 0)) { ux = -ux; uy = -uy; }

                double Project(DxfPoint p) => p.X * ux + p.Y * uy;

                var spans = line
                    .Select(s =>
                    {
                        double a = Project(s.Start), b = Project(s.End);
                        return a <= b ? (Lo: a, Hi: b, Seg: s) : (Lo: b, Hi: a, Seg: s);
                    })
                    .OrderBy(s => s.Lo)
                    .ToList();

                double runLo = spans[0].Lo, runHi = spans[0].Hi;
                var reference = spans[0].Seg;
                var run = new List<DxfSegment> { spans[0].Seg };

                void Emit(double lo, double hi)
                {
                    // A run of one segment is that segment, untouched: rebuilding it from a projection puts
                    // rounding noise on its ends, and an outline's corner has to stay the corner it was drawn.
                    if (run.Count == 1) { result.Add(run[0]); return; }
                    // Rebuild the segment on the same line, spanning the merged run.
                    double baseProjection = Project(reference.Start);
                    var start = new DxfPoint(
                        reference.Start.X + ux * (lo - baseProjection),
                        reference.Start.Y + uy * (lo - baseProjection));
                    var end = new DxfPoint(
                        reference.Start.X + ux * (hi - baseProjection),
                        reference.Start.Y + uy * (hi - baseProjection));
                    result.Add(new DxfSegment(reference.Layer, start, end));
                }

                for (int i = 1; i < spans.Count; i++)
                {
                    // Loose lines that touch or overlap along one line are one line drawn in pieces - a face
                    // cut where another wall's outline crosses it - and are joined as dashes are. The edges of
                    // closed outlines never reach here (OfClosedOutline): 31168's tower A core draws its two
                    // 30x41 returns and the 28" wall between them as three closed shapes whose bottom edges
                    // lie on one line, and joined into one segment they took the wall's outline apart (intake
                    // step 56, 2026-09-13: 18 core walls in one frame and 8 in the other, on which pair the
                    // cells merged).
                    double gap = spans[i].Lo - runHi;
                    if (LoopGeometry.Within(gap, maxGap))                     // a drafted gap equal to the maximum is within it (audit F13)
                    {
                        runHi = Math.Max(runHi, spans[i].Hi);
                        run.Add(spans[i].Seg);
                    }
                    else
                    {
                        Emit(runLo, runHi);
                        runLo = spans[i].Lo;
                        runHi = spans[i].Hi;
                        reference = spans[i].Seg;
                        run = new List<DxfSegment> { spans[i].Seg };
                    }
                }
                Emit(runLo, runHi);
            }
        }

        return result;
    }
}
