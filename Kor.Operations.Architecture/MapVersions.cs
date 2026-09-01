// EVERY DRAW IS KEPT, AND EVERY DRAW CAN BE COMPARED WITH THE ONE BEFORE IT.
//
// A map that only ever shows the present answers "what is this system". Keeping the versions answers
// the more useful question — "what changed" — which is what a person actually asks after a week of
// work they half remember.
//
// WHAT IS KEPT IS THE DRAWING AND A SUMMARY, NOT THE WHOLE MODEL. The full model is 2.7 MB of JSON
// and was deleted from this repo on purpose: a committed intermediate that rots is worse than no
// intermediate. The summary here is a few kilobytes — the counts, and the NAMES of the things a
// person would ask about — which is everything a comparison needs and nothing it does not.
//
// A text diff of two full models says everything moved, because ordering and coordinates shift. A
// comparison of named sets says which project appeared, which verb went, which duplication got
// worse. That is the difference between a diff and an answer.

using System.Globalization;
using System.Text.Json;

namespace Kor.Operations.Architecture;

/// <summary>The comparable shape of one draw. Small on purpose.</summary>
public sealed record MapSummary(
    int Version,
    string DrawnUtc,
    string Root,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Verbs,
    IReadOnlyList<string> Externals,
    IReadOnlyList<string> Scripts,
    IReadOnlyDictionary<string, int> TypesByProject,
    IReadOnlyDictionary<string, double> DuplicateSimilarity);

/// <summary>One line of "what changed", ready to print.</summary>
public sealed record MapChange(string Section, string Detail);

public static class MapVersions
{
    public const string FolderPrefix = "v";

    public static MapSummary Summarise(ArchModel m, string root, int version, DateTime drawnUtc)
        => new(
            version,
            drawnUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            root,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["projects"] = m.Projects.Count,
                ["types"] = m.Types.Count,
                ["mention edges"] = m.Mentions.Count,
                ["format edges"] = m.Formats.Count,
                ["external systems"] = m.Externals.Count,
                ["CLI verbs"] = m.Verbs.Count,
                ["scripts"] = m.Scripts.Count,
                ["unreferenced scripts"] = m.Scripts.Count(s => s.ReferencedBy == 0),
                ["duplicate names"] = m.Duplicates.Count,
                ["dependency cycles"] = m.Cycles.Count,
                ["files"] = m.Stats.Files,
                ["lines"] = m.Stats.Lines,
            },
            Sorted(m.Projects.Select(p => p.Name)),
            Sorted(m.Verbs.Select(v => v.Verb)),
            Sorted(m.Externals.Select(e => e.Name)),
            Sorted(m.Scripts.Select(s => s.Path)),
            m.Types.GroupBy(t => t.Project, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            m.Duplicates.ToDictionary(d => d.Name, d => d.Similarity, StringComparer.Ordinal));

    private static List<string> Sorted(IEnumerable<string> xs)
        => xs.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    // ---- where versions live -----------------------------------------------------------------

    /// <summary>The next version number under <paramref name="root"/>, and the folder for it.</summary>
    public static (int Version, string Folder) NextFolder(string root)
    {
        int highest = ExistingVersions(root).Select(v => v.Version).DefaultIfEmpty(0).Max();
        int next = highest + 1;
        return (next, Path.Combine(root, $"{FolderPrefix}{next:D3}"));
    }

    public static IReadOnlyList<(int Version, string Folder)> ExistingVersions(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<(int, string)>();

        var found = new List<(int, string)>();
        foreach (string dir in Directory.EnumerateDirectories(root, FolderPrefix + "*"))
        {
            string name = Path.GetFileName(dir);
            if (int.TryParse(name.AsSpan(FolderPrefix.Length), NumberStyles.None,
                             CultureInfo.InvariantCulture, out int n))
                found.Add((n, dir));
        }
        return found.OrderBy(x => x.Item1).ToList();
    }

