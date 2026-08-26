#nullable enable

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// The drawing's words, carried onto geometry that was exported without them.
///
/// THE PROBLEM THIS SOLVES. Almost every fault the engineer has reported on 31168 is one
/// fault: where a drawn slab edge does not close, this tool cannot tell a region that is slab
/// from a region that is a hole, so it guesses, and she rejects the guess. LEVEL 2 came out
/// "empty" because JBP_C_SLABEDG-2 carries 144 open segments and closes nothing; the roof
/// closes one 1,995 sq ft loop over a 14,840 sq ft floor; the parkade closes nothing at all.
/// Chain-closing and flood-fill were written to recover those outlines and were WITHDRAWN on
/// 25 August, because recovering a region is only safe if you then know what it is.
///
/// The drawing says what it is. «14" SLAB» is printed inside the slab. The DXFs this tool was
/// given carry no text at all — but a DXF exported through our own Revit bridge does, because
/// BridgeExec sets TextTreatment.Exact, and it writes the tag at the point the drafter placed
/// it.
///
/// THE TWO EXPORTS ARE THE SAME BUILDING IN DIFFERENT FRAMES. The bridge writes shared
/// coordinates; whatever produced the older set did not. Wall extents come out swapped —
/// 2,716 x 3,614 against 3,360 x 2,300 — which is a rotation, not an offset.
///
/// Measured on 31168, matching column midpoints level by level: ONE rigid transform fits every
/// sheet. Rotate 90 degrees, then translate. Derived independently on LEVEL 2, LEVEL 1 and
/// C-LEVEL 6 it agrees to within seven tenths of an inch, and applied as a single constant to
/// LEVEL 2, LEVEL 1, C-LEVEL 6, LEVEL P1 and LEVEL P2 the median residual is 0.3 in on all
/// five — on point sets of 112 to 455 columns.
///
/// So the tags can be lifted from the annotated export into the frame the model is actually
/// built in, and NOTHING about the proven geometry has to move.
/// </summary>
public static class AnnotationOverlay
{
    /// <summary>
    /// A rigid transform between two exports of one building: rotate about the origin, then
    /// translate. No scale — both are inches — and no reflection, which was tested and rejected
    /// (a flipped fit misses by 100 in or more where the true one misses by less than one).
    /// </summary>
    public readonly record struct Frame(double RotationDegrees, double OffsetX, double OffsetY)
    {
        public DxfPoint Apply(DxfPoint p)
        {
            double r = RotationDegrees * System.Math.PI / 180.0;
            double c = System.Math.Cos(r), s = System.Math.Sin(r);
            return new DxfPoint(p.X * c - p.Y * s + OffsetX, p.X * s + p.Y * c + OffsetY);
        }
    }

    /// <summary>
    /// Work out the transform from the annotated export onto the geometry export, by matching
    /// the two column clouds.
    ///
    /// Columns are the right feature: they are points rather than runs, there are hundreds of
    /// them, and both exports agree on their count almost exactly (112 against 112 on LEVEL 2,
    /// 445 against 449 on LEVEL P1). Only right-angle rotations are tried, because a drawing
    /// set is exported on axis; anything else would be a different building.
    ///
    /// Returns null when no rotation fits within <paramref name="toleranceInches"/>. Failing
    /// closed matters here: a wrong frame would put one storey's thickness on another storey's
    /// slab, silently.
    /// </summary>
    public static Frame? Solve(
        IReadOnlyList<DxfPoint> annotatedColumns,
        IReadOnlyList<DxfPoint> geometryColumns,
        double toleranceInches = 24.0)
    {
        if (annotatedColumns.Count == 0 || geometryColumns.Count == 0) return null;

        var target = Centroid(geometryColumns);
        Frame? best = null;
        double bestScore = double.MaxValue;

        foreach (double degrees in new[] { 0.0, 90.0, 180.0, 270.0 })
        {
            var spun = annotatedColumns.Select(p => new Frame(degrees, 0, 0).Apply(p)).ToList();
            var here = Centroid(spun);
            var frame = new Frame(degrees, target.X - here.X, target.Y - here.Y);

            // CENTROIDS ALONE ARE NOT ENOUGH, and the real data says so: LEVEL P2 carries 445
            // columns against 455, and those ten pull the centroid far enough that the sheet
            // reads as a 28 in miss when the true fit is a third of an inch. The extras are not
            // spread evenly -- they cluster at one end of a wing -- so they drag the mean.
            //
            // So the centroid only STARTS it. Each pass pairs every annotated column with its
            // nearest neighbour, keeps the pairs that already agree, and takes the offset from
            // those alone. Columns present in one export have no close partner and drop out.
            // Two passes settle it; a third never moved anything measurably.
            for (int pass = 0; pass < 2; pass++) frame = Refine(frame, annotatedColumns, geometryColumns);

            double score = MedianNearest(
                annotatedColumns.Select(frame.Apply).ToList(), geometryColumns);

            if (score < bestScore)
            {
                bestScore = score;
                best = frame;
            }
        }

        return bestScore <= toleranceInches ? best : null;
    }

