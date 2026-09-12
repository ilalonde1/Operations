#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// A model measured against the engineer's own model of the same job — the yardstick — column by
/// column, storey by storey: the positional check the counts never give. Ported 2026-09-11 from
/// <c>columns_vs_yardstick.py</c> (built 2026-09-10 to measure the centroid fix) into the code the
/// corpus analyzer runs, so every job that holds both a stick file and a model (66 on the share; 60
/// with an .e2k after the KOR-210 export) gets its figure in the ledger, not by hand for one.
/// </summary>
/// <remarks>
/// HOW THE TWO FRAMES MEET: by grid NAME. The same axis label in the same grid system in both
/// files is the same line, so the offset between the models is the median difference over the
/// shared X labels and over the shared Y labels — keyed by system too, since a set with two
/// buildings names two grids (Codex audit F23). With fewer than two shared labels a side, the
/// modal displacement between nearest columns stands in, and the result says GUESS.
/// HOW STOREYS MEET: by full name first (A-L1 to A-L1), and by the stripped name (building prefix
/// off, LEVEL to L) only where the yardstick has no full-name match — A-L1 and B-L1 both strip to
/// L1 and one overwrote the other before (F23). Storeys only one model names are listed, not
/// silently skipped: they are the finding as often as the residuals are.
/// WHAT IS MEASURED, BOTH WAYS: for each of OUR columns on a shared storey, the distance to the
/// nearest of THEIRS (precision — are ours where theirs are); and for each of THEIRS, the distance
/// to the nearest of ours (recall — did we read the columns they modelled). One-sided nearest
/// neighbour hides a missing column entirely; the script measured one way, this measures both.
/// Units come off each file's own CONTROLS line. WHAT THIS DOES NOT DO: walls and plates (next);
/// tell a column moved from a column misread — a 300 mm residual is either; say which issue of
/// the drawings the engineer's model was built from (the census records both dates).
/// </remarks>
public static class ModelYardstick
{
    public sealed record StoreyFigure(string Storey, string YardstickStorey, int Ours, int Theirs, double MedianMm, int OursWithin100, int TheirsWithin100);

    public sealed record Comparison(
        string Model, string Yardstick, double ModelUnitMm, double YardstickUnitMm,
        int SharedXLabels, int SharedYLabels, (double X, double Y)? ShiftMm, bool ShiftFromGrids, double LabelSpreadMm,
        int ModelStoreys, int YardstickStoreys, IReadOnlyList<StoreyFigure> Storeys,
        IReadOnlyList<string> StoreysOnlyInModel, IReadOnlyList<string> StoreysOnlyInYardstick,
        int ModelColumns, int YardstickColumns,
        int FrameSupport,
        int OursCompared, double OursMedianMm, int OursWithin50, int OursWithin100, int OursWithin300,
        int TheirsCompared, double TheirsMedianMm, int TheirsWithin100,
        IReadOnlyList<string> Notes)
    {
        public double OursWithin100Share => OursCompared == 0 ? 0 : (double)OursWithin100 / OursCompared;
        public double TheirsWithin100Share => TheirsCompared == 0 ? 0 : (double)TheirsWithin100 / TheirsCompared;
    }

