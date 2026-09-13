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
        => Decompose(loop, options, out _);

    /// <summary>
    /// As above, and hands back the edges no panel used — the faces of the outline this pass could
    /// not pair inside it. An OPEN chain is a shape only by accident of where the drafting broke:
    /// 31168's tower A core draws its two 30x41 returns and the 9 m bottom face of the south wall
    /// between them as ONE line, so the chain is a U, its returns pair inside it, and the bottom face
    /// — whose partner, the wall's top face, is a separate segment meeting the returns' inner faces
    /// at T-junctions — was never offered to the pooled pass because the chain had "been read". A
    /// chain is read edge by edge, and what is left over is offered on (intake step 56, 2026-09-13).
    /// For a CLOSED outline the leftovers are its end caps and the like, and the caller leaves them.
    /// </summary>
    public static IReadOnlyList<WallAxis> Decompose(PlanLoop loop, PlanClassificationOptions options, out IReadOnlyList<(DxfPoint A, DxfPoint B)> unused)
    {
        unused = Array.Empty<(DxfPoint, DxfPoint)>();
        var pts = loop.Points;
        int n = pts.Count;
        if (n < 4) return Array.Empty<WallAxis>();

        var edges = new List<(DxfPoint A, DxfPoint B)>(n);
        for (int i = 0; i < n; i++) edges.Add((pts[i], pts[(i + 1) % n]));

        var used = new bool[edges.Count];
        var walls = new List<WallAxis>();

        // Pairings that are sound in every way except that they are nearly square.
        var deferred = new List<(int I, int J, DxfPoint Start, DxfPoint End, double Thickness)>();

        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i]) continue;

            var (ai, bi) = edges[i];
            double lengthI = ai.DistanceTo(bi);

            // A FACE, not a wall. Demanding 48" here was the 48" rule leaking into the decomposer
            // for the second time: a stepped block's limbs measure 31x28 and 14x36, every face
            // short of 48, so none was ever considered and the whole block fell through to a
            // single fat pier or to columns. Her own 31138 model settles what a wall panel may
            // measure — it carries them at 9, 12, 15, 23 and 27 inches, all with pier labels — so
            // the floor belongs with the other face-scale threshold, not with the wall-vs-column
            // rule. Whether a short element is a column is decided later, and on connection.
            if (!LoopGeometry.Within(options.MinPanelOverlap, lengthI)) continue;    // to the micron: a 12" face meets a 12" floor in every frame

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
                if (LoopGeometry.Beyond(options.MinWallThickness - ThicknessSlack, separation) ||
                    LoopGeometry.Beyond(separation, options.MaxWallThickness + ThicknessSlack)) continue;

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
                if (LoopGeometry.Beyond(options.MinPanelOverlap, overlap)) continue;

                // Concrete, or a void? The material between two faces of one wall lies inside
                // the outline; the gap between walls on opposite sides of a shaft lies outside.
                // Without this, a stair core reads as one 36"-thick wall spanning the opening.
                double side = Math.Sign(d1 + d2) >= 0 ? 1.0 : -1.0;
                double midT = (t0 + t1) / 2.0, midOffset = separation / 2.0 * side;
                var probe = new DxfPoint(
                    ai.X + ux * midT + nx * midOffset,
                    ai.Y + uy * midT + ny * midOffset);
                // A PROBE ON A DRAWN LINE IS NOT A COIN TOSS (intake step 56, 2026-09-13). A wall drawn with both
                // faces AND its centreline puts the probe between the faces exactly on the centreline, and a ray
                // cast at a point on an edge answers with the rounding noise of the frame: 31170's LEVEL 2 read
                // a 229 mm wall as 114 in one frame and 229 in the other. Concrete either side of a drawn line is
                // concrete; the probe is asked a hair to each side, and either says so.
                double hair = 0.01 * Math.Max(1e-9, separation);
                if (!LoopGeometry.PointInPolygon(new DxfPoint(probe.X + nx * hair, probe.Y + ny * hair), pts)
                    && !LoopGeometry.PointInPolygon(new DxfPoint(probe.X - nx * hair, probe.Y - ny * hair), pts)) continue;

                // Prefer the closest opposite face: across a wall junction several faces overlap, and
                // the true partner is the nearest one, not the longest. At one separation, the longest
                // shared face; and at ONE LENGTH TOO, the first along this face. A TIE IS A TIE, TO THE
                // MICRON (intake step 56, 2026-09-13): three equal 6" loops along the tops of two piers,
                // their bottom edges run into one line by the dash joiner, offered this edge three
                // partners at 154.432 by 5,539.232 - and the same drawings shifted 5 m on the page made
                // one overlap 5,539.232000000002, two trillionths longer, which "overlap > bestOverlap"
                // took for the longer face. The frame decided the wall; nothing in the drawing did.
                if (bestJ < 0 || separation < bestDistance - 1e-6 ||
                    (Math.Abs(separation - bestDistance) < 1e-6 && (overlap > bestOverlap + 1e-6 ||
                        (Math.Abs(overlap - bestOverlap) <= 1e-6 && t0 < bestT0 - 1e-6))))
                {
                    bestJ = j;
                    bestOverlap = overlap;
                    bestDistance = separation;
                    bestT0 = t0;
                    bestT1 = t1;
                    bestSide = side;
                }
            }

            if (bestJ < 0) continue;

            // How long the panel is, is how far its MATERIAL runs — not how far its two faces
            // happen to overlap.
            //
            // Where a wall meets another, one of its faces stops early because the other wall's
            // body takes over: tower A's core has a 30x41 return turned up at each end of its
            // bottom wall, and only 13" of the return's inner face is exposed. Judged on that 13
            // the return is a sliver and was dropped — with it went the header over the door
            // beside it, which is the engineer's "Tower A core missing return walls (red) and
            // headers (blue)". Both marks, one cause.
            //
            // The midline is the honest measure: walk it out from the overlap while it stays
            // inside the outline. Material inside the outline is concrete; a void is not, so this
            // cannot run a wall out across an opening.
            (double runT0, double runT1) = MaterialRun(pts, ai, ux, uy, nx, ny, bestDistance * bestSide / 2.0,
                                                       bestT0, bestT1, lengthI);
            bestT0 = runT0;
            bestT1 = runT1;

            double half0 = bestDistance / 2.0 * bestSide;
            if (LoopGeometry.Beyond(bestDistance * options.MinPanelAspect, bestT1 - bestT0))
            {
                // Nearly square, so it is either a leftover sliver or a limb of a bigger shape.
                // Which one cannot be told from its own proportions — held over and decided below,
                // on whether it joins anything.
                deferred.Add((i, bestJ,
                    new DxfPoint(ai.X + ux * bestT0 + nx * half0, ai.Y + uy * bestT0 + ny * half0),
                    new DxfPoint(ai.X + ux * bestT1 + nx * half0, ai.Y + uy * bestT1 + ny * half0),
                    bestDistance));
                continue;
            }

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

        // A near-square piece that RUNS INTO one of the panels above is the limb of a corner, and
        // dropping it is what turned tower B's north corners into one fat wall across the top:
        // each corner is an L, a 67x28 north wall with a 36-thick leg turned down beside it, and
        // the leg measures 42x36 — aspect 1.17, just under the sliver rule. The whole L then
        // resolved as a single 42-thick panel, which is thicker than anything drawn there, and
        // the doorway below the leg went with it. "Still have that problem with the north corner
        // walls for tower B."
        //
        // A genuine leftover sliver touches nothing, because the faces that would have joined it
        // to a wall were consumed with that wall. So joining is what separates the two.
        foreach (var (i, j, start, end, thickness) in deferred)
        {
            if (used[i] || used[j]) continue;
            if (!walls.Any(w => LoopGeometry.SegmentsMeet(start, end, w.Start, w.End, 1.0))) continue;

            walls.Add(new WallAxis(start, end, thickness, loop.Layer));
            used[i] = true;
            used[j] = true;
        }

        // the closing edge of an open chain (last point back to the first) is not drawn; only drawn edges are left over
        int drawn = loop.ClosedExactly ? n : n - 1;
        unused = Enumerable.Range(0, drawn).Where(e => !used[e]).Select(e => edges[e]).ToList();
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

    /// <summary>
    /// How far the concrete between two faces runs, starting from where the faces overlap.
    ///
    /// Walks the panel's midline out in both directions and stops where it leaves the outline.
    /// Bounded by the face it is measured along, so a panel never claims material past its own
    /// end, and stepped in half-inches — a doorway is never narrower than that, so a step cannot
    /// stride over an opening and weld two walls into one.
    /// </summary>
    private static (double T0, double T1) MaterialRun(
        IReadOnlyList<DxfPoint> outline,
        DxfPoint origin, double ux, double uy, double nx, double ny, double halfOffset,
        double t0, double t1, double faceLength)
    {
        const double step = 0.5;

        bool Concrete(double t)
        {
            // asked a hair to each side of the midline, as the pairing probe is: the midline of a wall drawn
            // with its centreline lies ON that line, and a point on an edge is the frame's coin toss
            double hair = 0.01 * Math.Max(1e-9, Math.Abs(halfOffset));
            var p = new DxfPoint(origin.X + ux * t + nx * halfOffset, origin.Y + uy * t + ny * halfOffset);
            return LoopGeometry.PointInPolygon(new DxfPoint(p.X + nx * hair, p.Y + ny * hair), outline)
                || LoopGeometry.PointInPolygon(new DxfPoint(p.X - nx * hair, p.Y - ny * hair), outline);
        }

        double low = Math.Max(0, t0), high = Math.Min(faceLength, t1);
        if (high <= low) return (t0, t1);

        while (low - step >= -1e-9 && Concrete(low - step / 2.0)) low -= step;
        while (high + step <= faceLength + 1e-9 && Concrete(high + step / 2.0)) high += step;

        return (Math.Max(0, low), Math.Min(faceLength, high));
    }
}
