using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A footing is a dashed rectangle whose size the foundation schedule declares, and its mark is the
/// schedule's. The dashes arrive as separate two-point strokes, so a side is a chain of collinear
/// pieces across dash gaps, and a footing is four sides closing a box — or, where something stands
/// on the footing, one full side and the two stubs at its ends.
/// </summary>
/// <remarks>
/// Measured 2026-09-08 on three foundation plans before this was written. Not one footing outline
/// is a closed path; every one is dash pieces. Chained with the DXF side's own dash-join gap
/// (`dxf.dash-join-gap` = 14 in) and closed into boxes, the boxes match the schedule's sizes to the
/// millimetre. Read of placed, by the compiled reader (`takeoff pdf-inventory`, the ledger's
/// footing row), closed boxes only: 31130 p11 35 of 36 — F1 2 of 1, F2 14 of 14, F3 8 of 8,
/// F4 11 of 13; 31138 p9 11 of 11 — F1 7 of 7, F2 4 of 4; 31065 p14 26 of 31 — F1 4 of 4,
/// F2 12 of 13, F3 0 of 1, F4 10 of 13. Before this the pieces went to the DXF as BEAM lines or
/// were dropped as too short, and the intake placed no footing anywhere.
///
/// The 20 misses across the five foundation plans were looked at (brief 21): a footing outline is
/// drawn only where nothing stands on it. At the match line between the two halves of a plan, and
/// under the perimeter wall, the drafter draws one full side and two stubs; the side beneath is
/// absent or a few short pieces, and two of those pieces sat either side of a fixed 12 mm bucket
/// boundary. So sides now cluster by sorted coordinate, and a second pass places a footing from one
/// full side of a scheduled length and a stub at each end pointing the same way. Then the label:
/// the plan's own mark, standing in the box (31065, 31138: 37 of 37) or 254–461 mm beneath it
/// beside the column (31130: 67 of 68), names the footing it is nearest to; and nothing inside sheet
/// furniture is a footing or a placement — 31130 p11's hairpin-stirrup legend draws a dashed 4 ft
/// square (an F1 by size), and 31065 p14's note "ADD BOND BREAKER BETWEEN F4 &amp; CORE FOOTING"
/// says F4 twice (two placements by count). After, labelled footings of labels placed: 31130 p11
/// 36 of 36, p12 37 of 39 (two rotated footings on the angled wing); 31138 p9 11 of 11; 31065 p14
/// 29 of 29 plus one 4.5 m dashed box no label names — the core footing — listed apart; p15 22 of
/// 23 (one F2 with one side and one stub drawn). 135 of 138.
///
/// WHAT IT COVERS: axis-aligned dashed rectangles of a scheduled spread-footing size, either
/// orientation, within <see cref="SizeToleranceMm"/>, closed or interrupted as above, outside sheet
/// furniture; whether the plan's label names each; each consumed piece is reported so the
/// classifier can record its fate. WHAT IT DOES NOT: strip footings (a size, not a box), rotated
/// footings (31130 p12, 2 of 39), a footing drawn as a solid closed path (that is the slab
/// branch's, and a later rule), a footing whose size the schedule does not declare, a footing of
/// which one side and one stub are drawn (31065 p15, 1 of 23), and a dashed box of a scheduled
/// size that is not that footing — the core footing on 31065 p14 is emitted with the F4 mark its
/// size matches and <see cref="FootingOutline.LabelledOnThePlan"/> false; the consumer decides.
/// </remarks>
public static class FootingOutlines
{
    /// <summary>The DXF side's dash-join gap, 14 in, in millimetres.</summary>
    public const double DashGapMm = 355.6;
    /// <summary>How far a drawn side may differ from the scheduled size.</summary>
    public const double SizeToleranceMm = 60.0;
    /// <summary>Collinearity: pieces within this of one another are one side.</summary>
    public const double CollinearMm = 12.0;
    /// <summary>A dashed side has at least this many pieces; a solid line has one.</summary>
    public const int MinPieces = 3;
    /// <summary>When two scheduled sizes both fit a box, the label within this of the box decides between them.</summary>
    public const double LabelSlackMm = 150.0;

