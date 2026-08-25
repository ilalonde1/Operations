namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>A point in drawing units (whatever the DXF header declares — typically inches).</summary>
public readonly record struct DxfPoint(double X, double Y)
{
    public double DistanceTo(DxfPoint other)
    {
        double dx = X - other.X, dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// One straight segment lifted out of a DXF. Arcs and polylines are tessellated into
/// segments on read, so everything downstream deals with a single primitive.
/// </summary>
public sealed record DxfSegment(string Layer, DxfPoint Start, DxfPoint End)
{
    public double Length => Start.DistanceTo(End);

    /// <summary>
    /// This segment came from an arc or a circle rather than from a straight line.
    ///
    /// It is the only trustworthy way to know a column is round. Shape cannot tell: a square
    /// column with chamfered corners has a square bounding box, fills pi/4 of it, and even scores
    /// as round on perimeter — on 31168 it made 160 of them 10"-diameter circles when the drawings
    /// hold no 10" arc at all. What the drawing was drawn with is not a heuristic.
    /// </summary>
    public bool FromCurve { get; init; }
}

/// <summary>Annotation read from the drawing at the point where the drafter placed it.</summary>
public sealed record DxfPositionedTag(string Text, DxfPoint Point, string Layer, string RawText);

/// <summary>A closed ring of points, in order. First point is not repeated at the end.</summary>
public sealed class PlanLoop
{
    public PlanLoop(string layer, IReadOnlyList<DxfPoint> points, bool closedExactly)
    {
        Layer = layer;
        Points = points;
        ClosedExactly = closedExactly;
    }

    public string Layer { get; }
    public IReadOnlyList<DxfPoint> Points { get; }

    /// <summary>False when the ring was completed by bridging a gap rather than by exact endpoint matching.</summary>
    public bool ClosedExactly { get; }

    public double SignedArea
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < Points.Count; i++)
            {
                var a = Points[i];
                var b = Points[(i + 1) % Points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }
    }

    public double Area => Math.Abs(SignedArea);

    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (minX, minY, maxX, maxY);
    }

    public DxfPoint Centroid()
    {
        double a = SignedArea;
        if (Math.Abs(a) < 1e-9)
        {
            double sx = 0, sy = 0;
            foreach (var p in Points) { sx += p.X; sy += p.Y; }
            return new DxfPoint(sx / Points.Count, sy / Points.Count);
        }

        double cx = 0, cy = 0;
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            var q = Points[(i + 1) % Points.Count];
            double cross = p.X * q.Y - q.X * p.Y;
            cx += (p.X + q.X) * cross;
            cy += (p.Y + q.Y) * cross;
        }
        return new DxfPoint(cx / (6.0 * a), cy / (6.0 * a));
    }
}

/// <summary>
/// The oriented bounding box of a loop: the rectangle of least area that contains it.
/// For a wall or column footprint drawn as a rectangle this recovers the true
/// long axis and thickness even when the element is skewed to the global axes.
/// </summary>
public sealed record OrientedBox(DxfPoint Center, DxfPoint AxisStart, DxfPoint AxisEnd, double Length, double Thickness)
{
    public double Aspect => Thickness <= 1e-9 ? double.PositiveInfinity : Length / Thickness;
}

/// <summary>A wall footprint reduced to the centreline ETABS needs, plus its thickness.</summary>
public sealed record WallAxis(DxfPoint Start, DxfPoint End, double Thickness, string Layer)
{
    public double Length => Start.DistanceTo(End);
}

/// <summary>
/// A doorway or window in a wall run: the gap between two wall ends that are in line with each
/// other. The wall stops either side of it and a header spans over it — the engineer's rule, and
/// the reason a gap must not be closed up: "the wall should stop at the opening. A header
/// (spandrel) may be over the opening."
/// </summary>
public sealed record WallOpening(DxfPoint Start, DxfPoint End, double Thickness, string Layer)
{
    public double Span => Start.DistanceTo(End);
}

/// <summary>
/// A column footprint reduced to a location, a size and how it is turned.
/// <paramref name="Depth"/> is the long face and <paramref name="Width"/> the short one;
/// <paramref name="AxisAngleDegrees"/> is the bearing of the long face from global X.
/// </summary>
public sealed record ColumnFootprint(
    DxfPoint Center, double Width, double Depth, string Layer, double AxisAngleDegrees = 0,
    bool FromBelow = false)
{
    /// <summary>
    /// Read from DASHED linework, so it stands under this sheet's slab rather than on top of it.
    /// A solid member drawn on the plan for storey N rises N -> N+1 and belongs to N+1; a dashed
    /// one runs N-1 -> N and belongs to N. The two are the same rule from either side of the slab,
    /// and this flag is which side a member was found on.
    /// </summary>

    /// <summary>
    /// A round column. Its footprint has no long axis, so the least-area box lands at whatever
    /// angle the tessellation happened to favour: modelled from that box it comes out square and
    /// turned at random, which is what the engineer saw — "these are supposed to be round columns,
    /// but they're square. Square and rotated."
    /// </summary>
    public bool IsRound { get; init; }
}

/// <summary>Everything one drawing contributes to the model.</summary>
public sealed class PlanGeometrySet
{
    public List<WallAxis> Walls { get; } = new();
    public List<ColumnFootprint> Columns { get; } = new();

    /// <summary>Outer slab boundaries (largest closed rings on the slab-edge layers).</summary>
    public List<PlanLoop> Slabs { get; } = new();

    /// <summary>Rings fully inside a slab ring — shafts, stair openings.</summary>
    public List<PlanLoop> Openings { get; } = new();

    /// <summary>Doorways found between wall ends, each of which wants a header over it.</summary>
    public List<WallOpening> WallOpenings { get; } = new();

    /// <summary>Readable drawing annotation, carried without deciding what structural rule it means.</summary>
    public List<DxfPositionedTag> Tags { get; } = new();

    /// <summary>
    /// The inside face of a perimeter wall — a closed outline of the floor it encloses, used as a
    /// floor boundary only where the slab edges themselves would not close.
    /// </summary>
    public List<PlanLoop> EnclosedByWalls { get; } = new();

    /// <summary>Human-readable notes about anything that could not be resolved cleanly.</summary>
    public List<string> Flags { get; } = new();
}
