using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A footing is a dashed rectangle whose size the foundation schedule declares, and its mark is the
/// schedule's. The dashes arrive as separate two-point strokes, so a side is a chain of collinear
/// pieces across dash gaps, and a footing is four sides closing a box.
/// </summary>
/// <remarks>
/// Measured 2026-09-08 on three foundation plans before this was written. Not one footing outline
/// is a closed path; every one is dash pieces. Chained with the DXF side's own dash-join gap
/// (`dxf.dash-join-gap` = 14 in) and closed into boxes, the boxes match the schedule's sizes to the
/// millimetre. Read of placed, by the compiled reader (`takeoff pdf-inventory`, the ledger's
/// footing row): 31130 p11 35 of 36 — F1 2 of 1, F2 14 of 14, F3 8 of 8, F4 11 of 13; 31138 p9
/// 11 of 11 — F1 7 of 7, F2 4 of 4; 31065 p14 26 of 31 — F1 4 of 4, F2 12 of 13, F3 0 of 1,
/// F4 10 of 13. Before this the pieces went to the DXF as BEAM lines or were dropped as too short,
/// and the intake placed no footing anywhere.
///
/// WHAT IT COVERS: axis-aligned dashed rectangles of a scheduled spread-footing size, either
/// orientation, within <see cref="SizeToleranceMm"/>; each consumed piece is reported so the
/// classifier can record its fate. WHAT IT DOES NOT: strip footings (a size, not a box), rotated
/// footings, a footing drawn as a solid closed path (that is the slab branch's, and a later rule),
/// a footing whose size the schedule does not declare, tying a box to the mark label standing in
/// it (so a dashed 4 ft square with no F1 beside it is still counted as F1 — the "2 of 1" above),
/// and the misses — 2 of 13 F4 on 31130 p11; 1 of 13 F2, 1 of 1 F3, 3 of 13 F4 on 31065 p14 —
/// measured, not explained yet.
/// </remarks>
public static class FootingOutlines
{
    /// <summary>The DXF side's dash-join gap, 14 in, in millimetres.</summary>
    public const double DashGapMm = 355.6;
    /// <summary>How far a drawn side may differ from the scheduled size.</summary>
    public const double SizeToleranceMm = 60.0;
    /// <summary>Collinearity: pieces within this of one another's line are one side.</summary>
    public const double CollinearMm = 12.0;
    /// <summary>A dashed side has at least this many pieces; a solid line has one.</summary>
    public const int MinPieces = 3;

    public sealed record Chain(double At, double From, double To, IReadOnlyList<int> Pieces);

    /// <summary>Footings on the page, and which raw-path index belongs to which footing.</summary>
    public static (IReadOnlyList<FootingOutline> Footings, IReadOnlyDictionary<int, int> Pieces) Read(
        IReadOnlyList<RawSubpath> raw,
        IReadOnlyList<FootingScheduleReader.FootingType> types,
        double dashGapMm = DashGapMm,
        double sizeToleranceMm = SizeToleranceMm)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(types);
        var footings = new List<FootingOutline>();
        var pieces = new Dictionary<int, int>();
        var spread = types.Where(t => t.IsSpread).ToList();
        if (spread.Count == 0 || raw.Count == 0) return (footings, pieces);

