#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// A question whose answer cannot change any future build is a survey, not a question.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. Ian, 2026-09-25: "the 'learning system' doesn't seem to be doing any learning."
/// He was right, and the numbers are worse than the complaint. The store itself works — 99 settings
/// reach every build through <c>analysis.vw_RuleSetting</c>, ten of them confirmed in Andrea's own
/// words. What had rotted was the intake: of 41 question codes the questionnaire can write, 20
/// carried no <c>SettingKey</c>, so <c>RuleSettings.ImportQuestionAnswers</c> banked her answer as
/// prose and the next run behaved identically. Four of 88 rulings have ever arrived through that
/// import, all on 2026-08-15.
///
/// And the keyless ones were not the trivia. They were S5 (is the parkade this building's share or
/// the whole thing — class B, 16 sets, 463,040 sq ft of gap), S7 and S4 (is this a floor, how
/// thick, how many slabs — class C2, 269,608), and J8 (the storeys with no floor — class C1,
/// 561,627). The tool asked the only questions that would close the gap and then threw the answers
/// away.
///
/// WHAT THIS COVERS: that every question the workbook can put in front of an engineer declares a
/// setting key, and that every key so declared is READ by the build — it appears in a
/// <c>ValueOr</c>, <c>FlagOr</c> or <c>ListOr</c> call somewhere other than the questionnaire that
/// names it. Declaring a key nobody reads is the same defect wearing a key.
///
/// WHAT IT DOES NOT COVER, and these matter:
/// <list type="bullet">
/// <item>That the value she types is VALID for the key. A "yes" against a millimetre threshold is
/// read by <c>ImportQuestionAnswers</c>, not here.</item>
/// <item>That reading the key changes the OUTPUT. A key can be read into a field the geometry then
/// ignores; this sees the read, not the effect. Only a differential build proves the effect.</item>
/// <item>That the question is worth asking, or that its default is right.</item>
/// <item>The per-job half of the loop. A key is global: it says how KOR draws, not what is true of
/// 31005-01. A question that can only be answered per job — "which level is this sheet" — passes
/// this gate with a global key and still cannot record a job-specific answer. That is
/// <c>ARulingCanBeScopedToOneJobTests</c>, and until both hold the loop is not closed.</item>
/// </list>
///
/// A same-class fault this would NOT catch: giving S5 the key <c>dxf.building-share-only</c>, wiring
/// <c>settings.FlagOr("dxf.building-share-only", …)</c> into a field, and never branching on that
/// field. Green here, still a survey.
/// </remarks>
public sealed class EveryQuestionsAnswerHasSomewhereToLandTests
{
    /// <summary>
    /// A row that reports a FAULT rather than asking something. Nothing is being asked, so there is
    /// no answer to land — J8 says a storey came out with no floor, and what she would answer is
    /// S7, which has its own key. Kept short deliberately: every addition here is a question
    /// quietly demoted to a notice.
    /// </summary>
    private static readonly HashSet<string> ReportsAFaultRatherThanAsking = ["J6", "J8", "S6"];

    /// <summary>
    /// A row that is a matter of record — a bug we already fixed, or a convention only interesting
    /// on another office's drawings. It sits on the reference sheet and asks nothing.
    /// </summary>
    private static readonly HashSet<string> MattersOfRecord = ["C1", "P1"];

