#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE VOCABULARY IN FORCE IS THE CALLER'S, NOT ANOTHER SET'S (step 71, 2026-09-15). The corpus analyzer composes
/// twelve sets at once in one process; each composition sets <c>PlanSheetNaming.Vocabulary</c> to its set's ranked
/// floor words for its duration (step 60). As a process-wide static that leaked between the sets: 30992-01 - MAIN
/// FLOOR PLAN, UPPER FLOOR PLAN, ROOF PLAN, nothing to rank - built L1 L2 ROOF in run 14 and L2 ROOF in run 15,
/// reading MAIN with whichever set was composing beside it. WHAT THIS COVERS: two flows setting the vocabulary at
/// once, each reading its own afterwards and through Parse; the flow that started them keeping its own. WHAT IT
/// DOES NOT: a set whose OWN composition runs on more than one thread (none does today); the analyzer itself.
/// Deterministic: the two flows meet at a barrier between setting and reading, so a process-wide static fails it
/// every time, not sometimes.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class TheVocabularyInForceIsTheCallersTests
{
    [Fact]
    public async Task TwoFlowsSettingTheVocabularyAtOnceEachReadTheirOwn()
    {
        var mainIs1 = DrawingVocabulary.Default with { FloorWords = ["MAIN=1", "UPPER=2"] };
        var mainIs2 = DrawingVocabulary.Default with { FloorWords = ["GROUND=1", "MAIN=2", "UPPER=3"] };
        PlanSheetNaming.Vocabulary = DrawingVocabulary.Default;
        using var bothSet = new Barrier(2);
        int[] levelSeen = new int[2];
        string[] wordsSeen = new string[2];

        Task Flow(int i, DrawingVocabulary mine) => Task.Run(() =>
        {
            PlanSheetNaming.Vocabulary = mine;
            bothSet.SignalAndWait(TimeSpan.FromSeconds(10));                // the other flow has set ITS words by now
            wordsSeen[i] = string.Join(";", PlanSheetNaming.Vocabulary.FloorWords);
            levelSeen[i] = PlanSheetNaming.Parse("S2.02_1_MAIN FLOOR PLAN.dxf").Levels.Single();
        });
        await Task.WhenAll(Flow(0, mainIs1), Flow(1, mainIs2));

        Assert.Equal("MAIN=1;UPPER=2", wordsSeen[0]);
        Assert.Equal("GROUND=1;MAIN=2;UPPER=3", wordsSeen[1]);
        Assert.Equal(1, levelSeen[0]);
        Assert.Equal(2, levelSeen[1]);
        // and this flow, which set the default before the two ran, still reads it: neither reached back here
        Assert.Equal(1, PlanSheetNaming.Parse("S2.02_1_MAIN FLOOR PLAN.dxf").Levels.Single());
    }
}
