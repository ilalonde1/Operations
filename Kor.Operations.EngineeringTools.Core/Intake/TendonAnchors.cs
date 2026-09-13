using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A POST-TENSIONING TENDON'S ANCHOR IS NOT A COLUMN (intake step 48, 2026-09-12). A P/T slab plan
/// draws each tendon as a line labelled with its force — "270 Kips", "55 Kips/ft" — and a small
/// filled block at each end where the strand is stressed. The block is a filled rectangle of
/// column proportions, and the reader took every one of them for a column: on 31202 (Hotel Circle)
/// 55 of the 108 "columns" on the typical storey were anchors, 9x12 and 11x14 in, and the model
/// carried twice the columns the engineer's own model does (yardstick: 61% of ours within 100 mm of
/// theirs, 95% the other way). The tendon is known by its force label; its ends are known by the
/// tendon; a column standing at a tendon's end is the anchor.
/// </summary>
/// <remarks>
/// WHAT IT COVERS: a force word (<see cref="DefaultForceWords"/>, extended by the row
/// dxf.pdf.force-words) within reach of a long line's edge names that line a tendon; a column whose
/// footprint holds either end of a tendon is stood down and counted. WHAT IT DOES NOT: a column a
/// tendon passes OVER (banded tendons run over the columns; only the ends count); a beam ending at a
/// column (a beam carries no force label); a tendon drawn as a polyline that turns (the ends are
/// still its first and last points); an anchor drawn as a circle or an arrow (not a column
/// candidate to begin with).
/// </remarks>
public static class TendonAnchors
{
    /// <summary>The words a tendon's force is written in; a practice's own go in dxf.pdf.force-words and extend these.</summary>
    public static readonly IReadOnlyList<string> DefaultForceWords = ["KIPS", "KIP", "KIPS/FT", "KIPS/FT.", "KN", "KN/M"];

    /// <summary>A tendon this far from its force label, in label heights, is the label's tendon.</summary>
    public const double LabelReachHeights = 4.0;

    /// <summary>A tendon spans a bay at least: shorter lines beside a force word are leaders and ticks, in millimetres.</summary>
    public const double MinTendonLengthMm = 1500.0;

    /// <summary>A tendon's end inside a column's footprint, with this much slack, stands the column down.</summary>
    public const double EndSlackMm = 50.0;

    /// <summary>
    /// A tendon's run may pass through its anchor and stop a little beyond it - the leader stub to the
    /// force label, drawn on the tendon's own line (31202 p32: "189 Kips", the run ending 632 mm past the
    /// block, 2026-09-12). A block the run passes through with an end within this of it is its anchor.
    /// </summary>
    public const double EndOvershootMm = 800.0;

    /// <summary>A sheet with at least this many force labels is a post-tensioning plan, on which every long run is a tendon.</summary>
    public const int PostTensionedSheetMinLabels = 3;

    /// <summary>Pieces of one tendon lie on one line within this, and a gap between pieces (a label, a chair mark) is bridged up to <see cref="PieceGapMm"/>.</summary>
    public const double PieceLateralMm = 20.0;
    public const double PieceGapMm = 2500.0;

    public sealed record Tendon((double X, double Y) Start, (double X, double Y) End, string Label);

    /// <summary>The label of a run taken for a tendon by the sheet's signature rather than by its own force label.</summary>
    public const string SheetTendon = "(P/T sheet)";

