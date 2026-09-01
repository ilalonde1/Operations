using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record E2kStoreyContents(int Walls, int Columns, int Floors);

public sealed record E2kObjectContents(
    string Name,
    string Kind,
    IReadOnlyList<string> Storeys,
    string? SourceSheet);

public sealed record E2kFloorGaps(
    IReadOnlyList<string> FloorsWithNoPlate,
    IReadOnlyList<string> MostlyUncovered,
    IReadOnlyList<string> PlatesWithNoSupport);

public sealed record E2kModelContents(
    IReadOnlyList<string> Storeys,
    int Walls,
    int Columns,
    int Floors,
    int Headers,
    int Openings,
    int Joints,
    IReadOnlyDictionary<string, E2kStoreyContents> MembersByStorey,
    IReadOnlyDictionary<string, int> PlatesByStorey,
    IReadOnlyList<E2kObjectContents> Objects,
    IReadOnlySet<string> ReferencedJoints,
    IReadOnlySet<string> OrphanGeneratedJoints)
{
    public static E2kModelContents Empty { get; } = new(
        Array.Empty<string>(), 0, 0, 0, 0, 0, 0,
        new Dictionary<string, E2kStoreyContents>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<E2kObjectContents>(),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>
/// An ETABS .e2k text model held as its ordered sections.
///
/// The generator never authors a model from scratch: it reads one ETABS itself
/// exported, appends geometry to the relevant sections and writes the file back.
/// Stories, grids, materials and design settings therefore stay exactly as ETABS
/// wrote them, and the output can only differ from a known-good file by the lines
/// we added.
/// </summary>
public sealed class E2kDocument
{
    private readonly List<Section> _sections = new();

    private sealed class Section
    {
        public required string Header { get; init; }
        public List<string> Lines { get; } = new();
    }

    public static E2kDocument Load(string path) => Parse(File.ReadAllLines(path));

    public static E2kDocument Parse(IEnumerable<string> lines)
    {
        var doc = new E2kDocument();
        Section? current = null;

        foreach (string line in lines)
        {
            if (line.StartsWith('$'))
            {
                current = new Section { Header = line };
                doc._sections.Add(current);
            }
            else
            {
                current?.Lines.Add(line);
            }
        }

        return doc;
    }

    /// <summary>Section headers as they appear, e.g. "$ POINT COORDINATES".</summary>
    public IEnumerable<string> SectionHeaders => _sections.Select(s => s.Header);

    public IReadOnlyList<string> LinesOf(string headerContains)
        => Find(headerContains)?.Lines ?? (IReadOnlyList<string>)Array.Empty<string>();

    public void Append(string headerContains, IEnumerable<string> lines)
    {
        var section = Find(headerContains) ?? CreateSection(headerContains);

        // Sections end with a blank separator line; keep additions above it.
        int insertAt = section.Lines.Count;
        while (insertAt > 0 && string.IsNullOrWhiteSpace(section.Lines[insertAt - 1])) insertAt--;

        section.Lines.InsertRange(insertAt, lines);
    }

    private Section? Find(string headerContains)
        => _sections.FirstOrDefault(s => s.Header.Contains(headerContains, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a section a lean export happens to omit, placed ahead of the end-of-model
    /// marker so the file keeps ETABS's own ordering.
    /// </summary>
    private Section CreateSection(string headerContains)
    {
        var section = new Section { Header = "$ " + headerContains.ToUpperInvariant() };
        section.Lines.Add(string.Empty);

        int end = _sections.FindIndex(s => s.Header.Contains("END OF MODEL", StringComparison.OrdinalIgnoreCase));
        if (end >= 0) _sections.Insert(end, section);
        else _sections.Add(section);

        return section;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var section in _sections)
        {
            sb.AppendLine(section.Header);
            foreach (string line in section.Lines) sb.AppendLine(line);
        }
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    /// <summary>
    /// Storey names with their elevations, derived from the "$ STORIES" section.
    /// ETABS lists storeys from the top down with a HEIGHT each, so elevations are
    /// accumulated upward from the base storey.
    /// </summary>
    public IReadOnlyList<StoryLevel> ReadStories()
    {
        var parsed = new List<(string Name, double Height)>();

        double baseElevation = 0;

        // No real storey is taller than this; see FloorUnder for why a cap is needed at all.
        const double maxPlausibleStoreyHeight = 480.0;

        foreach (string raw in LinesOf("STORIES"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("STORY", StringComparison.OrdinalIgnoreCase)) continue;

            int firstQuote = line.IndexOf('"');
            int lastQuote = firstQuote < 0 ? -1 : line.IndexOf('"', firstQuote + 1);
            if (firstQuote < 0 || lastQuote < 0) continue;

            string name = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

            int e = line.IndexOf("ELEV", StringComparison.OrdinalIgnoreCase);
            if (e >= 0 && !line.Contains("HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                string elevTail = line[(e + "ELEV".Length)..].Trim();
                string elevToken = new(elevTail.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                if (double.TryParse(elevToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double elev))
                    baseElevation = elev;
                continue;
            }

            double height = 0;
            int h = line.IndexOf("HEIGHT", StringComparison.OrdinalIgnoreCase);
            if (h >= 0)
            {
                string tail = line[(h + "HEIGHT".Length)..].Trim();
                string token = new(tail.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out height);
            }

            parsed.Add((name, height));
        }

        // Listed top-down: walking the reversed list accumulates elevation from the base.
        var stack = new List<(string Name, double Elevation, double Height)>();
        double elevation = baseElevation;
        for (int i = parsed.Count - 1; i >= 0; i--)
        {
            var (name, height) = parsed[i];
            elevation += height;
            stack.Add((name, elevation, height));
        }

        // A typical storey in this building, used where a storey's own height cannot be believed.
        var believable = stack.Select(s => s.Height).Where(h => h >= 60 && h <= maxPlausibleStoreyHeight).OrderBy(h => h).ToList();
        double typical = believable.Count > 0 ? believable[believable.Count / 2] : maxPlausibleStoreyHeight;

        var result = new List<StoryLevel>();
        for (int i = 0; i < stack.Count; i++)
            result.Add(new StoryLevel(stack[i].Name, stack[i].Elevation, FloorUnder(stack, i, maxPlausibleStoreyHeight, typical)));

        result.Reverse();
        return result;
    }

    /// <summary>
    /// The elevation a member on this storey stands on — which is not, in a site model, its own
    /// storey top minus its own HEIGHT.
    ///
    /// ETABS keeps one global storey list, so a site with several towers gets a storey for every
    /// distinct floor elevation across all of them. Where tower B's 34th floor sits 2" above
    /// tower A's, the export contains a storey named "B-LEVEL 34" that is 2" tall. Reading that
    /// HEIGHT as a wall height makes tower B's walls two-inch wafers hanging a full storey above
    /// the floor below — on 31168 that was 78 of 897 panels, and it is what the model looked like.
    ///
    /// A tower's walls stand on that tower's previous floor, so the leading tag in the storey name
    /// is what resolves it. The towers here interleave at 4.67ft and 5.0ft, so no gap threshold can
    /// separate a real storey from a duplicate-floor sliver; only the name can.
    /// </summary>
    private static double FloorUnder(
        IReadOnlyList<(string Name, double Elevation, double Height)> stack, int index, double maxHeight, double typicalHeight)
    {
        var (name, top, height) = stack[index];
        string tag = BuildingTagOf(name);

        // A tower's own previous floor, where it has one within a storey's reach. A tower that
        // only separates from the site part-way up has no earlier storey of its own down at the
        // podium: 31168's tower B is named B-LEVEL 27 and above, and shares LEVEL 26 and below.
        // Taking B-LEVEL 1 as B-LEVEL 27's floor reached 271ft down, and clamping that to the
        // 40ft cap still spread one wall across five storeys.
        if (tag.Length > 0)
        {
            for (int i = index - 1; i >= 0; i--)
                if (BuildingTagOf(stack[i].Name) == tag)
                    return top - stack[i].Elevation <= maxHeight
                        ? stack[i].Elevation
                        : NearestFloorBelow(stack, index, top, maxHeight);
        }

        double floor = NearestFloorBelow(stack, index, top, maxHeight);
        if (!double.IsNaN(floor)) return floor;

        // Nothing below: the base storey. ETABS parks a model's base far under the structure and
        // absorbs the distance into the lowest storey — on 31168 the base reads -12000 and LEVEL P3
        // reads 13366, a storey 1,113ft tall. Honouring the base without bounding that storey turns
        // the lowest walls into 1,100ft spikes; ignoring it lifts the whole model 1,000ft up.
        //
        // Where that height cannot be believed, the storey is given a typical one from this building
        // rather than the 40ft ceiling: a parkade level is a parkade level, and the ceiling made it
        // four storeys tall — "the lowest level, which is P3, seems way too high". A base storey
        // whose own height is credible keeps it.
        return top - (height > maxHeight ? typicalHeight : height);
    }

    /// <summary>
    /// The nearest floor under this storey, stepping past the near-coincident storeys the other
    /// towers place at the same floor — C-LEVEL 3 sits 5" above LEVEL 3, and taking that as its
    /// height made tower C's lowest walls half a foot tall. NaN when there is nothing below.
    /// </summary>
    private static double NearestFloorBelow(
        IReadOnlyList<(string Name, double Elevation, double Height)> stack, int index, double top, double maxHeight)
    {
        const double duplicateFloorTolerance = 12.0;
        for (int i = index - 1; i >= 0; i--)
            if (top - stack[i].Elevation > duplicateFloorTolerance)
                return Math.Max(stack[i].Elevation, top - maxHeight);
        return double.NaN;
    }

    /// <summary>
    /// The tower a site-model storey belongs to — "B-LEVEL 34" is tower B. Empty for a storey
    /// shared by the site ("LEVEL 5", "LEVEL P1") and for any single-building model, where every
    /// storey is shared and the ordinary floor-below rule applies.
    /// </summary>
    /// <summary>
    /// The building a storey belongs to on a site model — "A" from "A-LEVEL 35" — or empty for a
    /// storey shared by the whole site. Public because geometry decisions need it too: a member
    /// must never be moved onto a storey belonging to a different building.
    /// </summary>
    public static string BuildingTagOf(string name)
        => name.Length > 2 && char.IsLetter(name[0]) && name[1] == '-'
            ? char.ToUpperInvariant(name[0]).ToString()
            : string.Empty;

    /// <summary>
    /// Gives the lowest storey a believable height and lifts the base up underneath it.
    ///
    /// ETABS parks a model's base far below the structure and absorbs the whole distance into the
    /// lowest storey: 31168 exports with the base at -1,000ft and LEVEL P3 1,113ft tall. Reading
    /// around that was not enough — the storey list is what ETABS builds from, so on import every
    /// member on that storey was extruded a thousand feet down and the parkade came in as one solid
    /// block half the height of the building. "The lowest level, which is P3, seems way too high."
    ///
    /// Every storey keeps its elevation: the base rises by exactly as much as the storey shrinks,
    /// so everything accumulating above it is untouched.
    /// </summary>
    /// <returns>True when the storey list was rewritten.</returns>
    /// <summary>
    /// Adds parkade storeys the drawings have and the model does not, below the lowest one it has.
    ///
    /// The engineer's answer, on seeing the first 31138 model: "the model needs to go to P5". Her
    /// model stops at P3 while drafting issues LEVEL P4 and LEVEL P5, so two whole parkade floors
    /// were read, produced geometry, and were then placed nowhere — the storeys did not exist to
    /// place them on.
    ///
    /// Each new storey takes the height the parkade already uses, and the base drops by exactly the
    /// total added, which leaves every existing elevation where it was. Returns the storeys added,
    /// lowest last.
    /// </summary>
    public IReadOnlyList<string> AddParkadeStoreysBelow(IReadOnlyCollection<int> parkadeLevelsWanted)
    {
        var added = new List<string>();
        if (parkadeLevelsWanted.Count == 0) return added;

        var section = Find("STORIES");
        if (section is null) return added;

        var lines = section.Lines;
        var have = new HashSet<int>();
        int lowestParkadeAt = -1, baseAt = -1;
        double parkadeHeight = 0, baseElevation = 0;
        string lowestParkadeLine = string.Empty;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("STORY", StringComparison.OrdinalIgnoreCase)) continue;

            var named = Regex.Match(line, @"^STORY\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!named.Success) continue;

            var parkade = Regex.Match(named.Groups[1].Value, @"^\s*(?:L(?:EVEL)?\s*)?P\s*(\d+)\s*$", RegexOptions.IgnoreCase);
            if (parkade.Success)
            {
                have.Add(int.Parse(parkade.Groups[1].Value));
                int h = line.IndexOf("HEIGHT", StringComparison.OrdinalIgnoreCase);
                if (h >= 0)
                {
                    string token = new(line[(h + "HEIGHT".Length)..].Trim().TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        lowestParkadeAt = i;          // listed top-down, so the last parkade seen is the lowest
                        parkadeHeight = value;
                        lowestParkadeLine = lines[i];
                    }
                }
                continue;
            }

            int e = line.IndexOf("ELEV", StringComparison.OrdinalIgnoreCase);
            if (e < 0) continue;
            string elevToken = new(line[(e + "ELEV".Length)..].Trim().TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
            if (double.TryParse(elevToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double elev))
            {
                baseAt = i;
                baseElevation = elev;
            }
        }

        // Nothing to hang them off, or nothing missing.
        if (lowestParkadeAt < 0 || baseAt < 0 || parkadeHeight <= 0) return added;

        var missing = parkadeLevelsWanted.Where(l => !have.Contains(l)).OrderBy(l => l).ToList();
        if (missing.Count == 0) return added;

        // Only ever extend the sequence downward, and only without a gap: P4 and P5 under a P3 are
        // the next two floors down, but a lone P7 under a P3 is a naming question, not a storey.
        int deepest = have.Count > 0 ? have.Max() : 0;
        var contiguous = new List<int>();
        foreach (int level in missing)
        {
            if (level != deepest + contiguous.Count + 1) break;
            contiguous.Add(level);
        }
        if (contiguous.Count == 0) return added;

        string Number(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        var name = Regex.Match(lowestParkadeLine.Trim(), @"^STORY\s+""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
        bool spelledOut = name.Contains("LEVEL", StringComparison.OrdinalIgnoreCase);

        var inserted = new List<string>();
        foreach (int level in contiguous)
        {
            string storey = spelledOut ? $"LEVEL P{level}" : $"P{level}";
            inserted.Add(Regex.Replace(
                Regex.Replace(lowestParkadeLine, @"(STORY\s+)""[^""]+""", $"${{1}}\"{storey}\"", RegexOptions.IgnoreCase),
                @"(HEIGHT\s+)\S+", $"${{1}}{Number(parkadeHeight)}", RegexOptions.IgnoreCase));
            added.Add(storey);
        }

        lines.InsertRange(lowestParkadeAt + 1, inserted);

        // The base drops by what was added, so every elevation above it is untouched.
        int baseNow = baseAt + inserted.Count;
        lines[baseNow] = Regex.Replace(lines[baseNow], @"(ELEV\s+)\S+",
            $"${{1}}{Number(baseElevation - parkadeHeight * inserted.Count)}", RegexOptions.IgnoreCase);

        return added;
    }

    public bool NormaliseBaseStorey()
    {
        var section = Find("STORIES");
        if (section is null) return false;

        var lines = section.Lines;
        int lowestAt = -1, baseAt = -1;
        double lowestHeight = 0, baseElevation = 0;
        var heights = new List<double>();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("STORY", StringComparison.OrdinalIgnoreCase)) continue;

            int h = line.IndexOf("HEIGHT", StringComparison.OrdinalIgnoreCase);
            if (h >= 0)
            {
                string token = new(line[(h + "HEIGHT".Length)..].Trim().TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;

                heights.Add(value);
                lowestAt = i;            // storeys are listed top-down, so the last one seen is the lowest
                lowestHeight = value;
                continue;
            }

            int e = line.IndexOf("ELEV", StringComparison.OrdinalIgnoreCase);
            if (e < 0) continue;

            string elevToken = new(line[(e + "ELEV".Length)..].Trim().TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
            if (double.TryParse(elevToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double elev))
            {
                baseAt = i;
                baseElevation = elev;
            }
        }

        const double maxPlausibleStoreyHeight = 480.0;
        if (lowestAt < 0 || baseAt < 0 || lowestHeight <= maxPlausibleStoreyHeight) return false;

        var believable = heights.Where(v => v >= 60 && v <= maxPlausibleStoreyHeight).OrderBy(v => v).ToList();
        double typical = believable.Count > 0 ? believable[believable.Count / 2] : maxPlausibleStoreyHeight;

        string Number(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        lines[lowestAt] = Regex.Replace(lines[lowestAt], @"(HEIGHT\s+)\S+", $"${{1}}{Number(typical)}",
            RegexOptions.IgnoreCase);
        lines[baseAt] = Regex.Replace(lines[baseAt], @"(ELEV\s+)\S+",
            $"${{1}}{Number(baseElevation + lowestHeight - typical)}", RegexOptions.IgnoreCase);
        return true;
    }

    /// <summary>
    /// Cuts the storey list down to one tower's, and rewrites it so ETABS reads the same elevations.
    ///
    /// A site model keeps every tower on one storey list, so a model of tower B carries tower A's
    /// and C's storeys too and they stand empty. That was the engineer's first complaint — "some
    /// levels don't exist, they're blank" — and her answer to what she wants instead: "Tower B model
    /// should only include tower B storeys".
    ///
    /// Storeys belonging to another tower are dropped; the tower's own and the shared podium ones
    /// are kept. HEIGHT is recomputed from the gaps that remain, so every retained storey stays at
    /// the elevation it had and the building neither grows nor shrinks.
    /// </summary>
    /// <returns>The storeys removed, for reporting.</returns>
    /// <summary>
    /// Drops every storey standing above <paramref name="topStorey"/>, keeping that one and
    /// everything below it whatever it is called.
    ///
    /// <see cref="KeepOnlyTower"/> cuts by NAME, which is the wrong axis for the ordinary request
    /// "give me the podium and the mid-rise, not the towers". On 31168 the towers' floors above
    /// level 26 carry an A- or B- prefix and are caught by name, but levels 11 to 26 are equally
    /// tower — the drawings label them BLDG A&amp;B — and carry no prefix at all, so a name filter
    /// keeps sixteen storeys of tower. Meanwhile the towers' ground floors, A-LEVEL 1 and
    /// B-LEVEL 1, ARE wanted: they sit at grade inside the podium the engineer asked for, and a
    /// name filter throws them away.
    ///
    /// Elevation gets both right in one rule, because that is the shape of the request: the
    /// engineer is pointing at a height, not at a naming convention.
    ///
    /// It does NOT get both right, and believing it did shipped eight storeys of tower to an
    /// engineer who had asked for none. 31168's towers carry LEVEL 3 through LEVEL 10 with no
    /// prefix, and every one of those sits BELOW the mid-rise's own roof, so cutting at C-ROOF
    /// kept all eight. Their names look like the podium's and their elevations look like the
    /// mid-rise's; neither axis can separate them. What separates them is where they stand on
    /// plan — the towers occupy y 213-308 ft and the mid-rise y 357-429 ft, no overlap at all.
    /// Use <see cref="DropStoreys"/> until that footprint test is the rule.
    /// </summary>
    /// <returns>The storeys removed, in the order they were listed.</returns>
    public IReadOnlyList<string> KeepStoreysUpTo(string topStorey)
    {
        var section = Find("STORIES");
        if (section is null) return Array.Empty<string>();

        var stories = ReadStories().OrderBy(s => s.Elevation).ToList();
        if (stories.Count == 0) return Array.Empty<string>();

        var top = stories.FirstOrDefault(s =>
            s.Name.Equals(topStorey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (top is null)
            throw new InvalidOperationException(
                $"The reference model has no storey named '{topStorey}'. It lists: " +
                string.Join(", ", stories.OrderByDescending(s => s.Elevation).Select(s => s.Name)) + ".");

        // At OR below, and ties are kept: two towers interleaving at one elevation is normal here,
        // and cutting one of a pair because it sorted second would remove real structure.
        var dropped = stories.Where(s => s.Elevation > top.Elevation).Select(s => s.Name).ToList();
        if (dropped.Count == 0) return dropped;

        var retained = stories.Where(s => s.Elevation <= top.Elevation).ToList();
        if (retained.Count == 0) return Array.Empty<string>();

        // ETABS lists storeys from the top down, each carrying the height of the storey below it.
        var rebuilt = new List<string>();
        double baseElevation = retained[0].ElevationBelow;
        for (int i = retained.Count - 1; i >= 0; i--)
        {
            double below = i == 0 ? baseElevation : retained[i - 1].Elevation;
            rebuilt.Add($"  STORY \"{retained[i].Name}\"  HEIGHT {(retained[i].Elevation - below).ToString("0.####", CultureInfo.InvariantCulture)}");
        }
        rebuilt.Add($"  STORY \"Base\"  ELEV {baseElevation.ToString("0.####", CultureInfo.InvariantCulture)}");
        rebuilt.Add(string.Empty);

        section.Lines.Clear();
        section.Lines.AddRange(rebuilt);
        return dropped;
    }

    /// <summary>
    /// Drops the named storeys outright, keeping every other one at the elevation it had.
    ///
    /// The blunt instrument, and it exists because the two sharp ones both missed. A storey that
    /// belongs to a building the engineer did not ask for can carry that building's prefix, in
    /// which case <see cref="KeepOnlyTower"/> finds it; or sit above the part she wants, in which
    /// case <see cref="KeepStoreysUpTo"/> finds it. 31168's tower levels 3 to 10 do neither, and
    /// they reached her model because there was no third option to reach for.
    ///
    /// Naming the storeys is not a rule and does not pretend to be one. The rule is a plan
    /// footprint test; this is what to use in the meantime, and the report's footprint table is
    /// what makes the need for it visible.
    /// </summary>
    /// <returns>The storeys removed, in the order they were listed.</returns>
    public IReadOnlyList<string> DropStoreys(IEnumerable<string> names)
    {
        var section = Find("STORIES");
        if (section is null) return Array.Empty<string>();

        var wanted = new HashSet<string>(
            names.Select(n => n.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return Array.Empty<string>();

        var stories = ReadStories().OrderBy(s => s.Elevation).ToList();
        if (stories.Count == 0) return Array.Empty<string>();

        // Naming a storey that is not there is a typo, not a no-op: silently keeping the tower
        // because its name was misspelled is exactly the failure this method exists to stop.
        var unknown = wanted.Where(n => !stories.Any(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"The model has no storey named {string.Join(", ", unknown.Select(u => $"'{u}'"))}. It lists: " +
                string.Join(", ", stories.OrderByDescending(s => s.Elevation).Select(s => s.Name)) + ".");

        var dropped = stories.Where(s => wanted.Contains(s.Name)).Select(s => s.Name).ToList();
        var retained = stories.Where(s => !wanted.Contains(s.Name)).ToList();
        if (retained.Count == 0)
            throw new InvalidOperationException("Dropping those storeys would leave the model with none.");

        section.Lines.Clear();
        section.Lines.AddRange(RebuildStoreyLines(retained));
        return dropped;
    }

    /// <summary>
    /// The STORIES section as ETABS writes it: top down, each storey carrying the height of the
    /// one below it, closed by the base elevation. Shared so that a storey kept by any of the
    /// cuts stays at the elevation it had and the building neither grows nor shrinks.
    /// </summary>
    private static List<string> RebuildStoreyLines(IReadOnlyList<StoryLevel> retained)
    {
        var rebuilt = new List<string>();
        double baseElevation = retained[0].ElevationBelow;
        for (int i = retained.Count - 1; i >= 0; i--)
        {
            double below = i == 0 ? baseElevation : retained[i - 1].Elevation;
            rebuilt.Add($"  STORY \"{retained[i].Name}\"  HEIGHT {(retained[i].Elevation - below).ToString("0.####", CultureInfo.InvariantCulture)}");
        }
        rebuilt.Add($"  STORY \"Base\"  ELEV {baseElevation.ToString("0.####", CultureInfo.InvariantCulture)}");
        rebuilt.Add(string.Empty);
        return rebuilt;
    }

    private readonly Dictionary<string, string> _storeyRenames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Storeys this document has renamed, old name to new. A cut that renames a storey has moved
    /// it, not removed it, and the sheets that belong to it must follow: the drawings are matched
    /// against the storey list as it was BEFORE any cut, so without this the LEVEL 1 PLAN sheet
    /// goes looking for "A-LEVEL 1", finds nothing standing under that name, and 59 walls and 63
    /// columns leave the model without a word said.
    /// </summary>
    public IReadOnlyDictionary<string, string> StoreyRenames => _storeyRenames;

    public IReadOnlyList<string> KeepOnlyTower(string tower)
    {
        var section = Find("STORIES");
        if (section is null) return Array.Empty<string>();

        string keep = tower.Trim().ToUpperInvariant();
        var stories = ReadStories().OrderBy(s => s.Elevation).ToList();
        if (stories.Count == 0) return Array.Empty<string>();

        // A storey belonging to another building is dropped -- but only from the point this
        // building starts. BELOW that, a tagged storey is the shared base the buildings stand on,
        // not somebody else's floor.
        //
        // 31168's ground floor is drafted twice, as A-LEVEL 1 and B-LEVEL 1, 1.7 in apart. Cutting
        // both by name left the YMCA with LEVEL 2 sitting on LEVEL 1 MEZZ and no ground floor at
        // all. The engineer: "It's all one big slab at L1, one elevation. Doesn't really matter
        // which one." One slab, so one storey: the lower of the pair is kept and renamed to the
        // name with no building in it, which is also what the sheet is called -- there is one
        // LEVEL 1 PLAN drawing, not one per tower.
        //
        // Restricted to the shared base on purpose. Site models interleave towers by a couple of
        // inches all the way up, and those pairs are two real storeys of two real buildings; this
        // merge must never reach them. Above this building's lowest storey nothing is merged.
        double buildingStarts = stories
            .Where(s => string.Equals(BuildingTagOf(s.Name), keep, StringComparison.OrdinalIgnoreCase))
            .Select(s => (double?)s.Elevation)
            .FirstOrDefault() ?? double.MinValue;

        var dropped = stories
            .Where(s => BuildingTagOf(s.Name) is var t && t.Length > 0 && t != keep)
            .Where(s => s.Elevation >= buildingStarts)
            .Select(s => s.Name)
            .ToList();

        // What is left of the shared base: storeys named for a building, standing under this one.
        // Where several sit within a foot of each other they are one level drawn more than once.
        var sharedBase = stories
            .Where(s => BuildingTagOf(s.Name).Length > 0 && s.Elevation < buildingStarts)
            .OrderBy(s => s.Elevation)
            .ToList();

        var renames = new List<(string From, string To)>();
        for (int i = 0; i < sharedBase.Count; i++)
        {
            var group = new List<StoryLevel> { sharedBase[i] };
            while (i + 1 < sharedBase.Count && sharedBase[i + 1].Elevation - group[0].Elevation <= 12.0)
                group.Add(sharedBase[++i]);

            string bare = group[0].Name[2..];
            if (stories.Any(s => string.Equals(s.Name, bare, StringComparison.OrdinalIgnoreCase))
                || renames.Any(r => string.Equals(r.To, bare, StringComparison.OrdinalIgnoreCase)))
                bare = group[0].Name;
            renames.Add((group[0].Name, bare));
            if (!string.Equals(group[0].Name, bare, StringComparison.OrdinalIgnoreCase))
                _storeyRenames[group[0].Name] = bare;
            foreach (var also in group.Skip(1))
                _storeyRenames[also.Name] = bare;
            dropped.AddRange(group.Skip(1).Select(s => s.Name));
        }

        if (dropped.Count == 0 && renames.All(r => r.From == r.To)) return Array.Empty<string>();

        var retained = stories
            .Where(s => !dropped.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(s => renames.FirstOrDefault(r => string.Equals(r.From, s.Name, StringComparison.OrdinalIgnoreCase)) is { From: not null } hit
                ? s with { Name = hit.To }
                : s)
            .ToList();
        if (retained.Count == 0) return Array.Empty<string>();

        // ETABS lists storeys from the top down, each with the height of the storey below it.
        var rebuilt = new List<string>();
        double baseElevation = retained[0].ElevationBelow;
        for (int i = retained.Count - 1; i >= 0; i--)
        {
            double below = i == 0 ? baseElevation : retained[i - 1].Elevation;
            rebuilt.Add($"  STORY \"{retained[i].Name}\"  HEIGHT {(retained[i].Elevation - below).ToString("0.####", CultureInfo.InvariantCulture)}");
        }
        rebuilt.Add($"  STORY \"Base\"  ELEV {baseElevation.ToString("0.####", CultureInfo.InvariantCulture)}");
        rebuilt.Add(string.Empty);

        section.Lines.Clear();
        section.Lines.AddRange(rebuilt);
        return dropped;
    }

    /// <summary>
    /// Rewrites every assign that names a storey this document has renamed.
    ///
    /// The shared ground floor is drafted twice, as A-LEVEL 1 and B-LEVEL 1 an inch and a half
    /// apart, and a one-building model merges them into LEVEL 1. The rename used to be consumed
    /// where SHEETS were matched to storeys, which worked while the cuts ran before anything was
    /// composed. They run after it now, so the map is empty at matching time and the assigns are
    /// written naming B-LEVEL 1 — a storey the cut is about to remove. The assigns are then
    /// orphaned, dropped, and the YMCA's ground floor is empty: no walls, no columns, no plate.
    ///
    /// So the document applies its own renames. It is the only thing that knows what it renamed.
    /// </summary>
    public int RenameStoreysInAssigns()
    {
        if (_storeyRenames.Count == 0) return 0;

        int changed = 0;

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS", "POINT ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;

            for (int i = 0; i < section.Lines.Count; i++)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    section.Lines[i], @"^(\s*\w+ASSIGN\s+""[^""]+""\s+"")([^""]+)("".*)$");
                if (!m.Success) continue;
                if (!_storeyRenames.TryGetValue(m.Groups[2].Value, out string? now)) continue;

                section.Lines[i] = m.Groups[1].Value + now + m.Groups[3].Value;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Removes members that are the same member on the same floor twice, and reports how many
    /// went.
    ///
    /// Two ways a model gets there, and both are 31168's ground floor.
    ///
    /// A CUT MERGES TWO STOREYS. The shared level 1 is drafted once per building, so the engineer's
    /// model carries it as A-LEVEL 1 and B-LEVEL 1, and a model of one building merges them.
    /// Everything the two sheets drew in common was distinct while the storeys were distinct and is
    /// one object the moment they are not: LEVEL 1 came out with 169,627 sq ft of floor as four
    /// plates that were two.
    ///
    /// OR THE MODEL ALREADY HAD THEM. A-LEVEL 1 and B-LEVEL 1 stand 1.67 IN apart — not two floors,
    /// one floor the engineer gave two names so each tower could rise through its own. A whole-site
    /// drawing names neither, matches both, and is placed on both: KF17 and KF18, the same 58
    /// points, 73,788 sq ft each, an inch and a half apart, in the site model as shipped. Nothing
    /// downstream could see it — the area is right, the position is right, and the floor is in the
    /// model twice.
    ///
    /// So storeys closer together than a storey — the twelve inches RisesTo already uses — count as
    /// one floor here. Within that, two members are the same member when they connect the same
    /// joints and carry the same section: the whole of what the file says about them, name aside.
    /// Nothing is judged by tolerance, because a duplicate of this kind is duplicated exactly, and
    /// anything that differs at all is two things the drawings really did draw differently.
    /// </summary>
    public int DropMembersDuplicatedOnOneFloor()
    {
        // Storeys nearer than a storey are one floor, and the copy that stays is the one lowest
        // down: a member belongs to the storey it rises TO, so the lower storey is the one the
        // rest of the model was built around.
        var floorOfStorey = FloorOfStorey();

        var connectivity = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
        {
            var section = Find(header);
            if (section is null) continue;

            foreach (string line in section.Lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.TrimStart(), @"^(\w+)\s+""([^""]+)""\s*(.*)$");
                if (m.Success) connectivity[m.Groups[2].Value] = m.Groups[1].Value + "|" + m.Groups[3].Value;
            }
        }

        // THE SURPLUS ASSIGN GOES, NOT THE OBJECT.
        //
        // Both kinds of duplicate arrive as an extra assign, and only one of them is an extra
        // OBJECT. A storey that borrows its neighbour's floor gets the SAME plate assigned twice —
        // that is how ETABS repeats a member up a building, one object with an assign per storey —
        // so dropping the object took the plate off the donor as well, and B-LEVEL 40 lost the
        // floor B-LEVEL 41 had just borrowed from it.
        //
        // Removing the assign is right in both cases. An object that loses its last one is swept
        // afterwards by the pass that already exists for exactly that.
        var elevationOf = ReadStories().ToDictionary(x => x.Name, x => x.Elevation, StringComparer.OrdinalIgnoreCase);
        var copies = new Dictionary<string, List<(string Line, double At)>>(StringComparer.Ordinal);

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;

            foreach (string line in section.Lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.TrimStart(), @"^\w+ASSIGN\s+""([^""]+)""\s+""([^""]+)""\s*(.*)$");
                if (!m.Success) continue;
                if (!connectivity.TryGetValue(m.Groups[1].Value, out string? shape)) continue;

                string onFloor = floorOfStorey.TryGetValue(m.Groups[2].Value, out string? f)
                    ? f
                    : m.Groups[2].Value;

                string signature = onFloor + "\u0001" + shape + "\u0001" + m.Groups[3].Value;
                if (!copies.TryGetValue(signature, out var alreadyHere))
                    copies[signature] = alreadyHere = new List<(string, double)>();

                alreadyHere.Add((
                    line,
                    elevationOf.TryGetValue(m.Groups[2].Value, out double e) ? e : double.MaxValue));
            }
        }

        var surplus = copies.Values
            .Where(c => c.Count > 1)
            .SelectMany(c => c.OrderBy(x => x.At).Skip(1).Select(x => x.Line))
            .ToHashSet(StringComparer.Ordinal);

        if (surplus.Count == 0) return 0;

        int removed = 0;
        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;
            removed += section.Lines.RemoveAll(surplus.Contains);
        }

        DropObjectsWithNoAssign();
        return removed;
    }

    /// <summary>
    /// Where each object stands on plan, as the joints its connectivity names.
    ///
    /// The e2k keeps joints in plan only — a point is x and y, and the storey the assign names
    /// supplies the third dimension — so this is the whole of an object's position.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(double X, double Y)>> PlanPointsOfObjects()
    {
        var joints = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        foreach (string line in LinesOf("POINT COORDINATES"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                line.TrimStart(), @"^POINT\s+""([^""]+)""\s+(-?[\d.eE+]+)\s+(-?[\d.eE+]+)");
            if (!m.Success) continue;
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                joints[m.Groups[1].Value] = (x, y);
        }

        var where = new Dictionary<string, IReadOnlyList<(double X, double Y)>>(StringComparer.Ordinal);

        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
        {
            var section = Find(header);
            if (section is null) continue;

            foreach (string line in section.Lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.TrimStart(), @"^(?:AREA|LINE)\s+""([^""]+)""\s+\w+\s+(.*)$");
                if (!m.Success) continue;

                var points = System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, @"""([^""]+)""")
                    .Select(j => j.Groups[1].Value)
                    .Where(joints.ContainsKey)
                    .Select(j => joints[j])
                    .ToList();

                if (points.Count > 0) where[m.Groups[1].Value] = points;
            }
        }

        return where;
    }

    /// <summary>
    /// How near two storeys have to be to be one floor, measured from this model rather than
    /// chosen: half a storey, where a storey is the median rise WITHIN ONE BUILDING'S STACK.
    ///
    /// Measuring it across the site measures the wrong thing. Where two towers interleave, the
    /// consecutive elevations in ETABS's one global storey list are the same floor drafted twice,
    /// so their median gap is half a storey and half of that is a quarter of one. Tower A's level
    /// 33 stands 37 in above tower B's — one floor by any reading — and a site-wide median called
    /// them different floors, which let an uncropped working view of floor 33 send both towers'
    /// 73 columns up tower B's stack.
    ///
    /// Half a storey is safe in the other direction by construction: two genuinely consecutive
    /// floors are a whole storey apart, so they can never be merged by it.
    /// </summary>
    public double SameFloorTolerance()
    {
        var gaps = new List<double>();

        foreach (var stack in ReadStories()
                     .GroupBy(s => BuildingTagOf(s.Name), StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.Select(s => s.Elevation).OrderBy(e => e).ToList()))
        {
            for (int i = 1; i < stack.Count; i++)
                if (stack[i] - stack[i - 1] > 12.0)
                    gaps.Add(stack[i] - stack[i - 1]);
        }

        gaps.Sort();
        return gaps.Count == 0 ? 12.0 : Math.Max(12.0, gaps[gaps.Count / 2] / 2.0);
    }

    /// <summary>
    /// Which floor each storey is part of. A site model gets a storey for every distinct floor
    /// elevation across every building, so one physical floor arrives as two or three storeys a
    /// few inches apart; each floor is named by the lowest of them.
    /// </summary>
    public IReadOnlyDictionary<string, string> FloorOfStorey()
    {
        double tolerance = SameFloorTolerance();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? floor = null;
        double at = double.NaN;

        foreach (var storey in ReadStories().OrderBy(s => s.Elevation))
        {
            if (floor is null || Math.Abs(storey.Elevation - at) > tolerance)
            {
                floor = storey.Name;
                at = storey.Elevation;
            }
            map[storey.Name] = floor;
        }

        return map;
    }

    /// <summary>
    /// Floors that hold a plate with no wall or column under them, and floors that hold walls or
    /// columns and no plate — read from the FINISHED model.
    ///
    /// Both were counted per storey and during composition, and both were therefore wrong twice
    /// over. Per storey, because a site model names one floor twice: after a merge the ground
    /// floor's plate sits on B-LEVEL 1 and its 108 columns on A-LEVEL 1, an inch and a half below,
    /// and the storey-wise reading called that a slab supported by air. During composition, because
    /// the cuts had not happened yet, so the list described a model nobody was going to receive.
    ///
    /// Reported by floor and after every cut, the list is the one an engineer would write down
    /// looking at the file she was sent.
    /// </summary>
    public E2kFloorGaps FloorGapDetails()
    {
        var floorOf = FloorOfStorey();
        var plates = PlateNames();
        var planOf = PlanPointsOfObjects();
        var storeysOf = StoreysByObject();

        string FloorNamed(string storey) => floorOf.TryGetValue(storey, out string? f) ? f : storey;

        var platesOnFloor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var membersOnFloor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var storeyOfObject = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (obj, storeys) in storeysOf)
        {
            if (!planOf.ContainsKey(obj)) continue;
            storeyOfObject[obj] = storeys[0];

            foreach (string floor in storeys.Select(FloorNamed).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var into = plates.Contains(obj) ? platesOnFloor : membersOnFloor;
                if (!into.TryGetValue(floor, out var list)) into[floor] = list = new List<string>();
                list.Add(obj);
            }
        }

        // UNDER, not merely on the same floor. Two towers reach level 28 within an inch of each
        // other and are still two buildings in the air: tower B's plate is not held up by tower A's
        // columns two hundred feet away. The shared ground floor is the opposite case — one slab
        // over both stacks — and only position tells them apart.
        bool AnyUnder(string plate, IReadOnlyList<string> members) =>
            planOf.TryGetValue(plate, out var outline)
            && outline.Count >= 3
            && members.Any(m => planOf.TryGetValue(m, out var pts)
                                && pts.Any(p => WithinOrNear(p, outline, 36.0)));

        var unsupported = new List<string>();
        foreach (var (floor, here) in platesOnFloor)
        {
            membersOnFloor.TryGetValue(floor, out var members);
            foreach (string plate in here)
                if (!AnyUnder(plate, members ?? new List<string>()))
                    unsupported.Add(storeyOfObject[plate]);
        }

        // MOST of a storey, not one member of it. Tower A's level 28 and tower B's stand 36 in
        // apart and are one floor by elevation; asking whether ANY of tower B's members fell under
        // tower A's plate found one at the edge and pronounced the whole storey floored. Tower B's
        // level 28 has no slab on the drawing at all — its outline would not close — and that is a
        // thing the engineer needs told.
        bool Covered(string member, IReadOnlyList<string> above) =>
            planOf.TryGetValue(member, out var pts)
            && above.Any(p => planOf.TryGetValue(p, out var outline)
                              && outline.Count >= 3
                              && pts.Any(q => WithinOrNear(q, outline, 36.0)));

        static bool IsMezzanineStorey(string storey)
            => storey.Contains("MEZZ", StringComparison.OrdinalIgnoreCase);

        var plateless = new List<string>();
        var mostlyUncovered = new List<string>();
        foreach (var (floor, here) in membersOnFloor)
        {
            platesOnFloor.TryGetValue(floor, out var above);
            above ??= new List<string>();

            foreach (var byStorey in here.GroupBy(m => storeyOfObject[m], StringComparer.OrdinalIgnoreCase))
            {
                var mine = byStorey.ToList();
                int covered = mine.Count(m => Covered(m, above));
                if (covered == 0) plateless.Add(byStorey.Key);
                else if (!IsMezzanineStorey(byStorey.Key)
                         && covered * 2 < mine.Count)
                    mostlyUncovered.Add(byStorey.Key);
            }
        }

        var order = ReadStories().Select(s => s.Name).ToList();
        int Rank(string s) => order.IndexOf(s) is var i && i < 0 ? int.MaxValue : i;

        return new E2kFloorGaps(
            plateless.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Rank).ToList(),
            mostlyUncovered.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Rank).ToList(),
            unsupported.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Rank).ToList());
    }

    public (IReadOnlyList<string> MostlyUncovered, IReadOnlyList<string> PlatesWithNoSupport) FloorGaps()
    {
        var gaps = FloorGapDetails();
        return (gaps.MostlyUncovered, gaps.PlatesWithNoSupport);
    }

    /// <summary>Whether a point lies in an outline, or near enough its edge to count as on it.</summary>
    private static bool WithinOrNear(
        (double X, double Y) point, IReadOnlyList<(double X, double Y)> outline, double margin)
    {
        var p = new DxfPoint(point.X, point.Y);
        var ring = outline.Select(q => new DxfPoint(q.X, q.Y)).ToList();

        if (ring.Count >= 3 && LoopGeometry.PointInPolygon(p, ring)) return true;

        // To the SEGMENT, not the infinite line through it. See LoopGeometry.DistanceToSegment:
        // the line reading called a column on the extension of a plate edge "on the plate",
        // however far outside it stood.
        for (int i = 0; i < ring.Count; i++)
            if (LoopGeometry.DistanceToSegment(p, ring[i], ring[(i + 1) % ring.Count]) <= margin)
                return true;

        return false;
    }

    /// <summary>The area objects that are floor plates rather than wall panels.</summary>
    public IReadOnlySet<string> PlateNames()
    {
        var plates = new HashSet<string>(StringComparer.Ordinal);
        var section = Find("AREA CONNECTIVITIES");
        if (section is null) return plates;

        foreach (string line in section.Lines)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                line.TrimStart(), @"^AREA\s+""([^""]+)""\s+FLOOR\b");
            if (m.Success) plates.Add(m.Groups[1].Value);
        }

        return plates;
    }

    /// <summary>
    /// Every object in the model against the storeys it stands on.
    ///
    /// One object can stand on several: a column drawn once on a sheet that covers a range of
    /// levels gets an assign per storey in the range.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StoreysByObject()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS", "POINT ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;

            foreach (string line in section.Lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.TrimStart(), @"^\w+ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
                if (!m.Success) continue;

                if (!found.TryGetValue(m.Groups[1].Value, out var storeys))
                    found[m.Groups[1].Value] = storeys = new List<string>();
                storeys.Add(m.Groups[2].Value);
            }
        }

        return found.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// What the saved model actually contains, read from the .e2k sections rather than from the
    /// composition that produced them.
    /// </summary>
    public E2kModelContents ReadContents(IReadOnlyDictionary<string, string>? sourceSheets = null)
    {
        var storeys = ReadStories().Select(s => s.Name).ToList();
        var points = GeneratedPointNames().ToHashSet(StringComparer.Ordinal);
        var referenced = ReferencedJoints();
        var storeysByObject = StoreysByObject();

        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREA\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }

        foreach (string raw in LinesOf("LINE CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^LINE\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }

        var members = storeys.ToDictionary(
            s => s,
            _ => new E2kStoreyContents(0, 0, 0),
            StringComparer.OrdinalIgnoreCase);
        var plates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var objects = new List<E2kObjectContents>();

        foreach (var (name, kind) in kinds)
        {
            storeysByObject.TryGetValue(name, out var onStoreys);
            onStoreys ??= Array.Empty<string>();

            if (sourceSheets is not null && sourceSheets.TryGetValue(name, out string? sourceSheet))
                objects.Add(new E2kObjectContents(name, kind, onStoreys, sourceSheet));
            else
                objects.Add(new E2kObjectContents(name, kind, onStoreys, null));

            foreach (string storey in onStoreys)
            {
                if (!members.TryGetValue(storey, out var had))
                    members[storey] = had = new E2kStoreyContents(0, 0, 0);

                if (name.StartsWith("KW", StringComparison.Ordinal)
                    && kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
                    members[storey] = had with { Walls = had.Walls + 1 };
                else if (name.StartsWith("KC", StringComparison.Ordinal)
                         && kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase))
                    members[storey] = had with { Columns = had.Columns + 1 };
                else if (name.StartsWith("KF", StringComparison.Ordinal)
                         && kind.Equals("FLOOR", StringComparison.OrdinalIgnoreCase))
                {
                    members[storey] = had with { Floors = had.Floors + 1 };
                    plates[storey] = plates.TryGetValue(storey, out int n) ? n + 1 : 1;
                }
            }
        }

        var orphanGenerated = points
            .Where(p => p.StartsWith("KP", StringComparison.Ordinal) && !referenced.Contains(p))
            .ToHashSet(StringComparer.Ordinal);

        return new E2kModelContents(
            storeys,
            kinds.Count(x => x.Key.StartsWith("KW", StringComparison.Ordinal)
                             && x.Value.Equals("PANEL", StringComparison.OrdinalIgnoreCase)),
            kinds.Count(x => x.Key.StartsWith("KC", StringComparison.Ordinal)
                             && x.Value.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)),
            kinds.Count(x => x.Key.StartsWith("KF", StringComparison.Ordinal)
                              && x.Value.Equals("FLOOR", StringComparison.OrdinalIgnoreCase)),
            kinds.Count(x => x.Key.StartsWith("KS", StringComparison.Ordinal)
                              && x.Value.Equals("PANEL", StringComparison.OrdinalIgnoreCase)),
            kinds.Count(x => x.Key.StartsWith("KO", StringComparison.Ordinal)
                              && x.Value.Equals("AREA", StringComparison.OrdinalIgnoreCase)),
            points.Count,
            members,
            plates,
            objects,
            referenced,
            orphanGenerated);
    }

    /// <summary>
    /// Drops unreferenced generated points left behind when a tower cut removes generated objects.
    /// Reference-model points are exempt because they are not this tool's geometry.
    /// </summary>
    public int DropGeneratedOrphanPoints(IEnumerable<string>? referencePointNames = null)
    {
        var referenced = ReferencedJoints();
        var reference = new HashSet<string>(referencePointNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        int removed = 0;

        var section = Find("POINT COORDINATES");
        if (section is null) return 0;

        removed += section.Lines.RemoveAll(line =>
        {
            var m = Regex.Match(line.TrimStart(), @"^POINT\s+""([^""]+)""");
            return m.Success
                && m.Groups[1].Value.StartsWith("KP", StringComparison.Ordinal)
                && !reference.Contains(m.Groups[1].Value)
                && !referenced.Contains(m.Groups[1].Value);
        });

        return removed;
    }

    private IEnumerable<string> GeneratedPointNames()
    {
        foreach (string raw in LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^POINT\s+""([^""]+)""");
            if (m.Success && m.Groups[1].Value.StartsWith("KP", StringComparison.Ordinal))
                yield return m.Groups[1].Value;
        }
    }

    public IReadOnlySet<string> PointNames()
    {
        var points = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^POINT\s+""([^""]+)""");
            if (m.Success) points.Add(m.Groups[1].Value);
        }
        return points;
    }

    private HashSet<string> ReferencedJoints()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREA\s+""[^""]+""\s+\w+\s+\d+\s+(.*)$");
            if (!m.Success) continue;
            foreach (Match joint in Regex.Matches(m.Groups[1].Value, @"""([^""]+)"""))
                referenced.Add(joint.Groups[1].Value);
        }

        foreach (string raw in LinesOf("LINE CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^LINE\s+""[^""]+""\s+\w+\s+""([^""]+)""\s+""([^""]+)""");
            if (!m.Success) continue;
            referenced.Add(m.Groups[1].Value);
            referenced.Add(m.Groups[2].Value);
        }

        return referenced;
    }

    /// <summary>
    /// Removes the named objects and every assign that stood on them, and reports how many
    /// objects went.
    ///
    /// Used to take one building's members out of a model of another. The storey cut works on
    /// names and a shared storey is named for nobody, so on LEVEL 2 -- one level, three buildings
    /// -- there is nothing in the file to cut on. The caller knows whose each member is because it
    /// knows which sheet drew it.
    /// </summary>
    public int DropObjects(IEnumerable<string> names)
    {
        var going = new HashSet<string>(names, StringComparer.Ordinal);
        if (going.Count == 0) return 0;

        int removed = 0;

        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
        {
            var section = Find(header);
            if (section is null) continue;

            removed += section.Lines.RemoveAll(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^\w+\s+""([^""]+)""");
                return m.Success && going.Contains(m.Groups[1].Value);
            });
        }

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS", "POINT ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;

            section.Lines.RemoveAll(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^\w+ASSIGN\s+""([^""]+)""");
                return m.Success && going.Contains(m.Groups[1].Value);
            });
        }

        return removed;
    }

    /// <summary>Drops every assign that names a storey the model no longer has.</summary>
    public int DropAssignsForMissingStoreys()
    {
        var known = new HashSet<string>(ReadStories().Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        int removed = 0;

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS", "POINT ASSIGNS" })
        {
            var section = Find(header);
            if (section is null) continue;

            removed += section.Lines.RemoveAll(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.Trim(), @"^\w+ASSIGN\s+""[^""]+""\s+""([^""]+)""");
                return m.Success && !known.Contains(m.Groups[1].Value);
            });
        }

        return removed;
    }

    /// <summary>
    /// Remove every AREA and LINE object that no longer has an assign, and report how many.
    ///
    /// Cutting a storey drops the assigns that stood on it; the OBJECT stays behind, defined and
    /// attached to nothing. When the cuts ran before composition that never happened — the members
    /// were simply never made. Composing the whole site once and cutting afterwards is what makes
    /// two models of one building agree, and this is its bill: 31168's YMCA came out of the shared
    /// composition with 1,416 wall panels defined and 338 assigned.
    ///
    /// A defined-but-unassigned area is not harmless. ETABS reads it, it has no storey, and it
    /// belongs to a building this model is explicitly not of.
    ///
    /// Points are left alone: they are cheap, shared between objects, and removing one still
    /// referenced elsewhere would break the file.
    /// </summary>
    public int DropObjectsWithNoAssign()
    {
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS" })
            foreach (string line in LinesOf(header))
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.Trim(), @"^\w+ASSIGN\s+""([^""]+)""");
                if (m.Success) assigned.Add(m.Groups[1].Value);
            }

        int removed = 0;

        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
        {
            var section = Find(header);
            if (section is null) continue;

            removed += section.Lines.RemoveAll(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.Trim(), @"^(?:AREA|LINE)\s+""([^""]+)""");

                // Only ours. A reference model's own object with no assign is the engineer's
                // business and was here before this tool ran.
                return m.Success
                    && m.Groups[1].Value.StartsWith("K", StringComparison.Ordinal)
                    && !assigned.Contains(m.Groups[1].Value);
            });
        }

        return removed;
    }

    /// <summary>
    /// How many objects THIS FILE holds with the given prefix, counted from the document rather
    /// than from what was composed. After a cut the two differ, and the report is read as the
    /// second when it is only ever entitled to be the first.
    /// </summary>
    public int CountGenerated(string keyword, string prefix)
    {
        int n = 0;
        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
            foreach (string raw in LinesOf(header))
            {
                string line = raw.TrimStart();
                if (!line.StartsWith(keyword + " ", StringComparison.Ordinal)) continue;

                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\w+\s+""([^""]+)""");
                if (m.Success && m.Groups[1].Value.StartsWith(prefix, StringComparison.Ordinal)) n++;
            }

        return n;
    }

    /// <summary>Names already used for points/areas/lines, so generated names never collide.</summary>
    /// <summary>
    /// How long the model's own length unit is, in inches, from its <c>CONTROLS UNITS</c> line.
    /// Null when the model does not say or uses a unit this tool has no factor for.
    ///
    /// The geometry written into a model has to be in the model's units, and every rule in this
    /// tool is stated in inches. Both jobs so far are inches — "KIP" "IN" "F" and "LB" "IN" "F" —
    /// so the two have never had to be reconciled.
    /// </summary>
    public double? LengthUnitInInches()
    {
        foreach (string raw in LinesOf("CONTROLS"))
        {
            var m = Regex.Match(raw.Trim(), @"^UNITS\s+""[^""]*""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            return m.Groups[1].Value.ToUpperInvariant() switch
            {
                "IN" => 1.0,
                "FT" => 12.0,
                "MM" => 1.0 / 25.4,
                "CM" => 1.0 / 2.54,
                "M"  => 1000.0 / 25.4,
                _    => null,
            };
        }
        return null;
    }

    /// <summary>
    /// The largest joint offset each panel carries — for a spandrel, its depth. A joint is a plan
    /// position, and the third value on its POINT line is how far it is raised above the storey.
    /// </summary>
    public Dictionary<string, double> PanelJointOffsets()
    {
        var offsetOfJoint = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.Trim(), @"^POINT\s+""([^""]+)""\s+-?[\d.]+\s+-?[\d.]+\s+(-?[\d.]+)");
            if (m.Success && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                offsetOfJoint[m.Groups[1].Value] = z;
        }

        var byPanel = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""([^""]+)""\s+PANEL\s+\d+\s+((?:""[^""]+""\s*)+)");
            if (!m.Success) continue;

            double deepest = 0;
            foreach (Match j in Regex.Matches(m.Groups[2].Value, @"""([^""]+)"""))
                if (offsetOfJoint.TryGetValue(j.Groups[1].Value, out double z) && z > deepest) deepest = z;

            if (deepest > 0) byPanel[m.Groups[1].Value] = deepest;
        }
        return byPanel;
    }

    public HashSet<string> ExistingObjectNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string header, string keyword)
        {
            foreach (string raw in LinesOf(header))
            {
                string line = raw.Trim();
                if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                int a = line.IndexOf('"');
                int b = a < 0 ? -1 : line.IndexOf('"', a + 1);
                if (a >= 0 && b > a) names.Add(line.Substring(a + 1, b - a - 1));
            }
        }

        Collect("POINT COORDINATES", "POINT");
        Collect("LINE CONNECTIVITIES", "LINE");
        Collect("AREA CONNECTIVITIES", "AREA");
        return names;
    }

    /// <summary>
    /// Storeys the model ALREADY has a floor on.
    ///
    /// A generated model is the engineer's own model with geometry added, so "this storey has no
    /// floor plate" has to mean no floor from anyone. Counting only generated plates told the
    /// engineer that thirteen storeys of 31138 had no plate and no diaphragm when every one of them
    /// carries her own floors — twelve to twenty-six of them, most already assigned a diaphragm.
    /// On a gap-fill project that is not a small error: it is the whole building reported missing.
    /// </summary>
    public HashSet<string> StoreysWithFloors()
    {
        var floors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in LinesOf("AREA CONNECTIVITIES"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("AREA", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = System.Text.RegularExpressions.Regex.Match(line, @"^AREA\s+""([^""]+)""\s+(\w+)");
            if (!parts.Success || !parts.Groups[2].Value.Equals("FLOOR", StringComparison.OrdinalIgnoreCase)) continue;
            floors.Add(parts.Groups[1].Value);
        }

        var storeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in LinesOf("AREA ASSIGNS"))
        {
            string line = raw.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^AREAASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (m.Success && floors.Contains(m.Groups[1].Value)) storeys.Add(m.Groups[2].Value);
        }
        return storeys;
    }

    /// <summary>
    /// An existing wall or slab section of the given thickness, if the model already defines one.
    /// Reusing the project's own sections means generated members carry the real concrete mix and
    /// a name the engineer recognises, instead of a new section with a borrowed material.
    /// </summary>
    /// <param name="propType">"Wall" or "Slab".</param>
    public string? FindShellProperty(string propType, double thickness, double tolerance = 0.25)
    {
        var matches = new List<string>();

        foreach (string raw in LinesOf(propType.Equals("Wall", StringComparison.OrdinalIgnoreCase)
                     ? "WALL PROPERTIES" : "SLAB PROPERTIES"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("SHELLPROP", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line.Contains($"PROPTYPE  \"{propType}\"", StringComparison.OrdinalIgnoreCase)) continue;

            int t = line.IndexOf(propType.Equals("Wall", StringComparison.OrdinalIgnoreCase)
                ? "WALLTHICKNESS" : "SLABTHICKNESS", StringComparison.OrdinalIgnoreCase);
            if (t < 0) continue;

            string tail = line[(t + "WALLTHICKNESS".Length)..].Trim();
            string token = new(tail.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double found)) continue;
            if (Math.Abs(found - thickness) > tolerance) continue;

            int a = line.IndexOf('"');
            int b = a < 0 ? -1 : line.IndexOf('"', a + 1);
            if (a >= 0 && b > a) matches.Add(line.Substring(a + 1, b - a - 1));
        }

        // Sections imported from Revit carry the project's own concrete; ETABS's template
        // sections (Wall1, Slab1) carry its default 4000 psi and must not be preferred.
        return matches.FirstOrDefault(m => m.StartsWith("Rvt-", StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault(m => !m.Equals("Wall1", StringComparison.OrdinalIgnoreCase)
                                        && !m.Equals("Slab1", StringComparison.OrdinalIgnoreCase)
                                        && !m.Equals("Plank1", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A concrete material defined in the model, preferring one that reads like a wall mix.</summary>
    public string? FindConcreteMaterial(string? preferredContains = null)
    {
        var concrete = new List<string>();
        foreach (string raw in LinesOf("MATERIAL PROPERTIES"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("MATERIAL", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line.Contains("TYPE \"Concrete\"", StringComparison.OrdinalIgnoreCase)) continue;

            int a = line.IndexOf('"');
            int b = a < 0 ? -1 : line.IndexOf('"', a + 1);
            // A material is declared over several lines (type, moduli, strength), so the
            // same name arrives more than once.
            if (a >= 0 && b > a)
            {
                string name = line.Substring(a + 1, b - a - 1);
                if (!concrete.Contains(name, StringComparer.OrdinalIgnoreCase)) concrete.Add(name);
            }
        }

        if (concrete.Count == 0) return null;

        if (preferredContains is not null)
        {
            var candidates = concrete
                .Where(m => m.Contains(preferredContains, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Several mixes can serve one element type — 45 and 65 MPa walls, say. Pick the one
            // the model actually uses most, rather than whichever is declared first.
            if (candidates.Count > 1)
            {
                var usage = candidates.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
                foreach (string header in new[] { "WALL PROPERTIES", "SLAB PROPERTIES", "FRAME SECTIONS" })
                    foreach (string raw in LinesOf(header))
                        foreach (string candidate in candidates)
                            if (raw.Contains($"\"{candidate}\"", StringComparison.OrdinalIgnoreCase))
                                usage[candidate]++;

                return usage.OrderByDescending(u => u.Value).First().Key;
            }

            if (candidates.Count == 1) return candidates[0];
        }

        return concrete.FirstOrDefault(m => m.Contains("Wall", StringComparison.OrdinalIgnoreCase)) ?? concrete[0];
    }
}

/// <summary>A storey and the elevations that bound it, in model units (inches).</summary>
public sealed record StoryLevel(string Name, double Elevation, double ElevationBelow);

