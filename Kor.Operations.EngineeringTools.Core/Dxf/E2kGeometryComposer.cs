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

    /// <summary>
    /// Give every generated wall a pier label — the engineer's answer to W4, "all walls should be
    /// assigned a pier label". Walls at the same plan position on different storeys share a label,
    /// which is what makes a pier one element up the building and gives wall forces to design from.
    /// </summary>
    public bool AssignPierLabels { get; init; } = true;

    /// <summary>
    /// Assign a rigid diaphragm to generated plates. Off: the engineer assigns diaphragms herself
    /// along with loads, stiffness modifiers and section properties, and a diaphragm arriving with
    /// the geometry is one more thing to undo.
    /// </summary>
    public bool AssignDiaphragms { get; init; }

    /// <summary>
    /// Height of a doorway, used to size the header over it: the engineer's rule for a spandrel's
    /// depth is the storey height less the opening height. 84" is a standard door; a plan cannot
    /// say, so this is the one number in that rule that is assumed rather than measured.
    /// </summary>
    public double OpeningHeight { get; init; } = 84.0;

    /// <summary>
    /// Skip a member the model already has at that place on that storey. The output is the
    /// reference model with geometry added, so without this an engineer's own walls and columns
    /// are duplicated by ours — doubling stiffness and self-weight exactly where they overlap.
    /// </summary>
    public bool SkipMembersAlreadyModelled { get; init; } = true;

    /// <summary>How close a generated member must be to an existing one to count as the same (inches).</summary>
    public double AlreadyModelledTolerance { get; init; } = 6.0;
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
        var roundProps = new SortedDictionary<double, string>();

        // Sections that already existed are reused, not redefined; only genuinely new
        // thicknesses need a section writing.
        var newWallProps = new SortedDictionary<double, string>();
        var newSlabProps = new SortedDictionary<double, string>();
        var reusedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diaphragms = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // What the model already contains, so nothing is modelled twice.
        var existing = options.SkipMembersAlreadyModelled
            ? E2kGeometryReader.Read(doc)
            : new E2kModelGeometry();
        var existingColumns = existing.Columns
            .GroupBy(c => c.Story, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(c => c.At).ToList(), StringComparer.OrdinalIgnoreCase);
        var existingWalls = existing.Walls
            .GroupBy(w => w.Story, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        int skippedColumns = 0, skippedWalls = 0;

        var pointNames = new Dictionary<(long, long, long), string>();
        var placedSlabs = new HashSet<(long, long, string)>();
        var placedColumns = new HashSet<(long, long, long, long, string)>();
        var placedWalls = new HashSet<(long, long, long, long, string)>();
        var storeysWithMembers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var storeysWithPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pierNames = new Dictionary<(long, long, long, long), string>();
        var spandrelNames = new Dictionary<(long, long, long, long), string>();
        var placedSpandrels = new HashSet<(long, long, long, long, string)>();
        var placedOpenings = new HashSet<(long, long, string)>();
        int spandrelCounter = 0, openingCounter = 0;
        int pointCounter = 0, wallCounter = 0, floorCounter = 0, colCounter = 0;

        // The model's whole storey stack, lowest first. A site model interleaves its towers here,
        // so one tower's wall crosses more than one of these; see StoreysSpannedBy.
        var allStories = doc.ReadStories().OrderBy(s => s.Elevation).ToList();

        /// <summary>
        /// The wall section for this thickness, preferring one the project already defines: it
        /// carries the real concrete mix and a name the engineer will recognise.
        /// </summary>
        string WallSection(double thickness)
        {
            if (wallProps.TryGetValue(thickness, out string? existing)) return existing;

            string? found = doc.FindShellProperty("Wall", thickness);
            if (found is not null) reusedSections.Add(found);
            else newWallProps[thickness] = found = $"KOR-W{Trim(thickness)}";

            wallProps[thickness] = found;
            return found;
        }

        string NextName(string kind, ref int counter)
        {
            string name;
            do { name = $"{prefix}{kind}{++counter}"; } while (used.Contains(name));
            used.Add(name);
            return name;
        }

        /// A joint is a plan position only. ETABS takes elevation from the storey an object is
        /// assigned to, not from its points — the third number on a POINT line is an offset from
        /// that storey, and writing an absolute elevation there throws the member hundreds of feet
        /// off the storey it belongs to. Andrea's own model writes 1,098 of its 1,156 points as
        /// bare X and Y for exactly this reason.
        /// A joint raised above its storey by <paramref name="zOffset"/>. Zero writes a plain plan
        /// joint; anything else writes the third value, which is the one thing that number is for —
        /// a member that does not run the full height of its storey, such as a header.
        string PointAt(double x, double y, double zOffset = 0)
        {
            // Quantise to 1/1000 inch so shared corners collapse to one joint.
            var key = ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000), (long)Math.Round(zOffset * 1000));
            if (pointNames.TryGetValue(key, out string? existing)) return existing;

            string name;
            do { name = $"{prefix}P{++pointCounter}"; } while (used.Contains(name));
            used.Add(name);
            pointNames[key] = name;

            pointLines.Add(Math.Abs(zOffset) < 1e-9
                ? $"  POINT \"{name}\"  {F(x)} {F(y)}"
                : $"  POINT \"{name}\"  {F(x)} {F(y)} {F(zOffset)}");
            return name;
        }

        /// <summary>
        /// A pier label for the wall at this plan position, shared by the same wall on every storey.
        ///
        /// A pier is one element up the building, so the label has to be the same on each storey or
        /// the forces come out per-panel and are no use to design from. Positions are rounded to
        /// 6" before matching, which is inside the drift between one storey's drafting and the next
        /// but well under the distance to a different wall.
        /// </summary>
        string PierFor(double x1, double y1, double x2, double y2)
        {
            static long Q(double v) => (long)Math.Round(v / 6.0);
            var ends = new[] { (Q(x1), Q(y1)), (Q(x2), Q(y2)) }.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
            var key = (ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2);

            if (!pierNames.TryGetValue(key, out string? pier))
                pierNames[key] = pier = $"{prefix}PIER{pierNames.Count + 1}";
            return pier;
        }

        /// <summary>A spandrel label for the header at this opening, shared up the building.</summary>
        string SpandrelFor(double x1, double y1, double x2, double y2)
        {
            static long Q(double v) => (long)Math.Round(v / 6.0);
            var ends = new[] { (Q(x1), Q(y1)), (Q(x2), Q(y2)) }.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
            var key = (ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2);

            if (!spandrelNames.TryGetValue(key, out string? spandrel))
                spandrelNames[key] = spandrel = $"{prefix}SPAN{spandrelNames.Count + 1}";
            return spandrel;
        }

        /// <summary>
        /// The storeys this member may still be assigned to, having removed any already carrying a
        /// member at the same place.
        ///
        /// Deduplicating on the storey a member was PLACED on is not enough once a member is
        /// assigned to every storey it spans: two placements from different source storeys expand
        /// onto a common one and both land there. On 31168 that doubled 22 walls and 18 columns —
        /// KC249 spans A-LEVEL 33 and B-LEVEL 33, KC2100 spans B-LEVEL 32 and A-LEVEL 33, and they
        /// meet on A-LEVEL 33 with the same joint and section. Doubled stiffness and self-weight,
        /// invisible in every count, because two members do look like two members.
        /// </summary>
        List<string> FreeStoreysFor(StoryLevel story, (long, long, long, long) where, HashSet<(long, long, long, long, string)> taken)
        {
            var spanned = StoreysSpannedBy(story);

            // All or nothing. Handing a member only the storeys that happen to be free leaves it
            // assigned to a two-inch sliver where a duplicate took the rest, which is the wafer
            // fault over again — a member has to be whole or absent.
            if (spanned.Any(s => taken.Contains((where.Item1, where.Item2, where.Item3, where.Item4, s))))
                return new List<string>();

            foreach (string s in spanned) taken.Add((where.Item1, where.Item2, where.Item3, where.Item4, s));
            return spanned;
        }

        /// <summary>
        /// Every storey a member on this storey passes through, bottom-up.
        ///
        /// ETABS builds a wall or column between consecutive storeys of its own global list. In a
        /// site model that list holds a storey for each tower's floor, so tower B's level-34 wall —
        /// which runs from B-LEVEL 33 up to B-LEVEL 34 — crosses A-LEVEL 34 on the way. Assigning
        /// it only to B-LEVEL 34 builds it between A-LEVEL 34 and B-LEVEL 34: a two-inch wafer
        /// hanging a storey above its floor. Assigning it to both builds one continuous wall.
        /// ETABS supports this directly — an object carries an assign line per storey.
        /// </summary>
        List<string> StoreysSpannedBy(StoryLevel story)
        {
            var spanned = allStories
                .Where(s => s.Elevation > story.ElevationBelow + 0.01 && s.Elevation <= story.Elevation + 0.01)
                .Select(s => s.Name)
                .ToList();
            return spanned.Count > 0 ? spanned : new List<string> { story.Name };
        }

        foreach (var placement in placements)
        {
            var story = placement.Story;

            foreach (var wall in placement.Geometry.Walls)
            {
                double thickness = SnapHalfInch(wall.Thickness);
                double x1 = wall.Start.X + options.OffsetX, y1 = wall.Start.Y + options.OffsetY;
                double x2 = wall.End.X + options.OffsetX, y2 = wall.End.Y + options.OffsetY;

                // Same panel from two overlapping sheets must not be modelled twice — tested against
                // the storeys it will actually be assigned to, not the one it was placed on.
                var ends = new[] { ((long)Math.Round(x1), (long)Math.Round(y1)), ((long)Math.Round(x2), (long)Math.Round(y2)) }
                    .OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
                var wallWhere = (ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2);
                var wallStoreys = FreeStoreysFor(story, wallWhere, placedWalls);
                if (wallStoreys.Count == 0) continue;

                // A wall the engineer has already modelled runs along the same line: compare the
                // midpoint, since one drawn wall may be modelled as several stacked panels.
                var mid = new DxfPoint((x1 + x2) / 2.0, (y1 + y2) / 2.0);
                if (existingWalls.TryGetValue(story.Name, out var alreadyWalls) &&
                    alreadyWalls.Any(w => DistanceToSegment(mid, w.A, w.B) <= options.AlreadyModelledTolerance))
                {
                    skippedWalls++;
                    continue;
                }

                // Claimed only once the wall is certain to be written, so a section is never
                // declared for a panel that the checks above then drop.
                string propName = WallSection(thickness);

                string pa = PointAt(x1, y1);
                string pb = PointAt(x2, y2);

                // A panel is its two plan points repeated: the pair at the storey above the one it
                // is assigned to (offset 1) and the same pair at that storey (offset 0).
                string name = NextName("W", ref wallCounter);
                areaLines.Add($"  AREA \"{name}\"  PANEL  4  \"{pa}\"  \"{pb}\"  \"{pb}\"  \"{pa}\"  1  1  0  0");
                storeysWithMembers.Add(story.Name);

                string pier = options.AssignPierLabels ? $"  PIER  \"{PierFor(x1, y1, x2, y2)}\"" : string.Empty;
                foreach (string on in wallStoreys)
                    areaAssigns.Add(
                        $"  AREAASSIGN  \"{name}\"  \"{on}\"  SECTION \"{propName}\"{pier}  OBJMESHTYPE \"DEFAULT\"  " +
                        "ADDRESTRAINT \"Yes\"  CARDINALPOINT \"MIDDLE\"");
            }

            foreach (var column in placement.Geometry.Columns)
            {
                double w = SnapInch(column.Width), d = SnapInch(column.Depth);
                double x = column.Center.X + options.OffsetX, y = column.Center.Y + options.OffsetY;

                // One member per place per storey, to the nearest inch. Quantising finer does not work: the// One column per location per storey — tested against the storeys it will actually
                // be assigned to. Sheets overlap, and duplicates double the stiffness at that point.
                var colWhere = ((long)Math.Round(x), (long)Math.Round(y), 0L, 0L);
                var colStoreys = FreeStoreysFor(story, colWhere, placedColumns);
                if (colStoreys.Count == 0) continue;

                if (existingColumns.TryGetValue(story.Name, out var already) &&
                    already.Any(p => p.DistanceTo(new DxfPoint(x, y)) <= options.AlreadyModelledTolerance))
                {
                    skippedColumns++;
                    continue;
                }

                string at = PointAt(x, y);

                // The section is claimed only once the column is certain to be written. Claimed any
                // earlier, a column that is then skipped as one the engineer already has leaves its
                // section declared and unused — clutter in her section list for a member that does
                // not exist.
                string? sectionName;
                if (column.IsRound)
                {
                    if (!roundProps.TryGetValue(d, out sectionName))
                        roundProps[d] = sectionName = $"KOR-D{Trim(d)}";
                }
                else if (!frameProps.TryGetValue((w, d), out sectionName))
                {
                    sectionName = $"KOR-C{Trim(w)}x{Trim(d)}";
                    frameProps[(w, d)] = sectionName;
                }

                // ETABS measures ANG from local axis 2, which lies along global Y for an
                // unrotated column; the section's D is its long face.
                double angle = Normalise(column.AxisAngleDegrees - 90.0);

                // A column is one plan point, rising one storey from the storey it is assigned to.
                string name = NextName("C", ref colCounter);
                lineLines.Add($"  LINE  \"{name}\"  COLUMN  \"{at}\"  \"{at}\"  1");
                storeysWithMembers.Add(story.Name);
                foreach (string on in colStoreys)
                    lineAssigns.Add(
                        $"  LINEASSIGN  \"{name}\"  \"{on}\"  SECTION \"{sectionName}\"  ANG {Trim(angle)} MINNUMSTA 3 " +
                        "AUTOMESH \"YES\"  MESHATINTERSECTIONS \"YES\"");
            }

            // Headers over the doorways in a wall run. The engineer asked for these directly —
            // "there are no headers" — and told us how they are modelled: "we don't model them as
            // line elements, we model them as shells. Since they're not the whole height of the
            // floor, they're just above the opening, in ETABS they're called spandrels."
            //
            // So a header is a wall panel, not a beam, standing only over the opening. Its depth is
            // the storey height less the opening height, which is her rule for it, and it is built
            // the way both reference models build one: a PANEL whose four joints sit at the storey
            // (flags 0 0 0 0) with two of them raised by the panel's depth.
            // Depth is the storey height less the opening height, held between the shallowest and
            // deepest spandrels KOR's own models use (24" on 30783, 60" on 31138). Without the
            // ceiling a double-height storey produced a 396"-deep header, which is a wall.
            double storeyHeight = story.Elevation - story.ElevationBelow;
            double spandrelDepth = SnapInch(Math.Clamp(storeyHeight - options.OpeningHeight, 24.0, 60.0));

            foreach (var opening in placement.Geometry.WallOpenings)
            {
                double thickness = SnapHalfInch(opening.Thickness);
                double sx = opening.Start.X + options.OffsetX, sy = opening.Start.Y + options.OffsetY;
                double ex = opening.End.X + options.OffsetX, ey = opening.End.Y + options.OffsetY;

                var span = new[] { ((long)Math.Round(sx * 100), (long)Math.Round(sy * 100)),
                                   ((long)Math.Round(ex * 100), (long)Math.Round(ey * 100)) }
                    .OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
                if (!placedSpandrels.Add((span[0].Item1, span[0].Item2, span[1].Item1, span[1].Item2, story.Name))) continue;

                string lowA = PointAt(sx, sy);
                string lowB = PointAt(ex, ey);
                if (lowA == lowB) continue;
                string highA = PointAt(sx, sy, spandrelDepth);
                string highB = PointAt(ex, ey, spandrelDepth);

                string headerSection = WallSection(thickness);
                string spandrel = SpandrelFor(sx, sy, ex, ey);
                string name = NextName("S", ref spandrelCounter);
                areaLines.Add($"  AREA \"{name}\"  PANEL  4  \"{highA}\"  \"{highB}\"  \"{lowB}\"  \"{lowA}\"  0  0  0  0");
                foreach (string on in StoreysSpannedBy(story))
                    areaAssigns.Add(
                        $"  AREAASSIGN  \"{name}\"  \"{on}\"  SECTION \"{headerSection}\"  SPANDREL  \"{spandrel}\"  " +
                        "OBJMESHTYPE \"DEFAULT\"  CARDINALPOINT \"MIDDLE\"");
            }

            if (!options.IncludeFloors) continue;

            foreach (var slab in placement.Geometry.Slabs)
            {
                double thickness = options.DefaultSlabThickness;

                var names = slab.Points
                    .Select(p => PointAt(p.X + options.OffsetX, p.Y + options.OffsetY))
                    .Distinct()
                    .ToList();

                if (names.Count < 3)
                {
                    flags.Add($"{placement.SourceSheet}: a slab outline collapsed to fewer than three joints and was skipped.");
                    continue;
                }

                // One plate per place per storey. Drafting issues both a range sheet (L4-L14) and a
                // sheet for the individual level, so the same floor arrives twice and was modelled
                // twice — "every floor we have two slabs on top of each other". Walls and columns
                // were already deduplicated; plates were not.
                var middle = slab.Centroid();
                var where = ((long)Math.Round((middle.X + options.OffsetX) / 12.0),
                             (long)Math.Round((middle.Y + options.OffsetY) / 12.0), story.Name);
                if (!placedSlabs.Add(where)) continue;

                // Claimed only once the plate is certain to be written.
                if (!slabProps.TryGetValue(thickness, out string? propName))
                {
                    propName = doc.FindShellProperty("Slab", thickness);
                    if (propName is not null) reusedSections.Add(propName);
                    else newSlabProps[thickness] = propName = $"KOR-S{Trim(thickness)}";
                    slabProps[thickness] = propName;
                }

                string name = NextName("F", ref floorCounter);
                storeysWithPlates.Add(story.Name);
                string joints = string.Join("  ", names.Select(n => $"\"{n}\""));
                string offsets = string.Join("  ", names.Select(_ => "0"));
                areaLines.Add($"  AREA \"{name}\"  FLOOR  {names.Count}  {joints}  {offsets}");

                string diaphragmAssign = string.Empty;
                if (options.AssignDiaphragms)
                {
                    // One per storey rather than one shared: a single diaphragm across elevations
                    // ties joints at different heights, which ETABS warns about.
                    string diaphragm = DiaphragmFor(story.Name, prefix);
                    diaphragms.Add(diaphragm);
                    diaphragmAssign = $"  DIAPH \"{diaphragm}\"";
                }

                areaAssigns.Add(
                    $"  AREAASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{propName}\"  OBJMESHTYPE \"DEFAULT\"" +
                    $"{diaphragmAssign}  CARDINALPOINT \"MIDDLE\"");
            }

            // Shafts and stair openings, cut out of the plate rather than left for the engineer.
            // ETABS models an opening as an area carrying no section, which is how her own 31138
            // model does it — 42 of them, drawn by hand.
            foreach (var opening in placement.Geometry.Openings)
            {
                var names = opening.Points
                    .Select(p => PointAt(p.X + options.OffsetX, p.Y + options.OffsetY))
                    .Distinct()
                    .ToList();
                if (names.Count < 3) continue;

                var centre = opening.Centroid();
                var key = ((long)Math.Round((centre.X + options.OffsetX) * 100),
                           (long)Math.Round((centre.Y + options.OffsetY) * 100), story.Name);
                if (!placedOpenings.Add(key)) continue;

                string name = NextName("O", ref openingCounter);
                string joints = string.Join("  ", names.Select(n => $"\"{n}\""));
                string offsets = string.Join("  ", names.Select(_ => "0"));
                areaLines.Add($"  AREA \"{name}\"  AREA  {names.Count}  {joints}  {offsets}");

                // OPENING "Yes" on its own, with no section — the way ETABS marks an opening, and
                // the way every model on the share does it. Her 31138 carries 220 of these against
                // 74 written the other way with a null section, and none of those 74 carry the
                // attribute, so the two are alternatives and this is the one in common use.
                areaAssigns.Add($"  AREAASSIGN  \"{name}\"  \"{story.Name}\"  OPENING \"Yes\"");
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
            $"  FRAMESECTION  \"{kv.Value}\"  MATERIAL \"{columnMaterial}\"  SHAPE \"Concrete Rectangular\"  D {Trim(kv.Key.D)} B {Trim(kv.Key.W)}")
            .Concat(roundProps.Select(kv =>
                $"  FRAMESECTION  \"{kv.Value}\"  MATERIAL \"{columnMaterial}\"  SHAPE \"Concrete Circle\"  D {Trim(kv.Key)}"))
            .ToList();

        if (diaphragms.Count > 0)
            doc.Append("DIAPHRAGM NAMES", diaphragms.Select(d => $"  DIAPHRAGM \"{d}\"    TYPE RIGID"));

        // A pier or spandrel label an assign refers to must be declared, or ETABS drops it on import.
        var labels = pierNames.Values.OrderBy(p => p, StringComparer.Ordinal).Select(p => $"  PIERNAME  \"{p}\"")
            .Concat(spandrelNames.Values.OrderBy(s => s, StringComparer.Ordinal).Select(s => $"  SPANDRELNAME  \"{s}\""))
            .ToList();
        if (labels.Count > 0) doc.Append("PIER/SPANDREL NAMES", labels);

        if (pointLines.Count > 0) doc.Append("POINT COORDINATES", pointLines);
        if (areaLines.Count > 0) doc.Append("AREA CONNECTIVITIES", areaLines);
        if (lineLines.Count > 0) doc.Append("LINE CONNECTIVITIES", lineLines);
        if (areaAssigns.Count > 0) doc.Append("AREA ASSIGNS", areaAssigns);
        if (lineAssigns.Count > 0) doc.Append("LINE ASSIGNS", lineAssigns);
        if (wallPropLines.Count > 0) doc.Append("WALL PROPERTIES", wallPropLines);
        if (slabPropLines.Count > 0) doc.Append("SLAB PROPERTIES", slabPropLines);
        if (framePropLines.Count > 0) doc.Append("FRAME SECTIONS", framePropLines);

        if (skippedWalls > 0 || skippedColumns > 0)
            flags.Add($"{skippedWalls} wall(s) and {skippedColumns} column(s) were already modelled at those " +
                      "locations and were not added again.");

        // A column wider than any in the model it is being added to is probably not a column. One
        // 65x82 came through on 31168 where the widest in the engineer's own 31138 is 36x72; it may
        // be a wall or a pier read as a frame, and it is not provable from geometry alone.
        var oversize = frameProps.Keys.Where(k => Math.Min(k.W, k.D) > 48).ToList();
        if (oversize.Count > 0)
            flags.Add(
                $"{oversize.Count} generated column section(s) are wider than 48\" on both faces " +
                $"({string.Join(", ", oversize.Select(k => $"{k.W:0}x{k.D:0}"))}). A column that wide is more " +
                "likely a wall or a pier; worth a look at those locations.");

        // A storey with walls and columns but no plate is the one thing that still reads as wrong
        // in a 3D view: members standing with nothing spanning between them. It happens where a
        // drawing's slab edges will not close — the parkade levels on 31168 — and it is worth
        // naming, because the storey has no diaphragm until a plate is drawn there.
        var plateless = storeysWithMembers.Where(s => !storeysWithPlates.Contains(s)).ToList();
        if (plateless.Count > 0)
            flags.Add(
                $"{plateless.Count} storey(s) carry walls or columns but no floor plate, because their slab " +
                $"edges would not close: {string.Join(", ", plateless)}. Those storeys have no diaphragm " +
                "until a plate is added.");

        var sections = wallProps.Values.Concat(slabProps.Values).Concat(frameProps.Values).Concat(roundProps.Values).ToList();
        return new ComposeSummary(
            wallCounter, colCounter, floorCounter, pointCounter,
            placements.Select(p => p.Story.Name).Distinct().Count(),
            sections, flags);
    }

    /// <summary>A diaphragm name for one storey, kept short and legible in ETABS.</summary>
    private static string DiaphragmFor(string storyName, string prefix)
    {
        var cleaned = new string(storyName.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (cleaned.Length > 12) cleaned = cleaned[^12..];
        return $"{prefix}D-{cleaned}";
    }

    /// <summary>Shortest distance from a point to a line segment.</summary>
    private static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return p.DistanceTo(a);

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return p.DistanceTo(new DxfPoint(a.X + dx * t, a.Y + dy * t));
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