    /// <summary>Every tag in the annotated set, moved into the geometry set's frame.</summary>
    public static IReadOnlyList<DxfPositionedTag> Carry(
        IReadOnlyList<DxfPositionedTag> tags, Frame frame)
        => tags.Select(t => t with { Point = frame.Apply(t.Point) }).ToList();

    /// <summary>
    /// Find this sheet's twin in the annotated export and bring its tags across.
    ///
    /// Matched by STOREY, not by file name: the two exports name their sheets differently --
    /// "LEVEL 2 PLAN - CONCRETE OUTLINE" against a plain "LEVEL 2" -- and PlanSheetNaming
    /// already reads either into the levels it serves.
    ///
    /// Returns nothing, and says why, whenever it cannot be sure. Silence is the wrong answer
    /// here: a thickness on the wrong storey is the fault this must be incapable of.
    /// </summary>
    public static IReadOnlyList<DxfPositionedTag> TagsFor(
        string annotatedFolder,
        PlanSheetInfo sheet,
        IReadOnlyList<DxfSegment> geometrySegments,
        PlanClassificationOptions options,
        out string? note)
    {
        note = null;
        var none = (IReadOnlyList<DxfPositionedTag>)System.Array.Empty<DxfPositionedTag>();
        if (!Directory.Exists(annotatedFolder)) return none;

        var mine = SheetIdentity(sheet);
        if (mine.Count == 0) return none;

        // A view exported more than once -- "(for reinforcing plan)", "(key plan)" -- describes
        // the same storey. Prefer the plain one: it is the structural view, and the variants
        // carry the same geometry with extra annotation layered over it.
        var twins = Directory.EnumerateFiles(annotatedFolder, "*.dxf", SearchOption.TopDirectoryOnly)
            .Where(f => SheetIdentity(PlanSheetNaming.Parse(Path.GetFileName(f))).SetEquals(mine))
            .OrderBy(f => Path.GetFileName(f).Contains('(') ? 1 : 0)
            .ThenBy(f => Path.GetFileName(f).Length)
            .ToList();

        if (twins.Count == 0) return none;

        var twin = twins[0];
        var theirTags = DxfPlanReader.ReadPositionedTags(twin);
        if (theirTags.Count == 0) return none;

        var frame = Solve(ColumnPoints(DxfPlanReader.ReadSegments(twin), options),
                          ColumnPoints(geometrySegments, options));
        if (frame is null)
        {
            note = $"{sheet.FileName}: an annotated export of this sheet was found " +
                   $"({Path.GetFileName(twin)}) but its columns could not be lined up with this " +
                   "drawing's, so none of its text was used. Nothing was placed from a frame that " +
                   "could not be proven.";
            return none;
        }

        var carried = Carry(theirTags, frame.Value);
        note = $"{sheet.FileName}: {carried.Count} annotation(s) read from " +
               $"{Path.GetFileName(twin)} and placed on this drawing " +
               $"(turned {frame.Value.RotationDegrees:0}°).";
        return carried;
    }

