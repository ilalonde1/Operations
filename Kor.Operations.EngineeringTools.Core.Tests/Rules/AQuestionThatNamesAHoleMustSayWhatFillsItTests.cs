#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// A question that reports missing structure must also say what this tool put there instead.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. Ian, 2026-09-24: "rather than skipping an unknown completely (like a slab) or
/// not running the thing at all if there's a question (there will ALWAYS be questions) - can AI
/// please make a best guess, then LOOK at the result as a sanity check, then proceed - whilst
/// showing clearly in the question workbook that this was an assumption."
///
/// A2 was built that way — a drawing whose title names no level takes one from where it sits, the
/// model carries it, and the row says so with its working. It is right 480 times out of 481 over
/// 1,776 drawings.
///
/// Five questions were not. On 2026-09-25, of the eight questions that can ever be put to an
/// engineer, five name what is missing and leave it missing:
///
///     S7  a slab outline closed and no thickness was printed  -> no floor built
///     S4  the slab edge is open on these levels                -> those slabs not built
///     J7  the drawings have a level the model does not         -> that level dropped
///     J3  outlines on the wall layers would not resolve        -> those walls dropped
///     J5  the floor stops short of the structure on it         -> left short
///
/// Between them they name most of the corpus shortfall: S7 and S4 are class C2 (269,608 sq ft),
/// J7 drops whole levels. This is rule 11's trigger — five symptoms of one shape — so the check
/// that fails on all five comes before any of the five fixes.
///
/// WHAT THIS COVERS: that every question an engineer can be asked either declares
/// <see cref="ModelQuestion.Assumed"/> — a guess standing in the model right now — or is named in
/// <see cref="OnlyAsks"/>, a list that may only shrink.
///
/// WHAT IT DOES NOT COVER:
/// <list type="bullet">
/// <item>That the guess is any GOOD. A wrong floor is worse than a missing one, which is why each
/// of these is measured on its target set before it is banked, not just wired up.</item>
/// <item>That the assumption declared here is the one the build actually made. The string is
/// written beside the code that guesses; nothing holds them together but review.</item>
/// <item>DECIDED rows. Those took a decision rather than leaving a hole, and they say so in the
/// row itself.</item>
/// </list>
///
/// A same-class fault this would NOT catch: a question that declares an assumption and builds
/// nothing — <see cref="ModelQuestion.Assumed"/> is prose, and prose can lie. The differential that
/// would catch it is a build with the guess suppressed, which is what each fix's own test does.
/// </remarks>
public sealed class AQuestionThatNamesAHoleMustSayWhatFillsItTests
{
    /// <summary>
    /// The questions that still only ask. THIS LIST MAY ONLY EVER SHRINK.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> OnlyAsks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S7"] = "a slab outline that closed with no thickness printed in it — build the floor at the set's commonest thickness and say so",
            ["S4"] = "levels where the slab edge does not close — build what the closed outlines give and name the count assumed",
            ["J7"] = "a level the drawings have and the model does not — add the storey the sheets name",
            ["J3"] = "outlines on the wall layers that would not resolve — build the panel the measured size implies",
            ["J5"] = "a floor that stops short of the structure standing on it — extend to the members or say why not",
        };

    [Fact]
    public void EveryQuestionEitherGuessesOrIsOnTheListOfOnesThatDoNot()
    {
        // A DEFECT row is not a question. J8 says a storey came out with no floor; nothing is being
        // asked and there is no answer that would change it. What she would answer about it is S7,
        // which is on the list below.
        var asking = QuestionsInSource()
            .Where(q => !q.Decided && !q.ForTheRecord && !q.Defect)
            .ToList();

        Assert.True(asking.Count >= 6,
            $"only {asking.Count} askable questions parsed; the reader has drifted from the source.");

        var shrugs = asking
            .Where(q => !q.Assumes)
            .Select(q => q.Code)
            .Where(c => !OnlyAsks.ContainsKey(c))
            .ToList();

        Assert.True(shrugs.Count == 0,
            $"{shrugs.Count} question(s) report missing structure and put nothing in its place: "
            + string.Join(", ", shrugs)
            + ". Make the best guess, build it, and set Assumed to what is standing in the model — "
            + "or, if the tool genuinely cannot guess, add it to OnlyAsks with what it would take.");
    }

    [Fact]
    public void TheListOfQuestionsThatOnlyAskOnlyEverShrinks()
    {
        var byCode = QuestionsInSource().ToDictionary(q => q.Code, q => q);

        var guessing = OnlyAsks.Keys
            .Where(c => byCode.TryGetValue(c, out var q) && q.Assumes)
            .ToList();

        Assert.True(guessing.Count == 0,
            $"{guessing.Count} question(s) now declare an assumption and are still listed as only asking: "
            + string.Join(", ", guessing) + ". Remove them from OnlyAsks.");

        Assert.True(OnlyAsks.Count <= 5,
            $"OnlyAsks holds {OnlyAsks.Count} entries and may hold at most the five that predate this gate "
            + "(2026-09-25). A new question does not get added here — it gets a guess.");
    }

    private sealed record ParsedQuestion(string Code, bool Decided, bool ForTheRecord, bool Defect, bool Assumes);

    private static List<ParsedQuestion> QuestionsInSource()
    {
        string source = File.ReadAllText(QuestionnairePath());
        var blocks = Regex.Split(source, @"new ModelQuestion\(");
        var found = new List<ParsedQuestion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string block in blocks.Skip(1))
        {
            var code = Regex.Match(block.Length > 80 ? block[..80] : block, "\"([A-Z][0-9]+)\"");
            if (!code.Success || !seen.Add(code.Groups[1].Value)) continue;

            string initializer = block.Length > 2500 ? block[..2500] : block;
            found.Add(new ParsedQuestion(
                code.Groups[1].Value,
                Regex.IsMatch(initializer, @"\bDecided = true"),
                Regex.IsMatch(initializer, @"\bForTheRecord = true"),
                Regex.IsMatch(initializer, @"\bDefect = true"),
                Regex.IsMatch(initializer, @"\bAssumed = ")));
        }

        return found;
    }

    private static string QuestionnairePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core")))
            dir = dir.Parent;

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("repo root not found"),
            "Kor.Operations.EngineeringTools.Core", "Dxf", "ModelQuestionnaire.cs");
    }
}
