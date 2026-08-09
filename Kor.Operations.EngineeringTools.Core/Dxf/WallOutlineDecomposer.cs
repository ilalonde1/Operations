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

    /// <summary>
    /// Slack on the thickness limits. Coordinates carry drift from the CAD export, so a wall
    /// drawn at exactly the maximum measures a fraction over it — without this, walls on the
    /// limit are kept or discarded according to rounding noise.
    /// </summary>
    private const double ThicknessSlack = 0.01;

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
                if (separation < options.MinWallThickness - ThicknessSlack ||
                    separation > options.MaxWallThickness + ThicknessSlack) continue;

                // Overlap of the two faces along edge i's direction.
                double ta0 = 0, ta1 = lengthI;
                double tb0 = (aj.X - ai.X) * ux + (aj.Y - ai.Y) * uy;
                double tb1 = (bj.X - ai.X) * ux + (bj.Y - ai.Y) * uy;
                if (tb0 > tb1) (tb0, tb1) = (tb1, tb0);

                double t0 = Math.Max(ta0, tb0), t1 = Math.Min(ta1, tb1);
                double overlap = t1 - t0;

                // How much face two walls must share to be one panel — not how long an element must
                // be to count as a wall. Those were the same number once, and raising the second to
                // 48" stopped every corner's short limb from decomposing.
                if (overlap < options.MinPanelOverlap) continue;

                // Concrete, or a void? The material between two faces of one wall lies inside
                // the outline; the gap between walls on opposite sides of a shaft lies outside.
                // Without this, a stair core reads as one 36"-thick wall spanning the opening.
                double side = Math.Sign(d1 + d2) >= 0 ? 1.0 : -1.0;
                double midT = (t0 + t1) / 2.0, midOffset = separation / 2.0 * side;
                var probe = new DxfPoint(
                    ai.X + ux * midT + nx * midOffset,
                    ai.Y + uy * midT + ny * midOffset);
                if (!LoopGeometry.PointInPolygon(probe, pts)) continue;

                // Prefer the longest shared face; break ties on the thinner pairing.
                // Prefer the closest opposite face: across a wall junction several faces
                // overlap, and the true partner is the nearest one, not the longest.
                if (bestJ < 0 || separation < bestDistance - 1e-6 ||
                    (Math.Abs(separation - bestDistance) < 1e-6 && overlap > bestOverlap))
                {
                    bestJ = j;
                    bestOverlap = overlap;
                    bestDistance = separation;
                    bestT0 = t0;
                    bestT1 = t1;
                    bestSide = side;
                }
            }

            if (bestJ < 0 || bestOverlap < bestDistance * options.MinPanelAspect) continue;

            double half = bestDistance / 2.0 * bestSide;
            var start = new DxfPoint(ai.X + ux * bestT0 + nx * half, ai.Y + uy * bestT0 + ny * half);
            var end = new DxfPoint(ai.X + ux * bestT1 + nx * half, ai.Y + uy * bestT1 + ny * half);

            walls.Add(new WallAxis(start, end, bestDistance, loop.Layer));
            used[i] = true;
            used[bestJ] = true;

            // Consume the short faces that cap this wall's ends. Left unused they pair with
            // each other and produce a sliver as wide as the wall is thick, which is what
            // forced the aspect rule to be strict enough to also reject real piers.
            ConsumeEndFaces(edges, used, ai, ux, uy, nx, ny, bestT0, bestT1, bestDistance * bestSide);
        }

        return walls;
    }

    /// <summary>
    /// Marks the edges that close the ends of a wall just paired, so they cannot be read as
    /// faces of some other member. An end face runs across the wall: both its endpoints sit
    /// within the band between the two faces, at one end of the wall's run.
    /// </summary>
    private static void ConsumeEndFaces(
        IReadOnlyList<(DxfPoint A, DxfPoint B)> edges, bool[] used,
        DxfPoint origin, double ux, double uy, double nx, double ny,
        double t0, double t1, double signedThickness)
    {
        const double slack = 1.0;
        double vLow = Math.Min(0, signedThickness) - slack;
        double vHigh = Math.Max(0, signedThickness) + slack;

        for (int k = 0; k < edges.Count; k++)
        {
            if (used[k]) continue;

            bool inside = true;
            foreach (var p in new[] { edges[k].A, edges[k].B })
            {
                double t = (p.X - origin.X) * ux + (p.Y - origin.Y) * uy;
                double v = (p.X - origin.X) * nx + (p.Y - origin.Y) * ny;
                if (t < t0 - slack || t > t1 + slack || v < vLow || v > vHigh) { inside = false; break; }
            }

            if (inside) used[k] = true;
        }
    }
}
