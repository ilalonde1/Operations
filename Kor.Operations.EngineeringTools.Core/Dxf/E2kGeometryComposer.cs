using System.Globalization;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One drawing's geometry, placed on one storey of the model.</summary>
public sealed record StoryPlacement(StoryLevel Story, PlanGeometrySet Geometry, string SourceSheet);

/// <summary>
/// The ground something covers on plan. Enough to answer "is this plate under those members",
/// which is the question that stops a floor being borrowed from a different building.
/// </summary>
public readonly record struct Extent(double MinX, double MinY, double MaxX, double MaxY)
{
    public static Extent At(double x, double y) => new(x, y, x, y);

    public Extent With(double x, double y)
        => new(Math.Min(MinX, x), Math.Min(MinY, y), Math.Max(MaxX, x), Math.Max(MaxY, y));

    /// <summary>
    /// How alike two footprints are, 0 to 1 — shared ground over combined ground.
    ///
    /// Coverage alone chooses badly. C-LEVEL 3 is the mid-rise and the storey below it is the
    /// ground floor, whose plate spans the whole site and therefore covers the mid-rise
    /// completely — so "the nearest plate below that stands under these members" handed a
    /// 206x73 ft building a 336x237 ft floor, out over the ground the towers stand on. Likeness
    /// says what coverage cannot: the floor a storey should borrow is the one shaped like it.
    ///
    /// Zero width or height is given a nominal foot first, for the same reason <see cref="CoverageOf"/>
    /// treats it as inside: a storey whose columns happen to sit in a straight line has an extent
    /// with no area, every candidate then scores zero, they all tie, and the choice falls silently
    /// back to "nearest" — which is the rule this replaced.
    /// </summary>
    public double LikenessTo(Extent other)
    {
        const double Nominal = 12.0;
        var mineBox = Fattened(this, Nominal);
        var theirsBox = Fattened(other, Nominal);

        double w = Math.Min(mineBox.MaxX, theirsBox.MaxX) - Math.Max(mineBox.MinX, theirsBox.MinX);
        double h = Math.Min(mineBox.MaxY, theirsBox.MaxY) - Math.Max(mineBox.MinY, theirsBox.MinY);
        if (w <= 0 || h <= 0) return 0;

        double shared = w * h;
        double mine = (mineBox.MaxX - mineBox.MinX) * (mineBox.MaxY - mineBox.MinY);
        double theirs = (theirsBox.MaxX - theirsBox.MinX) * (theirsBox.MaxY - theirsBox.MinY);
        return shared / (mine + theirs - shared);
    }

    private static Extent Fattened(Extent e, double least)
    {
        double padX = Math.Max(0, least - (e.MaxX - e.MinX)) / 2;
        double padY = Math.Max(0, least - (e.MaxY - e.MinY)) / 2;
        return new Extent(e.MinX - padX, e.MinY - padY, e.MaxX + padX, e.MaxY + padY);
    }

    /// <summary>
    /// How much of <paramref name="other"/>'s footprint this one covers, 0 to 1.
    ///
    /// Zero width or height is inside, not outside. A storey holding one column has an extent with
    /// no area at all, and treating that as no overlap says the plate below does not stand under
    /// it — which is exactly backwards, and would leave the storeys with least structure the ones
    /// least likely to get a floor.
    /// </summary>
    public double CoverageOf(Extent other)
    {
        double w = Math.Min(MaxX, other.MaxX) - Math.Max(MinX, other.MinX);
        double h = Math.Min(MaxY, other.MaxY) - Math.Max(MinY, other.MinY);
        if (w < 0 || h < 0) return 0;

        const double Thin = 1e-9;
        double area = Math.Max(other.MaxX - other.MinX, Thin) * Math.Max(other.MaxY - other.MinY, Thin);
        return Math.Min(1.0, Math.Max(w, Thin) * Math.Max(h, Thin) / area);
    }
}

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
    /// Where a storey has walls and columns but no floor, carry up the plate from the storey below.
    ///
    /// Not a tolerance workaround. On 31168 the Level 1, mezzanine and C-Level 3 sheets have no
    /// closed slab outline to read: JBP_C_SLABEDG spans the whole 334x235 ft footprint but arrives
    /// as sixty-odd open chains, and at every gap from 0.05" to 72" the largest region it encloses
    /// is 119 sq ft. There is nothing there to close, so no tolerance produces that floor, and four
    /// storeys stood with members and nothing spanning between them.
    ///
    /// A ground floor over a parkade has the parkade's extent, so the plate below is the honest
    /// stand-in and an engineer can drag its edges. It is INFERRED, not read, so it is reported as
    /// such and the storey is named -- a plate she cannot tell from a measured one is worse than
    /// the hole it fills.
    ///
    /// Off by default: a plate that was not drawn is a judgement, and judgements are opt-in.
    /// </summary>
    public bool InferMissingFloors { get; init; }

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
    /// depth is the storey height less the opening height.
    ///
    /// Measured, not assumed. Her own 31138 model carries 29 spandrels, and taking each one's depth
    /// off its storey height gives the opening she drew it for: 86" eleven times and 88" twelve
    /// times, with four taller ones at 98-100" and two at a double-height opening. 88" is her
    /// commonest, and the 84" standard door assumed before it made every header two to four inches
    /// too deep.
    /// </summary>
    public double OpeningHeight { get; init; } = 88.0;

    /// <summary>
    /// Skip a member the model already has at that place on that storey. The output is the
    /// reference model with geometry added, so without this an engineer's own walls and columns
    /// are duplicated by ours — doubling stiffness and self-weight exactly where they overlap.
    /// </summary>
    public bool SkipMembersAlreadyModelled { get; init; } = true;

    /// <summary>The same rules expressed in a different length unit; see PlanClassificationOptions.InUnitOf.</summary>
    public ComposeOptions InUnitOf(double unitInInches)
    {
        double f = 1.0 / unitInInches;
        return this with
        {
            DefaultSlabThickness = DefaultSlabThickness * f,
            OpeningHeight = OpeningHeight * f,
            AlreadyModelledTolerance = AlreadyModelledTolerance * f,
            SpandrelDepthFloor = SpandrelDepthFloor * f,
            SpandrelDepthCeiling = SpandrelDepthCeiling * f,
        };
    }

    /// <summary>How close a generated member must be to an existing one to count as the same (inches).</summary>
    public double AlreadyModelledTolerance { get; init; } = 6.0;

    /// <summary>
    /// How shallow and how deep a generated header may be. The engineer set these herself —
    /// "Bounding can be 18"-60"" — and they are options rather than literals so that answer can
    /// live in KorStandards and change the model without changing this file.
    /// </summary>
    public double SpandrelDepthFloor { get; init; } = 18.0;

    public double SpandrelDepthCeiling { get; init; } = 60.0;
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
        int skippedColumns = 0, skippedWalls = 0, skippedOpenings = 0;

        // Members a second sheet drew in a place a first sheet had already claimed, by storey.
        var droppedAsDuplicate = new List<(string Storey, long X, long Y)>();

        // Closed rings on a slab layer with no structure anywhere under them.
        var orphanPlates = new List<(string Sheet, string Storey, double AreaSqFt)>();

        // Every plate written, by the storey it was written on, with the ground it covers, so one
        // can be carried up to a storey whose own drawing has no closed outline -- and only if it
        // actually sits under that storey. See ComposeOptions.InferMissingFloors.
        var platesByStorey = new Dictionary<string, List<(string Name, string Prop, Extent Where)>>(StringComparer.OrdinalIgnoreCase);

        // Where each storey's own walls and columns stand, for the same reason.
        var memberExtents = new Dictionary<string, Extent>(StringComparer.OrdinalIgnoreCase);

        void Covers(string storeyName, double x, double y)
            => memberExtents[storeyName] = memberExtents.TryGetValue(storeyName, out var had)
                ? had.With(x, y)
                : Extent.At(x, y);

        // Every plan position that carries a wall or a column, from EITHER model, on ANY storey —
        // what a plate has to have some of underneath it to be a floor rather than a drawn box.
        var standing = new List<DxfPoint>();
        foreach (var p in placements)
        {
            foreach (var w in p.Geometry.Walls)
            {
                standing.Add(new DxfPoint(w.Start.X + options.OffsetX, w.Start.Y + options.OffsetY));
                standing.Add(new DxfPoint(w.End.X + options.OffsetX, w.End.Y + options.OffsetY));
            }
            foreach (var c in p.Geometry.Columns)
                standing.Add(new DxfPoint(c.Center.X + options.OffsetX, c.Center.Y + options.OffsetY));
        }
        foreach (var list in existingWalls.Values)
            foreach (var w in list) { standing.Add(w.A); standing.Add(w.B); }
        foreach (var list in existingColumns.Values)
            standing.AddRange(list);

        bool AnythingStandsUnder(PlanLoop slab, ComposeOptions o)
        {
            double minX = slab.Points.Min(p => p.X) + o.OffsetX, maxX = slab.Points.Max(p => p.X) + o.OffsetX;
            double minY = slab.Points.Min(p => p.Y) + o.OffsetY, maxY = slab.Points.Max(p => p.Y) + o.OffsetY;

            // The bounding box, generously: a plate genuinely carried by structure has some of it
            // well inside, so this only ever rejects a ring that is nowhere near any.
            return standing.Any(p => p.X >= minX - 24 && p.X <= maxX + 24 &&
                                     p.Y >= minY - 24 && p.Y <= maxY + 24);
        }

        var pointNames = new Dictionary<(long, long, long), string>();
        var placedSlabs = new HashSet<(long, long, string)>();
        var placedColumns = new HashSet<(long, long, long, long, string)>();
        var placedWalls = new HashSet<(long, long, long, long, string)>();
        var storeysWithMembers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        // Seeded with the storeys the engineer's own model already floors. "No floor plate" has to
        // mean no floor from anyone, or a gap-fill project reports her whole building as missing.
        var storeysWithPlates = doc.StoreysWithFloors();
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
            //
            // Dropping it SILENTLY is the part that had to change. A sheet reports the members it
            // contributed, so the LEVEL 35 tower A plan reported five walls and two columns while
            // the model got none of them: another sheet had already claimed those places and this
            // returned empty without a word. Reading the report, the walls are there. Reading the
            // model, tower A's core stops below its top storey. Whichever of the two is right, the
            // tool must not be the one keeping it quiet.
            if (spanned.Any(s => taken.Contains((where.Item1, where.Item2, where.Item3, where.Item4, s))))
            {
                droppedAsDuplicate.Add((story.Name, where.Item1, where.Item2));
                return new List<string>();
            }

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
                // ONE panel for the whole storey the tower actually has, not one per storey in the
                // model's global list.
                //
                // The engineer's instruction, with a drawing of it: "when modelling the walls of
                // tower B he should ignore tower A elevation system. The walls should not break at
                // tower A elevations." In a site model the storey list interleaves both towers, so
                // tower B's wall from B-LEVEL 33 to B-LEVEL 34 crosses A-LEVEL 34 on the way.
                // Assigning it to every storey it crosses builds it as a stack of separate panels
                // with a mesh break at each — she drew that as "how it is now" against "how it
                // should be", one panel from floor to floor.
                //
                // The storey offset in the panel's own connectivity is the mechanism: 1 spans one
                // storey of the list, so N spans N. The placement storey is always the top of the
                // span, because a storey's floor is its OWN tower's previous level.
                int wallSpan = wallStoreys.Count;
                string name = NextName("W", ref wallCounter);
                areaLines.Add($"  AREA \"{name}\"  PANEL  4  \"{pa}\"  \"{pb}\"  \"{pb}\"  \"{pa}\"  " +
                              $"{wallSpan}  {wallSpan}  0  0");

                // Every storey the wall is ASSIGNED to, not the one it was placed on. A storey that
                // carries structure only because a neighbour's wall spans into it is still a storey
                // carrying structure, and recording the placement alone hid A-LEVEL 1 from the list
                // of storeys left without a floor — 50 walls and 65 columns, reported as nothing.
                // Every storey the wall passes through still counts as carrying structure, even
                // though only one of them holds the assignment now — a storey a wall runs through
                // is not an empty storey.
                foreach (string on in wallStoreys)
                {
                    storeysWithMembers.Add(on);
                    Covers(on, wall.Start.X + options.OffsetX, wall.Start.Y + options.OffsetY);
                    Covers(on, wall.End.X + options.OffsetX, wall.End.Y + options.OffsetY);
                }

                string pier = options.AssignPierLabels ? $"  PIER  \"{PierFor(x1, y1, x2, y2)}\"" : string.Empty;
                areaAssigns.Add(
                    $"  AREAASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{propName}\"{pier}  OBJMESHTYPE \"DEFAULT\"  " +
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
                // "same for columns" — one column from floor to floor of its own tower, spanning
                // the other tower's storeys rather than being cut at each of them.
                int colSpan = colStoreys.Count;
                string name = NextName("C", ref colCounter);
                lineLines.Add($"  LINE  \"{name}\"  COLUMN  \"{at}\"  \"{at}\"  {colSpan}");
                foreach (string on in colStoreys)
                {
                    storeysWithMembers.Add(on);
                    Covers(on, column.Center.X + options.OffsetX, column.Center.Y + options.OffsetY);
                }
                lineAssigns.Add(
                    $"  LINEASSIGN  \"{name}\"  \"{story.Name}\"  SECTION \"{sectionName}\"  ANG {Trim(angle)} MINNUMSTA 3 " +
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
            // deepest spandrels her own 31138 model uses. Measured off that model: 29 spandrels
            // running 20" to 60". The engineer then set the range herself: "Bounding can be 18-60". Without the
            // ceiling a double-height storey produced a 396"-deep header, which is a wall.
            double storeyHeight = story.Elevation - story.ElevationBelow;
            double spandrelDepth = SnapInch(Math.Clamp(storeyHeight - options.OpeningHeight, options.SpandrelDepthFloor, options.SpandrelDepthCeiling));

            foreach (var opening in placement.Geometry.WallOpenings)
            {
                double thickness = SnapHalfInch(opening.Thickness);
                double sx = opening.Start.X + options.OffsetX, sy = opening.Start.Y + options.OffsetY;
                double ex = opening.End.X + options.OffsetX, ey = opening.End.Y + options.OffsetY;

                // Deduplicated the same way as a wall, and for the same reason: the key has to be
                // every storey the header is assigned to, not the storey it was placed on. Keying on
                // the placement storey put two headers over one opening on 31168 — one from
                // B-LEVEL 32 and one from A-LEVEL 33, both spanning A-LEVEL 33, at different depths
                // because the two storeys are different heights.
                var span = new[] { ((long)Math.Round(sx), (long)Math.Round(sy)),
                                   ((long)Math.Round(ex), (long)Math.Round(ey)) }
                    .OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
                var headerWhere = (span[0].Item1, span[0].Item2, span[1].Item1, span[1].Item2);
                var headerStoreys = FreeStoreysFor(story, headerWhere, placedSpandrels);
                if (headerStoreys.Count == 0) continue;

                string lowA = PointAt(sx, sy);
                string lowB = PointAt(ex, ey);
                if (lowA == lowB) continue;
                string highA = PointAt(sx, sy, spandrelDepth);
                string highB = PointAt(ex, ey, spandrelDepth);

                string headerSection = WallSection(thickness);
                string spandrel = SpandrelFor(sx, sy, ex, ey);
                string name = NextName("S", ref spandrelCounter);
                areaLines.Add($"  AREA \"{name}\"  PANEL  4  \"{highA}\"  \"{highB}\"  \"{lowB}\"  \"{lowA}\"  0  0  0  0");
                foreach (string on in headerStoreys)
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

                // A floor stands on something. A closed ring on a slab layer with no wall and no
                // column anywhere inside it — ours or the engineer's, on any storey — is not a
                // floor plate; it is a legend panel, a detail box, or a key plan that happens to
                // be drawn on that layer.
                //
                // 31138 grew one when the reader learned to read blocks: a 1,758 sq ft plate on
                // L01 sitting entirely outside the building, x -451 to -178 where every other
                // plate in the model runs 18 to 2,082, with nothing beneath it anywhere. Area
                // alone could not catch it — at 1,758 sq ft it is bigger than three real floors
                // in the same model.
                if (!AnythingStandsUnder(slab, options))
                {
                    orphanPlates.Add((placement.SourceSheet, story.Name, Math.Round(Math.Abs(slab.SignedArea) / 144.0)));
                    continue;
                }

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

                if (!platesByStorey.TryGetValue(story.Name, out var onThisStorey))
                    platesByStorey[story.Name] = onThisStorey = new List<(string, string, Extent)>();

                var plateExtent = Extent.At(slab.Points[0].X + options.OffsetX, slab.Points[0].Y + options.OffsetY);
                foreach (var p in slab.Points) plateExtent = plateExtent.With(p.X + options.OffsetX, p.Y + options.OffsetY);
                onThisStorey.Add((name, propName, plateExtent));
            }

            // Shafts and stair openings, cut out of the plate rather than left for the engineer.
            // ETABS models an opening as an area carrying no section, which is how her own 31138
            // model does it — 42 of them, drawn by hand.
            // An opening is a hole in a plate, so there has to be a plate for it to be a hole in.
            // A storey whose slab edges never closed gets no plate — and used to get its shafts
            // cut anyway, leaving an opening bounding nothing at all. 31138's L05 shipped with two.
            bool storeyHasAPlate = storeysWithPlates.Contains(story.Name);

            foreach (var opening in placement.Geometry.Openings)
            {
                if (!storeyHasAPlate) { skippedOpenings++; continue; }

                // IN PERIMETER ORDER, or ETABS refuses the area and ignores its assign.
                //
                // The loop's points do not arrive walked round the shape, so writing them as they
                // come produced polygons that cross themselves. 31168's KO3 went down the left
                // edge, across the bottom, then jumped back to the middle of the left edge before
                // heading right. ETABS said "Area Object KO4 not correctly defined" and threw the
                // opening away. Four-point openings survived by luck; every six-point one did not.
                // Found by importing a model, which is the only thing that could have found it.
                var ordered = InPerimeterOrder(opening.Points);
                var names = ordered
                    .Select(p => PointAt(p.X + options.OffsetX, p.Y + options.OffsetY))
                    .Distinct()
                    .ToList();
                if (names.Count < 3) continue;

                // Still crossing after ordering is not a shape this can write. Skipping it and
                // saying so beats emitting geometry ETABS discards silently.
                if (SelfIntersects(ordered)) { skippedOpenings++; continue; }

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

            // One complaint per sheet, however many storeys that sheet builds. A sheet covering a
            // range fills seven storeys and its drawing faults are the same seven times over; the
            // engineer would read the same line seven times, and a count of them would say seven
            // outlines were lost where one was.
            foreach (string flag in placement.Geometry.Flags)
            {
                string message = $"{placement.SourceSheet}: {flag}";
                if (!flags.Contains(message)) flags.Add(message);
            }
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

        // Carry a plate up to a storey whose own drawing has no closed outline to read. Assigning
        // the plate below to this storey as well is how ETABS itself repeats a member up a
        // building — one object, one assign per storey — so it needs no new geometry, only its own
        // diaphragm, which must not be shared across elevations.
        //
        // This has to run BEFORE the sections below are appended to the document. Written after
        // them, the assigns and diaphragms are built, counted, and reported in a flag that says
        // four storeys were given a floor — and none of it reaches the file.
        var inferredPlates = new List<(string Storey, string From)>();
        if (options.InferMissingFloors && options.IncludeFloors)
        {
            foreach (var storey in allStories)
            {
                if (!storeysWithMembers.Contains(storey.Name)) continue;
                if (storeysWithPlates.Contains(storey.Name)) continue;

                // The nearest storey BELOW whose plate actually stands under these members. Below,
                // not nearest either way: a floor is carried by what is under it, and reaching
                // downward cannot borrow from a tower above.
                //
                // "Nearest below" alone is not enough on a site model, and taking it produced a
                // visibly wrong floor: C-LEVEL 3 is the mid-rise at y 316-422 ft, the storey below
                // it is LEVEL 2 whose plate is the podium under the TOWERS at y 213-308, and the
                // mid-rise was handed a floor standing somewhere it is not. A donor has to cover
                // the ground this storey's own walls and columns stand on.
                if (!memberExtents.TryGetValue(storey.Name, out var standingOn)) continue;

                // The plate SHAPED like this storey, not merely the nearest one under it.
                //
                // Nearest-below covers the ordinary case and fails the interesting one: C-LEVEL 3
                // is the mid-rise, the storey beneath it is the ground floor whose plate spans the
                // whole site, and that plate covers the mid-rise completely. So it was chosen, and
                // a 206x73 ft building was given a 336x237 ft floor reaching out over the ground
                // the towers stand on.
                //
                // Above is allowed too. A mid-rise floor with no slab edge drawn looks like the
                // floor above it far more than like the podium below, and C-LEVEL 4's plate is the
                // right answer for C-LEVEL 3.
                //
                // Likeness picks the shortlist, not the winner. Taken raw it separates a 206.1x72.7
                // plate from a 206.6x71.9 one and decides on eight inches, which sent C-LEVEL 3 six
                // storeys up to borrow from C-LEVEL 9 instead of from C-LEVEL 4 directly above it.
                // Two plates that agree on the building to within inches are the same answer.
                //
                // The margin is relative, not a rounding. Rounded to a hundredth, a job whose plates
                // are large next to the members standing on them scores every candidate 0.00, every
                // candidate ties, and the choice collapses back to "nearest" -- which is the bug
                // this whole block exists to undo.
                //
                // Among the equally alike, the NEAREST storey wins: its slab edge is the least
                // likely to have moved. Equally near, below wins, because what a floor stands on is
                // still the better guess.
                var candidates = allStories
                    .Where(s => !s.Name.Equals(storey.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(s => (Storey: s, Plates: platesByStorey.TryGetValue(s.Name, out var p)
                        ? p.Where(x => x.Where.CoverageOf(standingOn) >= 0.5).ToList()
                        : new List<(string Name, string Prop, Extent Where)>()))
                    .Where(x => x.Plates.Count > 0)
                    .Select(x => (x.Storey, x.Plates, Likeness: x.Plates.Max(pl => pl.Where.LikenessTo(standingOn))))
                    .ToList();
                if (candidates.Count == 0) continue;

                double bestLikeness = candidates.Max(c => c.Likeness);
                var donor = candidates
                    .Where(c => c.Likeness >= bestLikeness * 0.98)
                    .OrderBy(c => Math.Abs(c.Storey.Elevation - storey.Elevation))
                    .ThenBy(c => c.Storey.Elevation < storey.Elevation ? 0 : 1)
                    .First();

                string inferredDiaphragm = string.Empty;
                if (options.AssignDiaphragms)
                {
                    string diaphragm = DiaphragmFor(storey.Name, prefix);
                    diaphragms.Add(diaphragm);
                    inferredDiaphragm = $"  DIAPH \"{diaphragm}\"";
                }

                foreach (var (plate, prop, _) in donor.Plates)
                    areaAssigns.Add(
                        $"  AREAASSIGN  \"{plate}\"  \"{storey.Name}\"  SECTION \"{prop}\"  OBJMESHTYPE \"DEFAULT\"" +
                        $"{inferredDiaphragm}  CARDINALPOINT \"MIDDLE\"");

                storeysWithPlates.Add(storey.Name);
                inferredPlates.Add((storey.Name, donor.Storey.Name));

                // A borrowed plate is this storey's floor for every purpose after this, the
                // coverage check below included.
                if (!platesByStorey.TryGetValue(storey.Name, out var borrowed))
                    platesByStorey[storey.Name] = borrowed = new List<(string, string, Extent)>();
                borrowed.AddRange(donor.Plates);
            }

            if (inferredPlates.Count > 0)
                flags.Add(
                    $"{inferredPlates.Count} storey(s) were given a floor plate they were not drawn one for, " +
                    "copied from the storey whose own plate is closest in shape to what stands on them: " +
                    string.Join(", ", inferredPlates.Select(p => $"{p.Storey} (from {p.From})")) +
                    ". These plates are INFERRED, not measured — the drawings for those storeys carry no " +
                    "closed slab outline — so check their edges before relying on them.");
        }

        // A storey can hold a floor and still have most of its structure standing under open air.
        // "Has a plate" is the only question asked until now, and 31168 answers it yes on LEVEL 2
        // -- whose plate reaches 96 ft of a 206 ft spread of walls and columns, because only the
        // towers' podium closed and the mid-rise half of the level did not. Nothing in the package
        // said so. It took rendering the storey and looking at it, which is the fault this module
        // keeps repeating: the count agrees, the model is wrong, and only a picture disagrees.
        //
        // Reported, not fixed. A mezzanine really is a small floor in a big room, and a podium
        // really does stop where the tower begins; which of those this is belongs to the engineer.
        // The number is what she cannot get from a count.
        var thinlyFloored = new List<(string Storey, int Percent)>();
        foreach (var storey in allStories)
        {
            if (!memberExtents.TryGetValue(storey.Name, out var standingOn)) continue;
            if (!platesByStorey.TryGetValue(storey.Name, out var plates) || plates.Count == 0) continue;

            var spanned = plates[0].Where;
            foreach (var plate in plates.Skip(1))
                spanned = spanned.With(plate.Where.MinX, plate.Where.MinY).With(plate.Where.MaxX, plate.Where.MaxY);

            double covered = spanned.CoverageOf(standingOn);
            if (covered < 0.6) thinlyFloored.Add((storey.Name, (int)Math.Round(covered * 100)));
        }

        if (thinlyFloored.Count > 0)
            flags.Add(
                "Floor does not reach the structure on " + thinlyFloored.Count + " storey(s): " +
                string.Join(", ", thinlyFloored.Select(t => $"{t.Storey} ({t.Percent}% of the ground its own " +
                                                            "walls and columns cover)")) +
                ". Those storeys have a plate, so nothing here says the floor is missing — it says the drawn " +
                "slab edge stops well short of the members. A mezzanine or a podium that ends where a tower " +
                "begins reads exactly like this and is correct; a slab edge that failed to close does too, and " +
                "is not.");

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

        if (orphanPlates.Count > 0)
            flags.Add(
                $"{orphanPlates.Count} closed slab outline(s) were not made into floor plates, because no wall " +
                $"or column stands anywhere under them — a legend or detail box drawn on a slab layer looks " +
                $"exactly like a floor otherwise: " +
                string.Join("; ", orphanPlates.Take(4).Select(p => $"{p.Storey} ({p.AreaSqFt:N0} sq ft, {p.Sheet})")) + ".");

        if (skippedOpenings > 0)
            flags.Add($"{skippedOpenings} shaft or stair opening(s) were not cut, because the storey they are " +
                      "drawn on has no floor plate to cut them from. They come back with the plate.");

        // Two sheets drawing the same member is normal and one of them has to give way. Saying
        // WHICH storeys lost members that way is what makes the sheet counts readable: a plan can
        // report members it contributed and put none of them in the model, and without this line
        // the only way to find out is to go looking in the model for something that is not there.
        if (droppedAsDuplicate.Count > 0)
        {
            var byStorey = droppedAsDuplicate
                .GroupBy(d => d.Storey)
                .OrderByDescending(g => g.Count())
                .ToList();

            flags.Add(
                $"{droppedAsDuplicate.Count} member(s) were drawn on a sheet in a place another sheet had " +
                $"already filled, and were not added a second time — " +
                string.Join(", ", byStorey.Take(6).Select(g => $"{g.Key} ({g.Count()})")) +
                (byStorey.Count > 6 ? $", and {byStorey.Count - 6} more storey(s)" : string.Empty) +
                ". Where a storey shows more members drawn than modelled, this is why.");
        }

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

        // And the reverse, which reads as wrong even faster: a floor with nothing under it. A plate
        // is carried by the walls and columns assigned to its own storey, so a storey holding a
        // plate and no members is a slab supported by air.
        //
        // It is not a geometry error the tool can fix — it means the plan that would have drawn
        // that storey's walls has none on it, and only the engineer knows whether the structure
        // really stops there or the sheet is not the one to read. 31168's building C roof is the
        // case: a roof plate, and no wall or column beneath it anywhere.
        var unsupported = storeysWithPlates
            .Where(s => !storeysWithMembers.Contains(s))
            .Where(s => !existingWalls.ContainsKey(s) && !existingColumns.ContainsKey(s))
            .ToList();

        if (unsupported.Count > 0)
            flags.Add(
                $"{unsupported.Count} storey(s) carry a floor plate with no wall or column beneath it: " +
                $"{string.Join(", ", unsupported)}. The plan placed there draws no vertical structure, so " +
                "either the structure stops below that level or another sheet holds it.");

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

    /// <summary>
    /// The vertices walked round the shape, by angle about the centroid.
    ///
    /// A closed outline knows its corners but not the order to visit them in, and an area written
    /// in the wrong order is a polygon that crosses itself. Exact for any convex shape, which is
    /// what a shaft or a stair opening is; anything concave is checked by the caller rather than
    /// trusted.
    /// </summary>
    private static List<DxfPoint> InPerimeterOrder(IReadOnlyList<DxfPoint> points)
    {
        if (points.Count < 3) return points.ToList();
        double cx = points.Average(p => p.X), cy = points.Average(p => p.Y);
        return points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
    }

    /// <summary>Whether any two non-adjacent edges of the closed polygon cross.</summary>
    private static bool SelfIntersects(IReadOnlyList<DxfPoint> polygon)
    {
        int n = polygon.Count;
        if (n < 4) return false;

        for (int i = 0; i < n; i++)
        {
            DxfPoint a1 = polygon[i], a2 = polygon[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                // Adjacent edges share a vertex; touching there is not crossing.
                if ((j + 1) % n == i || j == (i + 1) % n) continue;
                if (Crosses(a1, a2, polygon[j], polygon[(j + 1) % n])) return true;
            }
        }

        return false;

        static bool Crosses(DxfPoint p1, DxfPoint p2, DxfPoint q1, DxfPoint q2)
        {
            double d1 = Side(q1, q2, p1), d2 = Side(q1, q2, p2);
            double d3 = Side(p1, p2, q1), d4 = Side(p1, p2, q2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        static double Side(DxfPoint a, DxfPoint b, DxfPoint c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static double SnapHalfInch(double value) => Math.Round(value * 2.0, MidpointRounding.AwayFromZero) / 2.0;
    private static double SnapInch(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    private static string Trim(double value) => value.ToString("0.###", Inv);
    private static string F(double value) => value.ToString("0.####", Inv);
}


