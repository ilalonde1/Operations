namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Splits a wall outline into the individual panels it draws.
///
/// Drafting outlines a core or a run of walls as one closed ribbon: the polygon
/// traces both faces of every wall in the group. Treating that ring as a single
/// member gives a meaningless 81"x65" block, so each face is paired with the face
/// opposite it and the pair becomes one wall — centreline midway between them,
/// thickness equal to their separation.
/// </summary>
public static class WallOutlineDecomposer
{
    /// <summary>How parallel two faces must be before they can be opposite sides of one wall.</summary>
    private const double ParallelDot = 0.985;

    public static IReadOnlyList<WallAxis> Decompose(PlanLoop loop, PlanClassificationOptions options)
    {
        var pts = loop.Points;
        int n = pts.Count;
        if (n < 4) return Array.Empty<WallAxis>();

        var edges = new List<(DxfPoint A, DxfPoint B)>(n);
        for (int i = 0; i < n; i++) edges.Add((pts[i], pts[(i + 1) % n]));

        var used = new bool[edges.Count];
        var walls = new List<WallAxis>();

        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i]) continue;

            var (ai, bi) = edges[i];
            double lengthI = ai.DistanceTo(bi);
            if (lengthI < options.MinWallLength) continue;

            double ux = (bi.X - ai.X) / lengthI, uy = (bi.Y - ai.Y) / lengthI;
            double nx = -uy, ny = ux;

            int bestJ = -1;
            double bestOverlap = 0, bestDistance = 0, bestT0 = 0, bestT1 = 0, bestSide = 0;

            for (int j = 0; j < edges.Count; j++)
            {
                if (j == i || used[j]) continue;

                var (aj, bj) = edges[j];
                double lengthJ = aj.DistanceTo(bj);
                if (lengthJ < 1e-6) continue;

                double vx = (bj.X - aj.X) / lengthJ, vy = (bj.Y - aj.Y) / lengthJ;
                if (Math.Abs(ux * vx + uy * vy) < ParallelDot) continue;

                // Perpendicular separation, measured from edge i's line.
                double d1 = (aj.X - ai.X) * nx + (aj.Y - ai.Y) * ny;
                double d2 = (bj.X - ai.X) * nx + (bj.Y - ai.Y) * ny;
                if (Math.Sign(d1) != Math.Sign(d2) && Math.Abs(d1) > 1e-6 && Math.Abs(d2) > 1e-6) continue;

                double separation = (Math.Abs(d1) + Math.Abs(d2)) / 2.0;
                if (separation < options.MinWallThickness || separation > options.MaxWallThickness) continue;

                // Overlap of the two faces along edge i's direction.
                double ta0 = 0, ta1 = lengthI;
                double tb0 = (aj.X - ai.X) * ux + (aj.Y - ai.Y) * uy;
                double tb1 = (bj.X - ai.X) * ux + (bj.Y - ai.Y) * uy;
                if (tb0 > tb1) (tb0, tb1) = (tb1, tb0);

                double t0 = Math.Max(ta0, tb0), t1 = Math.Min(ta1, tb1);
                double overlap = t1 - t0;
                if (overlap < options.MinWallLength) continue;

                // Prefer the longest shared face; break ties on the thinner pairing.
                if (overlap > bestOverlap || (Math.Abs(overlap - bestOverlap) < 1e-6 && separation < bestDistance))
                {
                    bestJ = j;
                    bestOverlap = overlap;
                    bestDistance = separation;
                    bestT0 = t0;
                    bestT1 = t1;
                    bestSide = Math.Sign(d1 + d2) >= 0 ? 1.0 : -1.0;
                }
            }

            if (bestJ < 0) continue;

            double half = bestDistance / 2.0 * bestSide;
            var start = new DxfPoint(ai.X + ux * bestT0 + nx * half, ai.Y + uy * bestT0 + ny * half);
            var end = new DxfPoint(ai.X + ux * bestT1 + nx * half, ai.Y + uy * bestT1 + ny * half);

            walls.Add(new WallAxis(start, end, bestDistance, loop.Layer));
            used[i] = true;
            used[bestJ] = true;
        }

        return walls;
    }
}