    private static readonly Regex GridLine = new(@"^\s*GRID\s+""(?<sys>[^""]*)""\s+LABEL\s+""(?<label>[^""]+)""\s+DIR\s+""(?<dir>[XY])""\s+COORD\s+(?<coord>-?[\d.eE+]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BuildingPrefix = new(@"^[A-C]-", RegexOptions.Compiled);

    public static Comparison Compare(string modelE2k, string yardstickE2k)
    {
        var model = E2kDocument.Load(modelE2k);
        var yard = E2kDocument.Load(yardstickE2k);
        double mu = (model.LengthUnitInInches() ?? 1.0) * 25.4;   // mm per model unit; an unreadable unit reads as inches and says so
        double yu = (yard.LengthUnitInInches() ?? 1.0) * 25.4;
        var notes = new List<string>();
        if (model.LengthUnitInInches() is null) notes.Add("model's unit unreadable; taken as inches");
        if (yard.LengthUnitInInches() is null) notes.Add("yardstick's unit unreadable; taken as inches");

        var ours = ColumnsByStorey(model, mu);
        var theirs = ColumnsByStorey(yard, yu);

        // the frames, by grid name
        var gm = Grids(model, mu);
        var gy = Grids(yard, yu);
        var dx = gm.Keys.Where(k => k.Dir == "X" && gy.ContainsKey(k)).Select(k => gy[k] - gm[k]).ToList();
        var dy = gm.Keys.Where(k => k.Dir == "Y" && gy.ContainsKey(k)).Select(k => gy[k] - gm[k]).ToList();
        (double X, double Y)? shift = null;
        bool fromGrids = false;
        double spread = 0;
        if (dx.Count >= 2 && dy.Count >= 2)
        {
            shift = (Median(dx), Median(dy));
            fromGrids = true;
            spread = Math.Max(dx.Max() - dx.Min(), dy.Max() - dy.Min());
        }

        // storeys, full name first
        var theirsByFull = theirs.ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => (Name: kv.Key, Points: kv.Value), StringComparer.Ordinal);
        var theirsByStripped = new Dictionary<string, (string Name, List<(double X, double Y)> Points)>(StringComparer.Ordinal);
        foreach (var (name, pts) in theirs)
        {
            string k = Stripped(name);
            if (theirsByStripped.TryGetValue(k, out var had)) { had.Points.AddRange(pts); theirsByStripped[k] = (had.Name + "+" + name, had.Points); }
            else theirsByStripped[k] = (name, new List<(double X, double Y)>(pts));
        }
        (string Name, IReadOnlyList<(double X, double Y)> Points)? TheirsFor(string storey)
        {
            if (theirsByFull.TryGetValue(storey.ToUpperInvariant(), out var full)) return (full.Name, full.Points);
            // the stripped name meets only within the same building: our unprefixed L4 met their C-LEVEL 4
            // at 33 m on 31168 (2026-09-11) - a different tower's fourth floor
            if (theirsByStripped.TryGetValue(Stripped(storey), out var s) && s.Name.Split('+').All(n => Building(n) == Building(storey))) return (s.Name, s.Points);
            return null;
        }

        // the storeys both name, and their columns
        var shared = new List<(string Ours, string Theirs, IReadOnlyList<(double X, double Y)> OurPts, IReadOnlyList<(double X, double Y)> TheirPts)>();
        var matchedTheirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (storey, pts) in ours)
        {
            var t = TheirsFor(storey);
            if (t is null || t.Value.Points.Count == 0 || pts.Count == 0) continue;
            matchedTheirs.Add(t.Value.Name);
            shared.Add((storey, t.Value.Name, pts, t.Value.Points));
        }

        // THE FRAME FROM THE COLUMNS THEMSELVES when the grids give none: 68 of the 81 engineers' models
        // exported on 2026-09-11 carry no GRID lines at all, and a set's model can sit at survey
        // coordinates hundreds of metres from the drawing's frame, where "nearest column" means nothing.
        // Every pair of our column and their column on a shared storey votes for the translation between
        // them (100 mm bins); the fullest bin, refined to the median of the pairs it holds, is the frame,
        // and the SUPPORT - how many of our columns then have one of theirs within 100 mm - is reported
        // with it, so a weak registration (two different structures under one job number) is read as
        // "not comparable", never as a residual.
        int support = 0;
        if (!fromGrids && shared.Count > 0)
        {
            var (regShift, regSupport) = Register(shared);
            shift = regShift;
            support = regSupport;
            int oursTotal = shared.Sum(s => s.OurPts.Count);
            notes.Add($"frame from column registration, no shared grid labels ({dx.Count} X / {dy.Count} Y): {regSupport} of {oursTotal} of our columns within 100 mm of one of theirs after it");
        }
        else if (fromGrids)
        {
            var s0 = shift!.Value;
            support = shared.Sum(s => s.OurPts.Count(p => s.TheirPts.Any(q => Sq((q.X - s0.X, q.Y - s0.Y), p) <= 100.0 * 100.0)));
        }
        var sh = shift ?? (0, 0);