    private static HashSet<string> SheetIdentity(PlanSheetInfo s)
    {
        var id = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int level in s.Levels) id.Add((s.IsMezzanine ? "MEZZ " : "L") + level);
        foreach (int p in s.ParkadeLevels) id.Add("P" + p);
        if (s.IsRoof && s.Levels.Count == 0) id.Add(s.IsElevatorRoof ? "ELEVROOF" : "ROOF");
        if (s.IsFoundation) id.Add("FOUNDATION");
        foreach (string tag in s.BuildingTags) id.Add("BLDG " + tag);
        return id;
    }

    /// <summary>Column midpoints — the point cloud the two exports are matched on.</summary>
    private static IReadOnlyList<DxfPoint> ColumnPoints(
        IReadOnlyList<DxfSegment> segments, PlanClassificationOptions options)
        => segments
            .Where(s => options.RoleOf(s.Layer) == "columns")
            .Select(s => new DxfPoint((s.Start.X + s.End.X) / 2.0, (s.Start.Y + s.End.Y) / 2.0))
            .ToList();

    /// <summary>
    /// Take the offset from the columns that already agree, and ignore the rest.
    ///
    /// The inlier cut is the MEDIAN nearest distance under the current frame, so it adapts:
    /// while the fit is rough most pairs are admitted and it improves fast; once it is close,
    /// only genuinely matched columns count and the odd ones out stop mattering. No fixed
    /// threshold in inches, because the right one differs between a rough first pass and a
    /// settled one.
    /// </summary>
    private static Frame Refine(
        Frame frame, IReadOnlyList<DxfPoint> annotated, IReadOnlyList<DxfPoint> geometry)
    {
        var paired = new List<(DxfPoint Moved, DxfPoint Nearest, double Distance)>(annotated.Count);
        foreach (var a in annotated)
        {
            var moved = frame.Apply(a);
            DxfPoint nearest = geometry[0];
            double best = double.MaxValue;
            foreach (var g in geometry)
            {
                double dx = moved.X - g.X, dy = moved.Y - g.Y;
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; nearest = g; }
            }
            paired.Add((moved, nearest, System.Math.Sqrt(best)));
        }

        var cut = paired.Select(p => p.Distance).OrderBy(d => d).ToList();
        double limit = cut[cut.Count / 2];

        var inliers = paired.Where(p => p.Distance <= limit).ToList();
        if (inliers.Count == 0) return frame;

        double shiftX = inliers.Average(p => p.Nearest.X - p.Moved.X);
        double shiftY = inliers.Average(p => p.Nearest.Y - p.Moved.Y);
        return frame with { OffsetX = frame.OffsetX + shiftX, OffsetY = frame.OffsetY + shiftY };
    }

    private static DxfPoint Centroid(IReadOnlyList<DxfPoint> ps)
        => new(ps.Average(p => p.X), ps.Average(p => p.Y));

    /// <summary>
    /// Median distance from each point to its nearest neighbour in the other cloud. The MEDIAN
    /// rather than the mean: the two exports do not carry identical column counts — LEVEL P2 has
    /// 445 against 455 — and a handful of unmatched columns would drag a mean while leaving the
    /// median untouched. Deriving the offset from centroids has the same weakness, which is why
    /// P2 alone appeared not to fit until the constant from the other levels was applied to it.
    /// </summary>
    private static double MedianNearest(
        IReadOnlyList<DxfPoint> from, IReadOnlyList<DxfPoint> to)
    {
        var distances = new List<double>(from.Count);
        foreach (var p in from)
        {
            double nearest = double.MaxValue;
            foreach (var q in to)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                double d = dx * dx + dy * dy;
                if (d < nearest) nearest = d;
            }
            distances.Add(System.Math.Sqrt(nearest));
        }
        distances.Sort();
        return distances.Count == 0 ? double.MaxValue : distances[distances.Count / 2];
    }
}
