using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A NEW MEMBER INSERTED ABOVE AN EXISTING ONE STEALS ITS DOCUMENTATION (2026-09-24).
///
/// The shape, in one sentence:
///
///     A DOC COMMENT ATTACHES TO WHATEVER MEMBER FOLLOWS IT, SO DROPPING A NEW METHOD BETWEEN A
///     COMMENT AND ITS METHOD SILENTLY REASSIGNS THAT COMMENT AND LEAVES THE OLD METHOD BARE.
///
/// It happened TWICE in one day, both times to me, both times unnoticed until a diff was read:
/// <c>PlatesByStorey</c> lost its summary to <c>MostOfTheLadderGotNoFloor</c>, and
/// <c>SheetsResult</c> lost its summary to <c>NoPlanSheetReason</c>. Nothing catches it — the build
/// is clean, every test is green, and the only symptom is that a member's documentation now
/// describes something else. Rule 11's trigger is the second instance, so this is the check.
///
/// ⚠ IT IS A RATCHET, NOT A PASS/FAIL, and the reason is measured rather than assumed: the engine
/// already carries 34 doc blocks with two or more &lt;summary&gt; tags, most of them long-standing and
/// none of them mine. Failing the build on all 34 would hand somebody else's cleanup to whoever
/// next touches this file, which is how a gate gets deleted. So the count may not GROW — the next
/// insertion of this shape turns the build red, and the number can only come down.
///
/// ⚠ WHAT THIS COVERS AND WHAT IT DOES NOT (rule 11). It counts doc blocks carrying more than one
/// &lt;summary&gt;. That catches the insertion defect because the inserted member's own summary stacks
/// on top of the stolen one. It does NOT catch the same mistake where the new member has no doc
/// comment of its own — then there is one summary, on the wrong member, and this is blind to it.
/// It says nothing about whether any summary is ACCURATE. And it only reads the engine project.
/// </summary>
public class ADocCommentBelongsToTheMemberBelowItTests
{
    private readonly ITestOutputHelper _out;

    public ADocCommentBelongsToTheMemberBelowItTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// What the engine carried when this gate was written, counted rather than guessed.
    ///
    /// Lowering it is the only edit this constant is for: fix a stacked block, drop the number.
    /// </summary>
    private const int StackedSummariesWhenThisGateWasWritten = 34;

    [Fact]
    [Trait("Speed", "Fast")]
    public void NoNewMemberTakesTheDocCommentOfTheOneBelowIt()
    {
        string engine = Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.Core");
        var stacked = new List<string>();

        foreach (string path in Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length;)
            {
                if (!lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal)) { i++; continue; }

                int j = i;
                while (j < lines.Length && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal)) j++;

                int tags = 0;
                for (int k = i; k < j; k++)
                    if (lines[k].Contains("<summary>", StringComparison.Ordinal)) tags++;

                if (tags >= 2)
                    stacked.Add($"{Path.GetFileName(path)}:{i + 1} ({tags} <summary> tags on one member)");

                i = j;
            }
        }

        _out.WriteLine($"{stacked.Count} doc block(s) carry more than one <summary>; the ratchet is "
                     + $"{StackedSummariesWhenThisGateWasWritten}.");

        Assert.True(
            stacked.Count <= StackedSummariesWhenThisGateWasWritten,
            $"{stacked.Count} doc blocks now carry two or more <summary> tags, up from "
            + $"{StackedSummariesWhenThisGateWasWritten}. A doc comment attaches to the member BELOW "
            + "it, so this is the signature of a new member inserted between an existing comment and "
            + "its method — the old member silently loses its documentation and nothing else says so.\n\n"
            + "Move the new member below the one whose comment it took, then re-run. The newest "
            + "entries are the likely culprits:\n  "
            + string.Join("\n  ", stacked.TakeLast(6))
            + $"\n\nIf you genuinely reduced the count, lower {nameof(StackedSummariesWhenThisGateWasWritten)}. "
            + "It may only go down.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
