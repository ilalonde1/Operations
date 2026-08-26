using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Which drawing layers carry which structural element, and the size limits that
/// separate a real member from linework that merely happens to close.
/// Defaults follow KOR's drafting layer convention (JBP_V-WALL / JBP_V_COL /
/// JBP_C_SLABEDG) and are stated in drawing units — inches for these exports.
/// </summary>
public sealed record PlanClassificationOptions
{
    public IReadOnlyList<string> WallLayerPatterns { get; init; } = new[] { "WALL" };
    public IReadOnlyList<string> ColumnLayerPatterns { get; init; } = new[] { "_COL" };
    public IReadOnlyList<string> SlabLayerPatterns { get; init; } = new[] { "SLABEDG" };

    public double MinWallThickness { get; init; } = 4.0;
    public double MaxWallThickness { get; init; } = 36.0;

    /// <summary>
    /// Shorter than this on plan and the element is a column, not a wall — the engineer's rule,
    /// given as the answer to W1: "less than 48 in length should be a column".
    ///
    /// This governs only what a whole element IS. It must never govern how a shape is taken apart:
    /// see <see cref="MinPanelOverlap"/>.
    /// </summary>
    public double MinWallLength { get; init; } = 48.0;

    /// <summary>
    /// Shortest face pairing the decomposer will accept as a panel.
    ///
    /// Deliberately separate from <see cref="MinWallLength"/>, and much smaller. The two were the
    /// same number once, so raising the wall-versus-column rule to 48" also demanded that every
    /// face overlap by 48" — and the short limb of every corner stopped decomposing. Those corners
    /// then fell through to the pier branch and came out as ONE thick wall filling the corner
    /// volume: "it's almost like it filled the volume, but didn't do the actual corner", and the
    /// wall it produced lined up with neither of the two it replaced. It put 417 of 918 walls at
    /// 30" or thicker on 31168, which is not a residential tower.
    ///
    /// A limb of a corner is short by nature. Twelve inches is a wall's width, not a wall's length.
    /// </summary>
    public double MinPanelOverlap { get; init; } = 12.0;

    /// <summary>
    /// Join wall centrelines so that panels meeting at a corner or a T share a joint. Off only for
    /// tests that need to see what the decomposer produced before the network was built.
    /// </summary>
    public bool ConnectWalls { get; init; } = true;

    /// <summary>
    /// Where no slab edge closes on a storey, take its floor from the inside face of the perimeter
    /// wall. On, because a storey with no plate has no diaphragm at all and its walls and columns
    /// read as unsupported — an approximation beats nothing there. Off for an engineer who would
    /// rather see the gap than an outline she did not draw; a real slab edge always wins over this
    /// either way, so turning it off changes nothing on a storey that has one.
    /// </summary>
    public bool FloorFromPerimeterWall { get; init; } = true;

    /// <summary>
    /// Narrowest and widest gap between in-line wall ends that counts as an opening wanting a
    /// header over it. Measured on 31168: one cluster of 142 gaps between 36" and 48", nothing
    /// below 18", and a separate group past 120" that is different walls rather than an opening.
    /// </summary>
    public double MinOpeningSpan { get; init; } = 24.0;
    public double MaxOpeningSpan { get; init; } = 72.0;

    /// <summary>Depth of a generated header. Nominal — the engineer sets section properties.</summary>
    public double SpandrelDepth { get; init; } = 24.0;

    /// <summary>Below this the footprint is a column, not a wall, however it was drawn.</summary>
    public double MinWallAspect { get; init; } = 2.0;

    /// <summary>
    /// Aspect required of a panel cut out of a ribbon. Lower than <see cref="MinWallAspect"/>
    /// because the end faces of each wall are consumed with their wall, so a short panel here
    /// is a genuine pier rather than a leftover sliver.
    /// </summary>
    public double MinPanelAspect { get; init; } = 1.2;

    /// <summary>
    /// How slender a footprint may be and still be modelled as a column. Measured off the models
    /// the engineers built: the most slender column in her 31138 gravity model is 12x36, exactly
    /// 3:1, with nothing beyond it, and the 31168 Revit export carries no concrete rectangular
    /// column at all. Past that ratio the footprint is a wall in both models, and modelling it as a
    /// column throws away the in-plane shear it was drawn to carry.
    /// </summary>
    public double MaxColumnAspect { get; init; } = 3.0;

    public double MinColumnSize { get; init; } = 6.0;
    public double MaxColumnSize { get; init; } = 96.0;

    /// <summary>
    /// A wall outline is a thin ribbon tracing faces, so it fills little of its bounding box. A
    /// footprint that fills most of its box is solid concrete — a pier — and must not be sliced
    /// into panels.
    ///
    /// Fill alone does not separate the two: a pier is a solid rectangle, but so is an L of two
    /// walls meeting at a corner once its box is drawn round it. What separates them is shape —
    /// see the convexity test in AddWallOrColumn.
    /// </summary>
    public double PierFillRatio { get; init; } = 0.6;

    /// <summary>Walls thicker than this are reported for checking; they are unusual above a podium.</summary>
    public double UnusualWallThickness { get; init; } = 24.0;

    /// <summary>
    /// A pier drawn whole on the wall layer may be stockier than a wall run — a boundary element
    /// at the end of a core wall is routinely 40" or more — so it is allowed more thickness than
    /// a paired wall face before being called a column.
    /// </summary>
    public double MaxPierThickness { get; init; } = 48.0;

    /// <summary>
    /// Rings smaller than this on a slab layer are noise, not slabs or openings.
    /// 7,200 in² is 50 ft² — below a plate worth modelling, and the size at which
    /// interrupted slab edges start closing into meaningless slivers.
    /// </summary>
    public double MinSlabArea { get; init; } = 7200.0;

    /// <summary>
    /// Area a ring must reach to be modelled as a floor plate. Higher than <see cref="MinSlabArea"/>,
    /// which only decides whether a ring is worth keeping at all: a small ring inside a plate is a
    /// real opening, but a small ring standing on its own is slab-edge linework that happened to
    /// close, and in ETABS it draws as a scrap of floor hanging in space.
    ///
    /// Measured on both projects, the two populations do not overlap: standalone rings come out at
    /// 52-115 sq ft, real plates at 915 sq ft and up (31138's tower floor is 9,666). 400 sq ft sits
    /// in the empty middle with margin on both sides.
    /// </summary>
    public double MinPlateArea { get; init; } = 57600.0;

    /// <summary>
    /// How close two non-adjacent edges of one slab outline must come before the ring is treated
    /// as closing through itself and split into the plates it is drawing.
    /// See `dxf.outline-self-touch-tolerance`.
    /// </summary>
    public double OutlineSelfTouchTolerance { get; init; } = 0.05;

    /// <summary>
    /// How wide an interruption the flood-fill plate recovery may bridge, in drawing units.
    ///
    /// The stroke it rasterises with used to come from <see cref="DashJoinGap"/>, which is the
    /// wrong scale: that number is the DASH pitch, 11 inches on this office's hidden lines, and it
    /// closed gaps of roughly two feet. A slab edge is interrupted wherever other linework crosses
    /// it, and on 31168's C-LEVEL 3 those interruptions reach 103 inches -- so the fill escaped,
    /// no plate was recovered, and the storey borrowed its neighbour's floor. The engineer's answer
    /// was "level 3 has its own slab edge, it's on the drawings", and she was right.
    ///
    /// Measured on 31168: at 36 inches C-LEVEL 3 recovers its own 12,830 sq ft floor, LEVEL 1 and
    /// the mezzanine move less than a percent, LEVEL 2 keeps the two separate slabs the engineer
    /// confirmed, and C-LEVEL 4 does not move at all. Below 36 C-LEVEL 3 recovers nothing.
    ///
    /// Zero falls back to <see cref="DashJoinGap"/>, which is what it did before.
    /// </summary>
    public double FloodFillBridge { get; init; } = 36.0;

    /// <summary>
    /// Layers carrying the DASHED linework that shows what is below the slab.
    ///
    /// The engineer's rule, 24 Aug: "the dash lines are columns below supporting the slab, always.
    /// The solid ones are columns on top of the slab" -- and later the same day, "it's the same for
    /// walls too". A roof plan therefore draws no solid columns at all, because nothing stands on a
    /// roof, and 31168's does not: JBP_V_COL is absent from it entirely while S-HIDDEN carries
    /// seven closed 12x30 loops. Those are the columns holding the roof up, and the tool discarded
    /// the layer as non-structural and then reported a plate with nothing beneath it.
    ///
    /// Read ONLY where a sheet draws a slab and no structure to carry it. Applying the rule
    /// everywhere would double the columns: C-LEVEL 3 has 41 solid and 37 dashed, in entirely
    /// different places at different sizes, because it is a transfer level and the dashed ones are
    /// the podium columns already modelled on the storey below.
    ///
    /// She also warned that not every dashed line is structure -- a SPARSE dashed line is the
    /// building outline and is to be ignored, a DENSE one is an element below. This is not a
    /// dash-pitch test; it is protected only because it takes CLOSED loops and applies the column
    /// size bounds, so a curtain-wall outline does not qualify. See ruling two-kinds-of-dashed-line.
    /// </summary>
    public IReadOnlyList<string> BelowSlabLayerPatterns { get; init; } = new[] { "HIDDEN" };


    /// <summary>
    /// Largest dash gap to close when rebuilding a dashed line. Measured on KOR's exports:
    /// hidden edges dash at a constant 11", while genuine interruptions in a slab boundary
    /// run 18" and wider.
    /// </summary>
    public double DashJoinGap { get; init; } = 14.0;

    /// <summary>
    /// How far an interrupted edge may be carried along its own direction to reach the corner
    /// it was cut at. Off by default, on the evidence: extending was expected to recover slab
    /// plates and does not. On 31138 it left floors at 14 (13 at longer reaches) while walls
    /// fell from 232 to 217 at 48", and to 118 at 240" — the gaps in a slab edge are real
    /// breaks at openings and level changes, not cut corners, so extending invents corners and
    /// merges outlines that were never one.
    /// </summary>
    public double ExtendLimit { get; init; }

