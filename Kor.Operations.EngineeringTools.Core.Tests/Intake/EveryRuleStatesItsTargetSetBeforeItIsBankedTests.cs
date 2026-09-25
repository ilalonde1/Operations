using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A RULE'S ACCURACY IS NOT ITS REACH, AND ONLY ONE OF THEM DECIDES WHETHER TO BUILD IT (2026-09-24).
///
/// The class, in one sentence, which is the thing this file exists to make mechanical:
///
///     A RULE MEASURED FOR HOW OFTEN IT IS RIGHT, AND NEVER FOR HOW MANY SETS IT TOUCHES, IS BUILT
///     BEFORE ANYONE KNOWS WHETHER IT IS WORTH BUILDING.
///
/// Three instances, all in the same fortnight:
///
///   step 141  earned nothing. It was sized against a pool step 140 had already consumed.
///   step 142  earned nothing. The shape was measured — 10 storeys, 103,634 sq ft — and the
///             MECHANISM never was; EnclosedByWallPanels returns null on all 42 combined walls.
///   step 144  99.8% accurate over 1,776 drawings, and it reaches TWO SETS OF 297.
///
/// 144 is the one that named the class, because its accuracy number was real and completely
/// irrelevant to the decision. The back-test was leave-one-out: hide the level of a drawing whose
/// title DID parse and ask whether sheet order recovers it. That measures whether the inference is
/// RIGHT. It cannot measure whether anything NEEDS it — and in the corpus almost every title parses,
/// so there were six drawings in the world to fix. (They were worth fixing: 31005-01 went from 10.6%
/// to 31.0% of the engineer's plate area. That is luck, not method. The rule was built first and
/// sized afterwards.)
///
/// ⚠ AND THE RULE AGAINST THIS ALREADY EXISTED, IN PROSE, AND LOST.
/// `project_rule_judged_on_its_target_set_2026_09_18` is BINDING and says: build the target set and
/// the model yardstick, or park. It was written down, it was in memory, and it did not change the
/// next action — which is precisely what CLAUDE.md §12 says happens: *"a rule that lives only in
/// prose loses to momentum … the gate is what changes the next action."* Rule 7 is the one repeat
/// offence that actually stopped, and it stopped because a hook blocks the command.
///
/// So: every bisect knob names a rule that was banked. Every banked rule states, in its own
/// prediction doc, HOW MANY SETS IT TOUCHES — as a number, before the reply that builds it. A rule
/// whose reach is not written down turns this red.
///
/// ⚠ WHAT THIS COVERS AND WHAT IT DOES NOT (rule 11). It checks that a number was WRITTEN, not that
/// it was measured honestly — nothing here re-runs the corpus, and a wrong figure passes. It cannot
/// see a rule that ships without a knob at all, which is the obvious way around it. And it says
/// nothing about whether the reach is worth the work: two sets can be the right answer, as 144 was.
/// What it removes is the option of not knowing.
/// </summary>
public class EveryRuleStatesItsTargetSetBeforeItIsBankedTests
{
    private readonly ITestOutputHelper _out;

    public EveryRuleStatesItsTargetSetBeforeItIsBankedTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The knobs that predate this gate, each with why it is not being back-filled now.
    ///
    /// Back-filling them would mean re-measuring six banked rules tonight to satisfy a test, which is
    /// the same haste this file exists to stop. They are listed so the exemption is explicit and
    /// countable rather than silent — and so that removing one is a deliberate act.
    /// </summary>
    private static readonly Dictionary<string, string> BankedBeforeThisGate = new(StringComparer.Ordinal)
    {
        ["KOR_STEP130_OFF"] = "banked before 2026-09-24; reach never recorded",
        ["KOR_STEP131_OFF"] = "banked before 2026-09-24; characterised in log 164, reach never recorded",
        ["KOR_STEP133_OFF"] = "banked before 2026-09-24; reach never recorded",
        ["KOR_STEP134_OFF"] = "banked before 2026-09-24; reach never recorded",
        ["KOR_STEP135_OFF"] = "banked before 2026-09-24; reach never recorded",
        ["KOR_STEP136_OFF"] = "banked before 2026-09-24; judged in log 163, reach never recorded",
    };

    private static readonly Regex Knob = new(@"KOR_STEP(\d+)_OFF", RegexOptions.Compiled);

    /// <summary>The line a prediction doc has to carry. A heading would be prose; this is a figure.</summary>
    private static readonly Regex TargetSetLine =
        new(@"(?im)^\s*\*\*Target set:\*\*.*?\d", RegexOptions.Compiled);

    [Fact]
    [Trait("Speed", "Fast")]
    public void EveryBisectKnobHasAPredictionDocThatStatesHowManySetsItTouches()
    {
        string repo = RepoRoot();
        string engine = Path.Combine(repo, "Kor.Operations.EngineeringTools.Core");
        string docs = Path.Combine(repo, "docs", "pdf-intake");

        var knobs = Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Knob.Matches(File.ReadAllText(f)).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(knobs);

        var missing = new List<string>();

        foreach (string knob in knobs)
        {
            if (BankedBeforeThisGate.ContainsKey(knob)) continue;

            string step = Knob.Match(knob).Groups[1].Value;
            string doc = Path.Combine(docs, $"step{step}-prediction.md");

            if (!File.Exists(doc))
            {
                missing.Add($"{knob}: no docs/pdf-intake/step{step}-prediction.md at all");
                continue;
            }

            if (!TargetSetLine.IsMatch(File.ReadAllText(doc)))
                missing.Add($"{knob}: step{step}-prediction.md has no \"**Target set:**\" line with a figure");
        }

        _out.WriteLine(
            $"{knobs.Count} knob(s); {BankedBeforeThisGate.Count} predate this gate; " +
            $"{knobs.Count - BankedBeforeThisGate.Count} must state their reach.");

        Assert.True(
            missing.Count == 0,
            "These rules are banked without their REACH written down — how many corpus sets they "
            + "touch, as a number:\n  "
            + string.Join("\n  ", missing)
            + "\n\nAdd a line to the step's prediction doc reading:\n"
            + "    **Target set:** N of M corpus sets, and K of the 48 with the engineer's model.\n\n"
            + "Measure it BEFORE writing the rule, not after. Accuracy answers \"is it right\"; only "
            + "reach answers \"is it worth building\". Step 144 measured 99.8% and touches 2 sets of "
            + "297 — see project_rule_judged_on_its_target_set_2026_09_18, which said so in prose and "
            + "was ignored, which is why this is a test.");
    }

    /// <summary>
    /// The exemption list may only ever shrink.
    ///
    /// Without this it is a place to put the next rule that has not been sized, which would turn the
    /// gate into paperwork. Six is what existed when the gate was written.
    /// </summary>
    [Fact]
    [Trait("Speed", "Fast")]
    public void TheListOfRulesExemptFromThisGateNeverGrows()
    {
        Assert.True(
            BankedBeforeThisGate.Count <= 6,
            $"BankedBeforeThisGate holds {BankedBeforeThisGate.Count} entries and may hold at most the "
            + "six that predate this gate. A new rule does not get added here — it gets its reach "
            + "measured. Removing an entry, by going back and measuring that rule, is the only edit "
            + "this list is for.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
