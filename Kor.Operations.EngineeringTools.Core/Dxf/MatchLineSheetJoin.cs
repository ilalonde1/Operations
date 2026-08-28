namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>The seam a drawing was split on: the match line's two ends, in drawing units.</summary>
public sealed record MatchLineSeam(DxfPoint Start, DxfPoint End)
{
    /// <summary>Two sheets carry the SAME seam when their match lines sit on top of one another.
    /// A drawing set draws the same line on both sides of a split, in the same place — that is what
    /// makes it a match line rather than two unrelated lines.</summary>
    public bool SameAs(MatchLineSeam other, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(other);

        return (Near(Start, other.Start, tolerance) && Near(End, other.End, tolerance))
            || (Near(Start, other.End, tolerance) && Near(End, other.Start, tolerance));
    }

    private static bool Near(DxfPoint a, DxfPoint b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
}

/// <summary>
/// Sheets that are two halves of one plan, and the seam they were split on.
///
/// A structural drawing too wide for one sheet is cut on a MATCH LINE and drawn twice, and the
/// engineer reads the two together: "if you make the match lines correspond, you'll get the full
/// structure". Andrea Neuviale, 2026-08-28, about this exact job.
///
/// Read separately, neither half closes a slab edge — the edge runs off the page at the seam — so
/// the tool refuses to make a floor and the parkade comes out with no slab. That is not a
/// classification problem and no threshold fixes it: half a plan is not a plan.
///
/// This is COMPOSE ONCE, CUT AFTER applied one level earlier than the composer applies it. The
/// composer joins buildings into a site and then cuts; this joins sheets into a plan before either
/// happens — including sheets a building filter would otherwise have thrown away, because the half
/// of the parkade drawn on somebody else's sheet is still this building's parkade.
/// </summary>
public static class MatchLineSheetJoin
{
    /// <summary>Layer name fragments that mean "this is a match line". A firm's own layer standard
    /// decides; JBP's is JBP_G_MATCH_LINES. Overridable from the rules database.</summary>
    public static readonly IReadOnlyList<string> DefaultLayerPatterns = new[] { "MATCH" };

    /// <summary>How far apart two match lines may sit and still be the same seam. Half an inch in
    /// millimetre drawings; the two sheets are drawn from one model so they land on each other.</summary>
    public const double DefaultTolerance = 12.0;

    /// <summary>The seam a sheet was split on, or null if it carries no match line.</summary>
    public static MatchLineSeam? SeamOf(
        IEnumerable<DxfSegment> segments, IReadOnlyList<string>? layerPatterns = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var patterns = layerPatterns is { Count: > 0 } ? layerPatterns : DefaultLayerPatterns;

        // The longest line on a match-line layer. A sheet may carry the leader and label as well;
        // the seam is the line that spans the drawing.
        DxfSegment? longest = null;
        foreach (var s in segments)
        {
            if (!patterns.Any(p => s.Layer.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (longest is null || s.Length > longest.Length) longest = s;
        }

        return longest is null ? null : new MatchLineSeam(longest.Start, longest.End);
    }

    /// <summary>A set of sheets that together make one plan.</summary>
    /// <param name="Files">Every sheet in the group, in the order given.</param>
    /// <param name="Seam">The match line they share.</param>
    public sealed record SheetGroup(IReadOnlyList<string> Files, MatchLineSeam Seam);

    /// <summary>
    /// How lopsided a sheet's linework must be about the seam before it counts as one HALF of a
    /// split plan rather than a whole plan that happens to carry the line.
    ///
    /// A LINE ON THE MATCH-LINE LAYER IS NOT EVIDENCE OF A SPLIT. On 31138 all twenty-eight sheets
    /// carry the identical line at the identical place: it is on the drawing template, and joining
    /// sheets on it fused two different elevations of level 1 into one plan. On 31168 the seam
    /// really does divide the drawing — 2.8:1 one way and 8.2:1 the other — while 31138's sheets sit
    /// across it at 1.13:1 and 1.41:1.
    ///
    /// So the test is physical and not a name: the match line must actually separate the structure.
    /// Both real cases are far from this margin, in opposite directions.
    /// </summary>
    public const double MinimumSideRatio = 2.0;

    /// <summary>
    /// Groups sheets that carry the same seam, land on the same storeys, AND whose linework the seam
    /// genuinely divides — each sheet mostly on its own side, the two on opposite sides.
    /// </summary>
    /// <param name="sheets">File, its seam (null if none), the storeys it matched, and its linework.</param>
    public static IReadOnlyList<SheetGroup> Group(
        IEnumerable<(string File, MatchLineSeam? Seam, IReadOnlyList<string> Storeys, IReadOnlyList<DxfSegment> Segments)> sheets,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(sheets);

        var withSeam = sheets.Where(s => s.Seam is not null && s.Storeys.Count > 0).ToList();
        var groups = new List<SheetGroup>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in withSeam)
        {
            if (taken.Contains(a.File)) continue;

            int sideA = DominantSide(a.Segments, a.Seam!);
            if (sideA == 0) continue;                     // straddles the line: a whole plan

            var members = new List<string> { a.File };
            foreach (var b in withSeam)
            {
                if (taken.Contains(b.File) || ReferenceEquals(a.File, b.File) || b.File == a.File) continue;
                if (!a.Seam!.SameAs(b.Seam!, tolerance)) continue;
                if (!a.Storeys.Intersect(b.Storeys, StringComparer.OrdinalIgnoreCase).Any()) continue;

                // The other half has to be the OTHER half.
                if (DominantSide(b.Segments, b.Seam!) != -sideA) continue;

                members.Add(b.File);
                taken.Add(b.File);
            }

            // A sheet whose seam nobody shares is not half of anything — it is a sheet with a match
            // line to a drawing that is not in this set, and it is left exactly as it was.
            if (members.Count > 1) { taken.Add(a.File); groups.Add(new SheetGroup(members, a.Seam!)); }
        }

        return groups;
    }

    /// <summary>
    /// −1, +1 or 0: which side of the seam this sheet's linework is on, or 0 when it sits across the
    /// line and is therefore a whole plan rather than a half.
    /// </summary>
    public static int DominantSide(IReadOnlyList<DxfSegment>? segments, MatchLineSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        if (segments is null || segments.Count == 0) return 0;

        double dx = seam.End.X - seam.Start.X, dy = seam.End.Y - seam.Start.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return 0;

        int left = 0, right = 0;
        foreach (var s in segments)
        {
            foreach (var p in new[] { s.Start, s.End })
            {
                // Cross product: which side of the seam this point falls.
                double side = dx * (p.Y - seam.Start.Y) - dy * (p.X - seam.Start.X);
                if (side > 0) left++;
                else if (side < 0) right++;
            }
        }

        if (left >= right * MinimumSideRatio && left > 0) return +1;
        if (right >= left * MinimumSideRatio && right > 0) return -1;
        return 0;
    }
}
