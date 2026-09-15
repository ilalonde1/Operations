using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A PLATE ON EVERY STOREY is the engineers' own bar for a usable model ("as long as I have the vertical and
/// overall the shape of the slab, it's good enough for me to start" - 31 Aug; plan WP6a, 2026-09-15). This
/// is the instrument for it: for one built set, every storey of its model classed as carrying a plate or as
/// not, and for the ones that do not, WHERE the plate was lost - no sheet was placed on the storey (a
/// placement class), the placed sheets read no closed ring (a reading class), or rings were read and none
/// became a plate (a composer class). Corpus-wide the three counts say which class to work on first.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the finished .e2k's storeys and FLOOR areas by their storey assignment; the sheet ledger's
/// per-sheet ring count (slabs) and the storeys each sheet was placed on. WHAT IT DOES NOT: why a ring did not
/// close (the report's per-sheet lines say), whether a plate is the RIGHT shape, or a plate borrowed from a
/// neighbour (counted as a plate, since the model carries one). A same-class fault it would not catch: a
/// plate on the wrong storey - it counts plates where the model puts them.
/// </remarks>
public static class PlateCoverage
{
    public enum Class
    {
        /// <summary>The storey carries at least one FLOOR area.</summary>
        Plate,
        /// <summary>No plan sheet of the set was placed on the storey.</summary>
        NoSheetPlaced,
        /// <summary>Sheets were placed on the storey and every one read zero closed rings.</summary>
        NoRingRead,
        /// <summary>Rings were read on a placed sheet and none became a plate on the storey.</summary>
        RingsReadNoPlate,
    }

    public sealed record Storey(string Name, Class Class, int Plates, int PlacedSheets, int RingsRead);

    private static readonly Regex StoryLine = new(@"^\s*STORY\s+""(?<name>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex FloorArea = new(@"^\s*AREA\s+""(?<name>[^""]+)""\s+FLOOR\b", RegexOptions.Compiled);
    private static readonly Regex AreaAssign = new(@"^\s*AREAASSIGN\s+""(?<name>[^""]+)""\s+""(?<storey>[^""]+)""", RegexOptions.Compiled);

    /// <summary>Every storey of the model, top to bottom as the .e2k lists them, with its class.</summary>
    public static IReadOnlyList<Storey> Classify(IEnumerable<string> e2kLines, IEnumerable<CorpusAnalyzer.SheetRow> sheets)
    {
        ArgumentNullException.ThrowIfNull(e2kLines);
        ArgumentNullException.ThrowIfNull(sheets);
        var storeys = new List<string>();
        var floorAreas = new HashSet<string>(StringComparer.Ordinal);
        var platesOn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in e2kLines)
        {
            var s = StoryLine.Match(line);
            if (s.Success) { storeys.Add(s.Groups["name"].Value); continue; }
            var f = FloorArea.Match(line);
            if (f.Success) { floorAreas.Add(f.Groups["name"].Value); continue; }
            var a = AreaAssign.Match(line);
            if (a.Success && floorAreas.Contains(a.Groups["name"].Value))
                platesOn[a.Groups["storey"].Value] = platesOn.GetValueOrDefault(a.Groups["storey"].Value) + 1;
        }
        // the sheets placed on each storey, and the rings each read
        var placed = new Dictionary<string, List<CorpusAnalyzer.SheetRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            if (string.IsNullOrWhiteSpace(sheet.Storeys)) continue;
            foreach (string name in sheet.Storeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!placed.TryGetValue(name, out var list)) placed[name] = list = new List<CorpusAnalyzer.SheetRow>();
                list.Add(sheet);
            }
        }
        var result = new List<Storey>();
        foreach (string name in storeys)
        {
            int plates = platesOn.GetValueOrDefault(name);
            var on = placed.GetValueOrDefault(name) ?? new List<CorpusAnalyzer.SheetRow>();
            if (string.Equals(name, "Base", StringComparison.OrdinalIgnoreCase) && plates == 0 && on.Count == 0) continue;   // ETABS's base is not a storey the drawings draw
            int rings = on.Sum(r => r.Slabs);
            var cls = plates > 0 ? Class.Plate
                : on.Count == 0 ? Class.NoSheetPlaced
                : rings == 0 ? Class.NoRingRead
                : Class.RingsReadNoPlate;
            result.Add(new Storey(name, cls, plates, on.Count, rings));
        }
        return result;
    }
}