    public sealed record Chain(double At, double From, double To, IReadOnlyList<int> Pieces);
    /// <summary>A footing mark placed on the plan, in the same millimetre frame as the paths.</summary>
    public readonly record struct MarkLabel(string Mark, double X, double Y);
    private readonly record struct Piece(double At, double From, double To, int Index);

    /// <summary>Footings on the page, and which raw-path index belongs to which footing.</summary>
    public static (IReadOnlyList<FootingOutline> Footings, IReadOnlyDictionary<int, int> Pieces) Read(
        IReadOnlyList<RawSubpath> raw,
        IReadOnlyList<FootingScheduleReader.FootingType> types,
        double dashGapMm = DashGapMm,
        double sizeToleranceMm = SizeToleranceMm,
        IReadOnlyList<MarkLabel>? labels = null,
        SheetFurniture.Set? furniture = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(types);
        var footings = new List<FootingOutline>();
        var pieces = new Dictionary<int, int>();
        var spread = types.Where(t => t.IsSpread).ToList();
        if (spread.Count == 0 || raw.Count == 0) return (footings, pieces);
        labels ??= Array.Empty<MarkLabel>();

        // horizontal and vertical two-point strokes on the plan — not in a legend, note or
        // schedule box (31130 p11's hairpin-stirrup legend draws a dashed 4 ft square: an F1 by size)
        var hs = new List<Piece>();
        var vs = new List<Piece>();
        for (int i = 0; i < raw.Count; i++)
        {
            var s = raw[i];
            if (s.IsAnnotation || s.IsFilled || !s.IsStroked || s.Points.Count != 2) continue;
            var (a, b) = (s.Points[0], s.Points[1]);
            if (furniture is not null && furniture.IsFurniture((a.X + b.X) / 2, (a.Y + b.Y) / 2)) continue;
            if (Math.Abs(a.Y - b.Y) <= CollinearMm && Math.Abs(a.X - b.X) > CollinearMm)
                hs.Add(new Piece((a.Y + b.Y) / 2, Math.Min(a.X, b.X), Math.Max(a.X, b.X), i));
            else if (Math.Abs(a.X - b.X) <= CollinearMm && Math.Abs(a.Y - b.Y) > CollinearMm)
                vs.Add(new Piece((a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), i));
        }
        var hAll = Chains(hs, dashGapMm);
        var vAll = Chains(vs, dashGapMm);
        var hc = hAll.Where(c => c.Pieces.Count >= MinPieces).ToList();
        var vc = vAll.Where(c => c.Pieces.Count >= MinPieces).ToList();
        var taken = new HashSet<int>();

        void Place(FootingScheduleReader.FootingType type, double x0, double y0, double x1, double y1, IEnumerable<int> consumed)
        {
            int index = footings.Count;
            footings.Add(new FootingOutline(type.Mark,
                new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) },
                ((x0 + x1) / 2, (y0 + y1) / 2),
                type.LengthMm, type.WidthMm, type.DepthMm));
            foreach (int p in consumed) { taken.Add(p); pieces[p] = index; }
        }

        // Pass 1: four dashed sides closing a box of a scheduled size.
        if (hc.Count > 0 && vc.Count > 0)
        {
            for (int i = 0; i < hc.Count; i++)
            {
                for (int j = i + 1; j < hc.Count; j++)
                {
                    var top = hc[i]; var bot = hc[j];
                    if (Math.Abs(top.From - bot.From) > sizeToleranceMm || Math.Abs(top.To - bot.To) > sizeToleranceMm) continue;
                    double y0 = Math.Min(top.At, bot.At), y1 = Math.Max(top.At, bot.At);
                    double w = top.To - top.From, h = y1 - y0;
                    if (h < sizeToleranceMm) continue;

                    var type = TypeOf(spread, w, h, sizeToleranceMm, labels, top.From, y0, top.To, y1);
                    if (type is null) continue;

                    var left = vc.FirstOrDefault(v => Math.Abs(v.At - top.From) <= sizeToleranceMm && v.From <= y0 + sizeToleranceMm && v.To >= y1 - sizeToleranceMm);
                    var right = vc.FirstOrDefault(v => Math.Abs(v.At - top.To) <= sizeToleranceMm && v.From <= y0 + sizeToleranceMm && v.To >= y1 - sizeToleranceMm);
                    if (left is null || right is null) continue;

                    var all = top.Pieces.Concat(bot.Pieces).Concat(left.Pieces).Concat(right.Pieces).ToList();
                    if (all.Any(taken.Contains)) continue;   // a side belongs to one footing
                    Place(type, top.From, y0, top.To, y1, all);
                }
            }
        }

        // Pass 2: one full side of a scheduled length and a stub at each end, both pointing the
        // same way — the outline of a footing something stands on (a wall, the match line).
        foreach (var side in hc)
        {
            if (side.Pieces.Any(taken.Contains)) continue;
            double span = side.To - side.From;
            foreach (var (type, depth) in DepthsFor(spread, span, sizeToleranceMm))
            {
                var (a, dirA) = Stub(vAll, side.From, side.At, depth, sizeToleranceMm, taken);
                var (b, dirB) = Stub(vAll, side.To, side.At, depth, sizeToleranceMm, taken);
                if (a is null || b is null || dirA != dirB) continue;
                double y0 = dirA > 0 ? side.At : side.At - depth, y1 = y0 + depth;
                Place(type, side.From, y0, side.To, y1, side.Pieces.Concat(a.Pieces).Concat(b.Pieces));
                break;
            }
        }
        foreach (var side in vc)
        {
            if (side.Pieces.Any(taken.Contains)) continue;
            double span = side.To - side.From;
            foreach (var (type, depth) in DepthsFor(spread, span, sizeToleranceMm))
            {
                var (a, dirA) = Stub(hAll, side.From, side.At, depth, sizeToleranceMm, taken);
                var (b, dirB) = Stub(hAll, side.To, side.At, depth, sizeToleranceMm, taken);
                if (a is null || b is null || dirA != dirB) continue;
                double x0 = dirA > 0 ? side.At : side.At - depth, x1 = x0 + depth;
                Place(type, x0, side.From, x1, side.To, side.Pieces.Concat(a.Pieces).Concat(b.Pieces));
                break;
            }
        }

        // A label names the footing it is nearest to, when it stands in that footing or within half
        // its size of its edge. Measured: inside the box on 31065 and 31138 (37 of 37); 254–461 mm
        // beneath it on 31130 (67 of 68), whose drafter labels under the outline beside the column.
        foreach (var label in labels)
        {
            int nearest = -1; double best = double.MaxValue;
            for (int i = 0; i < footings.Count; i++)
            {
                double d = EdgeDistance(footings[i], label.X, label.Y);
                if (d < best) { best = d; nearest = i; }
            }
            if (nearest < 0) continue;
            var f = footings[nearest];
            if (best <= Math.Max(f.LengthMm, f.WidthMm) / 2 && string.Equals(f.Mark, label.Mark, StringComparison.OrdinalIgnoreCase))
                footings[nearest] = f with { LabelledOnThePlan = true };
        }
        return (footings, pieces);
    }

    /// <summary>Distance from a point to the footing's box: 0 inside, else to the nearest edge.</summary>
    private static double EdgeDistance(FootingOutline f, double x, double y)
    {
        double x0 = f.Outline.Min(p => p.X), x1 = f.Outline.Max(p => p.X), y0 = f.Outline.Min(p => p.Y), y1 = f.Outline.Max(p => p.Y);
        double dx = Math.Max(Math.Max(x0 - x, 0), x - x1), dy = Math.Max(Math.Max(y0 - y, 0), y - y1);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The scheduled type a w × h box is, either orientation; the one whose mark stands in the box when several fit.</summary>
    private static FootingScheduleReader.FootingType? TypeOf(List<FootingScheduleReader.FootingType> spread, double w, double h, double tol,
        IReadOnlyList<MarkLabel> labels, double x0, double y0, double x1, double y1)
    {
        var fits = spread.Where(t =>
            (Math.Abs(w - t.LengthMm) <= tol && Math.Abs(h - t.WidthMm) <= tol) ||
            (Math.Abs(w - t.WidthMm) <= tol && Math.Abs(h - t.LengthMm) <= tol)).ToList();
        if (fits.Count <= 1) return fits.FirstOrDefault();
        return fits.FirstOrDefault(t => labels.Any(l => string.Equals(l.Mark, t.Mark, StringComparison.OrdinalIgnoreCase)
                && l.X >= x0 - LabelSlackMm && l.X <= x1 + LabelSlackMm && l.Y >= y0 - LabelSlackMm && l.Y <= y1 + LabelSlackMm))
            ?? fits[0];
    }

    /// <summary>For a full side of this span, each scheduled type it could be and the box depth away from it.</summary>
    private static IEnumerable<(FootingScheduleReader.FootingType Type, double Depth)> DepthsFor(
        List<FootingScheduleReader.FootingType> spread, double span, double tol)
    {
        foreach (var t in spread)
        {
            if (Math.Abs(span - t.LengthMm) <= tol) yield return (t, t.WidthMm);
            else if (Math.Abs(span - t.WidthMm) <= tol) yield return (t, t.LengthMm);
        }
    }

    /// <summary>
    /// A perpendicular chain that starts at (<paramref name="at"/>, <paramref name="from"/>) — one end
    /// within tolerance of the side — and runs no further than the box is deep. +1 when it runs
    /// toward larger coordinates, −1 toward smaller; null when there is none.
    /// </summary>
    private static (Chain? Stub, int Direction) Stub(List<Chain> perpendicular, double at, double from, double depth, double tol, HashSet<int> taken)
    {
        Chain? best = null; int dir = 0;
        foreach (var c in perpendicular)
        {
            if (Math.Abs(c.At - at) > tol || c.To - c.From > depth + tol || c.Pieces.Any(taken.Contains)) continue;
            int d = Math.Abs(c.From - from) <= tol ? +1 : Math.Abs(c.To - from) <= tol ? -1 : 0;
            if (d == 0) continue;
            if (best is null || c.To - c.From > best.To - best.From) { best = c; dir = d; }
        }
        return (best, dir);
    }

    /// <summary>
    /// Pieces cluster into sides by their shared coordinate — sorted, a new side where the gap to the
    /// previous piece exceeds <see cref="CollinearMm"/>, never by a fixed bucket — and within a side
    /// chain across gaps no wider than the dash-join gap.
    /// </summary>
    private static List<Chain> Chains(List<Piece> pieces, double gapMm)
    {
        var out_ = new List<Chain>();
        if (pieces.Count == 0) return out_;
        pieces.Sort((a, b) => a.At.CompareTo(b.At));
        int start = 0;
        for (int i = 1; i <= pieces.Count; i++)
        {
            if (i < pieces.Count && pieces[i].At - pieces[i - 1].At <= CollinearMm) continue;
            var side = pieces.GetRange(start, i - start);
            side.Sort((a, b) => a.From.CompareTo(b.From));
            double from = side[0].From, to = side[0].To, at = side[0].At; var members = new List<int> { side[0].Index };
            for (int k = 1; k < side.Count; k++)
            {
                var s = side[k];
                if (s.From - to <= gapMm) { to = Math.Max(to, s.To); at += s.At; members.Add(s.Index); }
                else
                {
                    out_.Add(new Chain(at / members.Count, from, to, members));
                    from = s.From; to = s.To; at = s.At; members = new List<int> { s.Index };
                }
            }
            out_.Add(new Chain(at / members.Count, from, to, members));
            start = i;
        }
        return out_;
    }
}
