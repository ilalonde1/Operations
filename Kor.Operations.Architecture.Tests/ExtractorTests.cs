using System.Globalization;
using System.Reflection;
using System.Text;
using Kor.Operations.Architecture;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.Architecture.Tests;

/// <summary>
/// THE MAP IS ONLY WORTH ANYTHING IF ITS NUMBERS ARE.
///
/// Every fault this tool has had was in its measurements, not its drawing: it counted Visio, Excel
/// and Revit as systems this repo talks to because its own marker table matched; it reported every
/// script as referenced because its own output listed them all; it scored a one-line record 0%
/// similar to an identical one because the comparison was line-based. All three were silent, all
/// three produced a confident wrong answer, and none would have been caught by anything that only
/// checked the file rendered.
///
/// So the measurements are tested, on this repository, against facts stated in the test.
/// </summary>
public sealed class ExtractorTests
{
    private readonly ITestOutputHelper _out;
    public ExtractorTests(ITestOutputHelper output) => _out = output;

    private static ArchModel? _model;
    private static readonly object Gate = new();

    /// <summary>Extracted once for the whole class — it is seven seconds over 292k lines.</summary>
    private static ArchModel Model
    {
        get
        {
            lock (Gate) return _model ??= Extractor.Extract(RepoRoot());
        }
    }

    /// <summary>Shared with the renderer tests, which need the same tree.</summary>
    internal static string RepoRootForTests() => RepoRoot();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void ItFindsTheProjectsTypesAndVerbs()
    {
        var m = Model;
        _out.WriteLine($"{m.Projects.Count} projects, {m.Types.Count} types, {m.Verbs.Count} verbs, " +
                       $"{m.Scripts.Count} scripts");

        Assert.True(m.Projects.Count > 50, "this repo has more than fifty projects");
        Assert.True(m.Types.Count > 2000, "…and more than two thousand types");
        Assert.True(m.Stats.Lines > 250_000, "…and more than a quarter million lines");

        // Read off syntax that compares args[0] with a literal, so a string that merely looks like a
        // verb is not collected.
        Assert.Contains(m.Verbs, v => v.Verb == "dxf-render");
        Assert.Contains(m.Verbs, v => v.Verb == "e2k-ask");
    }

    [Fact]
    public void TheInstrumentDoesNotMeasureItself()
    {
        // Its marker table and tests name external systems. Left in scope, the extractor reports
        // those words as systems this repository talks to, with the mapper itself as evidence.
        foreach (var e in Model.Externals)
            Assert.DoesNotContain(e.Evidence, f =>
                SourceConventions.IsArchitectureToolPath(f));

        Assert.DoesNotContain(Model.Projects, p =>
            p.Name.StartsWith("Kor.Operations.Architecture", StringComparison.OrdinalIgnoreCase));
        foreach (var s in Model.Scripts)
            Assert.DoesNotContain(s.ReferencedIn, f => SourceConventions.IsArchitectureToolPath(f));
    }