    /// <summary>The tendons on the sheet: every line a force label sits beside, in the geometry's millimetres.</summary>
    public static IReadOnlyList<Tendon> Read(VectorPageReader.PageContent content, ExtractedGeometry geometry, double mmPerPoint, IReadOnlyList<string> forceWords)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(forceWords);
        var words = new HashSet<string>(forceWords.Select(w => w.Trim().ToUpperInvariant()), StringComparer.Ordinal);
        var tendons = new List<Tendon>();
        var taken = new HashSet<int>();
        var chains = Chains(geometry.Lines.Concat(geometry.StrokesOnGrid).ToList());   // the strokes along the grid are the tendons drawn on it (step 53)
        int forceLabels = 0;
        foreach (var word in content.Words)
        {
            string text = word.Text.Trim().TrimEnd('.', ',').ToUpperInvariant();
            if (!words.Contains(text) && !words.Contains(text + ".")) continue;
            forceLabels++;
            double cx = word.Cx * mmPerPoint, cy = word.Cy * mmPerPoint;
            double reach = Math.Max(word.Height, 1.0) * mmPerPoint * LabelReachHeights;
            int best = -1;
            double bestDistance = reach;
            for (int i = 0; i < chains.Count; i++)
            {
                var line = chains[i];
                if (Length(line) < MinTendonLengthMm) continue;
                double d = DistanceToPolyline(cx, cy, line);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best < 0 || !taken.Add(best)) continue;
            var pts = chains[best];
            tendons.Add(new Tendon(pts[0], pts[^1], word.Text.Trim()));
        }
        // A SHEET THAT LABELS FORCES IS A POST-TENSIONING PLAN, AND ON IT EVERY LONG RUN IS A TENDON. The
        // distributed tendons are not labelled one by one: one "55 Kips/ft" sits on the band's extent line
        // along the slab edge, and the tendons it describes run perpendicular to it, 43 m long, each
        // ending in its own anchor (31202 p32: 22 of them at x 517 pt, read with vector-lines). With the
        // label rule alone 10 of 55 anchors were caught; the sheet's own signature - three force labels
        // or more - names the rest. What ends in an undeclared block on such a sheet is an anchor.
        if (forceLabels >= PostTensionedSheetMinLabels)
            for (int i = 0; i < chains.Count; i++)
                if (!taken.Contains(i) && Length(chains[i]) >= MinTendonLengthMm)
                    tendons.Add(new Tendon(chains[i][0], chains[i][^1], SheetTendon));
        return tendons;
    }

    /// <summary>
    /// The sheet's linework as tendons would be drawn: a tendon is one straight run, and the drafter
    /// draws it in pieces — broken for the force label, the chair-height marks ("MID"), the crossing
    /// dimension strings — so the axis-aligned pieces that lie on one line within
    /// <see cref="PieceLateralMm"/> and reach one another within <see cref="PieceGapMm"/> are chained
    /// into the run they are pieces of; its two extremes are the tendon's ends. 31202's "270 Kips"
    /// tendon is drawn as a 5.5 m piece and four 500 mm pieces, and the nearest single piece to its
    /// label ended 3 m from the anchor. Diagonal linework is left as drawn.
    /// </summary>
    internal static List<List<(double X, double Y)>> Chains(IReadOnlyList<List<(double X, double Y)>> lines)
    {
        var horizontals = new List<(double At, double From, double To)>();
        var verticals = new List<(double At, double From, double To)>();
        var others = new List<List<(double X, double Y)>>();
        foreach (var line in lines)
        {
            if (line.Count < 2) continue;
            for (int i = 1; i < line.Count; i++)
            {
                var a = line[i - 1]; var b = line[i];
                if (Math.Abs(a.Y - b.Y) <= PieceLateralMm && Math.Abs(a.X - b.X) > PieceLateralMm) horizontals.Add(((a.Y + b.Y) / 2, Math.Min(a.X, b.X), Math.Max(a.X, b.X)));
                else if (Math.Abs(a.X - b.X) <= PieceLateralMm && Math.Abs(a.Y - b.Y) > PieceLateralMm) verticals.Add(((a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                else others.Add([a, b]);
            }
        }
        var chains = new List<List<(double X, double Y)>>();
        foreach (var (runs, horizontal) in new[] { (horizontals, true), (verticals, false) })
        {
            // pieces on one line: sorted by their constant, grouped while neighbours stay within the lateral slack
            var sorted = runs.OrderBy(r => r.At).ThenBy(r => r.From).ToList();
            int start = 0;
            while (start < sorted.Count)
            {
                int end = start;
                while (end + 1 < sorted.Count && sorted[end + 1].At - sorted[end].At <= PieceLateralMm) end++;
                var onLine = sorted.Skip(start).Take(end - start + 1).OrderBy(r => r.From).ToList();
                double at = onLine.Average(r => r.At);
                double from = onLine[0].From, to = onLine[0].To;
                for (int i = 1; i <= onLine.Count; i++)
                {
                    if (i < onLine.Count && onLine[i].From - to <= PieceGapMm) { to = Math.Max(to, onLine[i].To); continue; }
                    chains.Add(horizontal ? [(from, at), (to, at)] : [(at, from), (at, to)]);
                    if (i < onLine.Count) { from = onLine[i].From; to = onLine[i].To; }
                }
                start = end + 1;
            }
        }
        chains.AddRange(others);
        return chains;
    }

    /// <summary>
    /// Every column whose footprint holds a tendon's end is an anchor: flagged in
    /// <see cref="ExtractedGeometry.ColumnIsTendonAnchor"/> (parallel to the columns), not written,
    /// not counted as a column. Returns how many.
    /// </summary>
    /// <param name="sizeIsDeclared">Parallel to the columns, or null: true where the sheet's own column schedule declares the column's size. A declared size is a column whatever ends at it — 31202's 12x48 columns had a tendon ending in one on every typical storey and the rule without this stood one real column a storey down (2026-09-12).</param>
    /// <param name="smallestDeclaredAreaMm2">The smallest column the SET schedules, as an area. A run on a P/T sheet that carries no force label of its own (a "(P/T sheet)" tendon) stands a block down only when the block is smaller than every column the set has: an anchor is a fitting, and 31130's schedule reads incomplete (a ladder schedule the reader takes one band of), so its 14x36 columns were undeclared and a tendon ends on each (2026-09-12). Null: those runs stand nothing down.</param>
    public static int StandDownColumns(ExtractedGeometry geometry, IReadOnlyList<Tendon> tendons, IReadOnlyList<bool>? sizeIsDeclared = null, double? smallestDeclaredAreaMm2 = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(tendons);
        geometry.ColumnIsTendonAnchor.Clear();
        int stoodDown = 0;
        for (int i = 0; i < geometry.Columns.Count; i++)
        {
            var (cx, cy) = geometry.Columns[i];
            var (w, d) = i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (0.0, 0.0);
            double hx = w / 2 + EndSlackMm, hy = d / 2 + EndSlackMm;
            bool declared = sizeIsDeclared is not null && i < sizeIsDeclared.Count && sizeIsDeclared[i];
            bool smallerThanAnyColumn = smallestDeclaredAreaMm2 is double least && w * d < least;
            // the overshoot clause is for fittings only: a block no column the set schedules is as small as. A 36" round
            // column a tendon runs over, with a chain break at a MID mark a metre past it, is not an anchor (31130 west
            // tower, sixteen storeys of it, 2026-09-12).
            bool anchor = !declared && tendons.Any(t => (t.Label != SheetTendon || smallerThanAnyColumn) && (Inside(t.Start) || Inside(t.End) || (smallerThanAnyColumn && EndsJustPast(t))));
            geometry.ColumnIsTendonAnchor.Add(anchor);
            if (anchor) stoodDown++;

            bool Inside((double X, double Y) p) => Math.Abs(p.X - cx) <= Math.Max(hx, hy) && Math.Abs(p.Y - cy) <= Math.Max(hx, hy);
            // the run passes through the block (its line within the block's half-width of the centre) and one end is within the overshoot of it
            bool EndsJustPast(Tendon t)
            {
                bool horizontal = Math.Abs(t.End.Y - t.Start.Y) <= Math.Abs(t.End.X - t.Start.X);
                double along0 = horizontal ? t.Start.X : t.Start.Y, along1 = horizontal ? t.End.X : t.End.Y;
                double across = horizontal ? t.Start.Y : t.Start.X, centreAlong = horizontal ? cx : cy, centreAcross = horizontal ? cy : cx;
                double half = horizontal ? hy : hx, halfAlong = horizontal ? hx : hy;
                if (Math.Abs(across - centreAcross) > half) return false;
                if (centreAlong < Math.Min(along0, along1) || centreAlong > Math.Max(along0, along1)) return false;   // through it, not beside it
                return Math.Min(Math.Abs(along0 - centreAlong), Math.Abs(along1 - centreAlong)) <= halfAlong + EndOvershootMm;
            }
        }
        return stoodDown;
    }

    private static double Length(List<(double X, double Y)> line)
    {
        double total = 0;
        for (int i = 1; i < line.Count; i++) total += Math.Sqrt(Sq(line[i - 1], line[i]));
        return total;
    }

    private static double DistanceToPolyline(double x, double y, List<(double X, double Y)> line)
    {
        double best = double.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            var (ax, ay) = line[i - 1]; var (bx, by) = line[i];
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 < 1e-9 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
            double px = ax + t * dx, py = ay + t * dy;
            best = Math.Min(best, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
        }
        return best;
    }

    private static double Sq((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