        var pairs = new List<(string Storey, string Theirs, (double X, double Y) P, (double X, double Y) Q)>();
        foreach (var s in shared)
            foreach (var p in s.OurPts)
                pairs.Add((s.Ours, s.Theirs, p, s.TheirPts.MinBy(q => Sq((q.X - sh.X, q.Y - sh.Y), p))));

        var residuals = pairs.Select(pr => Math.Sqrt(Sq((pr.Q.X - sh.X, pr.Q.Y - sh.Y), pr.P))).ToList();

        // and theirs to ours: every column they modelled on a matched storey, to the nearest of ours -
        // ONCE per yardstick storey, against the union of our storeys that met it (two of ours can meet
        // one of theirs through the stripped name, and counting theirs per our storey counted them twice)
        var theirResiduals = new List<(string Storey, double R)>();
        foreach (var bucket in ours.Where(kv => kv.Value.Count > 0).Select(kv => (Ours: kv.Key, Theirs: TheirsFor(kv.Key))).Where(t => t.Theirs is not null).GroupBy(t => t.Theirs!.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ourPoints = bucket.SelectMany(t => ours[t.Ours]).ToList();
            string label = string.Join("+", bucket.Select(t => t.Ours));
            foreach (var q in bucket.First().Theirs!.Value.Points)
            {
                var qq = (q.X - sh.X, q.Y - sh.Y);
                theirResiduals.Add((label, Math.Sqrt(ourPoints.Min(p => Sq(p, qq)))));
            }
        }

        var storeyFigures = new List<StoreyFigure>();
        foreach (var g in pairs.GroupBy(pr => pr.Storey))
        {
            var rs = g.Select(pr => Math.Sqrt(Sq((pr.Q.X - sh.X, pr.Q.Y - sh.Y), pr.P))).ToList();
            var trs = theirResiduals.Where(t => t.Storey.Split('+').Contains(g.Key, StringComparer.Ordinal)).Select(t => t.R).ToList();
            storeyFigures.Add(new StoreyFigure(g.Key, g.First().Theirs, rs.Count, trs.Count, Median(rs), rs.Count(r => r <= 100), trs.Count(r => r <= 100)));
        }
        storeyFigures = storeyFigures.OrderByDescending(f => f.Ours).ThenBy(f => f.Storey, StringComparer.Ordinal).ToList();

        var onlyModel = ours.Keys.Where(s => TheirsFor(s) is null).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var onlyYard = theirs.Keys.Where(s => !matchedTheirs.Contains(s) && !matchedTheirs.Any(m => m.Split('+').Contains(s, StringComparer.OrdinalIgnoreCase))).OrderBy(s => s, StringComparer.Ordinal).ToList();

        return new Comparison(
            modelE2k, yardstickE2k, mu, yu,
            dx.Count, dy.Count, shift, fromGrids, spread,
            ours.Count, theirs.Count, storeyFigures, onlyModel, onlyYard,
            ours.Values.Sum(v => v.Count), theirs.Values.Sum(v => v.Count),
            support,
            residuals.Count, residuals.Count == 0 ? 0 : Median(residuals), residuals.Count(r => r <= 50), residuals.Count(r => r <= 100), residuals.Count(r => r <= 300),
            theirResiduals.Count, theirResiduals.Count == 0 ? 0 : Median(theirResiduals.Select(t => t.R).ToList()), theirResiduals.Count(t => t.R <= 100),
            notes);
    }