        // horizontal and vertical two-point strokes, bucketed by the coordinate they share
        var hs = new Dictionary<long, List<(double From, double To, int Index, double At)>>();
        var vs = new Dictionary<long, List<(double From, double To, int Index, double At)>>();
        for (int i = 0; i < raw.Count; i++)
        {
            var s = raw[i];
            if (s.IsAnnotation || s.IsFilled || !s.IsStroked || s.Points.Count != 2) continue;
            var (a, b) = (s.Points[0], s.Points[1]);
            if (Math.Abs(a.Y - b.Y) <= CollinearMm && Math.Abs(a.X - b.X) > CollinearMm)
                Add(hs, (long)Math.Round(a.Y / CollinearMm), (Math.Min(a.X, b.X), Math.Max(a.X, b.X), i, (a.Y + b.Y) / 2));
            else if (Math.Abs(a.X - b.X) <= CollinearMm && Math.Abs(a.Y - b.Y) > CollinearMm)
                Add(vs, (long)Math.Round(a.X / CollinearMm), (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), i, (a.X + b.X) / 2));
        }
        var hc = Chains(hs, dashGapMm).Where(c => c.Pieces.Count >= MinPieces).ToList();
        var vc = Chains(vs, dashGapMm).Where(c => c.Pieces.Count >= MinPieces).ToList();
        if (hc.Count == 0 || vc.Count == 0) return (footings, pieces);

        var taken = new HashSet<int>();
        for (int i = 0; i < hc.Count; i++)
        {
            for (int j = i + 1; j < hc.Count; j++)
            {
                var top = hc[i]; var bot = hc[j];
                if (Math.Abs(top.From - bot.From) > sizeToleranceMm || Math.Abs(top.To - bot.To) > sizeToleranceMm) continue;
                double y0 = Math.Min(top.At, bot.At), y1 = Math.Max(top.At, bot.At);
                double w = top.To - top.From, h = y1 - y0;
                if (h < sizeToleranceMm) continue;

                var type = spread.FirstOrDefault(t =>
                    (Math.Abs(w - t.LengthMm) <= sizeToleranceMm && Math.Abs(h - t.WidthMm) <= sizeToleranceMm) ||
                    (Math.Abs(w - t.WidthMm) <= sizeToleranceMm && Math.Abs(h - t.LengthMm) <= sizeToleranceMm));
                if (type is null) continue;

                var left = vc.FirstOrDefault(v => Math.Abs(v.At - top.From) <= sizeToleranceMm && v.From <= y0 + sizeToleranceMm && v.To >= y1 - sizeToleranceMm);
                var right = vc.FirstOrDefault(v => Math.Abs(v.At - top.To) <= sizeToleranceMm && v.From <= y0 + sizeToleranceMm && v.To >= y1 - sizeToleranceMm);
                if (left is null || right is null) continue;

                var all = top.Pieces.Concat(bot.Pieces).Concat(left.Pieces).Concat(right.Pieces).ToList();
                if (all.Any(taken.Contains)) continue;   // a side belongs to one footing

                int index = footings.Count;
                footings.Add(new FootingOutline(type.Mark,
                    new[] { (top.From, y0), (top.To, y0), (top.To, y1), (top.From, y1) },
                    ((top.From + top.To) / 2, (y0 + y1) / 2),
                    type.LengthMm, type.WidthMm, type.DepthMm));
                foreach (int p in all) { taken.Add(p); pieces[p] = index; }
            }
        }
        return (footings, pieces);
    }

    private static void Add<T>(Dictionary<long, List<T>> d, long key, T item)
    {
        if (!d.TryGetValue(key, out var list)) d[key] = list = new List<T>();
        list.Add(item);
    }

    /// <summary>Collinear pieces chained across gaps no wider than the dash-join gap.</summary>
    private static List<Chain> Chains(Dictionary<long, List<(double From, double To, int Index, double At)>> groups, double gapMm)
    {
        var out_ = new List<Chain>();
        foreach (var (_, segs) in groups)
        {
            segs.Sort((a, b) => a.From.CompareTo(b.From));
            double from = segs[0].From, to = segs[0].To, at = segs[0].At; var members = new List<int> { segs[0].Index };
            for (int i = 1; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.From - to <= gapMm) { to = Math.Max(to, s.To); at += s.At; members.Add(s.Index); }
                else
                {
                    out_.Add(new Chain(at / members.Count, from, to, members));
                    from = s.From; to = s.To; at = s.At; members = new List<int> { s.Index };
                }
            }
            out_.Add(new Chain(at / members.Count, from, to, members));
        }
        return out_;
    }
}
