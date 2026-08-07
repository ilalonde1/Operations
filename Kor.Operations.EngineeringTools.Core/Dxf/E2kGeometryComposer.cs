using System.Globalization;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One drawing's geometry, placed on one storey of the model.</summary>
public sealed record StoryPlacement(StoryLevel Story, PlanGeometrySet Geometry, string SourceSheet);

public sealed record ComposeOptions
{
    /// <summary>Material for generated walls, slabs and columns. Falls back to any concrete in the model.</summary>
    public string? MaterialContains { get; init; }

    /// <summary>
    /// Thickness for generated floor areas when the drawing does not state one (inches).
    /// 12" is the typical floor on 31168 per the project's own Revit sections; the model
    /// defines no 8" floor at all, so the old default understated every plate.
    /// </summary>
    public double DefaultSlabThickness { get; init; } = 12.0;

    /// <summary>Prefix for every generated object, so KOR-made geometry is filterable in ETABS.</summary>
    public string NamePrefix { get; init; } = "K";

    /// <summary>Translation applied to drawing coordinates before writing (inches).</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    public bool IncludeFloors { get; init; } = true;
}

public sealed record ComposeSummary(
    int Walls, int Columns, int Floors, int Points, int Stories,
    IReadOnlyList<string> Sections, IReadOnlyList<string> Flags);

