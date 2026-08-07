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
            // Bucket by the infinite line a segment sits on: its direction, and its
            // perpendicular distance from the origin.
            var lines = new Dictionary<(string Layer, long Angle, long Offset), List<DxfSegment>>();

            foreach (var seg in layerGroup)
            {
                double dx = seg.End.X - seg.Start.X, dy = seg.End.Y - seg.Start.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-9) continue;

                double ux = dx / length, uy = dy / length;
                if (ux < 0 || (Math.Abs(ux) < 1e-12 && uy < 0)) { ux = -ux; uy = -uy; }

                double angle = Math.Atan2(uy, ux) * 180.0 / Math.PI;
                double offset = ux * seg.Start.Y - uy * seg.Start.X;

                var key = (layerGroup.Key,
                    (long)Math.Round(angle / angleToleranceDegrees),
                    (long)Math.Round(offset / offsetTolerance));

                if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<DxfSegment>();
                list.Add(seg);
            }

            foreach (var line in lines.Values)
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

                void Emit(double lo, double hi)
                {
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
                    if (spans[i].Lo - runHi <= maxGap)
                    {
                        runHi = Math.Max(runHi, spans[i].Hi);
                    }
                    else
                    {
                        Emit(runLo, runHi);
                        runLo = spans[i].Lo;
                        runHi = spans[i].Hi;
                    }
                }
                Emit(runLo, runHi);
            }
        }

        return result;
    }
}
