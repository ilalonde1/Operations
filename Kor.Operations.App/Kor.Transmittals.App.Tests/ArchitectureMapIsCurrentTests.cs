#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Kor.Operations.App.Tests;

/// <summary>
/// THE MAP GOES STALE OR IT IS NOT A REFERENCE.
///
/// `docs/architecture/architecture.json` is extracted from the source by `tools/ArchitectureMap`,
/// and `docs/architecture/KOR-Application-Map.vsdx` is drawn from it. The whole reason it is derived
/// rather than drawn is that a hand-made architecture diagram is wrong the week after it is made —
/// and a confidently wrong map is worse than no map. So something has to notice when the code moves
/// out from under it, and that something is this.
///
/// WHAT IT CHECKS, AND WHY NOT MORE. It compares the SHAPE of the solution — which projects exist and
/// what each one references — against the model on disk. Those are the facts that change the picture.
/// It deliberately does NOT check type counts or file counts: adding one file to one project moves no
/// box on the diagram, and a test that fails on every new source file would be turned off within a
/// week. Type-level drift is caught by re-running the tool, which is one command.
///
/// It needs no Roslyn and no build of the mapper — it reads the .csproj files directly, so it costs
/// milliseconds in a suite that runs on every edit.
///
/// WHEN THIS FAILS: run `./tools/New-ArchitectureMap.ps1` and commit what changes. The failure text
/// names exactly what moved.
/// </summary>
public sealed class ArchitectureMapIsCurrentTests
{
    [Fact]
    public void TheCommittedMapStillMatchesTheSolution()
    {
        string repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        string modelPath = Path.Combine(repoRoot, "docs", "architecture", "architecture.json");

        Assert.True(File.Exists(modelPath),
            $"no architecture model at {modelPath} — run ./tools/New-ArchitectureMap.ps1");

        var onDisk = ProjectsOnDisk(repoRoot);
        var inModel = ProjectsInModel(modelPath);

        var added = onDisk.Keys.Except(inModel.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var gone = inModel.Keys.Except(onDisk.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var changed = new List<string>();
        foreach (string name in onDisk.Keys.Intersect(inModel.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var a = onDisk[name];
            var b = inModel[name];
            if (a.SequenceEqual(b, StringComparer.Ordinal)) continue;

            var newRefs = a.Except(b, StringComparer.Ordinal).ToList();
            var lostRefs = b.Except(a, StringComparer.Ordinal).ToList();
            var detail = new List<string>();
            if (newRefs.Count > 0) detail.Add("now references " + string.Join(", ", newRefs));
            if (lostRefs.Count > 0) detail.Add("no longer references " + string.Join(", ", lostRefs));
            changed.Add($"  {name}: {string.Join("; ", detail)}");
        }

        if (added.Count == 0 && gone.Count == 0 && changed.Count == 0) return;

        var message = new List<string>
        {
            "The architecture map is stale — the solution has moved since it was generated.",
            "Run ./tools/New-ArchitectureMap.ps1 and commit docs/architecture/.",
            "",
        };
        if (added.Count > 0) message.Add("Projects in the repo but not on the map: " + string.Join(", ", added));
        if (gone.Count > 0) message.Add("Projects on the map but not in the repo: " + string.Join(", ", gone));
        if (changed.Count > 0)
        {
            message.Add("Project references that changed:");
            message.AddRange(changed);
        }

        Assert.Fail(string.Join(Environment.NewLine, message));
    }

    /// <summary>Every .csproj and the projects it references, read straight off disk.</summary>
    private static Dictionary<string, List<string>> ProjectsOnDisk(string repoRoot)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The filter goes to the filesystem, not to a .Where() after the fact — the shape that
        // enumerates every file under 62 bin/ and obj/ trees.
        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            // The mapper does not map itself: its marker table would otherwise register as evidence
            // of external systems this repo does not talk to.
            if (rel.StartsWith("Kor.Operations.Architecture/", StringComparison.OrdinalIgnoreCase)) continue;

            XDocument doc;
            try { doc = XDocument.Load(path); }
            catch (System.Xml.XmlException) { continue; }

            result[Path.GetFileNameWithoutExtension(path)] = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
        }
        return result;
    }

    private static Dictionary<string, List<string>> ProjectsInModel(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.GetProperty("Projects").EnumerateArray())
        {
            string name = p.GetProperty("Name").GetString()!;
            result[name] = p.GetProperty("ProjectRefs").EnumerateArray()
                .Select(r => r.GetString()!)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
        }
        return result;
    }
}
