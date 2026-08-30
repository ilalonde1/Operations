// THE PARTS THAT ARE NOT C#.
//
// A map built only from .csproj and .cs says this system is 62 projects of C#. It is not. Work is
// done here by PowerShell that deploys, by Python that checks a shipped PDF, by SQL that migrates,
// by a batch file nobody has opened in a year — and none of it appeared anywhere on the map, so
// none of it could be reviewed, retired or even counted.
//
// Ian: "an accounting of tools should be part of this - along with any other nooks and crannies -
// that's what this visio engine should be producing."
//
// So every script in the tree is inventoried, and the useful column is not that it exists but
// WHETHER ANYTHING CALLS IT. A script nothing references is either dead or is run by a human from
// memory, and those are the two cases worth knowing about. The check is deliberately generous —
// a mention of the file name anywhere counts — because the failure that matters is calling a live
// script dead, not the other way round.

using System.Text.RegularExpressions;

namespace Kor.Operations.Architecture;

/// <summary>A script, config or data file that is part of the system but not part of any project.
/// <paramref name="ReferencedBy"/> counts the OTHER files that name it.</summary>
public sealed record ArchScript(
    string Path,
    string Kind,
    int Lines,
    long Bytes,
    int ReferencedBy,
    IReadOnlyList<string> ReferencedIn);

public static class ScriptInventory
{
    /// <summary>Extension to the kind of work it does. Anything not listed is not inventoried —
    /// this is an accounting of things that RUN, not of every file in the tree.</summary>
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ps1"] = "PowerShell",
        [".psm1"] = "PowerShell module",
        [".py"] = "Python",
        [".sql"] = "SQL",
        [".cmd"] = "batch",
        [".bat"] = "batch",
        [".sh"] = "shell",
        [".yml"] = "config",
        [".yaml"] = "config",
    };

    /// <summary>A numbered migration under a Schema folder — `277_ResolveIntelPersonResurrect.sql`.
    /// These are applied in ordinal order by a runner that scans the directory, so NOTHING names them
    /// and counting them as unreferenced is a false finding: it reported 252 dead files that are the
    /// live schema of the BD database.</summary>
    private static readonly Regex Migration = new(
        @"(^|/)Schema/\d+_[^/]+\.sql$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<ArchScript> Collect(string root)
    {
        var found = new List<(string Rel, string Full, string Kind)>();

        foreach (var (ext, kind) in Kinds)
            foreach (string path in Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Skip(rel)) continue;
                found.Add((rel, path, Migration.IsMatch(rel) ? "SQL migration" : kind));
            }

        // ONE PASS OVER THE HAYSTACK, not one pass per needle. A file-name search repeated for each
        // of ~200 scripts across ~4,000 candidate files is 800,000 reads; this reads each possible
        // caller once and asks which of the names it contains.
        //
        // Migrations are not needles: nothing names them by design, so searching for 252 of them is
        // pure cost for an answer already known.
        var names = found
            .Where(f => f.Kind != "SQL migration")
            .GroupBy(f => Path.GetFileName(f.Rel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Rel).ToList(), StringComparer.OrdinalIgnoreCase);

        var referencedIn = names.Keys
            .ToDictionary(n => n, _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
                          StringComparer.OrdinalIgnoreCase);

        foreach (string path in CallerCandidates(root))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }

            foreach (string name in names.Keys)
            {
                if (!text.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                // A file naming itself is not a reference.
                if (names[name].Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                referencedIn[name].Add(rel);
            }
        }

        var result = new List<ArchScript>();
        foreach (var (rel, full, kind) in found)
        {
            string name = Path.GetFileName(rel);
            var callers = referencedIn.TryGetValue(name, out var set)
                ? set.ToList()
                : new List<string>();

            int lines = 0;
            long bytes = 0;
            try
            {
                var info = new FileInfo(full);
                bytes = info.Length;
                lines = File.ReadLines(full).Count();
            }
            catch (IOException) { }

            result.Add(new ArchScript(rel, kind, lines, bytes, callers.Count, callers));
        }

        return result
            .OrderBy(s => s.ReferencedBy)                 // the unreferenced first — that is the point
            .ThenByDescending(s => s.Lines)
            .ThenBy(s => s.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Everything that could plausibly invoke a script: source, scripts, docs and config.
    /// Docs count — a runbook naming a script is the difference between "dead" and "run by hand".</summary>
    private static IEnumerable<string> CallerCandidates(string root)
    {
        foreach (string pattern in new[] { "*.cs", "*.ps1", "*.psm1", "*.py", "*.md", "*.json", "*.csproj", "*.yml", "*.yaml", "*.cmd", "*.bat", "*.sh", "*.xaml" })
            foreach (string path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Skip(rel)) continue;
                yield return path;
            }
    }

    /// <summary>Third-party PowerShell vendored into the tree. `_Scripts Rebuild/PowerShellGet` and
    /// `PackageManagement` are Microsoft's modules, sixteen thousand lines of them, and they made the
    /// largest "unreferenced script in this repository" a file nobody here has ever edited.</summary>
    private static bool Vendored(string rel)
        => rel.Contains("/PowerShellGet/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/PackageManagement/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase);

    /// <summary>THE MAP'S OWN OUTPUT IS NOT EVIDENCE OF ANYTHING.
    ///
    /// `architecture.json` lists every script it found, so on the very next run every one of them
    /// appeared to be referenced — 125 unreferenced PowerShell scripts became 0 between two runs,
    /// with nothing changed in the repository but the map's own output being read back in. The
    /// second time this session that the instrument has measured itself.</summary>
    private static bool Generated(string rel)
        => rel.StartsWith("docs/architecture/", StringComparison.OrdinalIgnoreCase);

    private static bool Skip(string rel)
        => Vendored(rel)
        || Generated(rel)
        || rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/.playwright/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/__pycache__/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
}