    /// <summary>
    /// The same rules expressed in a different length unit.
    ///
    /// Every threshold here is stated in inches because that is what KOR's drawings use. They are
    /// real lengths, not tuning knobs, so a model working in millimetres needs the same rule at a
    /// different number — 48 inches, not 48 millimetres. Areas scale by the square.
    /// </summary>
    public PlanClassificationOptions InUnitOf(double unitInInches)
    {
        double f = 1.0 / unitInInches;          // inches -> that unit
        double a = f * f;

        return this with
        {
            MinWallThickness = MinWallThickness * f,
            MaxWallThickness = MaxWallThickness * f,
            MinWallLength = MinWallLength * f,
            MinPanelOverlap = MinPanelOverlap * f,
            MinOpeningSpan = MinOpeningSpan * f,
            MaxOpeningSpan = MaxOpeningSpan * f,
            SpandrelDepth = SpandrelDepth * f,
            MinColumnSize = MinColumnSize * f,
            MaxColumnSize = MaxColumnSize * f,
            UnusualWallThickness = UnusualWallThickness * f,
            MaxPierThickness = MaxPierThickness * f,
            MinSlabArea = MinSlabArea * a,
            MinPlateArea = MinPlateArea * a,
            DashJoinGap = DashJoinGap * f,
            ExtendLimit = ExtendLimit * f,
            JoinTolerance = JoinTolerance * f,
            BridgeTolerance = BridgeTolerance * f,
            WallBridgeTolerance = WallBridgeTolerance * f,
            // Aspects and fill ratios are dimensionless and must not be touched.
        };
    }

    public double JoinTolerance { get; init; } = 0.05;
    public double BridgeTolerance { get; init; } = 6.0;

    /// <summary>
    /// Closure allowance for wall outlines, which are broken by door and opening symbols —
    /// a wider, well-defined gap than the incidental crossings that interrupt slab edges,
    /// and safe to close because a wall outline is short and its shape is unambiguous.
    /// </summary>
    public double WallBridgeTolerance { get; init; } = 12.0;

    public static bool Matches(string layer, IReadOnlyList<string> patterns)
        => patterns.Any(p => layer.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What this tool takes a layer to be, or null where it recognises nothing.
    ///
    /// The single copy on purpose. The classifier, the ledger and the unread-entity report each
    /// carried their own identical version, and a ledger that disagrees with the classifier is
    /// worse than no ledger — it says the geometry was read when it was not.
    ///
    /// Columns are tested first because a layer can satisfy more than one pattern and the column
    /// name is usually the more specific: "V_COL-WALL" is a column layer, not a wall layer.
    /// </summary>
    public string? RoleOf(string layer)
    {
        if (Matches(layer, ColumnLayerPatterns)) return "columns";
        if (Matches(layer, WallLayerPatterns)) return "walls";
        if (Matches(layer, SlabLayerPatterns)) return "slab edges";
        return null;
    }
}

/// <summary>Turns the raw segments of one plan into the structural members it depicts.</summary>
public static class StructuralPlanClassifier
{
    /// <summary>
    /// A layer name with its export suffix removed, so JBP_C_SLABEDG, JBP_C_SLABEDG-1 and
    /// JBP_C_SLABEDG-2 are one family and JBP_V-WALL and JBP_B_WALL are two.
    /// </summary>
    private static string LayerFamily(string layer)
        => System.Text.RegularExpressions.Regex.Replace(layer, @"[-_]\d+$", string.Empty);

    internal const string RoleWall = "walls";
    internal const string RoleColumn = "columns";
    internal const string RoleSlab = "slab edges";

    private static string? RoleOf(string layer, PlanClassificationOptions options)
        => options.RoleOf(layer) switch
        {
            "columns" => RoleColumn,
            "walls" => RoleWall,
            "slab edges" => RoleSlab,
            _ => null,
        };

    public static PlanGeometrySet Classify(
        IEnumerable<DxfSegment> segments,
        PlanClassificationOptions? options = null,
        PlanSheetInfo? sheet = null,
        IEnumerable<DxfPositionedTag>? tags = null)
    {
        options ??= new PlanClassificationOptions();
        var all = segments as IReadOnlyList<DxfSegment> ?? segments.ToList();
        segments = all;

        // Where the drawing used an arc or a circle. A loop standing on these points was drawn as
        // a curve, and that is the only sound basis for calling a column round.
        var curvePoints = new HashSet<(long, long)>();
        foreach (var s in all.Where(s => s.FromCurve))
        {
            curvePoints.Add(Quantise(s.Start));
            curvePoints.Add(Quantise(s.End));
        }

        var result = new PlanGeometrySet();
        if (tags is not null) result.Tags.AddRange(tags);

        var closedByRole = new List<(string Role, IReadOnlyList<PlanLoop> Loops, string Family)>();

        var slabBuilder = new PlanLoopBuilder(options.JoinTolerance, options.BridgeTolerance, options.ExtendLimit);
        var wallBuilder = new PlanLoopBuilder(options.JoinTolerance, options.WallBridgeTolerance, options.ExtendLimit);

        // Group by layer FAMILY, not by role alone. Revit splits one outline across
        // JBP_C_SLABEDG, -1 and -2 as it exports, so a plate boundary only closes when those are
        // stitched together — but they are variants of one name, and JBP_V-WALL and JBP_B_WALL are
        // not. Pooling everything that shares a role let a broken outline on one layer capture the
        // segments of a clean one on another: on 31168's LEVEL 27 tower B sheet, JBP_V-WALL's six
        // closed loops were welded to JBP_B_WALL-1's twelve open chains across a 12" bridge, and
        // the core came out as a single 326x173 rectangle that resolves to nothing. One wall was
        // written where the drawing holds eight, which is what the engineer saw as "L27 tower B
        // still has no walls".
        // CLOSED FIRST, THEN POOLED. Each layer family is built on its own, so an outline that
        // already closes is never at risk of being welded to a neighbour. Only what is left OPEN is
        // pooled across the families of the same role — which is how a boundary Revit really did
        // split across JBP_C_SLABEDG and -1 still closes, while JBP_V-WALL's six closed loops are
        // no longer captured by JBP_B_WALL-1's twelve open chains.
        //
        // Pooling everything that shared a role cost 31168's LEVEL 27 tower B its core: the drawing
        // holds ten walls and one was written, which the engineer reported as "L27 still has no
        // walls". Refusing to pool at all cost 31138 thirty-eight outlines that genuinely span two
        // layers. Closed first is the rule that serves both.
        // One edge drawn twice is one edge.
        //
        // Revit exports the same rectangle onto a layer and its variant — 31138's parkade columns
        // appear identically on JBP_V_COL and JBP_V_COL-1, four edges each, same coordinates to the
        // tenth of an inch. Those are one family, so both copies reach the same loop builder, and a
        // ring built from eight coincident edges can stitch through a neighbour instead of closing
        // on itself: one 36x104 column came out where the drawing holds a 12x30.
        //
        // Deduplicated per layer FAMILY and by geometry, undirected, so a segment and its reverse
        // count once. Across families it is left alone: JBP_V-WALL and JBP_B_WALL drawing the same
        // line is two different claims about the same place, and the layer ledger should keep both.
        var seenEdges = new HashSet<(string Family, long, long, long, long)>();
        var prepared = new List<(DxfSegment Segment, string? Role)>();
        int duplicateEdges = 0;

        foreach (var s in DashedLineJoiner.Join(segments, options.DashJoinGap))
        {
            string? role = RoleOf(s.Layer, options);
            if (role is null) continue;

            var a = ((long)Math.Round(s.Start.X * 10), (long)Math.Round(s.Start.Y * 10));
            var b = ((long)Math.Round(s.End.X * 10), (long)Math.Round(s.End.Y * 10));
            var (lo, hi) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);

            if (!seenEdges.Add((LayerFamily(s.Layer), lo.Item1, lo.Item2, hi.Item1, hi.Item2)))
            {
                duplicateEdges++;
                continue;
            }
            prepared.Add((s, role));
        }

        if (duplicateEdges > 0)
            result.Flags.Add($"{duplicateEdges} edge(s) were drawn more than once on the same layer family " +
                             "and were read once.");

        var byRole = new List<(string Role, List<PlanLoop> Loops, List<IReadOnlyList<DxfPoint>> OpenChains)>();

