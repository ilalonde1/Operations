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
/// `docs/architecture/architecture.json` is extracted from the source by `Kor.Operations.Architecture`,
/// which then draws `KOR-Application-Map.vsdx` from it. The whole reason it is derived
/// rather than drawn is that a hand-made architecture diagram is wrong the week after it is made —
/// and a confidently wrong map is worse than no map. So something has to notice when the code moves
/// out from under it, and that something is this.
///
/// WHAT IT CHECKS, AND WHY NOT MORE. It compares the SHAPE of the solution — which projects exist,
/// what each one references, and how many source files each contains — against the model on disk.
/// Those are structural facts that change the picture. It deliberately does NOT check line counts:
/// line-count drift is cosmetic and is refreshed on the next regeneration; a test that fails on
/// every method edit would be turned off within a week.
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
            if (a.ProjectRefs.SequenceEqual(b.ProjectRefs, StringComparer.Ordinal) && a.Files == b.Files) continue;

            var newRefs = a.ProjectRefs.Except(b.ProjectRefs, StringComparer.Ordinal).ToList();
            var lostRefs = b.ProjectRefs.Except(a.ProjectRefs, StringComparer.Ordinal).ToList();
            var detail = new List<string>();
            if (newRefs.Count > 0) detail.Add("now references " + string.Join(", ", newRefs));
            if (lostRefs.Count > 0) detail.Add("no longer references " + string.Join(", ", lostRefs));
            if (a.Files != b.Files) detail.Add($"has {a.Files} source file(s), model has {b.Files}");
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
            message.Add("Project structure that changed:");
            message.AddRange(changed);
        }

        Assert.Fail(string.Join(Environment.NewLine, message));
    }

    [Fact]
    public void ProjectShapeIncludesSourceFileCount()
    {
        string root = Path.Combine(Path.GetTempPath(), "archmap-current-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "App"));
            File.WriteAllText(Path.Combine(root, "App", "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "App", "A.cs"), "public class A { }");
            File.WriteAllText(Path.Combine(root, "App", "B.cs"), "public class B { }");

            Assert.Equal(2, ProjectsOnDisk(root)["App"].Files);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed record ProjectShape(List<string> ProjectRefs, int Files);

    /// <summary>Every .csproj, the projects it references, and the source files it owns, read straight off disk.</summary>
    private static Dictionary<string, ProjectShape> ProjectsOnDisk(string repoRoot)
    {
        var result = new Dictionary<string, ProjectShape>(StringComparer.Ordinal);

        // The filter goes to the filesystem, not to a .Where() after the fact — the shape that
        // enumerates every file under 62 bin/ and obj/ trees.
        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
                     .OrderBy(p => Rel(repoRoot, p), StringComparer.Ordinal))
        {
            string rel = Rel(repoRoot, path);
            if (rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            // The mapper does not map itself: its marker table and tests would otherwise register as
            // evidence of external systems this repo does not talk to.
            if (IsArchitectureToolPath(rel)) continue;

            XDocument doc;
            try { doc = XDocument.Load(path); }
            catch (System.Xml.XmlException) { continue; }

            result[Path.GetFileNameWithoutExtension(path)] = new ProjectShape(doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList(), 0);
        }

        var byDir = result.Keys
            .Select(name => (Name: name, Dir: ProjectDir(repoRoot, name)))
            .Where(p => p.Dir is not null)
            .OrderByDescending(p => p.Dir!.Length)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var sourceOwners = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(p => Rel(repoRoot, p), StringComparer.Ordinal))
        {
            string rel = Rel(repoRoot, path);
            if (IsArchitectureToolPath(rel) || SkipSource(rel)) continue;

            var owner = byDir.FirstOrDefault(p => rel.StartsWith(p.Dir + "/", StringComparison.OrdinalIgnoreCase));
            AddOwner(rel, owner.Name ?? "(loose)");
        }

        foreach (var (name, dir) in byDir)
        {
            string projectPath = Path.Combine(repoRoot, dir!, name + ".csproj");
            XDocument doc;
            try { doc = XDocument.Load(projectPath); }
            catch (System.Xml.XmlException) { continue; }

            foreach (string include in doc.Descendants()
                         .Where(e => e.Name.LocalName == "Compile")
                         .Select(e => (string?)e.Attribute("Include"))
                         .Where(v => !string.IsNullOrWhiteSpace(v))
                         .Select(v => v!.Replace('\\', '/')))
            {
                if (include.Contains('*')) continue;
                string full = Path.GetFullPath(Path.Combine(repoRoot, dir!, include));
                if (!File.Exists(full)) continue;
                string rel = Rel(repoRoot, full);
                if (IsArchitectureToolPath(rel) || SkipSource(rel)) continue;
                AddOwner(rel, name);
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var owners in sourceOwners.Values)
            foreach (string owner in owners)
                if (result.ContainsKey(owner))
                    counts[owner] = counts.TryGetValue(owner, out int n) ? n + 1 : 1;

        foreach (string name in result.Keys.ToList())
            result[name] = result[name] with { Files = counts.TryGetValue(name, out int n) ? n : 0 };

        return result;

        void AddOwner(string rel, string projectName)
        {
            if (!sourceOwners.TryGetValue(rel, out var set))
                sourceOwners[rel] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(projectName);
        }
    }

    private static Dictionary<string, ProjectShape> ProjectsInModel(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<string, ProjectShape>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.GetProperty("Projects").EnumerateArray())
        {
            string name = p.GetProperty("Name").GetString()!;
            result[name] = new ProjectShape(p.GetProperty("ProjectRefs").EnumerateArray()
                .Select(r => r.GetString()!)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList(), p.GetProperty("Files").GetInt32());
        }
        return result;
    }

    private static string? ProjectDir(string repoRoot, string name)
        => Directory.EnumerateFiles(repoRoot, name + ".csproj", SearchOption.AllDirectories)
            .OrderBy(p => Rel(repoRoot, p), StringComparer.Ordinal)
            .Select(p => Rel(repoRoot, Path.GetDirectoryName(p)!))
            .FirstOrDefault(d =>
                !IsArchitectureToolPath(d) &&
                !d.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                !d.Contains("/obj/", StringComparison.OrdinalIgnoreCase));

    private static bool SkipSource(string rel)
        => rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
           rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
           rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
           rel.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
           rel.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
           rel.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchitectureToolPath(string rel)
    {
        int slash = rel.IndexOf('/');
        string first = slash < 0 ? rel : rel[..slash];
        return first.StartsWith("Kor.Operations.Architecture", StringComparison.OrdinalIgnoreCase);
    }

    private static string Rel(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');
}
