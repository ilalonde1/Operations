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
    public sealed record StoreyFigure(string Storey, string YardstickStorey, int Ours, int Theirs, double MedianMm, int OursWithin100, int TheirsWithin100, int OursBeyond = 0)
    {
        /// <summary>
        /// WHAT KIND OF ERROR A STOREY'S RESIDUAL IS. The median of the offset VECTORS (ours to nearest of
        /// hers, after the global frame) over the storey's pairs within 600 mm: a storey whose columns all
        /// sit 300 mm east of hers has a rigid part of (300, 0) - the sheet was set on the grid 300 mm off,
        /// or she and we place the storey's columns by different rules (face on grid, centre) - where a
        /// storey read badly has a rigid part near zero and a spread that stays. Zero when no pair is
        /// within 600 mm.
        /// </summary>
        public (double X, double Y) RigidMm { get; init; }
        /// <summary>The storey's median residual after its rigid part is taken out; what a placement fix could reach.</summary>
        public double MedianAfterRigidMm { get; init; }
        /// <summary>Ours within 100 mm after the rigid part is taken out.</summary>
        public int OursWithin100AfterRigid { get; init; }
    }

    /// <summary>
    /// How far past the engineer's outermost column on a storey a column of ours may stand and still be
    /// judged against her model: registration slop and a column drawn at the slab edge, not a bay. A
    /// bay is 5 m and more; a second tower on the same storey starts a bay past her last column.
    /// </summary>
    public const double FootprintMarginMm = 1500;

    public sealed record Comparison(
        string Model, string Yardstick, double ModelUnitMm, double YardstickUnitMm,
        int SharedXLabels, int SharedYLabels, (double X, double Y)? ShiftMm, bool ShiftFromGrids, double LabelSpreadMm,
        int ModelStoreys, int YardstickStoreys, IReadOnlyList<StoreyFigure> Storeys,
        IReadOnlyList<string> StoreysOnlyInModel, IReadOnlyList<string> StoreysOnlyInYardstick,
        int ModelColumns, int YardstickColumns,
        int FrameSupport,
        int OursCompared, double OursMedianMm, int OursWithin50, int OursWithin100, int OursWithin300,
        int TheirsCompared, double TheirsMedianMm, int TheirsWithin100,
        int OursBeyondHerModel, int OursOnHerWalls,
        IReadOnlyList<string> Notes)
    {
        /// <summary>Our columns on shared storeys with none of theirs within 300 mm, counted by the section we gave them — what we read that the engineer did not model, named by its size.</summary>
        public IReadOnlyList<(string Section, int Count)> OursUnmatchedBySection { get; init; } = [];
        /// <summary>Their columns on shared storeys with none of ours within 300 mm, by their section — what the engineer modelled that we did not read.</summary>
        public IReadOnlyList<(string Section, int Count)> TheirsUnmatchedBySection { get; init; } = [];
        /// <summary>Of ours unmatched, those standing within 100 mm of one of her wall panels on that storey: modelled by her as a wall, by us as a column.</summary>
        public IReadOnlyList<(string Section, int Count)> OursOnHerWallsBySection { get; init; } = [];
        /// <summary>
        /// WHERE EACH MODEL PUTS A COLUMN AGAINST THE GRID (2026-09-14). On the storeys judged, of our
        /// columns and of hers (in our frame), how many stand within 50 mm of one of OUR grid lines in X, in
        /// Y, and of an intersection - our grid being the one the plans drew. A yardstick where hers sit on
        /// the intersections and ours do not says she models a column at its grid intersection, not at its
        /// drawn centre; the residual is then half a column, not a reading error. Zero when our model has
        /// no grid lines.
        /// </summary>
        public (int OursOnX, int OursOnY, int OursOnBoth, int OursN, int TheirsOnX, int TheirsOnY, int TheirsOnBoth, int TheirsN) OnOurGrid { get; init; }
        /// <summary>Every judged column of ours with the nearest of hers: the storey, our section, hers, our point (mm, our frame) and the offset to hers after the global frame - the raw material of every figure above, for looking at one column.</summary>
        public IReadOnlyList<(string Storey, string OurSection, string TheirSection, double X, double Y, double Dx, double Dy)> Pairs { get; init; } = [];
        public double OursWithin100Share => OursCompared == 0 ? 0 : (double)OursWithin100 / OursCompared;
        public double TheirsWithin100Share => TheirsCompared == 0 ? 0 : (double)TheirsWithin100 / TheirsCompared;
        /// <summary>
        /// OPENINGS AGAINST HERS (step 104, 2026-09-16). On the storeys both models name: our openings (an
        /// AREA assigned OPENING "Yes"), hers, ours with the centre of one of hers within
        /// <see cref="OpeningMatchMm"/> of our centre after the frame, and hers with one of ours. Step 104
        /// read the drafter's X as an opening and 31168 came out with 561 where her export carries 64 - this
        /// is the figure that says which of our openings she has, set by set, before any X rule is banked.
        /// </summary>
        public (int Ours, int Theirs, int OursMatched, int TheirsMatched, int OursBeyond) Openings { get; init; }
        /// <summary>Our openings on shared storeys with none of hers near, by plan size (metres, to the half): what we cut that she did not.</summary>
        public IReadOnlyList<(string Size, int Count)> OursOpeningsUnmatchedBySize { get; init; } = [];
        /// <summary>Her openings on shared storeys with none of ours near, by plan size: what she cut that we did not.</summary>
        public IReadOnlyList<(string Size, int Count)> TheirsOpeningsUnmatchedBySize { get; init; } = [];
        /// <summary>
        /// Her openings whose centre stands INSIDE one of ours on the storey (2026-09-17 00:50): a match by cover, beside the
        /// match by centre. 31065 and 31017 cut a stair as its flights (2 x 5.5 m each) where our well spans flights and
        /// landing (2.4 x 7.1 m): by centre the second flight is 2 m off and unmatched, by cover both are had.
        /// </summary>
        public int TheirsOpeningsCovered { get; init; }
        /// <summary>Of ours she has not on the storey, how many stand where she cuts an opening on SOME other storey (00:58): a
        /// storey mapping's miss rather than a mark's, when it is many.</summary>
        public int OursOpeningsHersElsewhere { get; init; }
        /// <summary>
        /// HER OPENINGS WE HAVE, honestly counted (2026-09-18 13:00): by centre (within OpeningMatchMm), by cover (her centre
        /// inside one of ours - a flight inside our well), or as a VOID WE CARRY NO PLATE OVER (her centre outside every floor
        /// plate of ours on the storey: 30993's two courtyards she cuts as 17.5 x 19 m openings in one plate where we leave
        /// them out of the plate - no slab either way). Each way counted, and the union.
        /// </summary>
        public (int ByCentre, int ByCover, int OffOurPlate, int Had) TheirsOpeningsHad { get; init; }
        /// <summary>Her openings under <see cref="SliverMm"/> across (0.0 x 4.5 m along a core wall on 31065, 88 of her 209): a modelling
        /// release, not a hole a drafter draws. Counted here and judged nowhere.</summary>
        public int TheirsOpeningsSlivers { get; init; }
        /// <summary>
        /// Every opening on the shared storeys, ours and hers, in our frame (mm): where it is, its plan box, the nearest of the
        /// other side's on the storey, whether her centre stands inside one of ours, and whether it stands on a plate of ours
        /// at all. The raw material behind the openings figure, for looking at what an unmatched one IS.
        /// </summary>
        public IReadOnlyList<(string Storey, bool Ours, double X, double Y, double W, double H, double NearestMm, bool Covered, bool OnOurPlate)> OpeningRows { get; init; } = [];
        /// <summary>
        /// PLATES, OURS AGAINST HERS, on the storeys both name (2026-09-17 20:50): the plate area each model carries on
        /// the storey, in sq ft - the engineer's own definition of a usable start is the verticals AND the overall
        /// shape of the slab on every storey, and the column figures above say nothing about the slab.
        /// </summary>
        public IReadOnlyList<(string Storey, string YardstickStorey, double OursSqFt, double TheirsSqFt)> Plates { get; init; } = [];
        /// <summary>
        /// SLAB THICKNESS, OURS AGAINST HERS, on the storeys both name (step 118, 2026-09-18): the thickness carried by
        /// most of each model's plate area on the storey, in inches (0 where the storey has no plate). The engineer's
        /// own words: one thickness per floor; and every plate of ours was the 12-in default until the plan's callout
        /// reached the model.
        /// </summary>
        public IReadOnlyList<(string Storey, string YardstickStorey, double OursIn, double TheirsIn)> Thickness { get; init; } = [];
    }

    /// <summary>How far apart the centres of our opening and hers may be and still be one opening: a shaft is 2-3 m across and the frame carries registration slop.</summary>
    public const double OpeningMatchMm = 1500;

    // A BUILDING'S PREFIX IS ANY TAG BEFORE A STOREY WORD (step 69): A-L27, C-ROOF, 1-L2, 12-LEVEL 3 - a letter or a
    // number with an optional letter, then the dash, then a level, parkade or roof word. It was ^[A-C]-, which read
    // building D's storeys as the whole job's and a numbered building's as nothing; and "L-1" (31170's own model
    // spells L-1..L-7) is a storey, not building L, because what follows its dash is a number, not a storey word.
    private static readonly Regex BuildingPrefix = new(@"^(?:[A-Z]|\d{1,2}[A-Z]?)-(?=L\d|P\d|LEVEL|ROOF|ELEV|MEZZ)", RegexOptions.Compiled);

    public static Comparison Compare(string modelE2k, string yardstickE2k) => Compare(modelE2k, yardstickE2k, DefaultStoreyQualifierWords);

    /// <summary>The words a storey is named with for what it carries (step 102): her L17 MECH is our L17. The row dxf.pdf.yardstick-storey-qualifier-words extends these; MEZZ is a level of its own and never here.</summary>
    public static readonly IReadOnlyList<string> DefaultStoreyQualifierWords = ["MECH", "MECHANICAL", "ROOF", "PH", "PENTHOUSE", "AMENITY", "MAIN", "UPPER", "LOWER", "EMR", "TYP", "TYPICAL"];

    public static Comparison Compare(string modelE2k, string yardstickE2k, IReadOnlyList<string> storeyQualifierWords)
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
        var gm = model.ReadGrids().ToDictionary(g => g.Key, g => g.Value * mu);
        var gy = yard.ReadGrids().ToDictionary(g => g.Key, g => g.Value * yu);
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
            string k = Stripped(name, storeyQualifierWords);
            if (theirsByStripped.TryGetValue(k, out var had)) { had.Points.AddRange(pts); theirsByStripped[k] = (had.Name + "+" + name, had.Points); }
            else theirsByStripped[k] = (name, new List<(double X, double Y)>(pts));
        }
        (string Name, IReadOnlyList<(double X, double Y)> Points)? TheirsFor(string storey)
        {
            if (theirsByFull.TryGetValue(storey.ToUpperInvariant(), out var full)) return (full.Name, full.Points);
            // the stripped name meets only within the same building: our unprefixed L4 met their C-LEVEL 4
            // at 33 m on 31168 (2026-09-11) - a different tower's fourth floor
            if (theirsByStripped.TryGetValue(Stripped(storey, storeyQualifierWords), out var s) && s.Name.Split('+').All(n => Building(n) == Building(storey))) return (s.Name, s.Points);
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
            support = shared.Sum(s => s.OurPts.Count(p => s.TheirPts.Any(q => LoopGeometry.Within(Math.Sqrt(Sq((q.X - s0.X, q.Y - s0.Y), p)), 100.0))));
        }
        var sh = shift ?? (0, 0);

        // THE YARDSTICK JUDGES ONLY WHERE THE ENGINEER MODELLED. Her model of 31065 is the podium and ONE
        // of its two towers (35 columns on L3 in a 34 m box; ours 90 across 93 m), so on every tower storey
        // two thirds of our columns had "none of theirs within 300 mm" and a median residual of 20 m - not
        // a defect of ours, a building she did not model (2026-09-12). A column of ours farther than
        // FootprintMarginMm past the box of her columns on that storey is beyond her model: counted and
        // named, judged by nothing. Her columns are all judged against ours as before - what she
        // modelled, we must have read.
        var judged = new List<(string Ours, string Theirs, IReadOnlyList<(double X, double Y)> OurPts, IReadOnlyList<(double X, double Y)> TheirPts)>();
        var beyondByStorey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in shared)
        {
            double x0 = s.TheirPts.Min(q => q.X) - sh.X - FootprintMarginMm, x1 = s.TheirPts.Max(q => q.X) - sh.X + FootprintMarginMm;
            double y0 = s.TheirPts.Min(q => q.Y) - sh.Y - FootprintMarginMm, y1 = s.TheirPts.Max(q => q.Y) - sh.Y + FootprintMarginMm;
            var inside = s.OurPts.Where(p => p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1).ToList();
            beyondByStorey[s.Ours] = s.OurPts.Count - inside.Count;
            if (inside.Count > 0) judged.Add((s.Ours, s.Theirs, inside, s.TheirPts));
        }
        int oursBeyond = beyondByStorey.Values.Sum();
        if (oursBeyond > 0)
            notes.Add($"{oursBeyond} of our columns stand beyond her model's footprint on their storey (more than {FootprintMarginMm / 1000:0.#} m past her outermost column) and are not judged: {string.Join(", ", beyondByStorey.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");

        var pairs = new List<(string Storey, string Theirs, (double X, double Y) P, (double X, double Y) Q)>();
        foreach (var s in judged)
            foreach (var p in s.OurPts)
                pairs.Add((s.Ours, s.Theirs, p, s.TheirPts.MinBy(q => Sq((q.X - sh.X, q.Y - sh.Y), p))));

        var residuals = pairs.Select(pr => Math.Sqrt(Sq((pr.Q.X - sh.X, pr.Q.Y - sh.Y), pr.P))).ToList();

        // WHAT THE UNMATCHED ARE, BY SIZE. A column of ours with none of theirs within 300 mm is something we
        // read that the engineer did not model; its section names its size, and the sizes say what it was
        // (31202, 2026-09-12: 55 of 108 on a storey were 9x12 and 11x14 in - the tendon anchors of a P/T
        // slab, drawn as small filled rectangles; their 53 columns were the 12x48 and 12x24 the schedule
        // declares). And theirs with none of ours: what we missed, by their section.
        var ourSections = SectionsByColumn(model);
        var theirSections = SectionsByColumn(yard);
        // A COLUMN OF OURS ON A WALL OF HERS IS A DIFFERENCE OF KIND, NOT OF PLACE (2026-09-12). 31138's
        // C3A/C3B/C2B, drawn and scheduled as 14x36 and 18x30 columns on the building's edge, and the 24x37
        // ends of its stair core, are wall piers in her gravity model: five a storey, on seventeen storeys,
        // at 0-1 mm from one of her panels. They are counted here, by section, and are still unmatched.
        var theirWalls = WallsByStorey(yard, yu);
        int oursOnHerWalls = 0;
        var oursUnmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        var oursOnWallsBySection = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in judged)
            foreach (var p in s.OurPts)
                if (Math.Sqrt(s.TheirPts.Min(q => Sq((q.X - sh.X, q.Y - sh.Y), p))) > 300)
                {
                    string sec = ourSections.TryGetValue((s.Ours, p.X, p.Y), out var os) ? os : "?";
                    oursUnmatched[sec] = oursUnmatched.GetValueOrDefault(sec) + 1;
                    if (theirWalls.TryGetValue(s.Theirs, out var panels) && panels.Any(w => LoopGeometry.Within(DistanceToWall((p.X + sh.X, p.Y + sh.Y), w), 100)))
                    {
                        oursOnHerWalls++;
                        oursOnWallsBySection[sec] = oursOnWallsBySection.GetValueOrDefault(sec) + 1;
                    }
                }
        var theirsUnmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var bucket in ours.Where(kv => kv.Value.Count > 0).Select(kv => (Ours: kv.Key, Theirs: TheirsFor(kv.Key))).Where(t => t.Theirs is not null).GroupBy(t => t.Theirs!.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ourPoints = bucket.SelectMany(t => ours[t.Ours]).ToList();
            foreach (var q in bucket.First().Theirs!.Value.Points)
            {
                var qq = (q.X - sh.X, q.Y - sh.Y);
                if (Math.Sqrt(ourPoints.Min(p => Sq(p, qq))) > 300)
                {
                    string sec = theirSections.TryGetValue((bucket.Key, q.X, q.Y), out var ts) ? ts : "?";
                    theirsUnmatched[sec] = theirsUnmatched.GetValueOrDefault(sec) + 1;
                }
            }
        }

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

        // OPENINGS, OURS AGAINST HERS, on the storeys both name (step 104): an opening of ours is hers when the
        // centre of one of hers lies within OpeningMatchMm of ours after the frame; the unmatched are counted
        // by plan size, which is what says WHAT they are (31168: 72 of 3.5 x 1.5 m at the perimeter beside
        // every column - not a shaft's shape).
        var ourOpenings = OpeningsByStorey(model, mu);
        var theirOpenings = OpeningsByStorey(yard, yu);
        int oursOpeningsJudged = 0, oursOpeningsMatched = 0, theirsOpeningsJudged = 0, theirsOpeningsMatched = 0, theirsOpeningsCovered = 0, oursHersElsewhere = 0;
        var oursOpeningsUnmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        var theirsOpeningsUnmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        // hers are judged on every storey both models name (the shared list), not only where we cut something: a storey
        // where we read no opening and she cut nine is the finding
        var theirOpeningStoreysMet = new HashSet<string>(shared.SelectMany(s => s.Theirs.Split('+')), StringComparer.OrdinalIgnoreCase);
        int oursOpeningsBeyond = 0, theirsSlivers = 0, theirsOffPlate = 0, theirsHad = 0;
        var openingRows = new List<(string Storey, bool Ours, double X, double Y, double W, double H, double NearestMm, bool Covered, bool OnOurPlate)>();
        var ourPlates = PlatePolygonsByStorey(model, mu);
        // a void we carry no plate over is only that when we carry the floor: our plate area on the storey at least
        // VoidNeedsOurPlateFraction of hers (31017's podium reads 1-6k of 58-64k sq ft, and her openings there stand
        // off our plate because the plate is missing, not because we left the void out)
        var ourPlateArea = PlatesByStorey(model); var theirPlateArea = PlatesByStorey(yard);
        foreach (var (storey, mine) in ourOpenings)
        {
            var t = TheirsFor(storey);
            if (t is null) continue;
            var hers = t.Value.Name.Split('+').SelectMany(n => theirOpenings.TryGetValue(n, out var h) ? h : []).ToList();
            // judged only inside her footprint on the storey, as the columns are: 31130's West tower is not in her East model
            var footprint = shared.FirstOrDefault(s => s.Ours.Equals(storey, StringComparison.OrdinalIgnoreCase)).TheirPts;
            foreach (var o in mine)
            {
                if (footprint is { Count: > 0 } && (o.Centre.X < footprint.Min(q => q.X) - sh.X - FootprintMarginMm || o.Centre.X > footprint.Max(q => q.X) - sh.X + FootprintMarginMm
                                                    || o.Centre.Y < footprint.Min(q => q.Y) - sh.Y - FootprintMarginMm || o.Centre.Y > footprint.Max(q => q.Y) - sh.Y + FootprintMarginMm))
                { oursOpeningsBeyond++; continue; }
                oursOpeningsJudged++;
                double nearestOfHers = hers.Count == 0 ? double.NaN : hers.Min(h => Math.Sqrt(Sq((h.Centre.X - sh.X, h.Centre.Y - sh.Y), o.Centre)));
                openingRows.Add((storey, true, o.Centre.X, o.Centre.Y, o.W, o.H, nearestOfHers, false, true));
                if (hers.Any(h => Math.Sqrt(Sq((h.Centre.X - sh.X, h.Centre.Y - sh.Y), o.Centre)) <= OpeningMatchMm)) oursOpeningsMatched++;
                else if (theirOpenings.Values.SelectMany(v => v).Any(h => Math.Sqrt(Sq((h.Centre.X - sh.X, h.Centre.Y - sh.Y), o.Centre)) <= OpeningMatchMm)) oursHersElsewhere++;
                else oursOpeningsUnmatched[SizeClass(o.W, o.H)] = oursOpeningsUnmatched.GetValueOrDefault(SizeClass(o.W, o.H)) + 1;
            }
        }
        foreach (var (storey, hers) in theirOpenings)
        {
            if (!theirOpeningStoreysMet.Contains(storey)) continue;
            var mine = ourOpenings.Where(kv => TheirsFor(kv.Key) is { } tt && tt.Name.Split('+').Contains(storey, StringComparer.OrdinalIgnoreCase)).SelectMany(kv => kv.Value).ToList();
            // our storeys that meet this one of hers, for the plates she may stand off
            var ourStoreysHere = ourOpenings.Keys.Concat(ourPlates.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => TheirsFor(k) is { } t2 && t2.Name.Split('+').Contains(storey, StringComparer.OrdinalIgnoreCase)).ToList();
            var platesHere = ourStoreysHere.SelectMany(k => ourPlates.TryGetValue(k, out var pl) ? pl : []).ToList();
            double oursSqFtHere = ourStoreysHere.Sum(k => ourPlateArea.GetValueOrDefault(k).SqFt), theirsSqFtHere = theirPlateArea.GetValueOrDefault(storey).SqFt;
            bool weCarryTheFloor = theirsSqFtHere > 0 && oursSqFtHere >= VoidNeedsOurPlateFraction * theirsSqFtHere;
            foreach (var h in hers)
            {
                if (Math.Min(h.W, h.H) < SliverMm) { theirsSlivers++; continue; }
                theirsOpeningsJudged++;
                var hh = (h.Centre.X - sh.X, h.Centre.Y - sh.Y);
                bool byCentre = mine.Any(o => Math.Sqrt(Sq(hh, o.Centre)) <= OpeningMatchMm);
                bool byCover = mine.Any(o => Math.Abs(hh.Item1 - o.Centre.X) <= o.W / 2 && Math.Abs(hh.Item2 - o.Centre.Y) <= o.H / 2);
                bool onPlate = !weCarryTheFloor || platesHere.Count == 0 || platesHere.Any(pl => Inside(hh, pl));
                double nearestOfOurs = mine.Count == 0 ? double.NaN : mine.Min(o => Math.Sqrt(Sq(hh, o.Centre)));
                openingRows.Add((storey, false, hh.Item1, hh.Item2, h.W, h.H, nearestOfOurs, byCover, onPlate));
                if (byCentre) theirsOpeningsMatched++;
                else theirsOpeningsUnmatched[SizeClass(h.W, h.H)] = theirsOpeningsUnmatched.GetValueOrDefault(SizeClass(h.W, h.H)) + 1;
                if (byCover) theirsOpeningsCovered++;
                if (!onPlate) theirsOffPlate++;
                if (byCentre || byCover || !onPlate) theirsHad++;
            }
        }
        static string SizeClass(double w, double h)
        {
            double a = Math.Round(Math.Min(w, h) / 500) / 2, b = Math.Round(Math.Max(w, h) / 500) / 2;
            return $"{a:0.#}x{b:0.#} m";
        }

        var storeyFigures = new List<StoreyFigure>();
        foreach (var g in pairs.GroupBy(pr => pr.Storey))
        {
            var rs = g.Select(pr => Math.Sqrt(Sq((pr.Q.X - sh.X, pr.Q.Y - sh.Y), pr.P))).ToList();
            var trs = theirResiduals.Where(t => t.Storey.Split('+').Contains(g.Key, StringComparer.Ordinal)).Select(t => t.R).ToList();
            // the rigid part of the storey's error: the median offset vector over the pairs within 600 mm
            var near = g.Where(pr => Math.Sqrt(Sq((pr.Q.X - sh.X, pr.Q.Y - sh.Y), pr.P)) <= 600).ToList();
            var rigid = near.Count == 0 ? (0.0, 0.0)
                : (Median(near.Select(pr => pr.Q.X - sh.X - pr.P.X).ToList()), Median(near.Select(pr => pr.Q.Y - sh.Y - pr.P.Y).ToList()));
            var after = g.Select(pr => Math.Sqrt(Sq((pr.Q.X - sh.X - rigid.Item1, pr.Q.Y - sh.Y - rigid.Item2), pr.P))).ToList();
            storeyFigures.Add(new StoreyFigure(g.Key, g.First().Theirs, rs.Count, trs.Count, Median(rs), rs.Count(r => r <= 100), trs.Count(r => r <= 100), beyondByStorey.GetValueOrDefault(g.Key))
            {
                RigidMm = rigid,
                MedianAfterRigidMm = Median(after),
                OursWithin100AfterRigid = after.Count(r => r <= 100),
            });
        }
        storeyFigures = storeyFigures.OrderByDescending(f => f.Ours).ThenBy(f => f.Storey, StringComparer.Ordinal).ToList();

        // where each model's columns stand against OUR grid lines (the plans'), on the judged storeys
        var gridX = gm.Where(g => g.Key.Dir == "X").Select(g => g.Value).Distinct().ToList();
        var gridY = gm.Where(g => g.Key.Dir == "Y").Select(g => g.Value).Distinct().ToList();
        (int OnX, int OnY, int OnBoth, int N) OnGrid(IEnumerable<(double X, double Y)> pts)
        {
            int onX = 0, onY = 0, onBoth = 0, n = 0;
            foreach (var p in pts)
            {
                n++;
                bool x = gridX.Count > 0 && gridX.Min(g => Math.Abs(g - p.X)) <= 50, y = gridY.Count > 0 && gridY.Min(g => Math.Abs(g - p.Y)) <= 50;
                if (x) onX++;
                if (y) onY++;
                if (x && y) onBoth++;
            }
            return (onX, onY, onBoth, n);
        }
        var oursOnGrid = OnGrid(judged.SelectMany(s => s.OurPts));
        var theirsOnGrid = OnGrid(judged.GroupBy(s => s.Theirs, StringComparer.OrdinalIgnoreCase).SelectMany(g => g.First().TheirPts.Select(q => (q.X - sh.X, q.Y - sh.Y))));

        // our plates judged only inside her footprint on the storey, as the columns and the openings are (30990: her model is
        // Tower A on the podium; Tower B's floors are ours to read and hers to have left out). Her footprint for a PLATE is
        // the box of her columns AND her plates on that storey: a mechanical level she models with four columns under a
        // whole floor of slab is a floor, and 31202's L13 (44,553 sq ft, a handful of columns) fell out of a columns-only box.
        var theirPlatePoints = PlatePointsByStorey(yard, yu);
        bool PlateJudged(string storey, (double X, double Y) centreMm)
        {
            var s = shared.FirstOrDefault(s => s.Ours.Equals(storey, StringComparison.OrdinalIgnoreCase));
            if (s.TheirPts is not { Count: > 0 }) return true;
            var footprint = s.TheirPts.Concat(s.Theirs.Split('+').SelectMany(n => theirPlatePoints.TryGetValue(n, out var pp) ? pp : [])).ToList();
            return !(centreMm.X < footprint.Min(q => q.X) - sh.X - FootprintMarginMm || centreMm.X > footprint.Max(q => q.X) - sh.X + FootprintMarginMm
                     || centreMm.Y < footprint.Min(q => q.Y) - sh.Y - FootprintMarginMm || centreMm.Y > footprint.Max(q => q.Y) - sh.Y + FootprintMarginMm);
        }
        var platesBeyond = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var plates = PlatesOnSharedStoreys(model, yard, storeyFigures, PlateJudged, platesBeyond);
        if (platesBeyond.Count > 0)
            notes.Add($"{platesBeyond.Values.Sum():N0} sq ft of our plates stand beyond her model's footprint on their storey (a plate whose centroid is more than {FootprintMarginMm / 1000:0.#} m past her outermost column) and are not judged: {string.Join(", ", platesBeyond.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"))}");

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
            oursBeyond, oursOnHerWalls,
            notes)
        {
            OnOurGrid = (oursOnGrid.OnX, oursOnGrid.OnY, oursOnGrid.OnBoth, oursOnGrid.N, theirsOnGrid.OnX, theirsOnGrid.OnY, theirsOnGrid.OnBoth, theirsOnGrid.N),
            Pairs = pairs.Select(pr => (pr.Storey,
                ourSections.TryGetValue((pr.Storey, pr.P.X, pr.P.Y), out var osec) ? osec : "?",
                theirSections.TryGetValue((pr.Theirs, pr.Q.X, pr.Q.Y), out var tsec) ? tsec : "?",
                pr.P.X, pr.P.Y, pr.Q.X - sh.X - pr.P.X, pr.Q.Y - sh.Y - pr.P.Y)).ToList(),
            OursUnmatchedBySection = oursUnmatched.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            OursOnHerWallsBySection = oursOnWallsBySection.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            TheirsUnmatchedBySection = theirsUnmatched.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            Openings = (oursOpeningsJudged, theirsOpeningsJudged, oursOpeningsMatched, theirsOpeningsMatched, oursOpeningsBeyond),
            OursOpeningsUnmatchedBySize = oursOpeningsUnmatched.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            TheirsOpeningsUnmatchedBySize = theirsOpeningsUnmatched.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            TheirsOpeningsCovered = theirsOpeningsCovered,
            OursOpeningsHersElsewhere = oursHersElsewhere,
            TheirsOpeningsHad = (theirsOpeningsMatched, theirsOpeningsCovered, theirsOffPlate, theirsHad),
            TheirsOpeningsSlivers = theirsSlivers,
            OpeningRows = openingRows,
            Plates = plates,
            Thickness = ThicknessOnSharedStoreys(model, yard, storeyFigures, PlateJudged),
        };
    }

    /// <summary>
    /// The plate area of each model on each shared storey, in sq ft: every AREA of kind FLOOR assigned to the storey
    /// and not an opening, its plan polygon's area. Read here, not through the query class (that file is not read-side
    /// and this one is - SixSetReadCacheTests.NoIncludedSourceReferencesAnExcludedOne).
    /// </summary>
    /// <summary>
    /// A PLATE BEYOND HER FOOTPRINT IS NOT JUDGED (2026-09-19), as a column or an opening there is not: her model of
    /// 30990 is Tower A on the shared podium, ours is both towers, and the moment Tower B's typical floors read
    /// (step 131) every shared storey stood at 170% of hers - a building she did not model, counted against her.
    /// A plate whose centroid lies more than <see cref="FootprintMarginMm"/> past the box of her columns on that
    /// storey is beyond her model: its area is counted and named, judged by nothing. Hers are all judged.
    /// </summary>
    private static IReadOnlyList<(string Storey, string YardstickStorey, double OursSqFt, double TheirsSqFt)> PlatesOnSharedStoreys(E2kDocument model, E2kDocument yard, IReadOnlyList<StoreyFigure> storeys,
        Func<string, (double X, double Y), bool>? judged = null, Dictionary<string, double>? beyondSqFt = null)
    {
        var ours = PlatesByStorey(model, judged, beyondSqFt); var theirs = PlatesByStorey(yard);
        return storeys.Select(f => (f.Storey, f.YardstickStorey, ours.GetValueOrDefault(f.Storey).SqFt, theirs.GetValueOrDefault(f.YardstickStorey).SqFt)).ToList();
    }

    private static IReadOnlyList<(string Storey, string YardstickStorey, double OursIn, double TheirsIn)> ThicknessOnSharedStoreys(E2kDocument model, E2kDocument yard, IReadOnlyList<StoreyFigure> storeys,
        Func<string, (double X, double Y), bool>? judged = null)
    {
        var ours = PlatesByStorey(model, judged); var theirs = PlatesByStorey(yard);
        return storeys.Select(f => (f.Storey, f.YardstickStorey, ours.GetValueOrDefault(f.Storey).ModalIn, theirs.GetValueOrDefault(f.YardstickStorey).ModalIn)).ToList();
    }

    /// <param name="judged">Whether a plate (by its storey and its centroid in mm) is judged; null judges every plate.</param>
    /// <param name="beyondSqFt">Where given, the area of the plates not judged, by storey.</param>
    private static Dictionary<string, (double SqFt, double ModalIn)> PlatesByStorey(E2kDocument doc, Func<string, (double X, double Y), bool>? judged = null, Dictionary<string, double>? beyondSqFt = null)
    {
        double inchesPerUnit = doc.LengthUnitInInches() ?? 1.0;
        // the thickness of each slab section, in inches (SLABTHICKNESS is in the model's length unit)
        var thicknessOf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string header in new[] { "SLAB PROPERTIES", "DECK PROPERTIES" })
            foreach (string raw in doc.LinesOf(header))
            {
                var m = Regex.Match(raw.Trim(), @"^SHELLPROP\s+""([^""]+)""", RegexOptions.IgnoreCase);
                var t = Regex.Match(raw, @"(?:SLABTHICKNESS|DECKSLABDEPTH)\s+(-?[\d.eE+]+)", RegexOptions.IgnoreCase);
                if (m.Success && t.Success && double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) thicknessOf[m.Groups[1].Value] = v * inchesPerUnit;
            }
        var floors = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""([^""]+)""\s+FLOOR\b", RegexOptions.IgnoreCase);
            if (m.Success) floors.Add(m.Groups[1].Value);
        }
        var openings = new HashSet<string>(StringComparer.Ordinal);
        var assigned = new List<(string Name, string Storey, string Section)>();
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var mo = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""[^""]*""\s+OPENING\s+""Yes""");
            if (mo.Success) { openings.Add(mo.Groups[1].Value); continue; }
            var ms = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""([^""]*)""\s+SECTION\s+""([^""]*)""");
            if (ms.Success) assigned.Add((ms.Groups[1].Value, ms.Groups[2].Value, ms.Groups[3].Value));
        }
        var points = doc.PlanPointsOfObjects();
        var area = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var byThickness = new Dictionary<string, Dictionary<double, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, storey, section) in assigned)
        {
            if (!floors.Contains(name) || openings.Contains(name)) continue;
            if (!points.TryGetValue(name, out var pts) || pts.Count < 3) continue;
            double sum = 0;
            for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var b = pts[(i + 1) % pts.Count]; sum += a.X * b.Y - b.X * a.Y; }
            double sqFt = Math.Abs(sum) / 2 * inchesPerUnit * inchesPerUnit / 144.0;
            if (judged is not null && !judged(storey, (pts.Average(p => p.X) * inchesPerUnit * 25.4, pts.Average(p => p.Y) * inchesPerUnit * 25.4)))
            {
                if (beyondSqFt is not null) beyondSqFt[storey] = beyondSqFt.GetValueOrDefault(storey) + sqFt;
                continue;
            }
            area[storey] = area.GetValueOrDefault(storey) + sqFt;
            if (thicknessOf.TryGetValue(section, out double thick))
            {
                if (!byThickness.TryGetValue(storey, out var d)) byThickness[storey] = d = new Dictionary<double, double>();
                double key = Math.Round(thick, 2);
                d[key] = d.GetValueOrDefault(key) + sqFt;
            }
        }
        var result = new Dictionary<string, (double SqFt, double ModalIn)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (storey, sqFt) in area)
            result[storey] = (sqFt, byThickness.TryGetValue(storey, out var d) && d.Count > 0 ? d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key : 0);
        return result;
    }

    /// <summary>Every FLOOR area's vertices (not an opening's), by storey, in mm: the plates' own footprint.</summary>
    private static Dictionary<string, List<(double X, double Y)>> PlatePointsByStorey(E2kDocument doc, double unitMm)
    {
        var floors = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""([^""]+)""\s+FLOOR\b", RegexOptions.IgnoreCase);
            if (m.Success) floors.Add(m.Groups[1].Value);
        }
        var openings = new HashSet<string>(StringComparer.Ordinal);
        var assigned = new List<(string Name, string Storey)>();
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var mo = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""[^""]*""\s+OPENING\s+""Yes""");
            if (mo.Success) { openings.Add(mo.Groups[1].Value); continue; }
            var ms = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""([^""]*)""\s+SECTION\s+");
            if (ms.Success) assigned.Add((ms.Groups[1].Value, ms.Groups[2].Value));
        }
        var points = doc.PlanPointsOfObjects();
        var result = new Dictionary<string, List<(double X, double Y)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, storey) in assigned)
        {
            if (!floors.Contains(name) || openings.Contains(name) || !points.TryGetValue(name, out var pts) || pts.Count < 3) continue;
            (result.TryGetValue(storey, out var list) ? list : result[storey] = new List<(double X, double Y)>()).AddRange(pts.Select(p => (p.X * unitMm, p.Y * unitMm)));
        }
        return result;
    }

    /// <summary>Every AREA assigned OPENING "Yes", by storey: its plan centre and box in mm.</summary>
    private static Dictionary<string, List<((double X, double Y) Centre, double W, double H)>> OpeningsByStorey(E2kDocument doc, double unitMm)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""[^""]*""\s+OPENING\s+""Yes""");
            if (m.Success) names.Add(m.Groups[1].Value);
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var result = new Dictionary<string, List<((double X, double Y) Centre, double W, double H)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!points.TryGetValue(name, out var pts) || pts.Count < 3) continue;
            if (!storeysOf.TryGetValue(name, out var on)) continue;
            double x0 = pts.Min(p => p.X) * unitMm, x1 = pts.Max(p => p.X) * unitMm, y0 = pts.Min(p => p.Y) * unitMm, y1 = pts.Max(p => p.Y) * unitMm;
            var o = (Centre: ((x0 + x1) / 2, (y0 + y1) / 2), W: x1 - x0, H: y1 - y0);
            foreach (var storey in on)
                (result.TryGetValue(storey, out var list) ? list : result[storey] = new List<((double X, double Y) Centre, double W, double H)>()).Add(o);
        }
        return result;
    }

    /// <summary>Her wall panels by storey, each as the plan box of its joints in mm.</summary>
    /// <summary>Her walls by storey, each as its plan polyline in mm (audit F20: "on her wall" is the distance to the wall's edges, not to its box - a diagonal wall's box holds metres of nothing).</summary>
    private static Dictionary<string, List<IReadOnlyList<(double X, double Y)>>> WallsByStorey(E2kDocument doc, double unitMm)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREA\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var result = new Dictionary<string, List<IReadOnlyList<(double X, double Y)>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, kind) in kinds)
        {
            if (!kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase) && !kind.Equals("WALL", StringComparison.OrdinalIgnoreCase)) continue;
            if (!points.TryGetValue(name, out var pts) || pts.Count < 2) continue;
            if (!storeysOf.TryGetValue(name, out var on)) continue;
            var ring = pts.Select(p => (X: p.X * unitMm, Y: p.Y * unitMm)).ToList();
            foreach (var storey in on)
                (result.TryGetValue(storey, out var list) ? list : result[storey] = new List<IReadOnlyList<(double X, double Y)>>()).Add(ring);
        }
        return result;
    }

    /// <summary>The distance from a point to a wall drawn as its plan polyline: to the nearest edge, or zero inside a closed ring.</summary>
    internal static double DistanceToWall((double X, double Y) p, IReadOnlyList<(double X, double Y)> ring)
    {
        double best = double.MaxValue;
        bool inside = false;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            if (n == 2 && i == 1) break;                                        // a two-point wall is one edge
            double vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
            double t = len2 <= 0 ? 0 : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2, 0, 1);
            double dx = a.X + t * vx - p.X, dy = a.Y + t * vy - p.Y;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            if (n >= 3 && ((a.Y > p.Y) != (b.Y > p.Y)) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) inside = !inside;
        }
        return inside ? 0 : best;
    }

    /// <summary>Every COLUMN's section, keyed by (storey, x, y) in mm — the same key ColumnsByStorey gives a point.</summary>
    private static Dictionary<(string Storey, double X, double Y), string> SectionsByColumn(E2kDocument doc)
    {
        double unitMm = (doc.LengthUnitInInches() ?? 1.0) * 25.4;
        var points = doc.PlanPointsOfObjects();
        var result = new Dictionary<(string, double, double), string>();
        foreach (string raw in doc.LinesOf("LINE ASSIGNS"))
        {
            var m = LineAssign.Match(raw.TrimStart());
            if (!m.Success) continue;
            if (!points.TryGetValue(m.Groups[1].Value, out var pts) || pts.Count == 0) continue;
            result[(m.Groups[2].Value, pts[0].X * unitMm, pts[0].Y * unitMm)] = m.Groups[3].Value;
        }
        return result;
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
            sb.AppendLine(CultureInfo.InvariantCulture, $"ours -> theirs: {c.OursCompared} columns on shared storeys inside her footprint{(c.OursBeyondHerModel > 0 ? $" ({c.OursBeyondHerModel} beyond it, not judged)" : "")}, nearest of theirs: median {c.OursMedianMm:N0} mm; within 50 mm {c.OursWithin50} ({100.0 * c.OursWithin50 / c.OursCompared:F0}%), within 100 mm {c.OursWithin100} ({100.0 * c.OursWithin100 / c.OursCompared:F0}%), within 300 mm {c.OursWithin300} ({100.0 * c.OursWithin300 / c.OursCompared:F0}%)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"theirs -> ours: {c.TheirsCompared} columns they modelled on those storeys, nearest of ours: median {c.TheirsMedianMm:N0} mm; within 100 mm {c.TheirsWithin100} ({100.0 * c.TheirsWithin100 / Math.Max(1, c.TheirsCompared):F0}%)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{c.Storeys.Count} storeys both models name, every one (ours judged / theirs / median / ours within 100 / theirs within 100 / the rigid part of the error and the median and share after it / ours beyond her footprint):");
            foreach (var f in c.Storeys)
                sb.AppendLine(CultureInfo.InvariantCulture, $"   {f.Storey,-10} {f.YardstickStorey,-10} {f.Ours,4} {f.Theirs,4}  {f.MedianMm,7:N0} mm  {100.0 * f.OursWithin100 / f.Ours,3:F0}%  {100.0 * f.TheirsWithin100 / Math.Max(1, f.Theirs),3:F0}%   rigid ({f.RigidMm.X,5:N0}, {f.RigidMm.Y,5:N0}) -> {f.MedianAfterRigidMm,5:N0} mm {100.0 * f.OursWithin100AfterRigid / f.Ours,3:F0}%{(f.OursBeyond > 0 ? $"   beyond {f.OursBeyond}" : "")}");
        }
        else if (c.TheirsCompared > 0)
            // audit F19 (step 61): every column of ours beyond her footprint is a complete recall miss, not "no columns" -
            // she modelled these and we stand none of ours near them
            sb.AppendLine(CultureInfo.InvariantCulture, $"ours -> theirs: none of ours inside her footprint on the shared storeys ({c.OursBeyondHerModel} beyond it); theirs -> ours: {c.TheirsCompared} columns she modelled there, within 100 mm of one of ours {c.TheirsWithin100} ({100.0 * c.TheirsWithin100 / c.TheirsCompared:F0}%)");
        else sb.AppendLine("no columns on a storey both models name");
        if (c.OnOurGrid.OursN > 0 && c.OnOurGrid.TheirsN > 0)
        {
            var g = c.OnOurGrid;
            sb.AppendLine(CultureInfo.InvariantCulture, $"against the plans' grid lines (within 50 mm; X / Y / an intersection): ours {g.OursOnX} / {g.OursOnY} / {g.OursOnBoth} of {g.OursN} ({100.0 * g.OursOnBoth / g.OursN:F0}% on an intersection); hers {g.TheirsOnX} / {g.TheirsOnY} / {g.TheirsOnBoth} of {g.TheirsN} ({100.0 * g.TheirsOnBoth / g.TheirsN:F0}%)");
        }
        if (c.OursUnmatchedBySection.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"ours with none of theirs within 300 mm, by section: {string.Join(", ", c.OursUnmatchedBySection.Take(8).Select(u => $"{u.Section} {u.Count}"))}{(c.OursUnmatchedBySection.Count > 8 ? " ..." : "")}");
        if (c.OursOnHerWalls > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  of which {c.OursOnHerWalls} stand on a wall she modelled (a column to us, a pier to her): {string.Join(", ", c.OursOnHerWallsBySection.Take(8).Select(u => $"{u.Section} {u.Count}"))}");
        if (c.TheirsUnmatchedBySection.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"theirs with none of ours within 300 mm, by their section: {string.Join(", ", c.TheirsUnmatchedBySection.Take(8).Select(u => $"{u.Section} {u.Count}"))}{(c.TheirsUnmatchedBySection.Count > 8 ? " ..." : "")}");
        if (c.Plates.Count > 0)
        {
            double po = c.Plates.Sum(p => p.OursSqFt), pt = c.Plates.Sum(p => p.TheirsSqFt);
            var under = c.Plates.Where(p => p.TheirsSqFt > 0 && p.OursSqFt < 0.5 * p.TheirsSqFt).ToList();
            var over = c.Plates.Where(p => p.OursSqFt > 1.5 * Math.Max(p.TheirsSqFt, 1)).ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"plates on the shared storeys: ours {po:N0} sq ft, hers {pt:N0} ({(pt == 0 ? 0 : 100.0 * po / pt):F0}%); storeys where ours is under half of hers: {under.Count} ({string.Join(" ", under.Take(8).Select(p => $"{p.Storey} {p.OursSqFt:N0}/{p.TheirsSqFt:N0}"))}{(under.Count > 8 ? " ..." : "")}); ours over half again hers: {over.Count}{(over.Count == 0 ? "" : " (" + string.Join(" ", over.Take(6).Select(p => $"{p.Storey} {p.OursSqFt:N0}/{p.TheirsSqFt:N0}")) + ")")}");
        }
        if (c.Thickness.Count > 0)
        {
            var judged = c.Thickness.Where(t => t.OursIn > 0 && t.TheirsIn > 0).ToList();
            int agree = judged.Count(t => Math.Abs(t.OursIn - t.TheirsIn) <= 0.5);
            var off = judged.Where(t => Math.Abs(t.OursIn - t.TheirsIn) > 0.5).Take(8).Select(t => $"{t.Storey} {t.OursIn:0.#}/{t.TheirsIn:0.#}").ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"slab thickness on the shared storeys (the thickness under most of the plate area, ours/hers in): {judged.Count} storeys both plate, {agree} agree within half an inch ({(judged.Count == 0 ? 0 : 100.0 * agree / judged.Count):F0}%){(off.Count > 0 ? "; off: " + string.Join(" ", off) : "")}");
        }
        if (c.Openings.Ours > 0 || c.Openings.Theirs > 0 || c.Openings.OursBeyond > 0)
        {
            var o = c.Openings;
            sb.AppendLine(CultureInfo.InvariantCulture, $"openings on the shared storeys: ours {o.Ours} inside her footprint{(o.OursBeyond > 0 ? $" ({o.OursBeyond} beyond it, not judged)" : "")}, hers {o.Theirs}; ours with one of hers within {OpeningMatchMm / 1000:0.#} m {o.OursMatched} ({100.0 * o.OursMatched / Math.Max(1, o.Ours):F0}%); hers with one of ours {o.TheirsMatched} ({100.0 * o.TheirsMatched / Math.Max(1, o.Theirs):F0}%)");
            if (c.OursOpeningsUnmatchedBySize.Count > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  ours she has not, by size: {string.Join(", ", c.OursOpeningsUnmatchedBySize.Take(8).Select(u => $"{u.Size} {u.Count}"))}{(c.OursOpeningsUnmatchedBySize.Count > 8 ? " ..." : "")}");
            if (c.TheirsOpeningsUnmatchedBySize.Count > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  hers we have not, by size: {string.Join(", ", c.TheirsOpeningsUnmatchedBySize.Take(8).Select(u => $"{u.Size} {u.Count}"))}{(c.TheirsOpeningsUnmatchedBySize.Count > 8 ? " ..." : "")}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  hers whose centre stands inside one of ours: {c.TheirsOpeningsCovered} ({100.0 * c.TheirsOpeningsCovered / Math.Max(1, o.Theirs):F0}%)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  hers we have, by centre or cover or as a void we carry no plate over: {c.TheirsOpeningsHad.Had} of {o.Theirs} ({100.0 * c.TheirsOpeningsHad.Had / Math.Max(1, o.Theirs):F0}%; by centre {c.TheirsOpeningsHad.ByCentre}, by cover {c.TheirsOpeningsHad.ByCover}, off our plate {c.TheirsOpeningsHad.OffOurPlate}){(c.TheirsOpeningsSlivers > 0 ? $"; hers under {SliverMm} mm across, a release not a hole, not judged: {c.TheirsOpeningsSlivers}" : "")}");
                if (c.OursOpeningsHersElsewhere > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"  of ours she has not, {c.OursOpeningsHersElsewhere} stand where she cuts on another storey");
        }
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

    /// <summary>
    /// A storey's name with the building prefix off and LEVEL shortened the way the PDF route names
    /// storeys: "A-LEVEL 1" and "A-L1" are one storey, "LEVEL P2" and "P2" are one storey (a parkade
    /// level is P-something, not L-P-something — the first port left "LEVEL P1" and "P1" unmatched on
    /// 31168), "LEVEL 1 MEZZ" is "L1 MEZZ".
    /// </summary>
    internal static string Stripped(string storey) => Stripped(storey, DefaultStoreyQualifierWords);

    internal static string Stripped(string storey, IReadOnlyList<string> qualifierWords)
    {
        string s = BuildingPrefix.Replace(storey.Trim().ToUpperInvariant(), "");
        s = ParkadeLevel.Replace(s, "$1");
        s = LevelWord.Replace(s, "L");
        // an engineer's model spells a storey L-1, L01 or LEVEL 01 as readily as L1 (31170's model names
        // L-1..L-7, 31138's L01..L09, 2026-09-12): the letter, no hyphen, no leading zero, is the name
        s = NumberedLevel.Replace(s, "$1$2");
        // AND A STOREY NAMED WITH WHAT IT CARRIES IS THE SAME STOREY (step 102, 2026-09-16): her "L17 MECH", "L18 ROOF",
        // "L2 AMENITY" are our L17, L18, L2 - the engineers' review counted 175 storeys "only hers" and 252 "only ours"
        // across 59 sets, MECH, ROOF, UPPER, MAIN among the shapes, and every one of them a storey the two models
        // both have and the comparison skipped. A trailing word after the number is dropped; MEZZ is not a qualifier
        // but a level of its own, and stays.
        var qualified = Regex.Match(s, @"^([A-Z]+\d+)\s*[- ]\s*([A-Z]+)\b");
        if (qualified.Success && qualifierWords.Contains(qualified.Groups[2].Value, StringComparer.OrdinalIgnoreCase)) return qualified.Groups[1].Value;
        return s;
    }

    private static readonly Regex NumberedLevel = new(@"^([A-Z]+)-?0*(\d+)$", RegexOptions.Compiled);
    private static readonly Regex LineAssign = new(@"^LINEASSIGN\s+""([^""]+)""\s+""([^""]+)""\s+SECTION\s+""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex ParkadeLevel = new(@"^LEVEL\s*(P\d+)", RegexOptions.Compiled);
    private static readonly Regex LevelWord = new(@"LEVEL\s*", RegexOptions.Compiled);

    /// <summary>The building a storey name is prefixed with ("A-L1" → A), or null for a storey named for the whole job.</summary>
    internal static string? Building(string storey)
    {
        var m = BuildingPrefix.Match(storey.Trim().ToUpperInvariant());
        return m.Success ? m.Value.TrimEnd('-') : null;
    }

    private static double Sq((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>An opening narrower than this across is a modelling release along a wall, not a hole (31065: 0.0-0.1 x 4.5 m, 88 of 209).</summary>
    public const double SliverMm = 150;
    /// <summary>Her opening off every plate of ours counts as a void we left out of the plate only when our plate area on the storey is this much of hers.</summary>
    public const double VoidNeedsOurPlateFraction = 0.8;

    /// <summary>The floor plates (FLOOR areas not assigned OPENING) by storey, each as its plan polygon in mm.</summary>
    private static Dictionary<string, List<IReadOnlyList<(double X, double Y)>>> PlatePolygonsByStorey(E2kDocument doc, double unitMm)
    {
        var floors = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""([^""]+)""\s+FLOOR\b", RegexOptions.IgnoreCase);
            if (m.Success) floors.Add(m.Groups[1].Value);
        }
        var openings = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var mo = Regex.Match(raw.TrimStart(), @"^AREAASSIGN\s+""([^""]+)""\s+""[^""]*""\s+OPENING\s+""Yes""");
            if (mo.Success) openings.Add(mo.Groups[1].Value);
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var result = new Dictionary<string, List<IReadOnlyList<(double X, double Y)>>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in floors)
        {
            if (openings.Contains(name)) continue;
            if (!points.TryGetValue(name, out var pts) || pts.Count < 3) continue;
            if (!storeysOf.TryGetValue(name, out var on)) continue;
            var poly = pts.Select(p => (p.X * unitMm, p.Y * unitMm)).ToList();
            foreach (var storey in on)
                (result.TryGetValue(storey, out var list) ? list : result[storey] = new List<IReadOnlyList<(double X, double Y)>>()).Add(poly);
        }
        return result;
    }

    /// <summary>Even-odd point in polygon.</summary>
    private static bool Inside((double X, double Y) p, IReadOnlyList<(double X, double Y)> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

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
        // THE FRAME IS JUDGED BY ITS SUPPORT, NEVER BY THE ORDER OF OUR COLUMNS (step 59, 2026-09-13): the
        // fullest bin by pair votes was taken by MaxBy, whose tie fell to whichever bin was voted first — the
        // order our columns come in — and a bin's votes count PAIRS, so three readings of one column at one
        // place out-voted three columns at three places. Run 7 against run 8 showed it on four sets whose
        // models were identical to the member (31174-01: 8 of 64 supported in one run, 5 in the other; the
        // frame had moved with the order). Now every bin within one vote of the fullest is refined to its
        // median and judged by the support it then has; a tie in support goes to the tighter cluster, then to
        // the lower bin in key order - which is the same correspondence whichever frame either model sits in,
        // where "the smaller move" was not (the second audit's finding 11: ours at 0 against theirs at -1000
        // and +1000 chose -1000; ours moved to 1500 chose -500, the OTHER column).
        int fullest = votes.Values.Max();
        (double X, double Y) shift = (0, 0); int support = -1; double spread = double.MaxValue;
        foreach (var (key, n) in votes.Where(kv => kv.Value >= fullest - 1).OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2))
        {
            var coarse = (X: key.Item1 * bin, Y: key.Item2 * bin);
            // refine: the median of the pairs inside the bin and its neighbours
            var dxs = new List<double>(); var dys = new List<double>();
            foreach (var s in shared)
                foreach (var p in s.OurPts)
                    foreach (var q in s.TheirPts)
                        if (Math.Abs(q.X - p.X - coarse.X) <= 1.5 * bin && Math.Abs(q.Y - p.Y - coarse.Y) <= 1.5 * bin) { dxs.Add(q.X - p.X); dys.Add(q.Y - p.Y); }
            var candidate = dxs.Count == 0 ? coarse : (X: Median(dxs), Y: Median(dys));
            int sup = shared.Sum(s => s.OurPts.Count(p => s.TheirPts.Any(q => LoopGeometry.Within(Math.Sqrt(Sq((q.X - candidate.X, q.Y - candidate.Y), p)), 100.0))));
            double spr = dxs.Count == 0 ? double.MaxValue : dxs.Select(d => Math.Abs(d - candidate.X)).Concat(dys.Select(d => Math.Abs(d - candidate.Y))).Average();
            bool better = sup > support || (sup == support && LoopGeometry.Beyond(spread, spr));   // an exact tie keeps the first: the lower bin
            if (better) { shift = candidate; support = sup; spread = spr; }
        }
        return (shift, Math.Max(support, 0));
    }

    private static double Median(List<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
