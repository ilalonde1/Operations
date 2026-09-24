using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A BISECT'S KNOB IS AN INSTRUMENT, AND AN UNTESTED INSTRUMENT ANSWERS THE SAME EITHER WAY (2026-09-23).
///
/// `corpus-gate --bisect KOR_STEP&lt;n&gt;_OFF,...` re-reads the sets that lost with each rule turned off in turn,
/// to say WHICH rule took them. That is only worth anything if setting the variable changes what the reader does.
/// The way it silently would not: caching the lookup in a `static readonly`. The bisect sets the variable and
/// re-reads inside the SAME process, so a field initialised at first touch freezes whatever the process started
/// with, every arm of the bisect reads the same, and the report says "no single rule explains it" — a green that
/// means nothing, which is the exact fault class Codex found five of in the gate itself on 2026-09-22.
///
/// THE SECOND INSTANCE, and why this file stopped being one witness and became a scan. The rule on this repo is
/// that at the second symptom of a shape you stop fixing and write the check that fails on every instance at
/// once. The shape, in one sentence:
///
///     A BISECT KNOB IS ONLY AN INSTRUMENT IF THE VARIABLE IS READ LIVE AT EVERY CALL, AND IF IT IS NAMED
///     WHERE THE GATE TELLS THE OPERATOR TO SET IT.
///
/// Symptom one was step 135's knob frozen in a field. Symptom two was found by writing this: the gate advertises
/// `--bisect KOR_STEP130_OFF,KOR_STEP131_OFF,KOR_STEP133_OFF,KOR_STEP134_OFF,KOR_STEP135_OFF` and the engine had
/// grown 136 and 138 since, so a loss either of them caused could not be attributed by the instrument that exists
/// to attribute it. This file's own previous summary named that gap as its blind spot — "a knob spelled one way
/// in the code and another way in the bisect's list, since both sides of that pairing are named here in one
/// place" — and the two sides were NOT in one place. They are now, because a scan puts them there.
///
/// WHAT THIS COVERS:
///   - step 130's knob, both ways, on the witness the fate ledger carries — 31009's east edge, a line as long as
///     the paper standing at mid-page;
///   - EVERY `KOR_STEP&lt;n&gt;_OFF` the shipped code reads: that the read is not the initialiser of a field or of
///     a static property, so it cannot freeze;
///   - that the set of knobs the code reads and the set the gate advertises are the same set, both ways round.
///
/// WHAT IT DOES NOT: it does not prove a knob CHANGES the reading — only that the value is fetched live and the
/// operator is told the knob exists; each rule still owes its own before/after, as step 130 has above and step
/// 139 has in <see cref="TheParenthesisSaysWhatTheSheetIsTests"/>. It does not prove a knob reaches the reader
/// from a CHILD process, which is how `corpus-analyze` is usually run. It reads the source as text, so a knob
/// whose name is built by concatenation is invisible to it. And a same-class fault it would NOT catch: a knob
/// read live into a local and then cached by the CALLER, or assigned in a static constructor — the read there is
/// live by the letter of this check and frozen in fact.
/// </summary>
public sealed class ABisectKnobActuallyTurnsItsRuleOffTests
{
    private readonly ITestOutputHelper _out;
    public ABisectKnobActuallyTurnsItsRuleOffTests(ITestOutputHelper output) => _out = output;

    // The fate ledger's own case (step 130): a 100,000 mm page, a 60,000 mm line at x = 50,000. Dead centre.
    private const double PageMm = 100_000;
    private const double MidPage = 50_000;
    private const double AtEdge = 3_000;

    [Fact]
    public void Step130StandsALineAsLongAsThePaperAtMidPageUpAsTheBuildings()
    {
        Assert.False(GeometryFilterService.AtTheMargin(MidPage, PageMm, step130Off: false));
        Assert.True(GeometryFilterService.AtTheMargin(AtEdge, PageMm, step130Off: false));
        Assert.True(GeometryFilterService.AtTheMargin(PageMm - AtEdge, PageMm, step130Off: false));
    }

    [Fact]
    public void WithItsKnobOnStep130IsGoneAndEveryLongLineIsTheFrameAgain()
    {
        Assert.True(GeometryFilterService.AtTheMargin(MidPage, PageMm, step130Off: true));
        Assert.True(GeometryFilterService.AtTheMargin(AtEdge, PageMm, step130Off: true));
    }

