using Frame = Kor.Operations.EngineeringTools.Dxf.AnnotationOverlay.Frame;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Puts the drawing on the engineer's grid -- turned the right way, not merely centred.
///
/// WHY THIS EXISTS. The model published on 28 August opened with the building running north-south
/// while every drawing and DXF of it runs east-west. Andrea Neuviale, 31 August: "the rotation,
/// which is actually kind of important... when I import the DXF, I can't compare. Well, I could if
/// I rotate it and then move it. I mean, it's doable, but it's kind of a pain." Until it matches,
/// she cannot lay the DXF over the model, which is the first thing she does.
///
/// The cause was that alignment only ever translated. A Revit export in shared site coordinates
/// lands thousands of feet from a model built at its own origin AND, on this job, ninety degrees
/// around from it -- project north against plan north. Centring fixed the first and left the second.
///
/// HOW IT IS SOLVED. Not by guessing from a bounding box, which cannot tell 0 from 180 and is
/// ambiguous on a square building. The drawings carry their own grid lines and the engineer's model
/// carries hers, with the spacings of a real building: on 31168 the X grids run
/// 93.4, 141.2, 326, 326, 326, 287, 39, 175, 188, 194.5, 131.5, 86.5, 239.5, 326, 326, 326, 326, 127
/// inches apart. That sequence is a fingerprint. It appears in the drawing exactly once, along the
/// drawing's Y axis and in reverse -- which is the whole answer: the rotation, its direction, and
/// the offset that makes the two coincide.
///
/// Measured on 31168 across 19 grid lines on one axis and 16 on the other, every spacing matching
/// within a tenth of an inch and the two spans agreeing to 3984.6 against 3984.7.
/// </summary>
public static class GridAlignment
{
    /// <summary>Two grid lines nearer than this are the same line drawn twice.</summary>
    private const double SamePosition = 1.0;

    /// <summary>A matched pair may be this far apart. Grid coordinates are drafted, not derived.</summary>
    private const double Tolerance = 2.0;

    /// <summary>Below this many matched lines the fingerprint is a coincidence, not a fit.</summary>
    private const int LeastConvincing = 4;

    public sealed record Fit(Frame Frame, int MatchedX, int MatchedY, string Note);

    /// <summary>
    /// The frame that carries drawing coordinates onto the reference model's grid, or null when the
    /// drawings and the model do not share enough grid to say. Null means "leave it alone": a wrong
    /// rotation is far worse than none, because it looks deliberate.
    /// </summary>
    public static Fit? Solve(
        IEnumerable<DxfSegment> segments,
        IReadOnlyList<double> referenceX,
        IReadOnlyList<double> referenceY,
        Func<string, bool>? isGridLayer = null)
    {
        isGridLayer ??= LooksLikeAGridLayer;

        var (drawingX, drawingY) = GridPositions(segments, isGridLayer);
        if (drawingX.Count == 0 && drawingY.Count == 0) return null;
        if (referenceX.Count == 0 && referenceY.Count == 0) return null;

        Fit? best = null;
        foreach (int degrees in new[] { 0, 90, 180, 270 })
        {
            // Where each drawing axis lands, and with what sign, under this rotation.
            //   0:   X <- +x   Y <- +y        90:  X <- -y   Y <- +x
            //   180: X <- -x   Y <- -y        270: X <- +y   Y <- -x
            var (forX, signX) = degrees switch
            {
                0 => (drawingX, 1.0),
                90 => (drawingY, -1.0),
                180 => (drawingX, -1.0),
                _ => (drawingY, 1.0),
            };
            var (forY, signY) = degrees switch
            {
                0 => (drawingY, 1.0),
                90 => (drawingX, 1.0),
                180 => (drawingY, -1.0),
                _ => (drawingX, -1.0),
            };

            var (offsetX, matchedX) = BestShift(forX.Select(v => v * signX).ToList(), referenceX);
            var (offsetY, matchedY) = BestShift(forY.Select(v => v * signY).ToList(), referenceY);

            if (best is null || matchedX + matchedY > best.MatchedX + best.MatchedY)
                best = new Fit(
                    new Frame(degrees, offsetX, offsetY), matchedX, matchedY,
                    $"{matchedX} of {referenceX.Count} grid line(s) on the model's X and " +
                    $"{matchedY} of {referenceY.Count} on its Y matched the drawing's own grid at " +
                    $"{degrees}°.");
        }

        if (best is null) return null;

        // One axis fitting well is enough to fix the rotation -- 31168's model carries nineteen
        // grids on X and two on Y, so demanding both would refuse a job that is plainly aligned.
        int strongest = Math.Max(best.MatchedX, best.MatchedY);
        return strongest >= LeastConvincing ? best : null;
    }