        foreach (string role in prepared.Select(x => x.Role!).Distinct())
        {
            var builder = role is RoleWall or RoleColumn ? wallBuilder : slabBuilder;

            // A SECOND chaining pass at the interruption width was tried here and is not used.
            //
            // The reasoning was sound -- a slab edge is cut wherever other linework crosses it, and
            // how wide those cuts run is banked as dxf.flood-fill-bridge, 36 in, against an
            // ordinary bridge of 6 in. It scores well too: floors within a fifth of the engineer's
            // on 31065 go from 18 of 23 to 19.
            //
            // It also welds separate buildings together. 31168's LEVEL 2 carries two towers on one
            // sheet, 12,380 and 12,271 sq ft, and the wider reach joins them into a single 26,309
            // sq ft plate spanning both. A storey may genuinely carry several slabs -- that is why
            // this reads more than one -- and a rule that cannot tell a second slab from a second
            // building is not ready, whatever it scores. One point of floor agreement does not buy
            // two buildings sharing a diaphragm.
            // BACK ON FOR SLAB EDGES, AND ONLY WHERE THE DRAWING SAYS SO.
            //
            // The fear that took it off was welding: 31168's LEVEL 2 carries two towers on one
            // sheet, 12,380 and 12,271 sq ft, and the wider reach joined them into a single
            // 26,309 sq ft plate spanning both, which is two buildings sharing a diaphragm.
            //
            // That is now guarded twice over. A recovered outline is kept only where a THICKNESS
            // CALL-OUT stands inside it -- so it can no longer invent a floor, only confirm one --
            // and a fill plate that swallows two floors already found is still refused as a weld
            // further down. The engineer, on the slab this is meant to reach: "there's a base slab
            // that's 14 inch and inside we have a thicker one", and the tool drew only the inner.
            //
            // Without tags this stays exactly as it was: off.
            PlanLoopBuilder? slabRescue = role == RoleSlab && result.Tags.Count > 0
                ? new PlanLoopBuilder(options.JoinTolerance, options.FloodFillBridge, options.ExtendLimit)
                : null;
            var loops = new List<PlanLoop>();
            var leftovers = new List<DxfSegment>();

            foreach (var family in prepared.Where(x => x.Role == role)
                                           .GroupBy(x => LayerFamily(x.Segment.Layer), x => x.Segment))
            {
                var attempt = builder.Build(family);
                loops.AddRange(attempt.Loops);

                foreach (var chain in attempt.OpenChains)
                    for (int i = 0; i < chain.Count - 1; i++)
                        leftovers.Add(new DxfSegment(family.Key, chain[i], chain[i + 1]));
            }

            // Second chance for everything that did not close on its own.
            var pooled = leftovers.Count > 0 ? builder.Build(leftovers) : null;
            if (pooled is not null) loops.AddRange(pooled.Loops);

            var stillOpen = pooled?.OpenChains.ToList() ?? new List<IReadOnlyList<DxfPoint>>();

            if (slabRescue is not null && stillOpen.Count > 0)
            {
                var rescued = slabRescue.Build(stillOpen
                    .SelectMany(c => Enumerable.Range(0, c.Count - 1)
                        .Select(i => new DxfSegment(RoleSlab, c[i], c[i + 1])))
                    .ToList());
                // Never silently: an outline that needed the wider reach is a reconstruction, and
                // the engineer checking this model is entitled to know which floors were read off
                // the drawing and which were inferred from it.
                // Only the ones the drawing names. A wider reach closes shapes that are not
                // floors as readily as ones that are, and the call-out is what tells them apart.
                var named = rescued.Loops
                    .Select(l => (Loop: l, Says: result.Tags.FirstOrDefault(t =>
                        SlabThicknessCallout.MatchNumberFirstText(t.Text).Any() &&
                        LoopGeometry.PointInPolygon(t.Point, l.Points))))
                    .Where(x => x.Says is not null)
                    .ToList();

                foreach (var (loop, says) in named)
                    result.Flags.Add(
                        $"A slab outline of {loop.Area / 144:N0} sq ft closed only at the interruption " +
                        $"width ({options.FloodFillBridge:0} in), not the ordinary " +
                        $"{options.BridgeTolerance:0} in — a slab edge cut by other linework — and was " +
                        $"modelled as floor because \"{says!.Text}\" is printed inside it. " +
                        "Recovered geometry: check the edge.");

                int declined = rescued.Loops.Count - named.Count;
                if (declined > 0)
                    result.Flags.Add(
                        $"{declined} further outline(s) closed at the interruption width but carry no " +
                        "slab thickness call-out inside them, so they were NOT modelled as floors. " +
                        "Nothing was invented where the drawing does not say what the shape is.");

                loops.AddRange(named.Select(x => x.Loop));
                stillOpen = rescued.OpenChains.ToList();
            }

            byRole.Add((role, loops, stillOpen));
        }

        // The ground this sheet's own structure stands on, as a box. A reconstructed slab outline
        // is judged against it: a floor covers a meaningful share of its building's footprint, and
        // a closed scrap of linework does not. Absolute area cannot tell them apart -- 400 sq ft is
        // the minimum plate and also the size of every stair landing on 31138 -- but 400 against
        // 30,000 is plainly not a floor, whatever the building.
        var structurePoints = prepared
            .Where(x => x.Role is RoleWall or RoleColumn)
            .SelectMany(x => new[] { x.Segment.Start, x.Segment.End })
            .ToList();

        double structureGround = structurePoints.Count > 0
            ? (structurePoints.Max(p => p.X) - structurePoints.Min(p => p.X))
              * (structurePoints.Max(p => p.Y) - structurePoints.Min(p => p.Y))
            : 0;

        var slabCandidates = new List<PlanLoop>();
        int chainClosedCount = 0;
        var chainRings = new List<PlanLoop>();

        foreach (var group in byRole)
        {
            string layer = group.Role;
            bool isWall = layer == RoleWall;
            bool isColumn = layer == RoleColumn;

            var built = (Loops: group.Loops, OpenChains: group.OpenChains);
            var closedFromChains = new List<PlanLoop>();

            // A wall enclosure is broken by its own doorway, so its outline never closes — but it
            // still traces the faces of real walls. Decompose those chains as well rather than
            // discarding the enclosure with the door.
            int recovered = 0;
            if (isWall)
            {
                // Chains the decomposer got nothing from, kept for the pooled pass below. A chain
                // it already read is not offered again: pooling is the fallback for what one chain
                // cannot describe on its own, not a second opinion on what it could.
                var unread = new List<IReadOnlyList<DxfPoint>>();

                foreach (var chain in built.OpenChains)
                {
                    if (chain.Count < 4) { unread.Add(chain); continue; }
                    var asLoop = new PlanLoop(layer, chain, closedExactly: false);
                    var panels = WallOutlineDecomposer.Decompose(asLoop, options);
                    if (panels.Count == 0) { unread.Add(chain); continue; }

                    result.Walls.AddRange(panels);
                    recovered += panels.Count;
                }

                // And then across the chains, not only inside each one.
                //
                // The pass above hands each open chain to the decomposer on its own, so it finds a
                // wall only where one chain happens to trace BOTH faces. Drafting does not promise
                // that: on 31065's ground floor the exterior wall arrives as nineteen open chains
                // on JBP_WALL_EXTERIOR, no two of which close, and the two faces of one wall sit in
                // different chains. Eight panels were recovered out of seventeen outlines and the
                // storey came out with 56% of the wall length the engineer's own model has, the
                // worst of any level in that building.
                //
                // Pooled, those faces pair immediately and at believable thicknesses: fifteen pairs
                // at 9.8" and 11.8", the largest running 85 feet. Her own walls there are 10 to 16
                // inches, so these are the walls, not an artefact of pairing anything with
                // anything. L1 goes from 56% to 82%.
                recovered += PairOpenFaces(result, unread, layer, options);
            }

            // A slab outline broken in ONE place is still that slab's outline.
            //
            // Drafting interrupts a slab edge wherever other linework crosses it, so an outline
            // arrives as a chain with two loose ends rather than a ring. Joining a chain's own two
            // ends is not a guess about what was drawn: it is the one reading that uses every
            // segment the draftsman put down and adds none he did not.
            //
            // The ring still has to pass the tests a drawn one does -- minimum plate area, and
            // filling enough of its own bounding box to be a slab rather than a strip of
            // annotation -- and a chain with more than two loose ends is left alone, because which
            // end joins which is a guess at that point and a wrong guess builds a floor nobody
            // drew.
            // ONLY where the drawing closed no FLOOR on this sheet.
            //
            // A closed outline big enough to be a floor is the draftsman saying what the floor is,
            // and joining loose ends where he has already said it adds floors nobody drew: 31138
            // goes from 13 floor plates to 50 that way. But "closed nothing at all" is too crude a
            // reading of the same idea -- the YMCA mezzanine closes three rings of about 110 sq ft,
            // which are stair nosings, and they were enough to stop the reconstruction on a sheet
            // whose actual slab outlines are three OPEN chains of 2,593, 1,961 and 502 sq ft. The
            // engineer: "there are actually 3 slabs at mezzanine level for the YMCA."
            //
            // Allowing it everywhere is worth something real and measured: 31065 goes from 16 to
            // 18 of 23 storeys whose floor area is within a fifth of the engineer's, because L2
            // through L5 gain plates the drawing does close elsewhere on the same sheet. Two rules
            // were tried to keep that and drop 31138's extras -- "must beat the largest drawn
            // outline" (kills both, since the gains ADD area rather than replace it) and "must
            // cover a tenth of the ground this storey's structure stands on" (31138 still comes out
            // at 37 plates, because its slab edges arrive as many large open chains). Neither
            // separates them. Until one does, this is a reconstruction of last resort, beside the
            // flood fill, and the two points on 31065 are left on the table deliberately.
            // OFF, 25 August. Closing a slab outline by joining its own loose ends, and the wider
            // chaining pass above, both scored well on floor AREA against an engineer's own model
            // -- 16 to 19 of 23 storeys within a fifth of hers -- and both produced shapes no
            // engineer draws. Area cannot see a donut. What reached her was C-LEVEL 3 as a 22,676
            // sq ft plate with an 11,809 sq ft hole in it, LEVEL 1 as 78,859 with 74,832 cut out,
            // and her reply: "on several levels (9, 3, mezz, 1) he inverted slab and opening."
            //
            // Three plate-recovery mechanisms went in on one night -- these two and fragment
            // borrowing -- each measured only by total area, each interacting with the other two
            // and with how openings are found. They come back one at a time, each judged by
            // looking at the geometry, not by a total.
            //
            // What remains is what it was before: outlines the drawing closes, and a flood fill of
            // the drawn linework where it does not.
            // BACK ON, 25 August, AND ONLY WHERE THE DRAWING SAYS SO.
            //
            // It came off because it guessed. Closing a chain recovers a REGION and says nothing
            // about what the region is, so C-LEVEL 3 shipped as a 22,676 sq ft plate with an
            // 11,809 sq ft hole in it and LEVEL 1 as 78,859 with 74,832 cut out, and she wrote
            // "on several levels (9, 3, mezz, 1) he inverted slab and opening."
            //
            // The drawing was never ambiguous. «14\" SLAB» is printed inside the slab. What was
            // missing was the tag: the DXFs this tool is given carry no text, so the sentence that
            // settles it never arrived. AnnotationOverlay brings it now, from an export that does
            // carry text, landing within a third of an inch.
            //
            // So a recovered outline is a floor when a thickness call-out stands inside it, and
            // nothing at all when none does. No tag, no plate -- which is why this is safe where
            // the same code was not: it can no longer invent a floor, only confirm one.
            if (layer == RoleSlab && built.OpenChains.Count > 0 && result.Tags.Count > 0)
            {
                foreach (var chain in built.OpenChains)
                {
                    if (chain.Count < 4) continue;

                    var ring = new PlanLoop(layer, chain, closedExactly: false);

                    if (ring.Area < options.MinPlateArea) continue;

                    var (minX, minY, maxX, maxY) = ring.Bounds();
                    double box = (maxX - minX) * (maxY - minY);
                    if (box <= 0 || ring.Area / box < 0.55) continue;

                    var says = result.Tags.FirstOrDefault(t =>
                        SlabThicknessCallout.MatchNumberFirstText(t.Text).Any() &&
                        LoopGeometry.PointInPolygon(t.Point, ring.Points));
                    if (says is null) continue;

                    closedFromChains.Add(ring);
                    chainRings.Add(ring);
                    chainClosedCount++;

                    // Never silently, and now with the reason. An engineer checking this model is
                    // entitled to know which of her floors this tool inferred, which it read, and
                    // which word on her own drawing it believed.
                    result.Flags.Add(
                        $"{layer}: a slab outline of {ring.Area / 144:N0} sq ft was closed by joining " +
                        "its own two loose ends — the drawing leaves it open where other linework " +
                        $"crosses it — and modelled as floor because \"{says.Text}\" is printed " +
                        "inside it. Recovered geometry: check the edge.");
                }
            }

            if (built.OpenChains.Count > 0)
            {
                double openLength = built.OpenChains.Sum(c =>
                {
                    double total = 0;
                    for (int i = 0; i < c.Count - 1; i++) total += c[i].DistanceTo(c[i + 1]);
                    return total;
                });
                result.Flags.Add(recovered > 0
                    ? $"{layer}: {built.OpenChains.Count} outline(s) would not close; {recovered} wall panel(s) read from them anyway ({openLength:0} units of edge)."
                    : $"{layer}: {built.OpenChains.Count} outline(s) would not close ({openLength:0} units of edge ignored).");
            }

            // A wall drawn as two separate rings — its outer face and its inner face — is one wall,
            // not two enormous ones. Pair them before anything looks at them singly.
            var loops = built.Loops.Concat(closedFromChains).ToList();
            if (isWall) loops = PairConcentricWallRings(result, loops, options);

            foreach (var loop in loops)
            {
                if (isColumn)
                {
                    AddColumn(result, loop, options, curvePoints);
                }
                else if (isWall)
                {
                    AddWallOrColumn(result, loop, options);
                }
                else if (loop.Area >= options.MinSlabArea)
                {
                    // A slab edge that closed through its own linework is a figure of eight, and
                    // it is two floors, not one. 31168's LEVEL 2 podium came back as a single
                    // 16-joint ring whose two wings met at (26, 248) ft -- sensible area, sensible
                    // bounding box, and an hourglass in the model. The crossing point is where one
                    // wing ends, so the drawing itself says where to cut.
                    //
                    // Slabs only. Wall and column rings go through their own paths above, and a
                    // wall outline that touches itself means something different there.
                    var rings = LoopGeometry.SplitSelfCrossings(loop.Points, options.OutlineSelfTouchTolerance);
                    if (rings.Count == 1)
                    {
                        slabCandidates.Add(loop);
                    }
                    else
                    {
                        var kept = rings
                            .Select(r => new PlanLoop(loop.Layer, r, closedExactly: true))
                            .Where(r => r.Area >= options.MinSlabArea)
                            .ToList();

                        // Never silently. If splitting loses the floor, the whole storey changes.
                        result.Flags.Add(
                            $"{loop.Layer}: a slab outline crossed itself and was read as {kept.Count} " +
                            $"separate plate(s) rather than one ring through its own edge " +
                            $"({string.Join(" + ", kept.Select(r => $"{r.Area / 144:0} sq ft"))}).");

                        if (kept.Count > 0) slabCandidates.AddRange(kept);
                        else slabCandidates.Add(loop);
                    }
                }
            }
        }