    /// <summary>The figures a person reads first — X of Y, both ways, every storey.</summary>
    public static string Summary(Comparison c)
    {
        var sb = new StringBuilder();
        string frame = c.ShiftFromGrids
            ? $"frames matched on {c.SharedXLabels} X and {c.SharedYLabels} Y grid labels both models name; offset ({c.ShiftMm!.Value.X:N0}, {c.ShiftMm.Value.Y:N0}) mm, the labels' own disagreement up to {c.LabelSpreadMm:N0} mm"
            : c.ShiftMm is { } s ? $"frames matched by column registration ({s.X:N0}, {s.Y:N0}) mm, no shared grid labels ({c.SharedXLabels} X / {c.SharedYLabels} Y): {c.FrameSupport} of {c.OursCompared} of our columns within 100 mm of one of theirs"
            : "frames not matched: no shared grid labels and no columns on a shared storey";
        sb.AppendLine(frame);
        sb.AppendLine(CultureInfo.InvariantCulture, $"storeys: ours {c.ModelStoreys}, theirs {c.YardstickStoreys}, shared {c.Storeys.Count}; only ours: {(c.StoreysOnlyInModel.Count == 0 ? "-" : string.Join(" ", c.StoreysOnlyInModel))}; only theirs: {(c.StoreysOnlyInYardstick.Count == 0 ? "-" : string.Join(" ", c.StoreysOnlyInYardstick))}");
        if (c.OursCompared > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"ours -> theirs: {c.OursCompared} columns on shared storeys, nearest of theirs: median {c.OursMedianMm:N0} mm; within 50 mm {c.OursWithin50} ({100.0 * c.OursWithin50 / c.OursCompared:F0}%), within 100 mm {c.OursWithin100} ({100.0 * c.OursWithin100 / c.OursCompared:F0}%), within 300 mm {c.OursWithin300} ({100.0 * c.OursWithin300 / c.OursCompared:F0}%)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"theirs -> ours: {c.TheirsCompared} columns they modelled on those storeys, nearest of ours: median {c.TheirsMedianMm:N0} mm; within 100 mm {c.TheirsWithin100} ({100.0 * c.TheirsWithin100 / Math.Max(1, c.TheirsCompared):F0}%)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{c.Storeys.Count} storeys both models name, every one (ours / theirs / median / ours within 100 / theirs within 100):");
            foreach (var f in c.Storeys)
                sb.AppendLine(CultureInfo.InvariantCulture, $"   {f.Storey,-10} {f.YardstickStorey,-10} {f.Ours,4} {f.Theirs,4}  {f.MedianMm,7:N0} mm  {100.0 * f.OursWithin100 / f.Ours,3:F0}%  {100.0 * f.TheirsWithin100 / Math.Max(1, f.Theirs),3:F0}%");
        }
        else sb.AppendLine("no columns on a storey both models name");
        foreach (var n in c.Notes) sb.AppendLine("  note: " + n);
        return sb.ToString();
    }