    /// <summary>
    /// The variable the bisect sets is the variable the rule reads. Set and restored in a finally, and nothing
    /// inside the window reads a PDF — the window is two property reads — so no class running beside this one
    /// can see the flag.
    /// </summary>
    [Fact]
    public void TheKnobTheBisectSetsIsTheKnobTheRuleReads()
    {
        string? was = Environment.GetEnvironmentVariable("KOR_STEP130_OFF");
        try
        {
            Environment.SetEnvironmentVariable("KOR_STEP130_OFF", "1");
            Assert.True(GeometryFilterService.Step130Off);
            Environment.SetEnvironmentVariable("KOR_STEP130_OFF", null);
            Assert.False(GeometryFilterService.Step130Off);
        }
        finally { Environment.SetEnvironmentVariable("KOR_STEP130_OFF", was); }
    }

    private static readonly Regex Read =
        new(@"Environment\.GetEnvironmentVariable\(\s*""(KOR_STEP[0-9A-Za-z]+_OFF)""\s*\)", RegexOptions.Compiled);

    private static readonly Regex Advertised =
        new(@"--bisect\s+((?:KOR_STEP[0-9A-Za-z]+_OFF,?)+)", RegexOptions.Compiled);

    /// <summary>Words that only appear at member scope; a read behind one of them, with no `=&gt;`, is frozen.</summary>
    private static readonly string[] MemberScope =
        ["public", "private", "internal", "protected", "static", "readonly", "const"];

    /// <summary>Every shipped .cs file of the two projects a bisect runs through.</summary>
    private static IEnumerable<string> ShippedSources()
        => new[] { "Kor.Operations.EngineeringTools.Core", "Kor.Operations.EngineeringTools.TakeoffCli" }
            .Select(p => Path.Combine(RepositoryRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void NoKnobIsCapturedInAFieldWhereTheSecondArmOfTheBisectCannotMoveIt()
    {
        var frozen = new List<string>();
        int reads = 0;
        foreach (string file in ShippedSources())
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Read.Matches(text))
            {
                reads++;
                // the declaration this read sits in: back to the end of the previous statement or brace
                int start = text.LastIndexOfAny([';', '{', '}'], m.Index) + 1;
                string head = text[start..m.Index];
                if (head.Contains("=>", StringComparison.Ordinal)) continue;         // expression-bodied: read at every call
                if (!MemberScope.Any(w => Regex.IsMatch(head, $@"\b{w}\b"))) continue; // a local inside a method
                frozen.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} — {Collapse(head)} …");
            }
        }
        _out.WriteLine($"{reads} knob read(s) across the shipped source.");
        Assert.True(reads > 0, "no KOR_STEP<n>_OFF read was found at all; this guard is reading the wrong place");
        Assert.True(frozen.Count == 0,
            "These bisect knobs are captured at member scope, so every arm of a bisect in one process reads the "
            + "same value and the report cannot attribute a loss. Make each one expression-bodied (`=> Environment"
            + ".GetEnvironmentVariable(...)`) or read it inside the method:\n  " + string.Join("\n  ", frozen));
    }

    [Fact]
    public void TheKnobsTheCodeReadsAndTheKnobsTheGateAdvertisesAreTheSameSet()
    {
        var inCode = new SortedSet<string>(StringComparer.Ordinal);
        var advertised = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in ShippedSources())
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Read.Matches(text)) inCode.Add(m.Groups[1].Value);
            foreach (Match m in Advertised.Matches(text))
                foreach (string k in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    advertised.Add(k.Trim());
        }
        _out.WriteLine("read by the code : " + string.Join(", ", inCode));
        _out.WriteLine("advertised by the gate: " + string.Join(", ", advertised));

        Assert.True(advertised.Count > 0,
            "the gate no longer advertises any --bisect knob; the operator is not told the instrument exists");
        var unadvertised = inCode.Except(advertised, StringComparer.Ordinal).ToList();
        Assert.True(unadvertised.Count == 0,
            "The engine reads these knobs and the gate does not name them, so a loss either of them caused cannot "
            + "be attributed by the bisect: " + string.Join(", ", unadvertised)
            + ". Add them to the --bisect line in CorpusGateVerb.");
        var stale = advertised.Except(inCode, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            "The gate advertises knobs no shipped code reads, so an operator setting one gets a silent no-op: "
            + string.Join(", ", stale) + ".");
    }

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>The repository root, from the test binary's own folder.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
