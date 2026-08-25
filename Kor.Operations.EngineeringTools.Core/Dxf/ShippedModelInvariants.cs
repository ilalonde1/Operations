using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One thing wrong with a finished model, in the words the reader needs.</summary>
public sealed record ModelViolation(string Rule, string What, string Where);

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
    public static IReadOnlyList<ModelViolation> Check(
        IEnumerable<string> lines,
        double jointTolerance = 0.05,
        IEnumerable<string>? droppedStoreys = null,
        IEnumerable<string>? referenceE2k = null)
    {
        var openings = new HashSet<string>(StringComparer.Ordinal);
        var carriedThrough = new HashSet<string>(StringComparer.Ordinal);
        if (referenceE2k is not null)
            foreach (string raw in referenceE2k)
            {
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

        // 3. A storey carrying members must have a floor, or everything on it reads as unsupported.
        var floors = kind.Where(x => x.Value.Equals("FLOOR", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var withMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withFloor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (obj, sts) in onStoreys)
            foreach (string st in sts)
                (floors.Contains(obj) ? withFloor : withMembers).Add(st);

        foreach (string st in withMembers)
            if (!withFloor.Contains(st))
                v.Add(new ModelViolation("storey-with-no-floor", $"'{st}' carries members and has no floor plate", st));

        // 4. A member must not belong to two storeys. A floor may -- that is a borrowed plate, and
        //    it is declared. A WALL, COLUMN or header on two storeys is one member counted twice:
        //    six spandrels shipped that way, 1.7 inches tall on the second storey.
        foreach (var (obj, sts) in onStoreys)
            // An OPENING on several storeys is one hole through several floors, which is what it
            // is: a lift shaft does not stop at each slab. The engineer's own model does exactly
            // this -- 31065 carries 359 opening assigns from 25 objects, about fourteen storeys
            // each -- and a borrowed floor here now carries its holes with it for the same reason.
            if (sts.Count > 1 && !floors.Contains(obj) && !openings.Contains(obj) && !carriedThrough.Contains(obj))
                v.Add(new ModelViolation("member-on-two-storeys",
                    $"'{obj}' is assigned to {sts.Count} storeys: {string.Join(", ", sts)}", obj));

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
}
