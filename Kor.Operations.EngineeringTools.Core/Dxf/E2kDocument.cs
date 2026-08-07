using System.Globalization;
using System.Text;

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

        foreach (string raw in LinesOf("STORIES"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("STORY", StringComparison.OrdinalIgnoreCase)) continue;

            int firstQuote = line.IndexOf('"');
            int lastQuote = firstQuote < 0 ? -1 : line.IndexOf('"', firstQuote + 1);
            if (firstQuote < 0 || lastQuote < 0) continue;

            string name = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

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
        var result = new List<StoryLevel>();
        double elevation = 0;
        for (int i = parsed.Count - 1; i >= 0; i--)
        {
            var (name, height) = parsed[i];
            double below = elevation;
            elevation += height;
            result.Add(new StoryLevel(name, elevation, below));
        }

        result.Reverse();
        return result;
    }

    /// <summary>Names already used for points/areas/lines, so generated names never collide.</summary>
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