    public static void Write(MapSummary summary, string folder)
        => File.WriteAllText(
            Path.Combine(folder, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })
                .ReplaceLineEndings("\n") + "\n");

    /// <summary>The change report that lives IN the version folder.
    ///
    /// Printing it to a console the user closes is not keeping it. Every version except the first
    /// carries a CHANGES.txt saying what moved since the one before, so the history answers "what
    /// happened in that fortnight" without anything needing to be re-run or re-derived. The first
    /// version says so rather than being left without a file, because an absent file reads as a
    /// failure and "this is the baseline" is a real answer.</summary>
    public static string WriteChanges(string folder, MapSummary current, MapSummary? previous)
    {
        var text = new System.Text.StringBuilder();
        text.Append("KOR Application Map — v").Append(current.Version.ToString("D3", CultureInfo.InvariantCulture))
            .Append("    drawn ").Append(current.DrawnUtc).AppendLine()
            .Append("Codebase: ").Append(current.Root).AppendLine()
            .AppendLine();

        if (previous is null)
        {
            text.AppendLine("FIRST VERSION — the baseline. Nothing to compare against yet.")
                .AppendLine()
                .AppendLine("Where it starts:");
            foreach (var (key, value) in current.Counts.OrderBy(k => k.Key, StringComparer.Ordinal))
                text.Append("    ").Append(key.PadRight(22)).Append(value.ToString("N0", CultureInfo.InvariantCulture)).AppendLine();
        }
        else
        {
            var changes = Compare(previous, current);
            text.Append("Since v").Append(previous.Version.ToString("D3", CultureInfo.InvariantCulture))
                .Append(", drawn ").Append(previous.DrawnUtc).AppendLine();
            text.AppendLine();

            if (changes.Count == 0)
            {
                text.AppendLine("NOTHING CHANGED. The codebase and this map agree exactly with the previous version.");
            }
            else
            {
                text.Append(changes.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" change(s).").AppendLine();
                string? section = null;
                foreach (var c in changes)
                {
                    if (c.Section != section) { text.AppendLine(c.Section.ToUpperInvariant()); section = c.Section; }
                    text.Append("    ").AppendLine(c.Detail);
                }
            }
        }

        string path = Path.Combine(folder, "CHANGES.txt");
        File.WriteAllText(path, text.ToString().ReplaceLineEndings("\r\n"));
        return path;
    }

    public static MapSummary? Read(string folder)
    {
        string path = Path.Combine(folder, "summary.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<MapSummary>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    // ---- what changed ------------------------------------------------------------------------

    /// <summary>What moved between two draws, as lines a person can read.
    ///
    /// Counts first, then the named comings and goings, then the duplication whose similarity moved
    /// — that last one being the only number here anybody acts on.</summary>
    public static List<MapChange> Compare(MapSummary before, MapSummary after)
    {
        var changes = new List<MapChange>();

        foreach (var (key, then) in before.Counts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!after.Counts.TryGetValue(key, out int now) || now == then) continue;
            changes.Add(new MapChange("counts",
                $"{key}: {then:N0} → {now:N0}  ({now - then:+#;-#;0})"));
        }

        Sets(changes, "projects", before.Projects, after.Projects);
        Sets(changes, "CLI verbs", before.Verbs, after.Verbs);
        Sets(changes, "external systems", before.Externals, after.Externals);
        Sets(changes, "scripts", before.Scripts, after.Scripts);
        Sets(changes, "duplicated names",
             before.DuplicateSimilarity.Keys.ToList(), after.DuplicateSimilarity.Keys.ToList());

        foreach (string name in before.DuplicateSimilarity.Keys.Intersect(after.DuplicateSimilarity.Keys, StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            double then = before.DuplicateSimilarity[name], now = after.DuplicateSimilarity[name];
            if (Math.Abs(now - then) <= 0.005) continue;
            changes.Add(new MapChange("duplication",
                $"{name}: {then:P0} → {now:P0} alike"));
        }

        foreach (string project in before.TypesByProject.Keys.Union(after.TypesByProject.Keys, StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            before.TypesByProject.TryGetValue(project, out int then);
            after.TypesByProject.TryGetValue(project, out int now);
            if (then == now) continue;
            changes.Add(new MapChange("types by project",
                $"{project}: {then} → {now}  ({now - then:+#;-#;0})"));
        }

        return changes;
    }

    private static void Sets(List<MapChange> into, string section,
                             IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var was = before.ToHashSet(StringComparer.Ordinal);
        var now = after.ToHashSet(StringComparer.Ordinal);
        foreach (string n in now.Except(was, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            into.Add(new MapChange(section, "+ " + n));
        foreach (string n in was.Except(now, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            into.Add(new MapChange(section, "− " + n));
    }
}