    /// <summary>
    /// The distinct positions of axis-parallel grid lines: one list of constant-x lines, one of
    /// constant-y. A grid line is long -- bubbles, tags and leaders on the same layer are not.
    /// </summary>
    private static (List<double> X, List<double> Y) GridPositions(
        IEnumerable<DxfSegment> segments, Func<string, bool> isGridLayer)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var s in segments)
        {
            if (!isGridLayer(s.Layer)) continue;

            double dx = Math.Abs(s.End.X - s.Start.X), dy = Math.Abs(s.End.Y - s.Start.Y);
            if (Math.Max(dx, dy) < 120.0) continue; // ten feet: a bubble is not a grid line

            if (dx <= SamePosition && dy > SamePosition) xs.Add(s.Start.X);
            else if (dy <= SamePosition && dx > SamePosition) ys.Add(s.Start.Y);
        }

        return (Distinct(xs), Distinct(ys));
    }

    private static List<double> Distinct(List<double> values)
    {
        values.Sort();
        var kept = new List<double>();
        foreach (double v in values)
            if (kept.Count == 0 || v - kept[^1] > SamePosition)
                kept.Add(v);
        return kept;
    }

    /// <summary>
    /// The translation that lands the most drawing lines on reference lines. Every pairing is a
    /// candidate shift; the best one wins, which is what makes an irregular grid a fingerprint
    /// rather than a guess.
    /// </summary>
    private static (double Offset, int Matched) BestShift(
        IReadOnlyList<double> drawing, IReadOnlyList<double> reference)
    {
        if (drawing.Count == 0 || reference.Count == 0) return (0.0, 0);

        double bestOffset = 0.0;
        int bestCount = 0;

        foreach (double d in drawing)
        {
            foreach (double r in reference)
            {
                double shift = r - d;
                int count = 0;
                foreach (double other in drawing)
                {
                    double moved = other + shift;
                    foreach (double candidate in reference)
                    {
                        if (Math.Abs(candidate - moved) > Tolerance) continue;
                        count++;
                        break;
                    }
                }

                if (count > bestCount) { bestCount = count; bestOffset = shift; }
            }
        }

        // One pairing fixes WHICH lines correspond; it should not fix the distance. Anchoring on a
        // single pair inherits that pair's drafting rounding -- a sixth of an inch on 31168, which
        // is nothing structurally and is still a model that does not quite land on its grid. The
        // residual is averaged over every matched pair instead, so the fit is the whole grid's
        // answer rather than one line's.
        if (bestCount > 0)
        {
            double total = 0.0;
            int counted = 0;
            foreach (double d in drawing)
            {
                double moved = d + bestOffset;
                double nearest = double.NaN;
                double gap = Tolerance;
                foreach (double candidate in reference)
                {
                    double away = Math.Abs(candidate - moved);
                    if (away > gap) continue;
                    gap = away;
                    nearest = candidate;
                }

                if (double.IsNaN(nearest)) continue;
                total += nearest - d;
                counted++;
            }

            if (counted > 0) bestOffset = total / counted;
        }

        return (bestOffset, bestCount);
    }

    /// <summary>
    /// A layer whose name says grid. Kept deliberately loose: the office's own layer names are the
    /// firm's and belong in KorStandards, but a grid layer is called a grid layer everywhere this
    /// tool has looked -- JBP_G_GRID, JBP_G_GRID-1, S-GRID, A-GRID.
    /// </summary>
    public static bool LooksLikeAGridLayer(string layer) =>
        layer.Contains("GRID", StringComparison.OrdinalIgnoreCase);

    // ── By name (intake step 15) ───────────────────────────────────────────────────────────────
    //
    // A SHEET FROM THE STICK FILE SITS ON THE MODEL'S GRID BY THE NAMES OF ITS AXES. The fit above
    // fingerprints grid POSITIONS across a whole set, one frame for all of it, which is right for a
    // Revit export in shared coordinates and wrong for a sheet read off a PDF: each page is in its
    // own frame, millimetres from its own corner. But the intake reads the grid as NAMED axes
    // (step 8) and writes each name at its line's ends on the GRID layer, and the model's GRIDS
    // table names its lines too. Axis "5" is grid "5": the match is exact, needs no fingerprint,
    // and is one frame per sheet.

    /// <summary>A grid axis the drawing names: a grid-layer line with its label at an end.</summary>
    public sealed record NamedAxis(string Name, bool Vertical, double At);

    /// <summary>A grid line of the model: LABEL, DIR and COORD from its GRIDS table.</summary>
    public sealed record ReferenceGrid(string Label, bool DirX, double Coord);

    /// <summary>Text on the grid layer names the line whose end it sits at, within this many text heights, or 60 units.</summary>
    private const double NameReachHeights = 4.0, NameReachFloor = 60.0;

    /// <summary>
    /// Two named lines may disagree by this much and still be the same line: a plan read at 1:96
    /// resolves to about an inch, and the drafter's grid is drafted, not derived. Wider than the
    /// fingerprint's <see cref="Tolerance"/>, because a name is the match; the position only confirms it.
    /// </summary>
    public const double NameTolerance = 6.0;

    /// <summary>Fewer named lines than this agreeing on one frame is a coincidence, not a fit.</summary>
    public const int LeastConvincingByName = 3;

    /// <summary>
    /// The drawing's named axes: each grid-layer text of a grid-name's length paired with the
    /// nearest end of a grid-layer line within reach; the axis is that line's constant coordinate.
    /// </summary>
    public static List<NamedAxis> NamedAxes(
        IEnumerable<DxfSegment> segments, IEnumerable<DxfPositionedTag> tags, Func<string, bool>? isGridLayer = null)
    {
        isGridLayer ??= LooksLikeAGridLayer;
        var lines = new List<(bool Vertical, double At, DxfPoint A, DxfPoint B)>();
        foreach (var s in segments)
        {
            if (!isGridLayer(s.Layer)) continue;
            double dx = Math.Abs(s.End.X - s.Start.X), dy = Math.Abs(s.End.Y - s.Start.Y);
            if (Math.Max(dx, dy) < 120.0) continue;
            if (dx <= SamePosition && dy > SamePosition) lines.Add((true, s.Start.X, s.Start, s.End));
            else if (dy <= SamePosition && dx > SamePosition) lines.Add((false, s.Start.Y, s.Start, s.End));
        }
        var axes = new List<NamedAxis>();
        if (lines.Count == 0) return axes;
        foreach (var t in tags)
        {
            if (!isGridLayer(t.Layer)) continue;
            // a grid name is "1", "19", "R", "AA", "1A": three characters at most; GRID is a word
            string name = t.Text.Trim();
            if (name.Length == 0 || name.Length > 3) continue;
            double reach = Math.Max(NameReachFloor, NameReachHeights * t.Height);
            (bool Vertical, double At, DxfPoint A, DxfPoint B) best = default;
            double bestDistance = double.MaxValue;
            foreach (var l in lines)
            {
                double d = Math.Min(t.Point.DistanceTo(l.A), t.Point.DistanceTo(l.B));
                if (d < bestDistance) { bestDistance = d; best = l; }
            }
            if (bestDistance > reach) continue;
            if (axes.Any(a => a.Vertical == best.Vertical && Math.Abs(a.At - best.At) <= SamePosition
                              && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            axes.Add(new NamedAxis(name, best.Vertical, best.At));
        }
        return axes;
    }

    /// <summary>
    /// The frame that carries this sheet onto the model's grid by the names of its axes, in the
    /// model's unit (<paramref name="scale"/> is drawing unit per model unit), or null when fewer
    /// than <see cref="LeastConvincingByName"/> named lines agree on one. Tried at each quarter
    /// turn: a sheet drawn to plan north whose vertical axes carry the model's Y labels is turned.
    /// </summary>
    public static Fit? SolveByName(IReadOnlyList<NamedAxis> axes, IReadOnlyList<ReferenceGrid> reference, double scale = 1.0)
    {
        if (axes.Count == 0 || reference.Count == 0) return null;
        var refX = reference.Where(g => g.DirX).ToList();
        var refY = reference.Where(g => !g.DirX).ToList();
        var vertical = axes.Where(a => a.Vertical).ToList();
        var horizontal = axes.Where(a => !a.Vertical).ToList();

        Fit? best = null;
        double bestSpread = double.MaxValue;
        foreach (int degrees in new[] { 0, 90, 180, 270 })
        {
            // Which drawing axes land on the model's X lines, and with what sign: Frame.Apply turns
            // about the origin, so at 90° a horizontal axis at y becomes a constant-x line at -y.
            var (toX, signX, toY, signY) = degrees switch
            {
                0 => (vertical, 1.0, horizontal, 1.0),
                90 => (horizontal, -1.0, vertical, 1.0),
                180 => (vertical, -1.0, horizontal, -1.0),
                _ => (horizontal, 1.0, vertical, -1.0),
            };
            var (ox, mx, sx) = AgreedOffset(toX, refX, signX * scale);
            var (oy, my, sy) = AgreedOffset(toY, refY, signY * scale);
            if (mx == 0 || my == 0) continue;
            double spread = Math.Max(sx, sy);
            if (best is null || mx + my > best.MatchedX + best.MatchedY
                || (mx + my == best.MatchedX + best.MatchedY && spread < bestSpread))
            {
                best = new Fit(new Frame(degrees, ox, oy), mx, my,
                    $"{mx} of {refX.Count} X and {my} of {refY.Count} Y grid lines matched by name at {degrees}°, agreeing within {spread:0.0}.");
                bestSpread = spread;
            }
        }
        return best is not null && best.MatchedX + best.MatchedY >= LeastConvincingByName ? best : null;
    }

    /// <summary>
    /// The translation the most named pairs agree on. Every axis carrying a label is paired with
    /// every grid line of that label and each pair casts one vote, coord - factor * at; the
    /// largest cluster of votes within <see cref="NameTolerance"/> is the frame, its mean the
    /// offset, the labels in it the match count (each label once), the farthest vote its spread.
    /// A vote and not a median because a sheet may carry a name more than once — a key plan with
    /// four buildings' grids, a sheet with two views — and pairing the first "2" with the first
    /// "2" set 31065's S1.11 on the wrong building (2026-09-08). The true pairs all vote for one
    /// offset; the cross pairs scatter theirs.
    /// </summary>
    private static (double Offset, int Matched, double Spread) AgreedOffset(
        List<NamedAxis> axes, List<ReferenceGrid> grids, double factor)
    {
        var votes = new List<(string Label, double Offset)>();
        foreach (var g in grids)
            foreach (var axis in axes.Where(a => a.Name.Equals(g.Label, StringComparison.OrdinalIgnoreCase)))
                votes.Add((g.Label, g.Coord - axis.At * factor));
        if (votes.Count == 0) return (0, 0, 0);

        var best = new List<(string Label, double Offset)>();
        int bestLabels = -1;
        foreach (var centre in votes)
        {
            var cluster = votes.Where(v => Math.Abs(v.Offset - centre.Offset) <= NameTolerance).ToList();
            int labels = cluster.Select(v => v.Label.ToUpperInvariant()).Distinct().Count();
            // more labels wins; then more votes; then the smaller move — one name each way is a
            // tie, and a reissue is the same page until the names say otherwise
            bool better = labels > bestLabels
                          || (labels == bestLabels && cluster.Count > best.Count)
                          || (labels == bestLabels && cluster.Count == best.Count
                              && Math.Abs(cluster.Average(v => v.Offset)) < Math.Abs(best.Average(v => v.Offset)));
            if (better)
            {
                best = cluster;
                bestLabels = labels;
            }
        }
        double offset = best.Average(v => v.Offset);
        return (offset, bestLabels, best.Max(v => Math.Abs(v.Offset - offset)));
    }

    /// <summary>
    /// A placed sheet's named axes as grid lines of the model: each carried through the sheet's
    /// frame in the model's unit. An axis placed on the grid is a grid line for the rest of the
    /// set — the model's GRIDS names only what the engineer drew, and a sheet whose letters the
    /// model lacks can still be placed by a sheet that shares them and was placed.
    /// </summary>
    public static List<ReferenceGrid> Carried(IReadOnlyList<NamedAxis> axes, Frame frame, double scale)
    {
        var carried = new List<ReferenceGrid>();
        foreach (var a in axes)
        {
            var p0 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 0) : new DxfPoint(0, a.At * scale));
            var p1 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 1000) : new DxfPoint(1000, a.At * scale));
            bool dirX = Math.Abs(p1.X - p0.X) <= 1e-6;
            carried.Add(new ReferenceGrid(a.Name, dirX, dirX ? p0.X : p0.Y));
        }
        return carried;
    }

    /// <summary>
    /// The GRIDS table a model built without a reference gets: every named axis of every sheet,
    /// carried through the sheet's frame into the model, one line per name, at the median where
    /// several sheets draw it. ETABS's own form, as the office's exports carry it.
    /// </summary>
    public static List<string> GridLines(IEnumerable<(IReadOnlyList<NamedAxis> Axes, Frame Frame, double Scale)> sheets)
    {
        var byName = new Dictionary<(string Name, bool DirX), List<double>>();
        foreach (var (axes, frame, scale) in sheets)
        {
            foreach (var a in axes)
            {
                var p0 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 0) : new DxfPoint(0, a.At * scale));
                var p1 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 1000) : new DxfPoint(1000, a.At * scale));
                bool dirX = Math.Abs(p1.X - p0.X) <= 1e-6;
                var key = (a.Name.ToUpperInvariant(), dirX);
                if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<double>();
                list.Add(dirX ? p0.X : p0.Y);
            }
        }
        if (byName.Count == 0) return new List<string>();
        var lines = new List<string> { "  GRIDSYSTEM \"G1\"  TYPE \"CARTESIAN\"  BUBBLESIZE 60 " };
        foreach (var entry in byName
                     .Select(kv => (kv.Key.Name, kv.Key.DirX, Coord: Median(kv.Value)))
                     .OrderByDescending(e => e.DirX).ThenBy(e => e.Coord))
        {
            lines.Add($"  GRID \"G1\"  LABEL \"{entry.Name}\"  DIR \"{(entry.DirX ? "X" : "Y")}\"  COORD " +
                      entry.Coord.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                      " VISIBLE \"Yes\"  BUBBLELOC \"End\"  ");
        }
        return lines;

        static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        }
    }
}
