using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public enum ModelViolationSeverity
{
    Advisory,
    Fatal
}

/// <summary>One thing wrong with a finished model, in the words the reader needs.</summary>
public sealed record ModelViolation(
    string Rule,
    string What,
    string Where,
    ModelViolationSeverity Severity = ModelViolationSeverity.Fatal)
{
    public bool BlocksPublishing => Severity == ModelViolationSeverity.Fatal;
}

/// <summary>
/// What must be true of a model before it is allowed to reach an engineer.
///
/// Every fault this checks for actually shipped, or came within one publish of shipping, on 31168
/// between 15 and 24 August. Every one of them passed the counts in the report. Every one was
/// found because somebody happened to look, and the ones found late were found by the engineer.
///
///   eight storeys of a building she had said was out of scope
///   a wall 132 inches thick
///   four members the reference model contributed that no drawing produced
///   a floor plate 336x237 ft on a building 206x73 ft
///   six spandrels 1.7 inches tall
///   a floor outline that closed through itself and rendered as an hourglass
///   five pairs of joints four thousandths of an inch apart, where walls should have joined
///   three of tower B's headers on a tower A storey
///
/// The report FLAGS things, which informs. This REFUSES, which is different, and the difference is
/// the whole point: a flag depends on somebody reading it, and the person reading it is usually the
/// person who already believes the model is right.
///
/// It reads the finished .e2k as text rather than any in-memory state, for the same reason the
/// document gates do: the file that ships is the only thing worth checking.
/// </summary>
public static class ShippedModelInvariants
{
    private static readonly Regex Point = new(@"^\s*POINT\s+""([^""]+)""\s+(-?[\d.]+)\s+(-?[\d.]+)(?:\s+(-?[\d.]+))?\s*$", RegexOptions.Compiled);
    private static readonly Regex AreaLine = new(@"^\s*AREA\s+""([^""]+)""\s+(\w+)\s+\d+\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex LineLine = new(@"^\s*LINE\s+""([^""]+)""\s+(\w+)\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Assign = new(@"^\s*(AREAASSIGN|LINEASSIGN)\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Story = new(@"^\s*STORY\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"""([^""]+)""", RegexOptions.Compiled);

    /// <param name="lines">The finished model, as written.</param>
    /// <param name="jointTolerance">Two joints closer than this are one joint. See dxf.joint-merge-tolerance.</param>
    /// <param name="droppedStoreys">Storeys the run was told to leave out; none of them may appear.</param>
    /// <param name="referenceE2k">
    /// The model this one was built into, when there is one. A job where the engineer has already
    /// modelled part of the building is a GAP-FILL: her objects are carried through into the
    /// output, and they are not this tool's to judge. Without this, 31138 fails 514 checks and
    /// every one of them is her work -- 361 objects "not from a drawing" because they carry her
    /// names rather than a K prefix, and 153 members "on two storeys" because she models a column
    /// running P3 to L01 as one member, which is correct and is what those rules exist to stop US
    /// doing. Give it and the rules apply to what this tool built; leave it out and everything in
    /// the file is treated as ours, which is right for a model built on an empty shell.
    /// </param>
    /// <param name="foundationStoreys">Storeys matched from FOUNDATION sheets, where S.O.G. is not a diaphragm.</param>
    public static IReadOnlyList<ModelViolation> Check(
        IEnumerable<string> lines,
        double jointTolerance = 0.05,
        IEnumerable<string>? droppedStoreys = null,
        IEnumerable<string>? referenceE2k = null,
        IEnumerable<string>? foundationStoreys = null,
        IEnumerable<string>? reportLines = null,
        IEnumerable<string>? workbookText = null)
    {
        var reportText = reportLines?.ToList();
        var workbookLines = workbookText?.ToList();
        var foundation = new HashSet<string>(foundationStoreys ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var openings = new HashSet<string>(StringComparer.Ordinal);
        var carriedThrough = new HashSet<string>(StringComparer.Ordinal);
        var carriedThroughPoints = new HashSet<string>(StringComparer.Ordinal);
        if (referenceE2k is not null)
            foreach (string raw in referenceE2k)
            {
                var asPoint = Point.Match(raw);
                if (asPoint.Success) { carriedThroughPoints.Add(asPoint.Groups[1].Value); continue; }
                var asArea = AreaLine.Match(raw);
                if (asArea.Success) { carriedThrough.Add(asArea.Groups[1].Value); continue; }
                var asLine = LineLine.Match(raw);
                if (asLine.Success) carriedThrough.Add(asLine.Groups[1].Value);
            }

        var v = new List<ModelViolation>();

        var pts = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.Ordinal);
        var kind = new Dictionary<string, string>(StringComparer.Ordinal);
        var joints = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var onStoreys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var storeys = new List<string>();

        foreach (string raw in lines)
        {
            var m = Point.Match(raw);
            if (m.Success)
            {
                pts[m.Groups[1].Value] = (
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    m.Groups[4].Success ? double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0);
                continue;
            }

            m = AreaLine.Match(raw);
            if (m.Success)
            {
                kind[m.Groups[1].Value] = m.Groups[2].Value;
                joints[m.Groups[1].Value] = Quoted.Matches(m.Groups[3].Value).Select(x => x.Groups[1].Value).ToList();
                continue;
            }

            m = LineLine.Match(raw);
            if (m.Success)
            {
                kind[m.Groups[1].Value] = m.Groups[2].Value;
                joints[m.Groups[1].Value] = new List<string> { m.Groups[3].Value, m.Groups[4].Value };
                continue;
            }

            m = Assign.Match(raw);
            if (m.Success)
            {
                if (raw.Contains("OPENING", StringComparison.OrdinalIgnoreCase))
                    openings.Add(m.Groups[2].Value);
                if (!onStoreys.TryGetValue(m.Groups[2].Value, out var list))
                    onStoreys[m.Groups[2].Value] = list = new List<string>();
                if (!list.Contains(m.Groups[3].Value, StringComparer.OrdinalIgnoreCase)) list.Add(m.Groups[3].Value);
                continue;
            }

            m = Story.Match(raw);
            if (m.Success) storeys.Add(m.Groups[1].Value);
        }

        // 0. The report and workbook ship beside the model, so their numbers and storey names are
        //    part of the deliverable. They must describe this file, not the pre-cut composition.
        // MEMBERS, NOT LABELS — the same number the report states.
        //
        // This counted connectivity rows, which is one per OBJECT. That is the same answer only
        // while the generator writes a fresh object per storey. Once a member carries one label its
        // whole height — the engineer's own convention, one object with an assign on every storey
        // it rises through — 744 columns become 238 rows, and this check fails a model that is
        // perfectly correct, because the report is counting the building and this was counting
        // names.
        int CountGenerated(string prefix, string objectKind) =>
            kind.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)
                            && x.Value.Equals(objectKind, StringComparison.OrdinalIgnoreCase))
                .Sum(x => onStoreys.TryGetValue(x.Key, out var on) ? on.Count : 0);

