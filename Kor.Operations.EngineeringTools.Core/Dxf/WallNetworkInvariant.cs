namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// WHAT THE WALL NETWORK MAY DO, CHECKED ON WHAT IT DID (2026-10-27). The network joins wall centrelines into joints: it may
/// move a wall's end ALONG the wall's own axis, never across it, and a header it puts over an opening lies on the axis of the
/// walls either side. Halifax LEVEL 1 broke the first at a 2 ft tolerance (four corner pieces tilted); 0f3e4ee4 put every
/// metric drawing's tolerance at the 2 in it was written for and broke it again across 27 of 56 gate sets - walls with
/// centrelines ~30 mm apart averaged into tilts, 139 headers run crooked between jambs out of line - and broke a third rule
/// besides: an outline drawn twice joined itself, so 31087's column was never a standalone stub and shipped as a wall.
///
/// <para>Three violations, each against the walls as they stood before the network (<see cref="PlanGeometrySet.WallsBeforeNetwork"/>):
/// TILT - a wall after the network parallel (within <see cref="ToleranceDegrees"/>) to no wall before it that it overlaps;
/// HEADER - an opening on the axis of no wall before it at either of its ends; SELF - a wall shorter than the stub length whose
/// every contact (a shared end, or another wall within an inch) is a near-copy of itself (parallel, overlapping, less than a
/// wall thickness apart).</para>
///
/// <para>COVERS: one sheet's classification, in the classifier's own unit. A wall drawn skewed is judged against its own
/// source wall, so a building drawn at 14.7 degrees is not a fault. DOES NOT: the composer's joint merge or storey stacking
/// (walls moved after classification); columns; a wall moved sideways but still parallel (a lateral shift up to the snap is
/// allowed - it is a joint, not a tilt). SAME-CLASS FAULT IT MISSES: a tilt under <see cref="ToleranceDegrees"/> - 25 mm over
/// 5 m is 0.29 degrees and passes; and a near-copy joined to one other wall as well as to itself reads as joined.</para>
/// </summary>
public static class WallNetworkInvariant
{
    public const double ToleranceDegrees = 0.5;

    public sealed record Violation(string Kind, DxfPoint Start, DxfPoint End, string Why);

    public static IReadOnlyList<Violation> Check(PlanGeometrySet geometry, double minWallLength, double inch = 1.0)
    {
        var found = new List<Violation>();
        var before = geometry.WallsBeforeNetwork;
        if (before.Count == 0) return found;
        double snap = WallNetwork.SnapTolerance * inch;

        foreach (var wall in geometry.Walls)
        {
            if (wall.Length <= 1e-9) continue;
            if (!before.Any(b => Parallel(wall.Start, wall.End, b.Start, b.End) && Overlaps(wall.Start, wall.End, b, (wall.Thickness + b.Thickness) / 2.0 + snap)))
                found.Add(new Violation("TILT", wall.Start, wall.End,
                    $"{Bearing(wall.Start, wall.End):0.##} deg, parallel to no wall it came from (nearest bearing {NearestBearing(wall.Start, wall.End, before):0.##} deg)"));
        }

        foreach (var opening in geometry.WallOpenings)
        {
            if (opening.Span <= 1e-9) continue;
            // On the jamb's AXIS, not its drawn segment: pass 1 may carry a jamb's end along its axis out to a corner (30990
            // S1.21, 295 mm past where it was drawn), so the end is held to the line, and the jamb to within a span of it.
            bool onJamb(DxfPoint end) => before.Any(b => Parallel(opening.Start, opening.End, b.Start, b.End)
                && DistanceToLine(end, b.Start, b.End) <= Math.Max(opening.Thickness, b.Thickness) / 2.0 + snap
                && DistanceToSegment(end, b.Start, b.End) <= opening.Span);
            if (!onJamb(opening.Start) && !onJamb(opening.End))
                found.Add(new Violation("HEADER", opening.Start, opening.End,
                    $"{Bearing(opening.Start, opening.End):0.##} deg, on the axis of neither jamb (nearest bearing {NearestBearing(opening.Start, opening.End, before):0.##} deg)"));
        }

        // Any contact counts - a shared end, or the segments within an inch (the classifier's RunsInto) - since either keeps a
        // short wall from being a stub; 31087's copies 21 mm apart were kept walls by RunsInto with no end shared.
        foreach (var wall in geometry.Walls.Where(w => w.Length < minWallLength))
        {
            var partners = geometry.Walls.Where(o => !ReferenceEquals(o, wall) && SegmentDistance(wall, o) <= inch).ToList();
            if (partners.Count > 0 && partners.All(p => NearCopy(wall, p)))
                found.Add(new Violation("SELF", wall.Start, wall.End,
                    $"{wall.Length:0.#} long, joined only to {partners.Count} near-cop{(partners.Count == 1 ? "y" : "ies")} of itself"));
        }
        return found;
    }