/// <summary>Writes classified plan geometry into an existing ETABS model document.</summary>
public static class E2kGeometryComposer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ComposeSummary Compose(E2kDocument doc, IReadOnlyList<StoryPlacement> placements, ComposeOptions? options = null)
    {
        options ??= new ComposeOptions();

        string material = doc.FindConcreteMaterial(options.MaterialContains)
            ?? throw new InvalidOperationException("The reference model defines no concrete material to build sections from.");

        var used = doc.ExistingObjectNames();
        string prefix = options.NamePrefix;

        var pointLines = new List<string>();
        var areaLines = new List<string>();
        var lineLines = new List<string>();
        var areaAssigns = new List<string>();
        var lineAssigns = new List<string>();
        var flags = new List<string>();

        var wallProps = new SortedDictionary<double, string>();
        var slabProps = new SortedDictionary<double, string>();
        var frameProps = new SortedDictionary<(double W, double D), string>();

        // Sections that already existed are reused, not redefined; only genuinely new
        // thicknesses need a section writing.
        var newWallProps = new SortedDictionary<double, string>();
        var newSlabProps = new SortedDictionary<double, string>();
        var reusedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pointNames = new Dictionary<(long, long, long), string>();
        var placedColumns = new HashSet<(long, long, string)>();
        var placedWalls = new HashSet<(long, long, long, long, string)>();
        int pointCounter = 0, wallCounter = 0, floorCounter = 0, colCounter = 0;

        string NextName(string kind, ref int counter)
        {
            string name;
            do { name = $"{prefix}{kind}{++counter}"; } while (used.Contains(name));
            used.Add(name);
            return name;
        }

        string PointAt(double x, double y, double z)
        {
            // Quantise to 1/1000 inch so shared corners collapse to one joint.
            var key = ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000), (long)Math.Round(z * 1000));
            if (pointNames.TryGetValue(key, out string? existing)) return existing;

            string name;
            do { name = $"{prefix}P{++pointCounter}"; } while (used.Contains(name));
            used.Add(name);
            pointNames[key] = name;

            pointLines.Add($"  POINT \"{name}\"  {F(x)} {F(y)} {F(z)}");
            return name;
        }

        foreach (var placement in placements)
        {
            var story = placement.Story;
            double zTop = story.Elevation;
            double zBottom = story.ElevationBelow;

            foreach (var wall in placement.Geometry.Walls)
            {
                double thickness = SnapHalfInch(wall.Thickness);
                if (!wallProps.TryGetValue(thickness, out string? propName))
                {
                    // Prefer a section the project already defines at this thickness: it carries
                    // the real concrete mix and a name the engineer will recognise.
                    propName = doc.FindShellProperty("Wall", thickness);
                    if (propName is not null) reusedSections.Add(propName);
                    else newWallProps[thickness] = propName = $"KOR-W{Trim(thickness)}";
                    wallProps[thickness] = propName;
                }

                double x1 = wall.Start.X + options.OffsetX, y1 = wall.Start.Y + options.OffsetY;
                double x2 = wall.End.X + options.OffsetX, y2 = wall.End.Y + options.OffsetY;

                // Same panel from two overlapping sheets must not be modelled twice.
                var ends = new[] { ((long)Math.Round(x1 * 100), (long)Math.Round(y1 * 100)), ((long)Math.Round(x2 * 100), (long)Math.Round(y2 * 100)) }
                    .OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
                if (!placedWalls.Add((ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2, story.Name))) continue;

                string p1 = PointAt(x1, y1, zBottom);
                string p2 = PointAt(x2, y2, zBottom);
                string p3 = PointAt(x2, y2, zTop);
                string p4 = PointAt(x1, y1, zTop);

                string name = NextName("W", ref wallCounter);
                areaLines.Add($"  AREA \"{name}\"  PANEL  4  \"{p1}\"  \"{p2}\"  \"{p3}\"  \"{p4}\"  0  0  0  0");
                areaAssigns.Add(
                    $"  AREAASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{propName}\"  OBJMESHTYPE \"DEFAULT\"  " +
                    "ADDRESTRAINT \"Yes\"  CARDINALPOINT \"MIDDLE\"");
            }

            foreach (var column in placement.Geometry.Columns)
            {
                double w = SnapInch(column.Width), d = SnapInch(column.Depth);
                if (!frameProps.TryGetValue((w, d), out string? sectionName))
                {
                    sectionName = $"KOR-C{Trim(w)}x{Trim(d)}";
                    frameProps[(w, d)] = sectionName;
                }

                double x = column.Center.X + options.OffsetX, y = column.Center.Y + options.OffsetY;

                // One column per location per storey: sheets overlap, and duplicated
                // members would otherwise double the stiffness at that point.
                var stack = ((long)Math.Round(x * 100), (long)Math.Round(y * 100), story.Name);
                if (!placedColumns.Add(stack)) continue;

                string bottom = PointAt(x, y, zBottom);
                string top = PointAt(x, y, zTop);

                // ETABS measures ANG from local axis 2, which lies along global Y for an
                // unrotated column; the section's D is its long face.
                double angle = Normalise(column.AxisAngleDegrees - 90.0);

                string name = NextName("C", ref colCounter);
                lineLines.Add($"  LINE  \"{name}\"  COLUMN  \"{bottom}\"  \"{top}\"  0");
                lineAssigns.Add(
                    $"  LINEASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{sectionName}\"  ANG {Trim(angle)} MINNUMSTA 3 " +
                    "AUTOMESH \"YES\"  MESHATINTERSECTIONS \"YES\"");
            }

            if (!options.IncludeFloors) continue;

            foreach (var slab in placement.Geometry.Slabs)
            {
                double thickness = options.DefaultSlabThickness;
                if (!slabProps.TryGetValue(thickness, out string? propName))
                {
                    propName = doc.FindShellProperty("Slab", thickness);
                    if (propName is not null) reusedSections.Add(propName);
                    else newSlabProps[thickness] = propName = $"KOR-S{Trim(thickness)}";
                    slabProps[thickness] = propName;
                }

                var names = slab.Points
                    .Select(p => PointAt(p.X + options.OffsetX, p.Y + options.OffsetY, zTop))
                    .Distinct()
                    .ToList();

                if (names.Count < 3)
                {
                    flags.Add($"{placement.SourceSheet}: a slab outline collapsed to fewer than three joints and was skipped.");
                    continue;
                }

                string name = NextName("F", ref floorCounter);
                string joints = string.Join("  ", names.Select(n => $"\"{n}\""));
                string offsets = string.Join("  ", names.Select(_ => "0"));
                areaLines.Add($"  AREA \"{name}\"  FLOOR  {names.Count}  {joints}  {offsets}");

                // No diaphragm is assigned. Naming one here would tie every storey's floors
                // into a single rigid diaphragm spanning elevations, and which diaphragm a
                // floor belongs to is the engineer's call in any case.
                areaAssigns.Add(
                    $"  AREAASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{propName}\"  OBJMESHTYPE \"DEFAULT\"  " +
                    "CARDINALPOINT \"MIDDLE\"");
            }

            foreach (string flag in placement.Geometry.Flags)
                flags.Add($"{placement.SourceSheet}: {flag}");
        }

        string wallMaterial = doc.FindConcreteMaterial("Wall") ?? material;
        string slabMaterial = doc.FindConcreteMaterial("Floor") ?? material;

        var wallPropLines = newWallProps.Select(kv =>
            $"  SHELLPROP  \"{kv.Value}\"  PROPTYPE  \"Wall\"  MATERIAL \"{wallMaterial}\"  MODELINGTYPE \"ShellThin\"  WALLTHICKNESS {Trim(kv.Key)}").ToList();
        var slabPropLines = newSlabProps.Select(kv =>
            $"  SHELLPROP  \"{kv.Value}\"  PROPTYPE  \"Slab\"  MATERIAL \"{slabMaterial}\"  MODELINGTYPE \"ShellThin\"  SLABTYPE \"Slab\"  SLABTHICKNESS {Trim(kv.Key)}").ToList();
        string columnMaterial = doc.FindConcreteMaterial("Column") ?? material;
        var framePropLines = frameProps.Select(kv =>
            $"  FRAMESECTION  \"{kv.Value}\"  MATERIAL \"{columnMaterial}\"  SHAPE \"Concrete Rectangular\"  D {Trim(kv.Key.D)} B {Trim(kv.Key.W)}").ToList();

        if (pointLines.Count > 0) doc.Append("POINT COORDINATES", pointLines);
        if (areaLines.Count > 0) doc.Append("AREA CONNECTIVITIES", areaLines);
        if (lineLines.Count > 0) doc.Append("LINE CONNECTIVITIES", lineLines);
        if (areaAssigns.Count > 0) doc.Append("AREA ASSIGNS", areaAssigns);
        if (lineAssigns.Count > 0) doc.Append("LINE ASSIGNS", lineAssigns);
        if (wallPropLines.Count > 0) doc.Append("WALL PROPERTIES", wallPropLines);
        if (slabPropLines.Count > 0) doc.Append("SLAB PROPERTIES", slabPropLines);
        if (framePropLines.Count > 0) doc.Append("FRAME SECTIONS", framePropLines);

        var sections = wallProps.Values.Concat(slabProps.Values).Concat(frameProps.Values).ToList();
        return new ComposeSummary(
            wallCounter, colCounter, floorCounter, pointCounter,
            placements.Select(p => p.Story.Name).Distinct().Count(),
            sections, flags);
    }

    private static double Normalise(double degrees)
    {
        while (degrees < 0) degrees += 360.0;
        while (degrees >= 360.0) degrees -= 360.0;
        return degrees;
    }

    private static double SnapHalfInch(double value) => Math.Round(value * 2.0, MidpointRounding.AwayFromZero) / 2.0;
    private static double SnapInch(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    private static string Trim(double value) => value.ToString("0.###", Inv);
    private static string F(double value) => value.ToString("0.####", Inv);
}
