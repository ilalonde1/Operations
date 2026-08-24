using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

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

    public IReadOnlyList<string> KeepOnlyTower(string tower)
    {
        var section = Find("STORIES");
        if (section is null) return Array.Empty<string>();

        string keep = tower.Trim().ToUpperInvariant();
        var stories = ReadStories().OrderBy(s => s.Elevation).ToList();
        if (stories.Count == 0) return Array.Empty<string>();

        var dropped = stories
            .Where(s => BuildingTagOf(s.Name) is var t && t.Length > 0 && t != keep)
            .Select(s => s.Name)
            .ToList();
        if (dropped.Count == 0) return dropped;

        var retained = stories.Where(s => !dropped.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
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

