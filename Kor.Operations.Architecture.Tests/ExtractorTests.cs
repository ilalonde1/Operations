using System.Reflection;
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

        // Read off `args[0].Equals("…")`, so a string that merely looks like a verb is not collected.
        Assert.Contains(m.Verbs, v => v.Verb == "dxf-render");
        Assert.Contains(m.Verbs, v => v.Verb == "e2k-ask");
    }

    [Fact]
    public void TheInstrumentDoesNotMeasureItself()
    {
        // Its marker table names Visio, Excel and Revit. Left in scope, the extractor reported all
        // three as systems this repository talks to, with its own source as the only evidence.
        foreach (var e in Model.Externals)
            Assert.DoesNotContain(e.Evidence, f =>
                f.StartsWith("Kor.Operations.Architecture/", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(Model.Projects, p => p.Name == "Kor.Operations.Architecture");
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
    public void TheProjectGraphHasNoCycles()
    {
        Assert.Empty(Model.Cycles);
    }
}
