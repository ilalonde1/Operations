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

    public double MinColumnSize { get; init; } = 6.0;
    public double MaxColumnSize { get; init; } = 96.0;

    /// <summary>Rings smaller than this on a slab layer are noise, not slabs or openings.</summary>
    public double MinSlabArea { get; init; } = 400.0;

    public double JoinTolerance { get; init; } = 0.05;
    public double BridgeTolerance { get; init; } = 6.0;

    public static bool Matches(string layer, IReadOnlyList<string> patterns)
        => patterns.Any(p => layer.Contains(p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Turns the raw segments of one plan into the structural members it depicts.</summary>
public static class StructuralPlanClassifier
{
    public static PlanGeometrySet Classify(IEnumerable<DxfSegment> segments, PlanClassificationOptions? options = null)
    {
        options ??= new PlanClassificationOptions();
        var result = new PlanGeometrySet();
        var builder = new PlanLoopBuilder(options.JoinTolerance, options.BridgeTolerance);

        var byLayer = segments.GroupBy(s => s.Layer);

        var slabCandidates = new List<PlanLoop>();

        foreach (var group in byLayer)
        {
            string layer = group.Key;
            bool isWall = PlanClassificationOptions.Matches(layer, options.WallLayerPatterns);
            bool isColumn = PlanClassificationOptions.Matches(layer, options.ColumnLayerPatterns);
            bool isSlab = PlanClassificationOptions.Matches(layer, options.SlabLayerPatterns);
            if (!isWall && !isColumn && !isSlab) continue;

            var built = builder.Build(group);

            if (built.OpenChains.Count > 0)
            {
                double openLength = built.OpenChains.Sum(c =>
                {
                    double total = 0;
                    for (int i = 0; i < c.Count - 1; i++) total += c[i].DistanceTo(c[i + 1]);
                    return total;
                });
                result.Flags.Add(
                    $"{layer}: {built.OpenChains.Count} outline(s) would not close ({openLength:0} units of edge ignored).");
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

        var panels = WallOutlineDecomposer.Decompose(loop, options);
        if (panels.Count > 0)
        {
            result.Walls.AddRange(panels);
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