    /// <summary>Every COLUMN line object's first joint, in mm, by the storey it is assigned to.</summary>
    private static Dictionary<string, List<(double X, double Y)>> ColumnsByStorey(E2kDocument doc, double unitMm)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^LINE\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var order = doc.ReadStories().Select(s => s.Name).ToList();
        var result = new Dictionary<string, List<(double X, double Y)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, kind) in kinds)
        {
            if (!kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)) continue;
            if (!points.TryGetValue(name, out var pts) || pts.Count == 0) continue;
            if (!storeysOf.TryGetValue(name, out var on)) continue;
            var p = (pts[0].X * unitMm, pts[0].Y * unitMm);
            foreach (var storey in on)
                (result.TryGetValue(storey, out var list) ? list : result[storey] = new List<(double X, double Y)>()).Add(p);
        }
        // in the model's storey order, top down, as every other reading of a model lists them
        return order.Where(result.ContainsKey).Concat(result.Keys.Where(k => !order.Contains(k, StringComparer.OrdinalIgnoreCase)))
            .ToDictionary(s => s, s => result[s], StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<(string System, string Label, string Dir), double> Grids(E2kDocument doc, double unitMm)
    {
        var grids = new Dictionary<(string, string, string), double>();
        foreach (string raw in doc.LinesOf("GRIDS"))
        {
            var m = GridLine.Match(raw);
            if (!m.Success) continue;
            if (double.TryParse(m.Groups["coord"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
                grids[(m.Groups["sys"].Value, m.Groups["label"].Value.ToUpperInvariant(), m.Groups["dir"].Value.ToUpperInvariant())] = c * unitMm;
        }
        return grids;
    }

    /// <summary>
    /// A storey's name with the building prefix off and LEVEL shortened the way the PDF route names
    /// storeys: "A-LEVEL 1" and "A-L1" are one storey, "LEVEL P2" and "P2" are one storey (a parkade
    /// level is P-something, not L-P-something — the first port left "LEVEL P1" and "P1" unmatched on
    /// 31168), "LEVEL 1 MEZZ" is "L1 MEZZ".
    /// </summary>
    internal static string Stripped(string storey)
    {
        string s = BuildingPrefix.Replace(storey.Trim().ToUpperInvariant(), "");
        s = ParkadeLevel.Replace(s, "$1");
        return LevelWord.Replace(s, "L");
    }

    private static readonly Regex ParkadeLevel = new(@"^LEVEL\s*(P\d+)", RegexOptions.Compiled);
    private static readonly Regex LevelWord = new(@"LEVEL\s*", RegexOptions.Compiled);

    /// <summary>The building a storey name is prefixed with ("A-L1" → A), or null for a storey named for the whole job.</summary>
    internal static string? Building(string storey)
    {
        var m = BuildingPrefix.Match(storey.Trim().ToUpperInvariant());
        return m.Success ? m.Value.TrimEnd('-') : null;
    }

    private static double Sq((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>The translation (theirs = ours + shift) most of the column pairs agree on, and how many of our columns it places within 100 mm of one of theirs.</summary>
    internal static ((double X, double Y) Shift, int Support) Register(
        IReadOnlyList<(string Ours, string Theirs, IReadOnlyList<(double X, double Y)> OurPts, IReadOnlyList<(double X, double Y)> TheirPts)> shared)
    {
        const double bin = 100.0;
        var votes = new Dictionary<(long, long), int>();
        foreach (var s in shared)
            foreach (var p in s.OurPts)
                foreach (var q in s.TheirPts)
                {
                    var key = ((long)Math.Round((q.X - p.X) / bin), (long)Math.Round((q.Y - p.Y) / bin));
                    votes[key] = votes.TryGetValue(key, out int n) ? n + 1 : 1;
                }
        if (votes.Count == 0) return ((0, 0), 0);
        var best = votes.MaxBy(kv => kv.Value).Key;
        var coarse = (X: best.Item1 * bin, Y: best.Item2 * bin);
        // refine: the median of the pairs inside the winning bin and its neighbours
        var dxs = new List<double>(); var dys = new List<double>();
        foreach (var s in shared)
            foreach (var p in s.OurPts)
                foreach (var q in s.TheirPts)
                    if (Math.Abs(q.X - p.X - coarse.X) <= 1.5 * bin && Math.Abs(q.Y - p.Y - coarse.Y) <= 1.5 * bin) { dxs.Add(q.X - p.X); dys.Add(q.Y - p.Y); }
        var shift = dxs.Count == 0 ? coarse : (X: Median(dxs), Y: Median(dys));
        int support = shared.Sum(s => s.OurPts.Count(p => s.TheirPts.Any(q => Sq((q.X - shift.X, q.Y - shift.Y), p) <= 100.0 * 100.0)));
        return (shift, support);
    }

    private static double Median(List<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
