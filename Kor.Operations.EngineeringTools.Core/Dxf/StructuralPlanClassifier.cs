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
}

/// <summary>Turns the raw segments of one plan into the structural members it depicts.</summary>
public static class StructuralPlanClassifier
{
    internal const string RoleWall = "walls";
    internal const string RoleColumn = "columns";
    internal const string RoleSlab = "slab edges";

    private static string? RoleOf(string layer, PlanClassificationOptions options)
    {
        // Columns first: a layer may satisfy more than one pattern, and the column
        // convention (JBP_V_COL) is the most specific.
        if (PlanClassificationOptions.Matches(layer, options.ColumnLayerPatterns)) return RoleColumn;
        if (PlanClassificationOptions.Matches(layer, options.WallLayerPatterns)) return RoleWall;
        if (PlanClassificationOptions.Matches(layer, options.SlabLayerPatterns)) return RoleSlab;
        return null;
    }

    public static PlanGeometrySet Classify(IEnumerable<DxfSegment> segments, PlanClassificationOptions? options = null)
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
        var slabBuilder = new PlanLoopBuilder(options.JoinTolerance, options.BridgeTolerance, options.ExtendLimit);
        var wallBuilder = new PlanLoopBuilder(options.JoinTolerance, options.WallBridgeTolerance, options.ExtendLimit);

        // Group by what a layer is for, not by its name. Revit splits one outline across
        // JBP_C_SLABEDG, -1 and -2 as it exports, so a plate boundary only closes when the
        // related layers are stitched together.
        var byRole = DashedLineJoiner.Join(segments, options.DashJoinGap)
            .Select(s => (Segment: s, Role: RoleOf(s.Layer, options)))
            .Where(x => x.Role is not null)
            .GroupBy(x => x.Role!, x => x.Segment);

        var slabCandidates = new List<PlanLoop>();

        foreach (var group in byRole)
        {
            string layer = group.Key;
            bool isWall = layer == RoleWall;
            bool isColumn = layer == RoleColumn;

            var built = (isWall || isColumn ? wallBuilder : slabBuilder).Build(group);

            // A wall enclosure is broken by its own doorway, so its outline never closes — but it
            // still traces the faces of real walls. Decompose those chains as well rather than
            // discarding the enclosure with the door.
            int recovered = 0;
            if (isWall)
            {
                foreach (var chain in built.OpenChains)
                {
                    if (chain.Count < 4) continue;
                    var asLoop = new PlanLoop(layer, chain, closedExactly: false);
                    var panels = WallOutlineDecomposer.Decompose(asLoop, options);
                    if (panels.Count == 0) continue;

                    result.Walls.AddRange(panels);
                    recovered += panels.Count;
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
            var loops = built.Loops.ToList();
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
                    slabCandidates.Add(loop);
                }
            }
        }

        SplitSlabsAndOpenings(result, slabCandidates, options);

        // A storey whose slab edges will not close still has a floor, and the inside of its
        // perimeter wall is the outline of it. Used only as a fallback: where the slab layers gave
        // a plate, that plate is the better boundary and this is ignored.
        if (result.Slabs.Count == 0 && result.EnclosedByWalls.Count > 0)
        {
            var enclosed = result.EnclosedByWalls
                .Where(l => l.Area >= options.MinPlateArea)
                .OrderByDescending(l => l.Area)
                .FirstOrDefault();

            if (enclosed is not null)
            {
                result.Slabs.Add(enclosed);
                result.Flags.Add(
                    $"No slab edge on this drawing would close, so the floor is taken from the inside face of " +
                    $"the perimeter wall — {enclosed.Area / 144:N0} sq ft, one outline, one thickness. It is an " +
                    "approximation offered because a storey with no plate has no diaphragm at all.");
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

        // Same half-inch of slack as the rectangle test above: a wall drawn at exactly 48" measures
        // a fraction under it after the export, and must not change from a wall into a column for
        // a tenth of an inch of drafting drift.
        // Slenderness overrides both. Her own 31138 model keeps standalone panels at 30" and 36"
        // as walls with pier labels — W39, W96, W97, W98, W109, W113 — and its most slender column
        // is 12x36, exactly 3:1, with nothing beyond it. So an element longer than three times its
        // thickness is a wall whatever it is joined to; converting it made an 8x38 column that no
        // engineer would draw and threw away the shear it was drawn to carry.
        var stubs = result.Walls
            .Where(w => w.Length < options.MinWallLength - 0.5 && !Joined(w.Start) && !Joined(w.End))
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

                // The inside face of a perimeter wall is a closed outline of the floor it encloses.
                // Where the slab edges will not close — the whole parkade on 31168 — this is the
                // only boundary the drawing offers, and the engineer said a general outline is what
                // she wants to start from: "we can even have just one thickness per floor, general
                // outline at first. That will help."
                result.EnclosedByWalls.Add(inner);

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

        if (shortSide < options.MinColumnSize || longSide > options.MaxColumnSize) return;

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
        double boxArea = box.Length * box.Thickness;
        if (boxArea > 0 && loop.Area / boxArea >= options.PierFillRatio && box.Aspect < 4.0 &&
            EffectiveWidth(loop) > options.MaxWallThickness)
        {
            if (box.Thickness <= options.MaxPierThickness && box.Length >= options.MinWallLength)
            {
                result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
                return;
            }

            if (box.Thickness >= options.MinColumnSize && box.Length <= options.MaxColumnSize)
            {
                result.Columns.Add(new ColumnFootprint(loop.Centroid(), box.Thickness, box.Length, loop.Layer, AxisAngle(box)));
                return;
            }
        }

        var panels = WallOutlineDecomposer.Decompose(loop, options);
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
            if (box.Aspect > options.MaxColumnAspect)
            {
                result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
                return;
            }

            result.Columns.Add(new ColumnFootprint(loop.Centroid(), box.Thickness, box.Length, loop.Layer, AxisAngle(box)));
            return;
        }

        result.Flags.Add(
            $"{loop.Layer}: outline {box.Length:0}x{box.Thickness:0} with {loop.Points.Count} vertices " +
            "could not be resolved into wall panels — check this location.");
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
            if (container is not null) result.Openings.Add(loop);
            else slabs.Add(loop);
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