        SplitSlabsAndOpenings(result, slabCandidates, options);

        // A WALL ENCLOSURE INSIDE A FLOOR IS NOT AUTOMATICALLY A SHAFT. Tried and withdrawn.
        //
        // The reasoning was that an elevator or stair core is a closed wall enclosure standing in a
        // floor, and the slab does not run through it -- and the enclosures were already being
        // found. It shipped, and the engineer opened it the same morning: "on several levels
        // (9, 3, mezz, 1) he inverted slab and opening", with a region cut as a hole marked SHOULD
        // BE SLAB.
        //
        // A wall enclosure is a ROOM at least as often as it is a shaft, and nothing in a concrete
        // outline distinguishes them: both are four walls around a space. Cutting them all turns
        // rooms into holes, which is worse than missing shafts -- a missing hole is slab an
        // engineer deletes, an invented one is diaphragm she has to notice is gone.
        //
        // Openings still come from rings drawn on a slab layer, which is drafting saying "hole".


        // A slab with nothing to carry it, and the support drawn dashed on a hidden layer.
        //
        // This fires only when the sheet gives a plate and no structure at all, which is what a
        // roof plan looks like: nothing stands on a roof, so nothing is drawn solid. 31168's roof
        // reported "a floor plate with no wall or column beneath it" and asked the engineer whether
        // the structure stopped below. It does not -- the seven columns holding it up are on the
        // sheet, drawn dashed, on a layer being discarded as non-structural.
        if (result.Slabs.Count > 0 && result.Columns.Count == 0 && result.Walls.Count == 0)
        {
            var below = segments
                .Where(x => PlanClassificationOptions.Matches(x.Layer, options.BelowSlabLayerPatterns))
                .ToList();

            if (below.Count > 0)
            {
                var asColumns = Classify(below, options with
                {
                    ColumnLayerPatterns = options.BelowSlabLayerPatterns,
                    WallLayerPatterns = Array.Empty<string>(),
                    SlabLayerPatterns = Array.Empty<string>(),
                    BelowSlabLayerPatterns = Array.Empty<string>(),
                });

                if (asColumns.Columns.Count > 0)
                {
                    result.Columns.AddRange(asColumns.Columns.Select(c => c with { FromBelow = true }));
                    result.Flags.Add(
                        $"{asColumns.Columns.Count} column(s) supporting this slab were read from dashed " +
                        "linework below it, because the sheet draws no structure on top of the slab. " +
                        "The engineer's rule: dashed is below and carries the slab, solid is above.");
                }
            }
        }

        // The fill also runs when every floor found came from JOINING A CHAIN'S ENDS, because
        // that reading and this one can disagree by a factor and the bigger one is the floor.
        //
        // Measured on 31065, where an engineer's own model says what the answer is. Closing chains
        // gains L2 through L5 outright -- L3 goes from 10,956 to 25,381 sq ft against her 29,046 --
        // and on L1 and P1 it closes a DETAIL inside the floor, which then counted as the floor and
        // stopped the fill from running at all: L1 fell from 36,280 to 5,499 sq ft against her
        // 58,229, and P1 from 34,458 to 1,359 against her 40,067. Both readings are reconstructions
        // of an outline the drawing leaves open; neither is authoritative, so the one that encloses
        // the ground wins, and a closed chain sitting inside it is a detail and becomes an opening.
        if (result.Slabs.Count == 0 || chainClosedCount > 0)
        {
            // One LAYER at a time, not every slab layer rasterised together.
            //
            // A flood fill needs a boundary and nothing else. Linework from a second slab layer
            // lands INSIDE that boundary, and interior marks can only break a fill -- they can
            // never help it. Drafting splits slab edges across JBP_C_SLABEDG, -1 and -2, and
            // rasterising all three of 31168's C-LEVEL 3 recovered nothing at any bridge width,
            // in a pattern that was not even monotonic: 48 in found a plate, 72 through 120 found
            // none, 144 found one far too big. On its own, JBP_C_SLABEDG recovers the floor
            // steadily -- 12,830 / 13,101 / 13,375 sq ft as the bridge widens.
            //
            // That storey borrowed its neighbour's plate for want of this, and the engineer came
            // back with "level 3 has its own slab edge, it's on the drawings". It is.
            //
            // Largest wins: the slab edge encloses more than any layer of steps or depressions
            // drawn inside it.
            var slabLayers = prepared
                .Where(x => x.Role == RoleSlab)
                .GroupBy(x => x.Segment.Layer, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Layer: g.Key, Segments: g.Select(x => x.Segment).ToList()))
                .ToList();

            PlanLoop? best = null;
            string bestNote = string.Empty;
            string bestLayer = string.Empty;

            foreach (var (layer, segs) in slabLayers)
                if (DxfFloodFillPlateDetector.TryRecover(segs, options, out var got, out string note)
                    && got is not null
                    && (best is null || got.Area > best.Area))
                {
                    best = got; bestNote = note; bestLayer = layer;
                }

            // Everything together, as a last resort, for a drawing that really does split one
            // outline across layers.
            if (best is null && slabLayers.Count > 1)
            {
                var pooled = slabLayers.SelectMany(x => x.Segments).ToList();
                if (DxfFloodFillPlateDetector.TryRecover(pooled, options, out var got, out string note) && got is not null)
                {
                    best = got; bestNote = note; bestLayer = "all slab layers together";
                }
            }

