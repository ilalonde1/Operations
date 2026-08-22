using System.Globalization;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One level of the building, as Revit names it and where it sits.</summary>
/// <param name="Name">The level's name. It is also what a plan sheet must be titled to land on it.</param>
/// <param name="Elevation">Height above the project base, in the model's own length unit.</param>
public sealed record BuildingLevel(string Name, double Elevation);

/// <summary>
/// Builds the model an ETABS export would have given us, out of a list of levels.
///
/// The generator has always demanded an engineer's .e2k as input, which reads as absurd — a tool
/// that makes ETABS models needing an ETABS model to run — and is the reason it has only ever run
/// on the two jobs that had one. Measured on 31168: of everything in the output, 98% was read off
/// the drawings and the reference supplied 25 members. What it was actually carrying was three
/// things, and only one of them is a fact about the job:
///
///   levels and their elevations   — the job's own, and plans are 2D so they cannot come from there
///   materials and sections        — office standards, the same on every job
///   grids                         — already drawn, on JBP_G_GRAPH2, on every sheet
///
/// Levels are what Revit knows and hands over: KOR.Drafter.Bridge already reads GenLevel off every
/// plan view it exports. So the input becomes a folder of drawings and a list of levels, both of
/// which come out of Revit in one pass, and CSiXRevit leaves the path entirely.
///
/// The sections here are a floor, not a ceiling. The composer defines whatever thickness a drawing
/// turns out to need — KOR-W12, KOR-S8 — so this only has to give it one concrete material to
/// build them from and somewhere to put them.
/// </summary>
public static class E2kShellBuilder
{
    /// <summary>
    /// KOR's default mix, used where a job brings no template of its own. Named so that an
    /// engineer opening the model can see at a glance that it is a default and not her spec.
    /// </summary>
    public const string DefaultConcrete = "KOR-DEFAULT-CONCRETE";

    /// <summary>
    /// Reads a level list as "name, elevation" per line: what a Revit level table looks like when
    /// it is written down. Blank lines and anything after # are ignored, and a header row naming
    /// its columns is skipped, so a table pasted out of a spreadsheet works unedited.
    /// </summary>
    public static IReadOnlyList<BuildingLevel> ParseLevels(IEnumerable<string> lines)
    {
        var levels = new List<BuildingLevel>();
        foreach (string raw in lines)
        {
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            // Comma or tab: a pasted spreadsheet gives tabs, a saved CSV gives commas.
            int split = line.LastIndexOfAny(new[] { ',', '\t' });
            if (split <= 0) continue;

            string name = line[..split].Trim().Trim('"');
            string value = line[(split + 1)..].Trim();
            if (name.Length == 0) continue;

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double elevation))
                continue;   // the header row lands here, which is how it gets skipped

            levels.Add(new BuildingLevel(name, elevation));
        }

        if (levels.Count == 0)
            throw new InvalidOperationException(
                "No levels could be read. Each line should be a level name and its elevation, " +
                "separated by a comma or a tab — for example: LEVEL 2, 144");

        var duplicate = levels.GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"The level list names '{duplicate.Key}' more than once. A plan sheet is placed by " +
                "level name, so two levels sharing one is not a thing this can resolve.");

        return levels;
    }

    /// <summary>
    /// The shell: storeys, one concrete material, and nothing else. Everything a model needs
    /// beyond this the composer writes as it discovers what the drawings contain.
    /// </summary>
    /// <param name="levels">The building's levels. Order does not matter; elevation decides.</param>
    /// <param name="unitName">The length unit the elevations are given in, as ETABS names it.</param>
    public static E2kDocument FromLevels(IReadOnlyList<BuildingLevel> levels, string unitName = "in")
    {
        if (levels.Count == 0)
            throw new InvalidOperationException("A model needs at least one level.");

        var ordered = levels.OrderBy(l => l.Elevation).ToList();

        // The base sits at the lowest level, not below it. Inventing a gap underneath puts the
        // lowest storey's walls on stilts of exactly that height — the failure a real export
        // causes when its base is parked a thousand feet down.
        double baseElevation = ordered[0].Elevation;

        var lines = new List<string>
        {
            "$ PROGRAM INFORMATION",
            "  PROGRAM  \"ETABS\"  VERSION \"21.2.0\"",
            string.Empty,
            "$ CONTROLS",
            $"  UNITS  \"Kip\"  \"{unitName}\"",
            string.Empty,
            "$ STORIES - IN SEQUENCE FROM TOP",
        };

        // ETABS lists storeys from the top down, each carrying the height of the storey below it.
        for (int i = ordered.Count - 1; i >= 1; i--)
        {
            double height = ordered[i].Elevation - ordered[i - 1].Elevation;
            lines.Add($"  STORY \"{ordered[i].Name}\"  HEIGHT {Trim(height)}");
        }

        // The lowest level is a storey in its own right and must not be swallowed by the base:
        // it carries the parkade slab and its walls. It is given no height of its own, and the
        // base beneath it carries the elevation.
        lines.Add($"  STORY \"{ordered[0].Name}\"  HEIGHT 0");
        lines.Add($"  STORY \"Base\"  ELEV {Trim(baseElevation)}");
        lines.Add(string.Empty);

        lines.Add("$ MATERIAL PROPERTIES");
        lines.Add($"  MATERIAL  \"{DefaultConcrete}\"  TYPE \"Concrete\"  GRADE \"KOR default\"");
        lines.Add(string.Empty);

        lines.Add("$ END OF MODEL FILE");
        lines.Add(string.Empty);

        return E2kDocument.Parse(lines);
    }

    private static string Trim(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