    private static double SegmentDistance(WallAxis a, WallAxis b) => Math.Min(
        Math.Min(DistanceToSegment(a.Start, b.Start, b.End), DistanceToSegment(a.End, b.Start, b.End)),
        Math.Min(DistanceToSegment(b.Start, a.Start, a.End), DistanceToSegment(b.End, a.Start, a.End)));

    private static bool NearCopy(WallAxis a, WallAxis b)
    {
        if (!Parallel(a.Start, a.End, b.Start, b.End, 2.0)) return false;
        double apart = Math.Min(DistanceToLine(b.Start, a.Start, a.End), DistanceToLine(b.End, a.Start, a.End));
        if (apart > Math.Max(a.Thickness, b.Thickness)) return false;
        double ux = (a.End.X - a.Start.X) / a.Length, uy = (a.End.Y - a.Start.Y) / a.Length;
        double t0 = (b.Start.X - a.Start.X) * ux + (b.Start.Y - a.Start.Y) * uy, t1 = (b.End.X - a.Start.X) * ux + (b.End.Y - a.Start.Y) * uy;
        double overlap = Math.Min(a.Length, Math.Max(t0, t1)) - Math.Max(0.0, Math.Min(t0, t1));
        return overlap >= 0.5 * Math.Min(a.Length, b.Length);
    }

    private static bool Overlaps(DxfPoint s, DxfPoint e, WallAxis b, double reach)
    {
        var mid = new DxfPoint((s.X + e.X) / 2.0, (s.Y + e.Y) / 2.0);
        return DistanceToSegment(mid, b.Start, b.End) <= reach + s.DistanceTo(e) / 2.0 && DistanceToLine(mid, b.Start, b.End) <= reach;
    }

    private static double Bearing(DxfPoint s, DxfPoint e)
    {
        double d = Math.Atan2(e.Y - s.Y, e.X - s.X) * 180.0 / Math.PI;
        return ((d % 180.0) + 180.0) % 180.0;
    }

    private static bool Parallel(DxfPoint s1, DxfPoint e1, DxfPoint s2, DxfPoint e2, double tolerance = ToleranceDegrees)
    {
        double d = Math.Abs(Bearing(s1, e1) - Bearing(s2, e2));
        return Math.Min(d, 180.0 - d) <= tolerance;
    }

    private static double NearestBearing(DxfPoint s, DxfPoint e, IReadOnlyList<WallAxis> before)
    {
        var mid = new DxfPoint((s.X + e.X) / 2.0, (s.Y + e.Y) / 2.0);
        var b = before.MinBy(w => DistanceToSegment(mid, w.Start, w.End));
        return b is null ? double.NaN : Bearing(b.Start, b.End);
    }

    private static double DistanceToLine(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double len = a.DistanceTo(b);
        return len <= 1e-12 ? p.DistanceTo(a) : Math.Abs((b.X - a.X) * (a.Y - p.Y) - (a.X - p.X) * (b.Y - a.Y)) / len;
    }

    private static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 <= 1e-24) return p.DistanceTo(a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0.0, 1.0);
        return p.DistanceTo(new DxfPoint(a.X + t * dx, a.Y + t * dy));
    }
}
