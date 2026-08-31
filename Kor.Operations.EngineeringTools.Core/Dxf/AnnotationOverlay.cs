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
            // A QUARTER TURN IS EXACT, AND HAS TO BE. Math.Cos(PI/2) is 6.1e-17, not zero, so a
            // line the drafter drew vertical comes back a hair off vertical. The error is far below
            // any tolerance in this tool -- and it is still enough to change which rings close,
            // because closure is decided on exact endpoint identity before any tolerance applies.
            // Turning 31168 by a computed 90 degrees lost the 2,754 sq ft mezzanine slab, which is
            // the one the engineer has corrected us on twice.
            var (c, s) = ((RotationDegrees % 360 + 360) % 360) switch
            {
                0 => (1.0, 0.0),
                90 => (0.0, 1.0),
                180 => (-1.0, 0.0),
                270 => (0.0, -1.0),
                _ => Trig(RotationDegrees),
            };

            return new DxfPoint(p.X * c - p.Y * s + OffsetX, p.X * s + p.Y * c + OffsetY);

            static (double C, double S) Trig(double degrees)
            {
                double r = degrees * System.Math.PI / 180.0;
                return (System.Math.Cos(r), System.Math.Sin(r));
            }
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

            // A CENTROID CANNOT START THIS ON ITS OWN, AND THE REASON IS THE GRID.
            //
            // The two exports do not find the same columns -- LEVEL 2 gives 60 against 73 -- and
            // those thirteen pull the mean sideways. On this job the miss was 276 in, which is
            // more than a column bay, so pairing each column with its NEAREST neighbour paired it
            // with the wrong one and the refinement settled happily one bay out: the turn correct,
            // the y correct to an inch, and the x a bay adrift.
            //
            // So the offset is voted on instead. Every pairing of a sampled annotated column with
            // a geometry column proposes an offset; the one the most columns agree with wins.
            // Wrong-bay proposals collect a handful of votes, the true one collects most of the
            // building, and no amount of unmatched columns changes which is which.
            frame = SweepOffset(frame, spun, geometryColumns);

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

    /// <summary>
    /// Column layer names used by an export this job's rules were NOT written for.
    ///
    /// The job's own column pattern is the drafting office's — JBP_V_COL here — and the bridge
    /// export does not use it. Revit has no export-layer table set, so it falls back to the AIA
    /// default and writes columns on S-COLS. Looked up through the job's patterns that yields no
    /// columns at all, the frame cannot be solved, and every sheet is refused for a reason that
    /// sounds geometric and is not: LEVEL 2 registers to three tenths of an inch and was still
    /// turned away, because nothing was being compared.
    ///
    /// The job's own patterns are tried first and still win. This is only what to fall back on.
    /// </summary>
    private static readonly string[] StandardColumnLayers =
    {
        "S-COLS", "S-COL", "A-COLS", "A-COL", "S-COLS-HDLN",
    };

    /// <summary>
    /// COLUMN CENTRES, not segment midpoints.
    ///
    /// A column is drawn as a closed outline, and the two exports do not break that outline into
    /// the same edges — LEVEL 2 yields 688 segments on one side against 670 on the other for the
    /// same columns. The midpoint of an edge is a point on the column's SIDE, and which side
    /// depends on how the exporter decomposed it, so those clouds never line up however good the
    /// transform is. Matching them refused LEVEL 2 outright while the true fit was three tenths
    /// of an inch.
    ///
    /// The centre of a column is the same point in both, whatever the drafting. So the classifier
    /// finds the columns and their centres are what get matched.
    /// </summary>
    private static IReadOnlyList<DxfPoint> ColumnPoints(
        IReadOnlyList<DxfSegment> segments, PlanClassificationOptions options)
    {
        var found = StructuralPlanClassifier.Classify(segments, options);
        if (found.Columns.Count > 0) return found.Columns.Select(c => c.Center).ToList();

        // The job's own patterns did not recognise this export, which is the ordinary case for
        // the bridge's own output: Revit has no export-layer table set, so it writes the AIA
        // default and columns land on S-COLS rather than the office's JBP_V_COL.
        var standard = options with { ColumnLayerPatterns = StandardColumnLayers };
        var again = StructuralPlanClassifier.Classify(segments, standard);
        return again.Columns.Select(c => c.Center).ToList();
    }

    /// <summary>
    /// Search around the centroid guess for the offset the most columns agree with.
    ///
    /// A centroid cannot start this on its own, and the reason is the grid. The two exports do
    /// not find the same columns -- LEVEL 2 gives 60 against 73 -- and those thirteen pull the
    /// mean sideways. On this job the miss was 276 in, which is MORE THAN A COLUMN BAY, so
    /// pairing each column with its nearest neighbour paired it with the wrong one and the
    /// refinement settled happily one bay out: the turn right, the y right to an inch, and the x
    /// a bay adrift. Nothing downstream could tell.
    ///
    /// So the offset is searched rather than computed: a bounded sweep around the centroid guess,
    /// scored by how many columns land on a column. A one-bay error agrees with a handful; the
    /// true offset agrees with most of the floor. The sweep reaches a bay and a half in each
    /// direction, which is what the centroid can plausibly be wrong by, and steps finely enough
    /// that the true offset cannot fall between two samples.
    /// </summary>
    private static Frame SweepOffset(
        Frame frame, IReadOnlyList<DxfPoint> spun, IReadOnlyList<DxfPoint> geometry)
    {
        const double reach = 420.0;   // a bay and a half on this job's 240 in grid
        const double step = 12.0;     // an inch of drafting slop, not a bay
        const double agree = 18.0;

        double bestX = frame.OffsetX, bestY = frame.OffsetY;
        int bestScore = -1;

        for (double dx = -reach; dx <= reach; dx += step)
        {
            for (double dy = -reach; dy <= reach; dy += step)
            {
                double ox = frame.OffsetX + dx, oy = frame.OffsetY + dy;
                int score = 0;
                foreach (var p in spun)
                {
                    double px = p.X + ox, py = p.Y + oy;
                    foreach (var q in geometry)
                    {
                        if (System.Math.Abs(px - q.X) <= agree && System.Math.Abs(py - q.Y) <= agree)
                        {
                            score++;
                            break;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = ox;
                    bestY = oy;
                }
            }
        }

        return frame with { OffsetX = bestX, OffsetY = bestY };
    }

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
