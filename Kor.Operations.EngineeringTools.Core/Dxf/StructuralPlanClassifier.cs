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
    public double MinWallLength { get; init; } = 12.0;

    /// <summary>Below this the footprint is a column, not a wall, however it was drawn.</summary>
    public double MinWallAspect { get; init; } = 2.0;

    /// <summary>
    /// Aspect required of a panel cut out of a ribbon. Lower than <see cref="MinWallAspect"/>
    /// because the end faces of each wall are consumed with their wall, so a short panel here
    /// is a genuine pier rather than a leftover sliver.
    /// </summary>
    public double MinPanelAspect { get; init; } = 1.2;

    public double MinColumnSize { get; init; } = 6.0;
    public double MaxColumnSize { get; init; } = 96.0;

    /// <summary>
    /// A wall outline is a thin ribbon tracing faces, so it fills little of its bounding
    /// box. A footprint that fills most of its box is solid concrete — a pier — and must
    /// not be sliced into panels.
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

            foreach (var loop in built.Loops)
            {
                if (isColumn)
                {
                    AddColumn(result, loop, options);
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

        SplitSlabsAndOpenings(result, slabCandidates);
        return result;
    }

    private static void AddColumn(PlanGeometrySet result, PlanLoop loop, PlanClassificationOptions options)
    {
        var box = LoopGeometry.MinAreaBox(loop.Points);
        double longSide = Math.Max(box.Length, box.Thickness);
        double shortSide = Math.Min(box.Length, box.Thickness);

        if (shortSide < options.MinColumnSize || longSide > options.MaxColumnSize) return;
        result.Columns.Add(new ColumnFootprint(loop.Centroid(), shortSide, longSide, loop.Layer, AxisAngle(box)));
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

        if (simpleRectangle &&
            box.Thickness >= options.MinWallThickness &&
            box.Thickness <= options.MaxWallThickness &&
            box.Length >= options.MinWallLength)
        {
            result.Walls.Add(new WallAxis(box.AxisStart, box.AxisEnd, box.Thickness, loop.Layer));
            return;
        }

        // Solid footprint rather than a ribbon of faces: a pier. It is drawn on the wall layer and
        // belongs to the lateral system, so it stays a wall panel on its long axis — modelled as a
        // column it would carry no in-plane shear and the core would be softer than it is.
        double boxArea = box.Length * box.Thickness;
        if (boxArea > 0 && loop.Area / boxArea >= options.PierFillRatio && box.Aspect < 4.0)
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
            result.Walls.AddRange(panels);
            foreach (var panel in panels.Where(p => p.Thickness > options.UnusualWallThickness))
                result.Flags.Add(
                    $"{loop.Layer}: wall {panel.Length:0}\" long modelled at {panel.Thickness:0}\" thick — " +
                    "unusually thick, confirm against the drawing.");
            return;
        }

        // Nothing paired up: a stubby footprint is a pier, anything larger is unreadable.
        if (box.Aspect < options.MinWallAspect &&
            box.Thickness >= options.MinColumnSize &&
            box.Length <= options.MaxColumnSize)
        {
            result.Columns.Add(new ColumnFootprint(loop.Centroid(), box.Thickness, box.Length, loop.Layer, AxisAngle(box)));
            return;
        }

        result.Flags.Add(
            $"{loop.Layer}: outline {box.Length:0}x{box.Thickness:0} with {loop.Points.Count} vertices " +
            "could not be resolved into wall panels — check this location.");
    }

    /// <summary>Largest rings are slabs; rings sitting inside one of them are openings.</summary>
    private static void SplitSlabsAndOpenings(PlanGeometrySet result, List<PlanLoop> candidates)
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

        result.Slabs.AddRange(slabs);
    }
}