    /// <summary>
    /// The questions that still have nowhere for an answer to land, named one by one. THIS LIST MAY
    /// ONLY EVER SHRINK.
    /// </summary>
    /// <remarks>
    /// It is here rather than left as a red build because a red build stops everything else, and it
    /// is a LIST rather than a count because a count hides which ones. Every entry is a question an
    /// engineer can be shown today and whose answer the next run will ignore.
    ///
    /// They are not evenly weighted. S5 is class B — 16 corpus sets and 463,040 sq ft of gap. S7
    /// and S4 are class C2, 269,608. Those three are the next work, not the bottom of a list.
    ///
    /// A new question does NOT get added here. Adding one is the gate telling you the question has
    /// no answer worth taking, which is worth knowing before it reaches an engineer.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NoLandingPlaceYet =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S5"] = "whose parkade is it — needs the model cut to one building's share of a shared floor, which does not exist yet",
            ["S7"] = "is this a floor and how thick — needs a per-storey thickness the plate builder reads",
            ["S4"] = "how many slabs on each level — needs a per-level expected count the composer checks against",
            ["F2"] = "storeys with no floor — her answer names another layer to read as the slab, and nothing reads a per-set layer list",
            ["J7"] = "a level the drawings have and the model does not — needs a storey she can add by name",
            ["J3"] = "wall outlines that would not resolve — her answer is about individual outlines, with no handle to name one",
            ["J4"] = "floors copied from another storey — needs a per-storey 'this plate is wrong, use that one'",
            ["J5"] = "floors that stop short of their structure — same handle as J4, not yet built",
            ["J2"] = "a floor with nothing under it — currently informational; an answer would add support she has not drawn",
            ["J1"] = "storeys she said she would take on herself — a list she keeps, not a setting the build applies",
            ["O1"] = "openings between perimeter elements — needs an opening rule keyed on where they sit",
            ["S2"] = "slab openings as null-section areas — needs a switch the plate builder reads",
            ["M1"] = "storey framework — describes what was built; no single value would change it",
            ["M2"] = "beams — no beam is generated by any path, so there is nothing for an answer to turn on",
        };

    [Fact]
    public void EveryQuestionDeclaresWhereItsAnswerLands()
    {
        var questions = QuestionsInSource();
        Assert.True(questions.Count >= 40, $"only {questions.Count} questions parsed; the reader has drifted from the source.");

        var homeless = questions
            .Where(q => !ReportsAFaultRatherThanAsking.Contains(q.Code) && !MattersOfRecord.Contains(q.Code))
            .Where(q => q.Keys.Count == 0)
            .Select(q => q.Code)
            .ToList();

        var unexpected = homeless.Where(c => !NoLandingPlaceYet.ContainsKey(c)).ToList();

        Assert.True(unexpected.Count == 0,
            $"{unexpected.Count} question(s) have no SettingKey, so answering one changes nothing on the "
            + "next run: " + string.Join(", ", unexpected) + ". Give each a key the build reads, or — if it "
            + "truly reports a fault rather than asking — name it in ReportsAFaultRatherThanAsking with "
            + "the reason. Do NOT add it to NoLandingPlaceYet; that list may only shrink.");
    }

    /// <summary>
    /// The debt list may only come down. An entry left in it after its question has been wired is a
    /// question the next reader will assume is still broken.
    /// </summary>
    [Fact]
    public void TheListOfQuestionsWithNowhereToLandOnlyEverShrinks()
    {
        var questions = QuestionsInSource().ToDictionary(q => q.Code, q => q.Keys);

        var landed = NoLandingPlaceYet.Keys
            .Where(c => questions.TryGetValue(c, out var keys) && keys.Count > 0)
            .ToList();

        Assert.True(landed.Count == 0,
            $"{landed.Count} question(s) now have a setting key and are still listed as having none: "
            + string.Join(", ", landed) + ". Remove them from NoLandingPlaceYet.");

        Assert.True(NoLandingPlaceYet.Count <= 14,
            $"NoLandingPlaceYet holds {NoLandingPlaceYet.Count} entries and may hold at most the fourteen "
            + "that predate this gate (2026-09-25). A new question does not get added here — it gets a key.");
    }

    [Fact]
    public void EveryKeyAQuestionDeclaresIsReadByTheBuild()
    {
        var read = KeysTheBuildReads();
        Assert.True(read.Count >= 60, $"only {read.Count} keys found being read; the accessor scan has drifted.");

        var unread = QuestionsInSource()
            .SelectMany(q => q.Keys.Select(k => (q.Code, Key: k)))
            .Where(x => !read.Contains(x.Key))
            .ToList();

        Assert.True(unread.Count == 0,
            $"{unread.Count} declared setting key(s) are never read by the build, so an answer against "
            + "one is banked and ignored: "
            + string.Join(", ", unread.Select(x => $"{x.Code} -> {x.Key}"))
            + ". Wire each into a settings.ValueOr / FlagOr / ListOr call, or drop the key.");
    }

    private sealed record ParsedQuestion(string Code, IReadOnlyList<string> Keys);

    /// <summary>
    /// The questions as the source declares them. Read from the text rather than by reflection
    /// because they are built inline against a model — there is no catalogue to enumerate, and a
    /// test that could only see the ones a synthetic model happens to trigger would miss most.
    /// </summary>
    private static List<ParsedQuestion> QuestionsInSource()
    {
        string source = File.ReadAllText(QuestionnairePath());
        var blocks = Regex.Split(source, @"new ModelQuestion\(");
        var found = new List<ParsedQuestion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string block in blocks.Skip(1))
        {
            // The code is the first argument, so it is within the first line or two.
            var code = Regex.Match(block.Length > 80 ? block[..80] : block, "\"([A-Z][0-9]+)\"");
            if (!code.Success || !seen.Add(code.Groups[1].Value)) continue;

            // The object initializer for THIS question ends where the next constructor begins, and
            // Split has already cut there.
            var key = Regex.Match(block, "SettingKey\\s*=\\s*\"([^\"]+)\"");
            var keys = key.Success
                ? key.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : new List<string>();

            found.Add(new ParsedQuestion(code.Groups[1].Value, keys));
        }

        return found;
    }

    /// <summary>
    /// Every key the engine actually consults. The idiom is uniform — <c>settings.ValueOr("dxf.x",
    /// fallback)</c> and its two siblings — which is what makes this checkable at all.
    /// </summary>
    private static HashSet<string> KeysTheBuildReads()
    {
        string engine = Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.Core");
        string questionnaire = QuestionnairePath();
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(file, questionnaire, StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

            // JobAnswer is the fourth: a fact about one building, read under a key carrying the
            // job. The literal in the source is the base key, which is what a question declares.
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\\.(?:ValueOr|FlagOr|ListOr|JobAnswer)\\(\\s*\"([^\"]+)\""))
                read.Add(m.Groups[1].Value);
        }

        return read;
    }

    private static string QuestionnairePath() =>
        Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.Core", "Dxf", "ModelQuestionnaire.cs");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
