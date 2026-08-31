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
}
