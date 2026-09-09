namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// The differential's eyes: how many entities each layer of a DXF carries, and which layers moved
/// between two folders of DXFs written from the same pages. A change to the intake is supposed to
/// alter one property and nothing else; this is what says whether it did.
/// </summary>
/// <remarks>
/// Every intake step of 2026-09-08 was verified with a scratch Python script doing this; the rule
/// that everything ships as C# applies to the verifier too. Counts are of entities in the ENTITIES
/// section only; a POLYLINE counts once and its VERTEX records not at all, so a slab with 40 vertices
/// and a slab with 4 are both one SLAB. WHAT IT DOES NOT SEE: a change inside an entity — a moved
/// vertex, a different length, a renamed axis — on a layer whose count did not move. Where that
/// matters the check is a hash or a geometry comparison, not a census.
/// </remarks>
public static class DxfLayerCensus
{
    /// <summary>Entity types counted; VERTEX and SEQEND belong to their POLYLINE and are not.</summary>
    public static readonly IReadOnlySet<string> Entities = new HashSet<string>(StringComparer.Ordinal)
    {
        "LINE", "LWPOLYLINE", "POLYLINE", "CIRCLE", "ARC", "TEXT", "MTEXT", "HATCH", "POINT", "INSERT", "SPLINE", "ELLIPSE", "SOLID", "3DFACE",
    };

    public sealed record FileDiff(string Name, IReadOnlyDictionary<string, (int Before, int After)> Changed, bool MissingBefore, bool MissingAfter)
    {
        public bool Identical => Changed.Count == 0 && !MissingBefore && !MissingAfter;
    }

    /// <summary>Entities per layer in the ENTITIES section of a DXF given as its lines.</summary>
    public static IReadOnlyDictionary<string, int> Of(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? code = null;
        bool inEntities = false, expectSectionName = false, inEntity = false;
        string layer = "0";
        void Commit() { if (inEntity) { counts[layer] = counts.GetValueOrDefault(layer) + 1; inEntity = false; } }
        foreach (var raw in lines)
        {
            if (code is null) { code = raw.Trim(); continue; }
            string value = raw.Trim();
            switch (code)
            {
                case "0":
                    if (value == "SECTION") { expectSectionName = true; }
                    else if (value == "ENDSEC") { Commit(); inEntities = false; }
                    else if (inEntities)
                    {
                        Commit();
                        if (Entities.Contains(value)) { inEntity = true; layer = "0"; }
                    }
                    break;
                case "2" when expectSectionName:
                    inEntities = value == "ENTITIES";
                    expectSectionName = false;
                    break;
                case "8" when inEntity:
                    layer = value;
                    break;
            }
            code = null;
        }
        Commit();
        return counts;
    }

    public static IReadOnlyDictionary<string, int> OfFile(string path) => Of(File.ReadLines(path));

    /// <summary>Every DXF name in either folder, with the layers whose counts differ.</summary>
    public static IReadOnlyList<FileDiff> Compare(string beforeDir, string afterDir)
    {
        var names = Directory.EnumerateFiles(beforeDir, "*.dxf").Select(Path.GetFileName)
            .Concat(Directory.EnumerateFiles(afterDir, "*.dxf").Select(Path.GetFileName))
            .Where(n => n is not null).Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var diffs = new List<FileDiff>();
        foreach (var name in names)
        {
            string b = Path.Combine(beforeDir, name), a = Path.Combine(afterDir, name);
            bool missingBefore = !File.Exists(b), missingAfter = !File.Exists(a);
            var before = missingBefore ? new Dictionary<string, int>() : OfFile(b);
            var after = missingAfter ? new Dictionary<string, int>() : OfFile(a);
            var changed = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal)
                .Where(l => before.GetValueOrDefault(l) != after.GetValueOrDefault(l))
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToDictionary(l => l, l => (before.GetValueOrDefault(l), after.GetValueOrDefault(l)), StringComparer.Ordinal);
            diffs.Add(new FileDiff(name, changed, missingBefore, missingAfter));
        }
        return diffs;
    }

    /// <summary>One line per file and a summary: which layers moved, and on how many files nothing did.</summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<FileDiff> diffs)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        var lines = new List<string>();
        foreach (var d in diffs)
        {
            if (d.MissingBefore) { lines.Add($"{d.Name,-22} only in AFTER"); continue; }
            if (d.MissingAfter) { lines.Add($"{d.Name,-22} only in BEFORE"); continue; }
            lines.Add(d.Identical ? $"{d.Name,-22} identical"
                : $"{d.Name,-22} " + string.Join("   ", d.Changed.Select(kv => $"{kv.Key} {kv.Value.Before}->{kv.Value.After}")));
        }
        var moved = diffs.SelectMany(d => d.Changed.Keys).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal).ToList();
        lines.Add($"{diffs.Count(d => d.Identical)} of {diffs.Count} identical" + (moved.Count == 0 ? "" : $"; layers that moved: {string.Join(", ", moved)}"));
        return lines;
    }
}