            // A chain-closed ring the fill's plate encloses is a detail inside the floor, not the
            // floor. Handing it back as an opening is what it is.
            if (best is not null && result.Slabs.Count > 0)
            {
                // A chain-closed ring loses to the drawn linework wherever the two overlap, whichever
                // is bigger. Joining a chain's ends is a reconstruction of an outline the drawing
                // leaves open; the fill traces what the draftsman actually put down. Where both
                // describe the same ground, his linework is the better witness.
                //
                // Size cannot decide it. C-LEVEL 3's chain closes into 22,676 sq ft against the
                // 12,830 the fill recovers, and 12,830 is the banked answer -- it is the figure
                // dxf.flood-fill-bridge = 36 in was set BY, and the one that answered the engineer
                // when she said "level 3 has its own slab edge, it's on the drawings". Taking the
                // larger would have quietly overwritten a value she has already seen and accepted.
                var swallowed = result.Slabs
                    .Where(x => LoopGeometry.PointInPolygon(x.Centroid(), best.Points)
                                && (x.Area < best.Area || chainRings.Contains(x)))
                    .ToList();

                // Swallowing MORE THAN ONE is a weld, not a floor. 31168's LEVEL 2 carries two
                // towers, 12,380 and 12,271 sq ft, and letting the fill run on that storey
                // produced a single 26,309 sq ft plate spanning x -109..191 -- both towers on one
                // diaphragm, traced off 1,924 raster points. A reading that covers two floors
                // already found has found the ground between them, which is not floor.
                if (swallowed.Count > 1)
                {
                    result.Flags.Add(
                        $"a floor of {best.Area / 144:N0} sq ft recovered from the drawn linework covers " +
                        $"{swallowed.Count} floors already read on this storey and was not modelled — one " +
                        "plate spanning separate structures is a diaphragm they do not share.");
                    best = null;
                }
                else if (swallowed.Count > 0)
                {
                    // DISCARDED, NOT CUT OUT. Two readings of one floor is not a floor with a hole
                    // in it.
                    //
                    // These made openings until 25 August, and it shipped: LEVEL 1 went to the
                    // engineer as a 78,859 sq ft plate with a 74,832 sq ft hole in it -- the real
                    // slab, cut out of the recovered one -- and C-LEVEL 3, C-LEVEL 9 and the
                    // mezzanine the same way. She opened it and wrote "on several levels
                    // (9, 3, mezz, 1) he inverted slab and opening", with a region marked SHOULD
                    // BE SLAB. She was describing this line.
                    //
                    // A ring inside a floor IS an opening when the drawing puts it there, and
                    // SplitSlabsAndOpenings still reads it that way. What this branch has is
                    // different: the same ground described twice, once by a vector outline and once
                    // by a raster fill of the same linework. Keeping the larger and dropping the
                    // smaller is all that is wanted.
                    foreach (var inside in swallowed) result.Slabs.Remove(inside);
                    result.Flags.Add(
                        $"{swallowed.Count} closed outline(s) totalling {swallowed.Sum(x => x.Area) / 144:N0} sq ft " +
                        $"lie inside the {best.Area / 144:N0} sq ft floor recovered from the same linework, so they " +
                        "are the same floor read twice and only the larger is modelled.");
                }
                // And the other way round: a floor already found may enclose the fill's plate, in
                // which case the fill has recovered part of a floor that is already there. Two
                // plates stacked on the same ground are one diaphragm counted twice, which is
                // worse than either of them alone -- 31168's C-LEVEL 3 came out carrying 22,676
                // and 12,830 sq ft at once.
                //
                // If neither encloses the other the two readings found different ground and both
                // are floors. Discarding one because the other exists is how LEVEL P1 came out at
                // 1,359 sq ft against the engineer's 40,067: a chain closed around a small part of
                // a parkade whose outline the fill had whole.
                else if (result.Slabs.Any(x =>
                             LoopGeometry.PointInPolygon(best.Centroid(), x.Points) && x.Area >= best.Area))
                {
                    best = null;
                }
            }

            if (best is not null)
            {
                result.Slabs.Add(best);
                result.Flags.Add(bestNote + $" Read from {bestLayer} alone.");
            }
        }

        // A storey whose slab edges will not close still has a floor, and the inside of its
        // perimeter wall is the outline of it. Used only as a fallback: where the slab layers gave
        // a plate, that plate is the better boundary and this is ignored.
        // Also when every floor found came from joining a chain's ends, for the same reason the
        // flood fill runs then: a chain can close around a detail inside the floor and stop the
        // reading that had the floor whole. LEVEL P1 on 31065 came out at 1,359 sq ft against the
        // engineer's 40,067 that way -- the parkade's outline is its perimeter wall, and a closed
        // scrap of linework inside it counted as the floor and suppressed it.
        // ONLY where the sheet gives no slab at all.
        //
        // This was widened on 24 August to also run where every slab came from joining a chain's
        // ends, on the reasoning that a chain can close around a detail and leave the real floor
        // unfound. The reasoning holds and the cost is worse: C-LEVEL 3 and the mezzanine were
        // handed a 75,832 sq ft outline -- the whole site, taken from a perimeter wall drawn on
        // their sheets -- on top of their own floors. A storey the drawing gives a slab has a slab.
        if (options.FloorFromPerimeterWall
            && result.Slabs.Count == 0
            && result.EnclosedByWalls.Count > 0)
        {
            var enclosed = result.EnclosedByWalls
                .Where(l => l.Area >= options.MinPlateArea)
                .OrderByDescending(l => l.Area)
                .FirstOrDefault();

            if (enclosed is not null)
            {
                if (sheet?.IsFoundation == true)
                {
                    // Reading (a), not (b): only the perimeter-wall fallback is suppressed on a
                    // FOUNDATION sheet. A closed slab edge still models a plate, because a
                    // foundation plan can draw a real suspended slab over a pit or transfer. The
                    // fault Andrea named on 25 August was the fallback doing the opposite of the
                    // drawing: "There is only a slab-on-grade (S.O.G on our drawings) at P3 but we
                    // don't model those."
                    result.Flags.Add(
                        $"No slab edge on this foundation drawing would close. The inside face of " +
                        $"the perimeter wall encloses {enclosed.Area / 144:N0} sq ft, but it is not " +
                        "modelled as a floor plate because a FOUNDATION sheet can be slab-on-grade, " +
                        "and S.O.G. is not a suspended slab.");
                }
                else
                {
                    result.Slabs.Add(enclosed);
                    result.Flags.Add(
                        $"No slab edge on this drawing would close, so the floor is taken from the inside face of " +
                        $"the perimeter wall — {enclosed.Area / 144:N0} sq ft, one outline, one thickness. It is an " +
                        "approximation offered because a storey with no plate has no diaphragm at all.");
                }
            }
        }

        // Walls only carry force between them where they share a joint, so the centrelines are
        // joined into a network before anything downstream sees them.
        if (options.ConnectWalls && result.Walls.Count > 1)
        {
            var connected = WallNetwork.Connect(result.Walls);
            result.Walls.Clear();
            result.Walls.AddRange(connected);
        }

        // A short element standing on its own is a column; a short face joined to other walls is
        // part of a core and stays a wall. The engineer's rule was "less than 48 in length should
        // be a column", and when that also caught the short faces inside a core her answer was
        // "this should be a wall" — so what decides it is connection, not length alone.
        var ends = result.Walls.SelectMany(w => new[] { w.Start, w.End }).ToList();
        bool Joined(DxfPoint p) => ends.Count(q => q.DistanceTo(p) < 0.01) > 1;

        // Meeting a wall along its length is being joined to it just as much as meeting it at a
        // corner. Tower A's core has a 30x41 return standing on the middle of its bottom wall:
        // neither of the return's ends touches an end of anything, so an end-to-end test called it
        // a standalone stub and made it a 30x41 column. The engineer had already settled what it
        // should be — "a short face joined to other walls is part of a core and stays a wall" —
        // and she marked these very elements as missing return WALLS.
        bool RunsInto(WallAxis stub) => result.Walls.Any(other =>
            !ReferenceEquals(other, stub) && LoopGeometry.SegmentsMeet(stub.Start, stub.End, other.Start, other.End, 1.0));

        // Same half-inch of slack as the rectangle test above: a wall drawn at exactly 48" measures
        // a fraction under it after the export, and must not change from a wall into a column for
        // a tenth of an inch of drafting drift.
        // Slenderness overrides both. Her own 31138 model keeps standalone panels at 30" and 36"
        // as walls with pier labels — W39, W96, W97, W98, W109, W113 — and its most slender column
        // is 12x36, exactly 3:1, with nothing beyond it. So an element longer than three times its
        // thickness is a wall whatever it is joined to; converting it made an 8x38 column that no
        // engineer would draw and threw away the shear it was drawn to carry.
        var stubs = result.Walls
            .Where(w => w.Length < options.MinWallLength - 0.5 && !Joined(w.Start) && !Joined(w.End) && !RunsInto(w))
            .Where(w => w.Thickness <= 0 || w.Length / w.Thickness <= options.MaxColumnAspect)
            .ToList();

        foreach (var stub in stubs)
        {
            result.Walls.Remove(stub);
            var middle = new DxfPoint((stub.Start.X + stub.End.X) / 2.0, (stub.Start.Y + stub.End.Y) / 2.0);
            double bearing = Math.Atan2(stub.End.Y - stub.Start.Y, stub.End.X - stub.Start.X) * 180.0 / Math.PI;
            while (bearing < 0) bearing += 180.0;
            while (bearing >= 180.0) bearing -= 180.0;
            result.Columns.Add(new ColumnFootprint(middle, stub.Thickness, stub.Length, stub.Layer, bearing));
        }

        if (stubs.Count > 0)
            result.Flags.Add(
                $"{stubs.Count} element(s) under {options.MinWallLength:0}\" long and joined to no other wall " +
                "were modelled as columns. Short faces that form part of a core stay walls.");

