namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// A slab edge that finishes on a layer this tool does not read is still a slab edge.
///
/// WHY THIS EXISTS. 31168's C-LEVEL 3 shipped at 12,862 sq ft when the floor is nearly twice that.
/// Andrea Neuviale, 31 August: "it is not matching the outer edge of the slab... It's only grabbing
/// this, which is just a step, actually... But we will want the outer edge, which is also a
/// continuous line."
///
/// She was right that it is continuous. Measured on the sheet, the outer ring is one chain of ten
/// points whose two loose ends are joined by exactly two more segments:
///
///   JBP_C_SLABEDG    (40475.2, 30973.7) -> (40475.2, 28370.1)    the long left edge
///   JBP_C_SLABEDG    (40475.2, 28370.1) -> (40475.7, 28369.8)    a 0.6 in stub, chain end
///   JBP_C_B_STRUCT   (40475.7, 28369.8) -> (40647.8, 28264.7)    201.6 in, the chamfer
///   JBP_C_B_STRUCT   (40647.8, 28264.7) -> (40647.8, 28242.4)     22.3 in, the return
///   JBP_C_SLABEDG    (40647.8, 28242.4) -> (40647.8, 27926.7)    chain start
///
/// Endpoint to endpoint, no gap anywhere. The ring closes; it just does not close on ONE LAYER.
/// And JBP_C_B_STRUCT is excluded by a banked ruling -- "not structural and must not be modelled"
/// -- which is true of the 1,078 other entities on it and false of these two. A rule with no scope
/// cannot be contradicted by the one drawing where it does not hold, so the edge stayed open, the
/// flood fill found only the inner step, and it shipped with nothing said.
///
/// WHAT THIS IS NOT. Not a wider bridge, not a tolerance, not a rescue pass judged on area. The
/// borrowed linework has to meet the chain's loose ends within the ordinary join tolerance -- the
/// same exactness any other corner is held to. Where the drawing really is open, nothing here
/// closes it, and the storey is still reported as having an edge that would not close.
/// </summary>
public static class SlabEdgeClosure
{
    public sealed record Closed(IReadOnlyList<DxfPoint> Ring, IReadOnlyList<DxfSegment> Borrowed);

    /// <summary>
    /// Completes an open chain using linework from layers with no structural role, or null where it
    /// cannot be completed exactly.
    /// </summary>
    /// <param name="chain">An open chain: first and last point are its loose ends.</param>
    /// <param name="others">Segments this tool reads for shape but does not model.</param>
    /// <param name="joinTolerance">Endpoints closer than this are one node. dxf.join-tolerance.</param>
    /// <param name="mostSegments">
    /// How many borrowed pieces a closure may use. A slab edge finishes in a corner or two, not in
    /// a wander through the drawing: keeping this small is what stops the search finding a way home
    /// through unrelated linework.
    /// </param>
    public static Closed? Close(
        IReadOnlyList<DxfPoint> chain,
        IReadOnlyList<DxfSegment> others,
        double joinTolerance,
        int mostSegments = 6)
    {
        if (chain.Count < 3 || others.Count == 0) return null;

        DxfPoint from = chain[^1];
        DxfPoint to = chain[0];
        if (Near(from, to, joinTolerance)) return null; // already closed; not ours to touch

        var used = new bool[others.Count];
        var path = new List<DxfSegment>();

        return Walk(from) ? Finish() : null;

        bool Walk(DxfPoint at)
        {
            if (path.Count >= mostSegments) return false;

            for (int i = 0; i < others.Count; i++)
            {
                if (used[i]) continue;

                var s = others[i];
                DxfPoint next;
                if (Near(s.Start, at, joinTolerance)) next = s.End;
                else if (Near(s.End, at, joinTolerance)) next = s.Start;
                else continue;

                used[i] = true;
                path.Add(s);

                if (Near(next, to, joinTolerance)) return true;
                if (Walk(next)) return true;

                path.RemoveAt(path.Count - 1);
                used[i] = false;
            }

            return false;
        }

        Closed Finish()
        {
            var ring = new List<DxfPoint>(chain);
            DxfPoint at = from;
            foreach (var s in path)
            {
                DxfPoint next = Near(s.Start, at, joinTolerance) ? s.End : s.Start;
                // The last hop lands on the ring's first point, which is already there.
                if (!Near(next, to, joinTolerance)) ring.Add(next);
                at = next;
            }

            return new Closed(ring, path.ToList());
        }
    }

    /// <summary>
    /// Whether a completed ring is one a floor could be made from. The same two guards every other
    /// plate passes -- an outline that crosses itself, or is cut nearly in two by a slot, is not a
    /// floor however exactly it closed.
    /// </summary>
    public static bool IsUsable(IReadOnlyList<DxfPoint> ring, double minNeckWidth) =>
        ring.Count >= 4
        && !LoopGeometry.SelfIntersects(ring)
        && !LoopGeometry.HasNarrowNeck(ring, minNeckWidth);

    private static bool Near(DxfPoint a, DxfPoint b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
}