        // STOREYS THE BUILDING HAS, not rows in the list. The base is a datum, not a floor: it
        // carries an elevation and nothing stands on it, and the report has always counted it out.
        // Counting it in here made every model fail this check by exactly one.
        int modelStoreys = storeys.Count(s => !s.Equals("Base", StringComparison.OrdinalIgnoreCase));
        int modelWalls = CountGenerated("KW", "PANEL");
        int modelColumns = CountGenerated("KC", "COLUMN");
        int modelFloors = CountGenerated("KF", "FLOOR");
        int modelJoints = pts.Keys.Count(p => p.StartsWith("KP", StringComparison.Ordinal));

        if (reportText is not null)
            CheckReportNumbers(reportText, modelStoreys, modelWalls, modelColumns, modelFloors, modelJoints, v);

        CheckStoreyNames(reportText, storeys, "report", v);
        CheckStoreyNames(workbookLines, storeys, "workbook", v);

        var referencedJointNames = joints.Values
            .SelectMany(x => x)
            .ToHashSet(StringComparer.Ordinal);
        var orphanGenerated = pts.Keys
            .Where(p => p.StartsWith("KP", StringComparison.Ordinal)
                        && !referencedJointNames.Contains(p)
                        && !carriedThroughPoints.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (orphanGenerated.Count > 0)
            v.Add(new ModelViolation(
                "orphan-generated-joint",
                $"{orphanGenerated.Count} generated joint(s) are defined but not referenced by any generated object",
                string.Join(", ", orphanGenerated.Take(8))));

        // 1. A storey the run was told to drop must not be in the finished file. Eight tower storeys
        //    reached an engineer because the cut was by elevation and could not see them.
        var dropped = (droppedStoreys ?? Array.Empty<string>()).ToList();
        foreach (string d in dropped)
            if (storeys.Contains(d, StringComparer.OrdinalIgnoreCase))
                v.Add(new ModelViolation("storey-cut", $"'{d}' was to be dropped and is in the model", d));

        // 2. Nothing in the model may come from anywhere but a drawing. Generated objects carry the
        //    K prefix; anything else is the reference model's, and four of those were circled in
        //    ETABS with "these are not walls".
        foreach (var (name, _) in kind)
            if (!name.StartsWith("K", StringComparison.Ordinal) && !carriedThrough.Contains(name))
                v.Add(new ModelViolation("not-from-a-drawing", $"object '{name}' did not come from a drawing", name));

        // 3. A storey carrying members with no floor is a disclosure, not a refusal. Andrea has
        //    now rejected the alternative twice: borrowing made false slabs look like measured
        //    ones. A missing diaphragm an engineer can add beats a fabricated one she has to notice
        //    is wrong, so this stays in the invariant list but no longer blocks publishing.
        var floors = kind.Where(x => x.Value.Equals("FLOOR", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var withMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withFloor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (obj, sts) in onStoreys)
            foreach (string st in sts)
                (floors.Contains(obj) ? withFloor : withMembers).Add(st);

        foreach (string st in withMembers)
            // Narrow S.O.G. exception: only storeys that the DXF sheet matcher marked as coming
            // from FOUNDATION sheets are allowed to carry members without a floor plate. Andrea
            // Neuviale's 25 August answer on LEVEL P3 was explicit: "There is only a slab-on-grade
            // (S.O.G on our drawings) at P3 but we don't model those." A slab on grade is not a
            // suspended diaphragm, so this invariant is wrong there and still right everywhere else.
            if (!withFloor.Contains(st) && !foundation.Contains(st))
                v.Add(new ModelViolation(
                    "storey-with-no-floor",
                    $"'{st}' carries members and has no floor plate, so it has no diaphragm until a plate is added",
                    st,
                    ModelViolationSeverity.Advisory));

        // 3b. AN OPENING BIGGER THAN THE FLOOR AROUND IT IS THE FAULT SHE REJECTED THE MODEL FOR.
        //
        //     "on several levels (9, 3, mezz, 1) he inverted slab and opening" — 25 August, with a
        //     region marked SHOULD BE SLAB. Every count in that model was healthy. Nothing in the
        //     report said a floor had been turned inside out, because nothing measured a hole
        //     against the plate it was cut from.
        //
        //     The classifier now refuses the two shapes that produced it — a slab edge drawn twice,
        //     and one floor read twice — but a refusal in the reader is not a check on the file.
        //     This is the check on the file: every opening, measured against the smallest floor on
        //     a storey it shares that encloses it. Over half is reported; nothing around it at all
        //     is reported, because a hole in nothing is not a hole.
        //
        //     Advisory. A genuine atrium can be large, and 31168 has one — 4,219 sq ft, 34% of
        //     LEVEL 2 — so this discloses rather than refuses. What it will not do again is stay
        //     silent.
        double PolygonArea(IReadOnlyList<string> ring)
        {
            double sum = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                if (!pts.TryGetValue(ring[i], out var a)) return 0;
                if (!pts.TryGetValue(ring[(i + 1) % ring.Count], out var b)) return 0;
                sum += a.Item1 * b.Item2 - b.Item1 * a.Item2;
            }
            return Math.Abs(sum) / 2.0;
        }

        bool Encloses(IReadOnlyList<string> ring, double x, double y)
        {
            bool inside = false;
            for (int i = 0; i < ring.Count; i++)
            {
                if (!pts.TryGetValue(ring[i], out var a)) return false;
                if (!pts.TryGetValue(ring[(i + 1) % ring.Count], out var b)) return false;
                if ((a.Item2 > y) != (b.Item2 > y) &&
                    x < (b.Item1 - a.Item1) * (y - a.Item2) / (b.Item2 - a.Item2) + a.Item1)
                    inside = !inside;
            }
            return inside;
        }

        foreach (string hole in openings)
        {
            if (carriedThrough.Contains(hole)) continue;                  // hers, not ours to judge
            if (!joints.TryGetValue(hole, out var holeRing) || holeRing.Count < 3) continue;

            double holeArea = PolygonArea(holeRing);
            if (holeArea <= 0) continue;

            double cx = 0, cy = 0; int n = 0;
            foreach (string j in holeRing)
                if (pts.TryGetValue(j, out var q)) { cx += q.Item1; cy += q.Item2; n++; }
            if (n == 0) continue;
            cx /= n; cy /= n;

            onStoreys.TryGetValue(hole, out var holeStoreys);
            double smallest = double.MaxValue;

            foreach (string plate in floors)
            {
                if (!joints.TryGetValue(plate, out var plateRing) || plateRing.Count < 3) continue;
                if (holeStoreys is not null && onStoreys.TryGetValue(plate, out var plateStoreys)
                    && !plateStoreys.Any(st => holeStoreys.Contains(st, StringComparer.OrdinalIgnoreCase)))
                    continue;
                if (!Encloses(plateRing, cx, cy)) continue;

                double a = PolygonArea(plateRing);
                if (a > 0 && a < smallest) smallest = a;
            }

            if (smallest == double.MaxValue)
                v.Add(new ModelViolation("opening-with-no-floor",
                    $"'{hole}' is a {holeArea / 144:N0} sq ft opening with no floor plate around it on its " +
                    "own storey, so it cuts nothing",
                    hole, ModelViolationSeverity.Advisory));
            else if (holeArea > smallest * 0.5)
                v.Add(new ModelViolation("opening-bigger-than-half-its-floor",
                    $"'{hole}' cuts {holeArea / 144:N0} sq ft out of a {smallest / 144:N0} sq ft floor — " +
                    $"{holeArea / smallest:P0} of it. Check it is a hole and not the inner face of the slab edge",
                    hole, ModelViolationSeverity.Advisory));
        }

        // 3c. AN OUTLINE ETABS WILL NOT READ.
        //
        //     KF54 shipped with three joints running down 24 inches along one x and back up 96
        //     along the same one. ETABS said "Area Object KF54 not correctly defined", ignored its
        //     assign, and the floor was absent from the model an engineer opened -- with the report
        //     still counting it. Nothing in this tool could see it: right area, right position, no
        //     coincident joints, no proper self-crossing. Importing the file is what found it.
        //
        //     BLOCKING, not advisory. A plate ETABS refuses is not a plate.
        foreach (var (obj, ring) in joints)
        {
            if (!obj.StartsWith("K", StringComparison.Ordinal)) continue;
            if (carriedThrough.Contains(obj)) continue;
            // FLOORS AND OPENINGS ONLY. A wall panel is four joints -- two at the bottom of a
            // plan line and two at the top -- so in PLAN it is "KP1 KP2 KP2 KP1", a line and not
            // a polygon. Read two-dimensionally every wall in the model doubles back on itself,
            // and the first version of this check called all 1,788 of them broken. What is flat
            // in plan is not flat in the model.
            if (!kind.TryGetValue(obj, out string? k) ||
                !(k.Equals("FLOOR", StringComparison.OrdinalIgnoreCase) ||
                  k.Equals("AREA", StringComparison.OrdinalIgnoreCase))) continue;
            if (ring.Count < 3) continue;

            var shape = new List<DxfPoint>();
            bool complete = true;
            foreach (string j in ring)
            {
                if (!pts.TryGetValue(j, out var q)) { complete = false; break; }
                shape.Add(new DxfPoint(q.Item1, q.Item2));
            }

            if (complete && shape.Count >= 3 && LoopGeometry.HasSpur(shape))
                v.Add(new ModelViolation("outline-doubles-back-on-itself",
                    $"'{obj}' has an outline that doubles back along itself. ETABS refuses it and " +
                    "drops the object without naming it, so the model an engineer opens is missing " +
                    "this one", obj));

            // A PLATE EDGE IS NOT A FLIGHT OF STAIRS.
            //
            // A floor recovered by rasterising linework carries the raster's own steps unless it is
            // straightened, and straightening below the cell size cannot remove them. 31168's
            // LEVEL 2 shipped with 67 segments of exactly 6.0 in alternating vertical, horizontal,
            // vertical along one diagonal edge -- 114 vertices where the drawing has about twenty.
            // Every count in the report was right. The engineer sent a picture of it.
            //
            // Deliberately blunt: a real outline does not spend a quarter of its edges on runs
            // under a foot that alternate direction. Anything that does is a trace, not an outline.
            if (complete && shape.Count >= 12)
            {
                int stairs = 0;
                for (int i = 0; i < shape.Count; i++)
                {
                    var a = shape[i];
                    var b = shape[(i + 1) % shape.Count];
                    var c = shape[(i + 2) % shape.Count];

                    double first = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
                    double next = Math.Abs(c.X - b.X) + Math.Abs(c.Y - b.Y);
                    if (first >= 12.0 || next >= 12.0) continue;

                    bool firstFlat = Math.Abs(b.Y - a.Y) < 0.5;
                    bool nextFlat = Math.Abs(c.Y - b.Y) < 0.5;
                    if (firstFlat != nextFlat) stairs++;
                }

                if (stairs * 4 > shape.Count)
                    v.Add(new ModelViolation("outline-is-a-raster-staircase",
                        $"'{obj}' has {stairs} stair step(s) in a {shape.Count}-point outline — runs " +
                        "under a foot alternating between horizontal and vertical. That is the shape of " +
                        "the raster it was traced from, not the edge the drawing draws, and it is what " +
                        "an engineer sees the moment the model is opened", obj));
            }
        }

        // 4. A member must not belong to two storeys. A floor may -- that is a borrowed plate, and
        //    it is declared. A WALL, COLUMN or header on two storeys is one member counted twice:
        //    six spandrels shipped that way, 1.7 inches tall on the second storey.
        foreach (var (obj, sts) in onStoreys)
            // An OPENING on several storeys is one hole through several floors, which is what it
            // is: a lift shaft does not stop at each slab. The engineer's own model does exactly
            // this -- 31065 carries 359 opening assigns from 25 objects, about fourteen storeys
            // each -- and a borrowed floor here now carries its holes with it for the same reason.
            // WITHDRAWN FOR WALLS AND COLUMNS. It forbade the engineer's own convention.
            //
            // This was written from six spandrels that shipped 1.7 inches tall on a second storey,
            // and it read the symptom as "assigned twice" when the fault was "1.7 inches tall".
            // Andrea Neuviale's own 31138 model does the thing this refused, everywhere: 57 of its
            // 87 columns carry an assign on 5.1 storeys each (C100, C102, C103 on nineteen), and
            // 101 of its 247 area objects the same. That is how ETABS is given one label at full
            // height with a separate member between each pair of floors — which is exactly what
            // she asked for on 31 August: "Columns have to be broken down at every floor, from
            // slab to slab", "same with walls", "the same label full height".
            //
            // Only this tool obeyed it, and obeying it is why a column was written spanning two
            // storeys instead: the rule made the correct output illegal, so the composer worked
            // around its own gate and shipped members running through the mezzanine.
            //
            // The real fault it was reaching for is a member no taller than a wafer, and
            // ModelPlausibilityTests.NoColumnIsShorterThanAPerson measures that directly. A
            // HEADER is still one member per storey -- a spandrel carries its own depth in its
            // joints, so a second assign really is a second copy of it.
            if (sts.Count > 1 && obj.StartsWith("KS", StringComparison.Ordinal)
                && !floors.Contains(obj) && !openings.Contains(obj) && !carriedThrough.Contains(obj))
                v.Add(new ModelViolation("member-on-two-storeys",
                    $"'{obj}' is a header assigned to {sts.Count} storeys: {string.Join(", ", sts)}. A header " +
                    "carries its own depth in its joints, so a second assign is a second copy of it, not the " +
                    "same one carried up", obj));

        // 5. A member must not sit on a storey belonging to a different building. On a site model
        //    the storey below A-LEVEL 35 is B-LEVEL 35, and six of tower B's headers landed on a
        //    tower A storey 130 ft from where they were drawn.
        var buildings = storeys
            .Select(s => (Storey: s, Tag: E2kDocument.BuildingTagOf(s)))
            .Where(x => x.Tag.Length > 0)
            .GroupBy(x => x.Tag)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Storey).ToList(), StringComparer.OrdinalIgnoreCase);

        if (buildings.Count > 1)
        {
            var footprint = new Dictionary<string, (double MinX, double MaxX)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tag, tagStoreys) in buildings)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var (obj, sts) in onStoreys)
                {
                    if (floors.Contains(obj)) continue;
                    if (!sts.Any(s => tagStoreys.Contains(s, StringComparer.OrdinalIgnoreCase))) continue;
                    foreach (string j in joints.GetValueOrDefault(obj) ?? new List<string>())
                        if (pts.TryGetValue(j, out var p)) { lo = Math.Min(lo, p.X); hi = Math.Max(hi, p.X); }
                }
                if (lo <= hi) footprint[tag] = (lo, hi);
            }

            // Only meaningful where the buildings do not overlap on plan; where they do, plan
            // position cannot say which building a member belongs to and this stays quiet.
            var tags = footprint.Keys.ToList();
            bool separable = tags.Count > 1 && tags.All(a => tags.All(b =>
                a == b || footprint[a].MaxX < footprint[b].MinX || footprint[b].MaxX < footprint[a].MinX));

            if (separable)
                foreach (var (obj, sts) in onStoreys)
                {
                    if (floors.Contains(obj)) continue;
                    string tag = sts.Select(E2kDocument.BuildingTagOf).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
                    if (tag.Length == 0 || !footprint.ContainsKey(tag)) continue;

                    foreach (string j in joints.GetValueOrDefault(obj) ?? new List<string>())
                    {
                        if (!pts.TryGetValue(j, out var p)) continue;
                        var own = footprint[tag];
                        if (p.X >= own.MinX && p.X <= own.MaxX) continue;

                        string? belongs = footprint.FirstOrDefault(f => p.X >= f.Value.MinX && p.X <= f.Value.MaxX).Key;
                        if (belongs is not null && !belongs.Equals(tag, StringComparison.OrdinalIgnoreCase))
                        {
                            v.Add(new ModelViolation("member-on-another-building",
                                $"'{obj}' is on building {tag}'s storey but stands in building {belongs}",
                                $"x {p.X / 12:0} ft"));
                            break;
                        }
                    }
                }
        }

        // 6. No two joints closer than the tolerance. ETABS calls them "too close", and they are
        //    walls that should have been joined and were not -- connectivity is what the model is for.
        var cells = new Dictionary<(long, long, long), List<string>>();
        foreach (var (name, p) in pts)
        {
            if (!name.StartsWith("K", StringComparison.Ordinal)) continue;
            var key = ((long)Math.Round(p.X / jointTolerance), (long)Math.Round(p.Y / jointTolerance), (long)Math.Round(p.Z / jointTolerance));
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<string>();
            list.Add(name);
        }

        int tooClose = 0;
        string firstPair = string.Empty;
        foreach (var (key, names) in cells)
        {
            var near = new List<string>();
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            for (long dz = -1; dz <= 1; dz++)
                if (cells.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var got)) near.AddRange(got);

            foreach (string a in names)
            foreach (string b in near)
            {
                if (string.CompareOrdinal(a, b) >= 0) continue;
                var pa = pts[a]; var pb = pts[b];
                double d = Math.Sqrt(Math.Pow(pa.X - pb.X, 2) + Math.Pow(pa.Y - pb.Y, 2) + Math.Pow(pa.Z - pb.Z, 2));
                if (d >= jointTolerance) continue;
                tooClose++;
                if (firstPair.Length == 0) firstPair = $"{a}/{b} {d:0.0000} in apart";
            }
        }
        if (tooClose > 0)
            v.Add(new ModelViolation("joints-too-close", $"{tooClose} pair(s) of joints closer than {jointTolerance} in", firstPair));

        return v;
    }

    private static void CheckReportNumbers(
        IReadOnlyList<string> report,
        int storeys,
        int walls,
        int columns,
        int floors,
        int joints,
        List<ModelViolation> violations)
    {
        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Storeys built"] = storeys,
            ["Walls"] = walls,
            ["Columns"] = columns,
            ["Floors"] = floors,
            ["Joints"] = joints,
        };

        var printed = new Regex(@"^\s*(Storeys built|Walls|Columns|Floors|Joints)\s*:\s*([\d,]+)\b",
            RegexOptions.IgnoreCase);

        foreach (string raw in report)
        {
            var m = printed.Match(raw);
            if (!m.Success) continue;

            string label = m.Groups[1].Value;
            int actual = int.Parse(m.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);
            if (expected.TryGetValue(label, out int want) && actual != want)
                violations.Add(new ModelViolation(
                    "report-count-mismatch",
                    $"report says {label} is {actual:N0}; the .e2k beside it contains {want:N0}",
                    label));
        }

        var denominator = new Regex(@"\bof\s+([\d,]+)\s+floor plate\(s\)", RegexOptions.IgnoreCase);
        foreach (string raw in report)
        foreach (Match m in denominator.Matches(raw))
        {
            int actual = int.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
            if (actual != floors)
                violations.Add(new ModelViolation(
                    "report-count-mismatch",
                    $"report says a floor-plate denominator is {actual:N0}; the .e2k beside it contains {floors:N0}",
                    raw.Trim()));
        }
    }

    private static void CheckStoreyNames(
        IReadOnlyList<string>? lines,
        IReadOnlyList<string> storeys,
        string document,
        List<ModelViolation> violations)
    {
        if (lines is null) return;

        var known = new HashSet<string>(storeys, StringComparer.OrdinalIgnoreCase);
        // ONE SPACE, not \s+. A storey is "C-LEVEL 3" or "LEVEL 1 MEZZ" -- never "LEVEL" and then
        // thirty spaces. Allowing a run of whitespace let the word LEVEL at the end of a sheet name
        // pair up with the first number of the table column beside it and invent a storey.
        var named = new Regex(@"\b(?:[A-Z]-)?LEVEL P?\d+(?: MEZZ)?\b|\b[A-Z]-ROOF\b",
            RegexOptions.IgnoreCase);

        // A LINE WHOSE SUBJECT IS ABSENCE IS ALLOWED TO NAME WHAT IS ABSENT.
        //
        // The point of this check is a sentence that treats a storey as though the engineer has it
        // -- a question about B-LEVEL 28 in a file with no B storeys. It is not a sentence that
        // exists precisely to tell her what was read and then left out, and those must keep their
        // names or they say nothing: "2 drawing(s) carry structure that is NOT IN THIS MODEL" is a
        // piece of the building she needs told about, by name.
        string[] aboutAbsence =
        {
            "not placed", "no storey", "not in this model", "were removed", "was removed",
            "do not exist in it", "does not exist in it", "cut away", "removed from the storeys",
            "left out", "gave up", "stood down", "superseded",
        };

        // A STOREY NAME INSIDE A DRAWING'S FILENAME IS THE DRAWING'S NAME, NOT A CLAIM.
        //
        // "--Structural Plan - A-LEVEL 28.dxf" under the heading "Read but not placed on any storey
        // in this model" is the report doing its job. The heading carries the caveat and the rows
        // carry the names, so a line-by-line reading of the rows saw 49 storeys the file does not
        // have and refused to publish a model that was correct.
        // Greedy, back to the start of the line: a drawing name comes first on these lines, either
        // as a table row or as the "<sheet>: <what happened>" prefix of a flag. Filenames contain
        // spaces, so anything that stops at whitespace strips half a name and leaves the other half
        // to be misread.
        var drawingNames = new Regex(@"^.*\.dxf", RegexOptions.IgnoreCase);

        foreach (string raw in lines)
        {
            if (aboutAbsence.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

            string text = drawingNames.Replace(raw, string.Empty);

            foreach (Match m in named.Matches(text))
            {
                string storey = m.Value.Trim();
                if (known.Contains(storey)) continue;
                // ADVISORY, DELIBERATELY, until it can tell a claim from a disclosure.
                //
                // What this was written for is a sentence that treats a storey as PRESENT -- the
                // workbook asking her to price a floor on B-LEVEL 28 in a file with no B storeys.
                // That defect is fixed at its source and tested.
                //
                // What it actually matches is any mention, and a report names absent storeys all
                // the time on purpose: the sheet table's "read but not placed" rows, members cut
                // away, drawings whose structure is not in this model, and J7, whose entire text is
                // "YOUR MODEL HAS NO LEVEL P1 MEZZ, BUT THE DRAWINGS DO". Blocking on those refuses
                // to publish a model that is right because the report is doing its job. Telling a
                // claim from a disclosure needs the question's shape, not a phrase list, and that
                // is a piece of work rather than a patch.
                violations.Add(new ModelViolation(
                    "storey-name-not-in-file",
                    $"{document} names '{storey}', but the .e2k beside it does not contain that storey",
                    storey,
                    ModelViolationSeverity.Advisory));
            }
        }
    }
}