        // Openings are found last, once the walls are the walls. Found any earlier, a header could
        // span to a panel that the rule above then turned into a column, leaving the header
        // attached to nothing at one end — three of them on 31138.
        if (options.ConnectWalls && result.Walls.Count > 1)
            result.WallOpenings.AddRange(
                WallNetwork.FindOpenings(result.Walls, options.MinOpeningSpan, options.MaxOpeningSpan));

        return result;
    }

    /// <summary>
    /// Finds walls drawn as a ring inside a ring and reads each pair as the one wall it is.
    ///
    /// A basement retaining wall runs the whole perimeter, and drafting closes its outer face and
    /// its inner face as two separate outlines rather than one ribbon. Taken singly each is a
    /// building-sized rectangle: 130ft "thick", far past any wall, so both were discarded and the
    /// perimeter wall never reached the model. On 31168's parkade that was every below-grade wall —
    /// the engineer's first observation was "below grade, the basement walls are missing".
    ///
    /// The test is the band between the two rings. Its width is the area they differ by spread over
    /// their average perimeter, and a pair only counts as a wall when that width is a wall's.
    /// </summary>
    private static List<PlanLoop> PairConcentricWallRings(
        PlanGeometrySet result, List<PlanLoop> loops, PlanClassificationOptions options)
    {
        var remaining = loops.OrderByDescending(l => l.Area).ToList();
        var consumed = new HashSet<PlanLoop>();

        foreach (var outer in remaining)
        {
            if (consumed.Contains(outer)) continue;

            foreach (var inner in remaining)
            {
                if (ReferenceEquals(inner, outer) || consumed.Contains(inner)) continue;
                if (inner.Area >= outer.Area) continue;
                if (!LoopGeometry.PointInPolygon(inner.Centroid(), outer.Points)) continue;

                double band = (outer.Area - inner.Area) / ((Perimeter(outer) + Perimeter(inner)) / 2.0);
                if (band < options.MinWallThickness || band > options.MaxWallThickness) continue;

                // Feed the decomposer both faces at once; it pairs each outer edge with the inner
                // edge facing it, exactly as it does for a wall drawn as a single ribbon.
                var ribbon = new PlanLoop(outer.Layer, Keyhole(outer.Points, inner.Points), closedExactly: false);
                var panels = WallOutlineDecomposer.Decompose(ribbon, options);
                if (panels.Count == 0) continue;

                result.Walls.AddRange(panels);

                // THE OUTER FACE, NOT THE INNER ONE. The engineer, 25 August, on a markup showing
                // a slab edge stopping short: "it should always follow the outer edge of the walls
                // ... you take the other edge of the wall and it extends here."
                //
                // A slab runs to the outside of the wall it sits on, not to the inside face. Taking
                // the inner ring lost a band the width of the wall all the way round — on 31168's
                // parkade, a 12in wall around a 250ft perimeter is roughly a thousand square feet
                // per storey, and it read as a floor that stopped short of its own structure.
                //
                // This is a boundary of last resort either way: it stands in where the slab edges
                // will not close, and she asked for exactly that — "we can even have just one
                // thickness per floor, general outline at first. That will help."
                result.EnclosedByWalls.Add(outer);

                consumed.Add(outer);
                consumed.Add(inner);
                break;
            }
        }

        return remaining.Where(l => !consumed.Contains(l)).ToList();
    }

    /// <summary>
    /// Joins an outer ring to the ring inside it as one outline, cut open along the shortest bridge
    /// between them and traced back along the same bridge.
    ///
    /// The decomposer decides whether the material between two faces is concrete by probing whether
    /// the midpoint lies inside the outline, so the outline has to enclose the band and nothing
    /// else. Simply listing one ring after the other leaves a slit wherever the two lists happen to
    /// start, and a face crossing that slit probes as void and is dropped: on 31168's parkade,
    /// three sides of the perimeter wall came through and the west one did not.
    /// </summary>
    private static List<DxfPoint> Keyhole(IReadOnlyList<DxfPoint> outer, IReadOnlyList<DxfPoint> inner)
    {
        int bestOuter = 0, bestInner = 0;
        double best = double.MaxValue;
        for (int i = 0; i < outer.Count; i++)
        for (int j = 0; j < inner.Count; j++)
        {
            double d = outer[i].DistanceTo(inner[j]);
            if (d < best) { best = d; bestOuter = i; bestInner = j; }
        }

        var points = new List<DxfPoint>();
        for (int k = 0; k <= outer.Count; k++) points.Add(outer[(bestOuter + k) % outer.Count]);
        for (int k = 0; k <= inner.Count; k++) points.Add(inner[((bestInner - k) % inner.Count + inner.Count) % inner.Count]);
        return points;
    }

    private static double Perimeter(PlanLoop loop)
    {
        double total = 0;
        for (int i = 0; i < loop.Points.Count; i++)
            total += loop.Points[i].DistanceTo(loop.Points[(i + 1) % loop.Points.Count]);
        return total;
    }

    private static (long, long) Quantise(DxfPoint p) =>
        ((long)Math.Round(p.X * 100), (long)Math.Round(p.Y * 100));

    private static void AddColumn(
        PlanGeometrySet result, PlanLoop loop, PlanClassificationOptions options, HashSet<(long, long)> curvePoints)
    {
        var box = LoopGeometry.MinAreaBox(loop.Points);
        double longSide = Math.Max(box.Length, box.Thickness);
        double shortSide = Math.Min(box.Length, box.Thickness);

        // The same half-inch of slack the wall rules carry, and for the same reason: a column
        // drawn at exactly the smallest size the tool accepts measures a hair under it once the
        // export and the oriented-box fit have both been through it. 31138's HSS 6x6 columns —
        // 22 on level 5, 22 on level 6 — close into perfect 6.000000 x 6.000000 loops that still
        // land fractionally below a bare 6.0, and every one was discarded without a word.
        const double SizeSlack = 0.5;
        if (shortSide < options.MinColumnSize - SizeSlack || longSide > options.MaxColumnSize + SizeSlack)
        {
            // Say so. A footprint on a column layer that falls outside the size rules is a column
            // the engineer drew and this tool declined to model, and returning quietly makes it
            // one more thing that leaves without appearing in any count.
            //
            // The upper bound is the one that bites: measured across 1,126 engineer models,
            // 207 of 7,538 concrete rectangular column sections run from 98" to 165" — blade
            // columns and wall piers modelled as frame elements, routine in residential towers.
            result.Flags.Add(
                $"{loop.Layer}: a {shortSide:0}x{longSide:0} footprint on a column layer is outside " +
                $"{options.MinColumnSize:0}-{options.MaxColumnSize:0}\" and was not modelled — check this location.");
            return;
        }

        // Round only if the drawing drew it with a curve. Every shape test fails here: a square
        // column with chamfered corners has a square bounding box, fills pi/4 of it, and scores
        // above 0.95 on perimeter efficiency, exactly as a circle does. On 31168 those tests made
        // 160 chamfered columns into 10"-diameter circles, while every arc on a column layer in
        // the whole drawing set measures 16", 24" or 30" and no 10" circle exists anywhere.
        // Whether an arc was used is a fact about the drawing, not an inference from its shape.
        int onCurve = loop.Points.Count(p => curvePoints.Contains(Quantise(p)));
        bool round = loop.Points.Count > 0 &&
                     onCurve >= loop.Points.Count * 0.8 &&
                     longSide > 0 && (longSide - shortSide) / longSide < 0.10;

        // Drawn round, but not with arcs.
        //
        // Roundness is taken from arc provenance and nothing else, because every shape test fails
        // it: a chamfered square fills pi/4 of its box and scores as a circle, which is how 160 of
        // them once became 10" cylinders. But the converse is a real risk in the other direction —
        // a drafter who draws a circle as a many-sided polyline gets a SQUARE column and nothing
        // says so. That cannot be settled from shape either, so it is reported rather than decided:
        // a footprint with many vertices, near-square, filling about pi/4 of its box, and carrying
        // no arc at all, is exactly what a polygonised circle looks like.
        if (!round && loop.Points.Count >= 8 && longSide > 0 &&
            (longSide - shortSide) / longSide < 0.10)
        {
            double fill = Math.Abs(loop.SignedArea) / (longSide * shortSide);
            if (fill is > 0.72 and < 0.85)
                result.Flags.Add(
                    $"{loop.Layer}: a {shortSide:0}x{longSide:0} footprint with {loop.Points.Count} vertices fills " +
                    $"{fill:0.00} of its box and is drawn with no arc, which is what a circle drawn as a polyline " +
                    "looks like. Modelled square — check whether it is round.");
        }

        result.Columns.Add(round
            ? new ColumnFootprint(loop.Centroid(), longSide, longSide, loop.Layer, 0) { IsRound = true }
            : new ColumnFootprint(loop.Centroid(), shortSide, longSide, loop.Layer, AxisAngle(box)));
    }

    /// <summary>
    /// How wide the outline is where it has substance — four times its area over its perimeter.
    ///
    /// This is what separates a solid pier from two walls meeting at a corner, which the bounding
    /// box cannot: both fill most of their box. A pier notched at one corner still measures wider
    /// than any wall in the building; an L of two 12" walls measures about a wall thick, however
    /// large the box drawn round it. Piers stay whole and corners go to the decomposer, which
    /// returns the two walls that are actually there instead of one thick one laid across them.
    /// </summary>
    private static double EffectiveWidth(PlanLoop loop)
    {
        double perimeter = Perimeter(loop);
        return perimeter < 1e-9 ? 0 : 4.0 * loop.Area / perimeter;
    }

    /// <summary>
    /// Pairs wall faces lying in DIFFERENT open chains, which the per-chain decomposition cannot
    /// see. Each face is used once and the nearest qualifying partner wins, so across a corridor
    /// the separation exceeds the wall-thickness ceiling and no pair is made.
    /// </summary>
    /// <returns>How many panels were recovered.</returns>
    private static int PairOpenFaces(
        PlanGeometrySet result,
        IReadOnlyList<IReadOnlyList<DxfPoint>> chains,
        string layer,
        PlanClassificationOptions options)
    {
        var faces = new List<(DxfPoint A, DxfPoint B, double Length, int Chain)>();
        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            var chain = chains[chainIndex];
            for (int i = 0; i < chain.Count - 1; i++)
            {
                double length = chain[i].DistanceTo(chain[i + 1]);
                if (length >= options.MinPanelOverlap) faces.Add((chain[i], chain[i + 1], length, chainIndex));
            }
        }

        if (faces.Count < 2) return 0;

        var used = new bool[faces.Count];
        int made = 0;

        for (int i = 0; i < faces.Count; i++)
        {
            if (used[i]) continue;
            var (ai, bi, li, chainI) = faces[i];
            double ux = (bi.X - ai.X) / li, uy = (bi.Y - ai.Y) / li;
            double nx = -uy, ny = ux;

            int best = -1;
            double bestSeparation = 0, bestT0 = 0, bestT1 = 0, bestSide = 0;

            for (int j = 0; j < faces.Count; j++)
            {
                if (j == i || used[j]) continue;
                var (aj, bj, lj, chainJ) = faces[j];
                if (chainI == chainJ) continue;

                double vx = (bj.X - aj.X) / lj, vy = (bj.Y - aj.Y) / lj;
                if (Math.Abs(ux * vx + uy * vy) < 0.985) continue;

                double d1 = (aj.X - ai.X) * nx + (aj.Y - ai.Y) * ny;
                double d2 = (bj.X - ai.X) * nx + (bj.Y - ai.Y) * ny;

                // Both ends on one side, or the faces cross rather than face each other.
                if (Math.Sign(d1) != Math.Sign(d2) && Math.Abs(d1) > 1e-6 && Math.Abs(d2) > 1e-6) continue;

                double separation = (Math.Abs(d1) + Math.Abs(d2)) / 2.0;

                // This fallback is for ordinary wall faces split across open chains, and it is
                // capped well below the wall-thickness rule on purpose. A thick pair across two
                // chains is ambiguous -- the two sides of a corridor read exactly like one core
                // wall -- and inside a closed outline the decomposer settles it by asking whether
                // the material between the faces lies within the polygon. Open chains have no
                // polygon and there is no equivalent: a test for "does a third face stand in the
                // gap" was written and measured, and it does NOT separate them. It let three
                // ambiguous 28-30" pairs through on 31168's tower A level 35 plan and put the
                // coverage ratchet back to 10 against a ceiling of 7.
                //
                // The cap costs real coverage and is known to: 31065's ground floor recovers to
                // 64% of the engineer's wall length rather than the 82% an uncapped pass reaches.
                // That is the price of not inventing members, and it is the right way round.
                double maxOpenFacePairThickness = Math.Min(options.MaxWallThickness, 18.0);
                if (separation < options.MinWallThickness || separation > maxOpenFacePairThickness) continue;

                double tb0 = (aj.X - ai.X) * ux + (aj.Y - ai.Y) * uy;
                double tb1 = (bj.X - ai.X) * ux + (bj.Y - ai.Y) * uy;
                if (tb0 > tb1) (tb0, tb1) = (tb1, tb0);

                double t0 = Math.Max(0, tb0), t1 = Math.Min(li, tb1);
                if (t1 - t0 < options.MinPanelOverlap) continue;

                if (best < 0 || separation < bestSeparation - 1e-6)
                {
                    best = j;
                    bestSeparation = separation;
                    bestT0 = t0;
                    bestT1 = t1;
                    bestSide = Math.Sign(d1 + d2) >= 0 ? 1.0 : -1.0;
                }
            }

            if (best < 0) continue;

            double offset = bestSeparation / 2.0 * bestSide;
            var start = new DxfPoint(ai.X + ux * bestT0 + nx * offset, ai.Y + uy * bestT0 + ny * offset);
            var end = new DxfPoint(ai.X + ux * bestT1 + nx * offset, ai.Y + uy * bestT1 + ny * offset);

            used[i] = used[best] = true;

            // The per-chain pass runs first and finds a wall wherever one chain traced both of its
            // faces, so pooling re-derives some of what it already has. Keyed the way the composer
            // keys one, because letting a duplicate through is not free: it is reported as a member
            // read and then not modelled, which is the count the coverage ratchet watches.
            var candidate = new WallAxis(start, end, bestSeparation, layer);
            if (result.Walls.Any(w => SameWall(w, candidate))) continue;

            // Nor a second reading of a run this sheet already has. Identical endpoints are the
            // easy case; the one that costs is a pooled pair lying ALONG a panel the per-chain
            // pass already produced, ending a few inches short of it. Both describe one wall, the
            // composer writes whichever it meets first, and the other is reported as a member read
            // and not modelled -- three of those on 31168's tower A level 35 plan alone.
            var mid = new DxfPoint((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
            if (result.Walls.Any(w => DistanceToSegment(mid, w.Start, w.End) <= options.MinWallThickness))
                continue;

            result.Walls.Add(candidate);
            made++;
        }

        return made;
    }

    /// <summary>Perpendicular distance from a point to a segment, clamped to the segment's ends.</summary>
    private static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-12) return p.DistanceTo(a);

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return p.DistanceTo(new DxfPoint(a.X + dx * t, a.Y + dy * t));
    }

    /// <summary>
    /// The same wall, by the rule the composer uses to decide it has one already: the two ends,
    /// rounded to the inch, in either order. Thickness is deliberately not part of it — the
    /// composer's key does not carry thickness either, so two passes deriving one centreline at
    /// 9.8" and 10.2" are one wall there and must be one wall here.
    /// </summary>
    private static bool SameWall(WallAxis a, WallAxis b)
    {
        static (long, long, long, long) Key(WallAxis w)
        {
            var ends = new[]
            {
                ((long)Math.Round(w.Start.X), (long)Math.Round(w.Start.Y)),
                ((long)Math.Round(w.End.X), (long)Math.Round(w.End.Y)),
            }.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
            return (ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2);
        }

        return Key(a) == Key(b);
    }

    /// <summary>Bearing of the footprint's long face from global X, in degrees.</summary>
    private static double AxisAngle(OrientedBox box)
    {
        double dx = box.AxisEnd.X - box.AxisStart.X;
        double dy = box.AxisEnd.Y - box.AxisStart.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return 0;

        double degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        // A rectangle reads the same turned by half a turn; keep it in [0,180).
        while (degrees < 0) degrees += 180.0;
        while (degrees >= 180.0) degrees -= 180.0;
        return degrees;
    }

    private static void AddWallOrColumn(PlanGeometrySet result, PlanLoop loop, PlanClassificationOptions options)
    {
        var box = LoopGeometry.MinAreaBox(loop.Points);

        // A simple rectangle is one wall; anything else is a ribbon tracing both faces
        // of a group of walls (a core, an L or a U) and has to be split face by face.
        bool simpleRectangle = loop.Points.Count == 4 && box.Aspect >= options.MinWallAspect;

        // Half an inch of slack on the length, for the same reason thickness carries some: a wall
        // drawn at exactly the minimum measures a fraction under it after the CAD export, and 29
        // real 48"-long walls on 31138 were failing a 48" test at 47.9.
        const double LengthSlack = 0.5;

        if (simpleRectangle &&
            box.Thickness >= options.MinWallThickness &&
            box.Thickness <= options.MaxWallThickness &&
            box.Length >= options.MinWallLength - LengthSlack)
        {
            result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
            return;
        }

        // Solid footprint rather than a ribbon of faces: a pier. It is drawn on the wall layer and
        // belongs to the lateral system, so it stays a wall panel on its long axis — modelled as a
        // column it would carry no in-plane shear and the core would be softer than it is.
        //
        // Only a convex footprint though. Two walls meeting at a corner make an L, which fills
        // enough of its box to look solid and was being modelled as one thick wall laid across the
        // corner — "it's almost like it filled the volume, but didn't do the actual corner", and
        // the single wall it produced lined up with neither of the two it replaced. An L is not
        // convex; a pier is, so the corner goes to the decomposer and comes back as two walls.
        // A stepped block used to stay one pier, and its centreline lined up with neither limb —
        // "this wall and this wall should be aligned... it's doing just one big wall that's not
        // aligned with this one". Splitting it was tried first and was worse: the limbs measure
        // 31x28 and 14x36, every face short of 48", so the decomposer would not look at them and
        // all 70 fell through to columns, losing their in-plane shear.
        //
        // Her own 31138 model says what the answer should look like: fifteen of its pier labels put
        // several panels on one storey, and three group limbs at right angles — cw9 is a 23" limb
        // and a 27" limb under one pier, cw15 a 272" and a 104", cw6 ten panels from 2" to 15". So
        // a stepped block is limbs on their own centrelines under one shared pier label, and the
        // limbs stay walls however stubby.
        //
        // Getting there is not just a threshold. Routing these to the decomposer was tried again
        // with the face floor lowered to 12" and the panel aspect relaxed to 0.8, and the 70 blocks
        // still do not come apart — three levers, three walls' difference. Something earlier than
        // the aspect rule is refusing them, and guessing at it is how the last two rounds went. It
        // stays question C1 until that is measured, but the question now carries her answer.
        // MEASURED, and it settles C1. The lever was never the aspect rule or the face floor: it
        // is that this pier branch ran BEFORE the decomposer and took the shape first. Tower B's
        // north-west corner is an L — a 67x28 north wall with a 36-thick leg turned down beside
        // it — and it fills 85% of its box, so the pier test claimed it and wrote ONE panel 67
        // long and 42 thick. Forty-two inches is thicker than anything drawn there, the leg was
        // gone, and so was the doorway under it: "still have that problem with the north corner
        // walls for tower B".
        //
        // Handed the same loop, the decomposer returns both limbs on their own centrelines. So it
        // is asked first, and the pier branch keeps only what genuinely does not come apart — a
        // solid footprint yields one panel or none, an L yields two.
        var decomposed = WallOutlineDecomposer.Decompose(loop, options);

        double boxArea = box.Length * box.Thickness;
        if (decomposed.Count < 2 &&
            boxArea > 0 && loop.Area / boxArea >= options.PierFillRatio && box.Aspect < 4.0 &&
            EffectiveWidth(loop) > options.MaxWallThickness)
        {
            if (box.Thickness <= options.MaxPierThickness && box.Length >= options.MinWallLength)
            {
                result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
                return;
            }

            // A column only if it is short enough to BE one, by her own rule: "less than 48 in
            // length should be a column".
            if (box.Length < options.MinWallLength &&
                box.Thickness >= options.MinColumnSize && box.Length <= options.MaxColumnSize)
            {
                result.Columns.Add(new ColumnFootprint(loop.Centroid(), box.Thickness, box.Length, loop.Layer, AxisAngle(box)));
                return;
            }

            // Too stocky to be a pier and too big to be a column, but it is solid concrete drawn
            // on a WALL layer, so it is a pier whatever its proportions. It stays a wall on its
            // long axis and keeps the in-plane shear it was drawn to carry.
            //
            // Without this, one 65x82 footprint on 31168's B-LEVEL 1 became a 65x82 FRAME COLUMN.
            // The widest column in her own 31138 model is 36x72 and 31168's export has no concrete
            // rectangular column at all, so nothing that size is a column in either engineer's
            // hands. The run flagged it for checking, which is not the same as getting it right.
            //
            // "Whatever its proportions" had no ceiling on it, and that is how a 132" wall — eleven
            // feet thick — reached an engineer's model. Nothing in either reference is near it:
            // 31168's own walls run 10 to 16 inches, with 36 for the tower core and nothing between.
            // A footprint this stocky is not a pier drawn thick, it is a shape that is not a wall,
            // and saying so is worth more than a member nobody can account for.
            if (box.Thickness > options.MaxPierThickness)
            {
                result.Flags.Add(
                    $"{loop.Layer}: solid outline {box.Length:0}x{box.Thickness:0} is thicker than any wall in " +
                    $"this building ({options.MaxPierThickness:0}\" limit) — not modelled, check this location.");
                return;
            }

            result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
            return;
        }

        var panels = decomposed;
        if (panels.Count > 0)
        {
            // Thickness is no longer flagged. It produced 615 notes on 31168 asking the engineer to
            // confirm walls over 24", and the answer was that they are real: "some walls are
            // thicker than 24"". A flag every reader learns to ignore is worse than no flag.
            result.Walls.AddRange(panels);
            return;
        }

        // Nothing paired up. A footprint small enough to be a member becomes a column rather than
        // being discarded — an element too short to be a wall and too slender to be a stubby one
        // used to satisfy no branch at all and vanish without ever being modelled: 29 of them on
        // 31138 and 8 on 31168, gone silently. Anything that is concrete on a structural layer is
        // worth carrying, and the engineer's rule already says a short element is a column.
        if (box.Thickness >= options.MinColumnSize && box.Length <= options.MaxColumnSize)
        {
            // Too slender to be anyone's column. The most slender column in her own 31138 model is
            // 12x36, exactly 3:1, and there is nothing beyond it; the 31168 export has no concrete
            // rectangular column at all. A footprint longer than three times its width is a wall in
            // both models, so it is modelled as one — on its centreline, keeping its in-plane
            // shear — rather than as an 8x38 column no engineer would draw.
            // Long enough to be a wall by the engineer's own rule — "less than 48 in length should
            // be a column" — so it is one, whatever its proportions.
            //
            // Slenderness alone is not enough to decide this. A 36x104 footprint on 31138's
            // JBP_B_WALL measures 2.89:1, slips under the 3.0 limit, and became a 36x104 FRAME
            // COLUMN once the portfolio raised the size cap past 104. The 96" cap had been the only
            // thing stopping it, and stopping it silently. This is wall-layer concrete a hundred
            // inches long; modelling it as a frame element throws away the in-plane shear it was
            // drawn to carry, exactly as it did for the 65x82 in the pier branch above.
            if (box.Length >= options.MinWallLength - LengthSlack || box.Aspect > options.MaxColumnAspect)
            {
                result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
                return;
            }

            result.Columns.Add(new ColumnFootprint(loop.Centroid(), box.Thickness, box.Length, loop.Layer, AxisAngle(box)));
            return;
        }

        // The bounding box says nothing about how much concrete is in it. An L-shaped path three
        // inches wide measures 541x145 and reads, in a flag, exactly like a wall would — which is
        // how eighteen of these went into an engineer's workbook as questions about her building
        // when every one was linework: 2 to 4 inches of implied material, against her thinnest
        // real wall at 10. The material per unit of run is the number that separates them, so the
        // flag carries it and whoever reads the flag does not have to go back to the drawing.
        double implied = box.Length > 1e-9 ? loop.Area / box.Length : 0;

        result.Flags.Add(
            $"{loop.Layer}: outline {box.Length:0}x{box.Thickness:0} with {loop.Points.Count} vertices " +
            $"could not be resolved into wall panels — check this location. " +
            $"[implied thickness {implied:0.0} in]");
    }

    /// <summary>Largest rings are slabs; rings sitting inside one of them are openings.</summary>
    private static void SplitSlabsAndOpenings(PlanGeometrySet result, List<PlanLoop> candidates, PlanClassificationOptions options)
    {
        var ordered = candidates.OrderByDescending(l => l.Area).ToList();
        var slabs = new List<PlanLoop>();

        foreach (var loop in ordered)
        {
            var centre = loop.Centroid();
            var container = slabs.FirstOrDefault(s => LoopGeometry.PointInPolygon(centre, s.Points));
            if (container is not null)
            {
                // TWO FACES OF ONE BOUNDARY ARE NOT A FLOOR WITH A HOLE IN IT.
                //
                // A slab edge drawn as an outer and an inner line -- the two faces of the perimeter
                // wall -- gives two rings, one inside the other, a wall's thickness apart. Taking
                // the outer as the floor and cutting the inner out of it leaves the storey as a
                // thin ring of concrete round the outside and nothing in the middle.
                //
                // It shipped that way: LEVEL 1 went out as a 78,859 sq ft floor with a 74,832 sq ft
                // hole in it, C-LEVEL 3 as 75,832 with 22,676 cut out, and the engineer opened it
                // and wrote "on several levels (9, 3, mezz, 1) he inverted slab and opening".
                //
                // The band between them says which case this is, exactly as it does for a wall
                // drawn as two faces: a gap the width of a wall is one boundary, a gap of anything
                // else is a genuine hole.
                double band = (container.Area - loop.Area)
                              / ((Perimeter(container) + Perimeter(loop)) / 2.0);

                if (band >= options.MinWallThickness && band <= options.MaxWallThickness)
                {
                    result.Flags.Add(
                        $"{loop.Layer}: a slab outline of {loop.Area / 144:N0} sq ft sits " +
                        $"{band:0} in inside one of {container.Area / 144:N0} sq ft — read as the two " +
                        "faces of one edge, not as a hole. The outer face is the floor.");
                    continue;
                }

                // Say how far inside it sits. An opening the size of the floor it is cut from is
                // the thing an engineer spots first, and the band is what says whether it is a hole
                // or the other face of the same edge.
                result.Flags.Add(
                    $"{loop.Layer}: an opening of {loop.Area / 144:N0} sq ft cut from a floor of " +
                    $"{container.Area / 144:N0} sq ft, its edge {band:0} in inside — " +
                    (loop.Area > container.Area * 0.5
                        ? "MORE THAN HALF the floor, which is worth checking."
                        : "check it is a hole and not the inner face of the edge."));

                result.Openings.Add(loop);
                continue;
            }

            // Two floors on one storey may abut; they may not lie on top of each other. Containment
            // by centroid is not enough to see that -- two readings of the same floor can each have
            // their centre outside the other while covering most of the same ground, and both then
            // ship as floors. That is one diaphragm counted twice, and ETABS has no way to know.
            //
            // 31168's LEVEL 2 arrived as four plates that way: 26,309 sq ft, then 10,838 and 8,216
            // lying across it and each other.
            var (aMinX, aMinY, aMaxX, aMaxY) = loop.Bounds();
            double own = (aMaxX - aMinX) * (aMaxY - aMinY);

            var clash = slabs.FirstOrDefault(s =>
            {
                var (bMinX, bMinY, bMaxX, bMaxY) = s.Bounds();
                double w = Math.Min(aMaxX, bMaxX) - Math.Max(aMinX, bMinX);
                double h = Math.Min(aMaxY, bMaxY) - Math.Max(aMinY, bMinY);
                return w > 0 && h > 0 && own > 0 && (w * h) / own >= 0.5;
            });

            if (clash is not null)
            {
                result.Flags.Add(
                    $"a floor plate of {loop.Area / 144:N0} sq ft lies over one of " +
                    $"{clash.Area / 144:N0} sq ft already read on this storey and was not modelled — two " +
                    "readings of one floor, not two floors. The larger is kept.");
                continue;
            }

            slabs.Add(loop);
        }

        // A ring too small to be a floor and not inside one is linework, not structure. Modelling
        // it puts a scrap of slab in mid-air with nothing under it.
        foreach (var scrap in slabs.Where(s => s.Area < options.MinPlateArea))
            result.Flags.Add(
                $"{scrap.Layer}: closed ring of {scrap.Area / 144:0} sq ft on its own — too small for a " +
                "floor plate and not inside one, so it is linework rather than slab; not modelled.");

        result.Slabs.AddRange(slabs.Where(s => s.Area >= options.MinPlateArea));
    }
}