    [Fact]
    public void AnExternalSystemHasRealEvidence()
    {
        var deltek = Model.Externals.SingleOrDefault(e => e.Name.StartsWith("Deltek", StringComparison.Ordinal));
        Assert.NotNull(deltek);
        Assert.True(deltek!.Evidence.Count > 50, "Deltek is named in a great many files here");
        foreach (string f in deltek.Evidence)
            Assert.EndsWith(".cs", f, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicationIsMeasuredByCONTENTNotByName()
    {
        var m = Model;

        // The whole point: a shared name is where to look, a similarity score is the finding.
        var runner = m.Duplicates.SingleOrDefault(d => d.Name == "KorMapSyncRunner");
        Assert.NotNull(runner);
        Assert.True(runner!.Similarity > 0.99,
            $"KorMapSyncRunner is copied verbatim into docs/, but scored {runner.Similarity:P0}");
        Assert.True(runner.Lines > 300, "it is a 371-line file");

        // …and names that merely collide must NOT score as duplication.
        Assert.Contains(m.Duplicates, d => d.Similarity < 0.55);

        _out.WriteLine($"{m.Duplicates.Count} duplicated name(s); " +
                       $"{m.Duplicates.Count(d => d.Similarity >= 0.9)} are 90%+ identical, " +
                       $"{m.Duplicates.Count(d => d.Similarity < 0.55)} share only the name");
    }

    [Fact]
    public void AMigrationIsNotADeadScript()
    {
        // Numbered migrations are applied in ordinal order by a runner that scans the folder, so
        // NOTHING names them. Counting them as unreferenced reported 252 dead files that are in fact
        // the live schema of the BD database.
        var migrations = Model.Scripts.Where(s => s.Kind == "SQL migration").ToList();
        Assert.True(migrations.Count > 200, "the BD schema is a few hundred numbered migrations");
        Assert.All(migrations, s => Assert.Contains("/Schema/", s.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheMapsOwnOutputIsNotEvidenceOfAnything()
    {
        // architecture.json lists every script it found. Read back on the next run it made every one
        // of them look referenced — 125 unreferenced PowerShell scripts became 0 with nothing changed
        // in the repository at all.
        foreach (var s in Model.Scripts)
            Assert.DoesNotContain(s.ReferencedIn, f =>
                f.StartsWith("docs/architecture/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScriptInventoryDoesNotUseArchitectureToolFilesAsReferences()
    {
        using var repo = TestRepo.Create();
        repo.File("tools/Run-Thing.ps1", "Write-Host thing");
        repo.File("Kor.Operations.Architecture.Tests/Noise.cs", "// Run-Thing.ps1");

        var script = Assert.Single(ScriptInventory.Collect(repo.Root));
        Assert.Equal("tools/Run-Thing.ps1", script.Path);
        Assert.Equal(0, script.ReferencedBy);
    }

    [Fact]
    public void TheLayoutIsDeterministic()
    {
        // A layout seeded from a clock would rewrite the committed model on every run and make its
        // diff — the whole reason the model is committed as text — worthless.
        var a = Extractor.Extract(RepoRoot());
        var b = Extractor.Extract(RepoRoot());

        for (int g = 0; g < a.Graphs.Count; g++)
            for (int n = 0; n < a.Graphs[g].Nodes.Count; n++)
            {
                Assert.Equal(a.Graphs[g].Nodes[n].X, b.Graphs[g].Nodes[n].X);
                Assert.Equal(a.Graphs[g].Nodes[n].Y, b.Graphs[g].Nodes[n].Y);
            }
    }

    [Fact]
    public void GraphDetailsAreInvariantCulture()
    {
        using var repo = TestRepo.Create();
        repo.Project("App");
        repo.File("App/Big.cs", "public class Big { }\n" + string.Join("\n", Enumerable.Repeat("// x", 1200)));

        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string en = Extractor.Extract(repo.Root).Graphs.Single(g => g.Name == "Relationships")
                .Nodes.Single(n => n.Id == "App").Detail;

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
            string fr = Extractor.Extract(repo.Root).Graphs.Single(g => g.Name == "Relationships")
                .Nodes.Single(n => n.Id == "App").Detail;

            Assert.Equal(en, fr);
            Assert.Contains(",", en, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [Fact]
    public void PartialTypesCollapseToOneDeterministicTypeEntry()
    {
        using var repo = TestRepo.Create();
        repo.Project("App");
        repo.File("App/A.cs", "namespace Demo; public partial class Widget { public void A() { } }");
        repo.File("App/B.cs", "namespace Demo; public partial class Widget { public void B() { } }");

        var type = Assert.Single(Extractor.Extract(repo.Root).Types.Where(t => t.Name == "Widget"));
        Assert.Equal("App:Demo.Widget", type.Id);
        Assert.Equal("App/A.cs; App/B.cs", type.File);
    }

    [Fact]
    public void LinkedCompileItemsAreOwnedByEveryCompilingProject()
    {
        using var repo = TestRepo.Create();
        repo.Project("Data");
        repo.Project("App", extraItemGroup:
            """<Compile Include="..\Data\Shared.cs" Link="Shared.cs" />""");
        repo.File("Data/Shared.cs", "namespace Demo; public sealed class SharedThing { }");

        var model = Extractor.Extract(repo.Root);
        Assert.Contains(model.Types, t => t.Id == "Data:Demo.SharedThing");
        Assert.Contains(model.Types, t => t.Id == "App:Demo.SharedThing");
        Assert.Equal(1, model.Projects.Single(p => p.Name == "Data").Files);
        Assert.Equal(1, model.Projects.Single(p => p.Name == "App").Files);
    }

    /// <summary>THE INPUTS NOBODY GIVES IT.
    ///
    /// Three separate empty-sequence crashes have been found in this component — `Layered`,
    /// `Normalise`, and `Relationships` — and each was fixed where a failing test happened to point
    /// rather than swept for as a class. All three had the same trigger: a repository that is not
    /// this one. Extraction must survive a tree with nothing in it, a tree with source but no
    /// project, and a project with no source, because those are what every OTHER repository looks
    /// like on the way to looking like this one.</summary>
    [Theory]
    [InlineData("nothing at all")]
    [InlineData("loose source, no project")]
    [InlineData("project, no source")]
    public void ExtractionSurvivesARepositoryThatIsNotThisOne(string shape)
    {
        using var repo = TestRepo.Create();
        switch (shape)
        {
            case "loose source, no project":
                repo.File("Loose.cs", "public sealed class Loose { }");
                break;
            case "project, no source":
                repo.Project("Empty");
                break;
        }

        var model = Extractor.Extract(repo.Root);

        // Not "it does not throw" — every graph must still be WELL FORMED, because the renderer
        // walks them straight afterwards and a half-built graph fails somewhere less obvious.
        Assert.NotNull(model.Graphs);
        foreach (var g in model.Graphs)
        {
            Assert.All(g.Nodes, n => Assert.False(double.IsNaN(n.X) || double.IsNaN(n.Y),
                $"{g.Name}/{n.Id} has a NaN coordinate"));
            var ids = g.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            Assert.All(g.Edges, e => Assert.True(ids.Contains(e.From) && ids.Contains(e.To),
                $"{g.Name} has an edge to a node that is not on it"));
        }
    }

    [Fact]
    public void ASHAREDFileIsNotADuplicate()
    {
        // Teaching the extractor about `Compile Include` (the fix above) immediately turned three
        // deliberately-shared files into 100% duplicates across two and four projects on the real
        // repo — SqlTimeouts, AppConfigKeys, ConnectionStrings. Sharing one file is the OPPOSITE of
        // duplicating it, and the report would have sent someone to de-duplicate code that is
        // already single-sourced.
        using var repo = TestRepo.Create();
        repo.Project("Data");
        repo.Project("App", extraItemGroup:
            """<Compile Include="..\Data\Shared.cs" Link="Shared.cs" />""");
        repo.File("Data/Shared.cs", "namespace Demo; public sealed class SharedThing { }");

        Assert.DoesNotContain(Extractor.Extract(repo.Root).Duplicates, d => d.Name == "SharedThing");
    }

    [Fact]
    public void TwoCOPIESOfTheSameTypeStillAre()
    {
        // …and the guard must not silence real duplication: two projects, two files, one name.
        using var repo = TestRepo.Create();
        repo.Project("One");
        repo.Project("Two");
        repo.File("One/Thing.cs", "namespace Demo; public sealed class CopiedThing { public int A; }");
        repo.File("Two/Thing.cs", "namespace Demo; public sealed class CopiedThing { public int A; }");

        var dup = Assert.Single(Extractor.Extract(repo.Root).Duplicates, d => d.Name == "CopiedThing");
        Assert.True(dup.Similarity > 0.99, $"identical copies scored {dup.Similarity:P0}");
    }

    [Fact]
    public void CliVerbsFindEqualityAndSwitchForms()
    {
        using var repo = TestRepo.Create();
        repo.Project("Tool");
        repo.File("Tool/Program.cs",
            """
            if (args.Length > 0 && args[0] == "sector") return;
            if (args.Length > 0 && "emit" == args[0]) return;
            switch (args[0])
            {
                case "ensure":
                    return;
            }
            """);

        var verbs = Extractor.Extract(repo.Root).Verbs.Select(v => v.Verb).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("sector", verbs);
        Assert.Contains("emit", verbs);
        Assert.Contains("ensure", verbs);
    }

    [Fact]
    public void LongProjectCyclesAreReported()
    {
        using var repo = TestRepo.Create();
        const int count = 14;
        for (int i = 0; i < count; i++)
        {
            string name = "P" + i.ToString("00", CultureInfo.InvariantCulture);
            string next = "P" + ((i + 1) % count).ToString("00", CultureInfo.InvariantCulture);
            repo.Project(name, projectReference: $@"..\{next}\{next}.csproj");
        }

        var cycle = Assert.Single(Extractor.Extract(repo.Root).Cycles);
        Assert.Equal(count + 1, cycle.Projects.Count);
    }

    [Fact]
    public void FormatHandlersExcludeTestsWithoutDroppingModelEdges()
    {
        var model = new ArchModel(
            1,
            Array.Empty<ArchProject>(),
            new[]
            {
                new ArchType("App:Demo.PdfReader", "PdfReader", "class", "Demo", "App", "App/PdfReader.cs", "read"),
                new ArchType("App.Tests:Demo.PdfReaderTests", "PdfReaderTests", "class", "Demo", "App.Tests", "App.Tests/PdfReaderTests.cs", "test"),
            },
            Array.Empty<ArchEdge>(),
            new[]
            {
                new ArchFormat("App:Demo.PdfReader", ".pdf", "reads", "name"),
                new ArchFormat("App.Tests:Demo.PdfReaderTests", ".pdf", "touches", "name"),
            },
            Array.Empty<ArchExternal>(),
            Array.Empty<ArchVerb>(),
            Array.Empty<ArchDuplicate>(),
            Array.Empty<ArchOrphan>(),
            Array.Empty<ArchCycle>(),
            Array.Empty<ArchGraph>(),
            Array.Empty<ArchScript>(),
            new ArchStats(0, 0, 0));

        Assert.Equal(2, model.Formats.Count);
        var handler = Assert.Single(VisioRenderer.HandlerFormats(model));
        Assert.Equal("App:Demo.PdfReader", handler.Type);
    }

    [Fact]
    public void NonUtf8TextIsDecodedDeliberately()
    {
        using var repo = TestRepo.Create();
        string path = Path.Combine(repo.Root, "latin1.txt");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("caf\u00e9"));

        Assert.Equal("caf\u00e9", TextFiles.ReadAllText(path));
    }

    [Fact]
    public void TheProjectGraphHasNoCycles()
    {
        Assert.Empty(Model.Cycles);
    }

    private sealed class TestRepo : IDisposable
    {
        private TestRepo(string root) => Root = root;

        public string Root { get; }

        public static TestRepo Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "archmap-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestRepo(root);
        }

        public void Project(string name, string? projectReference = null, string? extraItemGroup = null)
        {
            string dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            var refs = projectReference is null
                ? ""
                : $"""
                    <ItemGroup>
                      <ProjectReference Include="{projectReference}" />
                    </ItemGroup>
                  """;
            var extras = extraItemGroup is null
                ? ""
                : $"""
                    <ItemGroup>
                      {extraItemGroup}
                    </ItemGroup>
                  """;
            // Qualified: the helper below is called File(…), which shadows System.IO.File inside
            // this class. `repo.File("a.cs", …)` reads well at every call site, so the two writes
            // give way rather than the name.
            System.IO.File.WriteAllText(Path.Combine(dir, name + ".csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  {refs}
                  {extras}
                </Project>
                """);
        }

        public void File(string rel, string text)
        {
            string path = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
