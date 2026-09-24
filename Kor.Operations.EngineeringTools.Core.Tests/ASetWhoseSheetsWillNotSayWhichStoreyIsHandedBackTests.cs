using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A drawing set the tool cannot read is handed back with a question, not shipped as a model of
/// part of a building (intake step 143, 2026-09-24).
///
/// Ian gave the tool permission to fail out loud: "if there's an anomalous project drawing that is
/// so broken you can't do it - ignore it and move on to the other 95% we CAN build. If something is
/// SO garbled and shitty - just reject it with the list of questions we would usually present to the
/// engineer." And the reason it lands as a QUESTION rather than a warning: "the machine does as MUCH
/// as it possibly can (and it gets better every time with the gained knowledge of answered
/// questions)." A warning is read once. An answer is banked and never asked again.
///
/// ⚠ WHAT THESE TESTS COVER, AND WHAT THEY DO NOT (rule 11).
///
/// COVERED: that the signal is computed from the SHIPPED ladder against the storeys that received a
/// plate; that it needs four storeys before it judges; that it stays silent on a model that floored
/// most of its ladder; that a storey present in <c>PlatesByStorey</c> with a zero count is treated as
/// unfloored; and that the question reaches the workbook naming both the storeys with no floor and
/// the drawings whose title named no storey.
///
/// NOT COVERED, and worth saying so plainly because the name of this class is wider than the check:
/// it is a COUNT of storeys and nothing else. A storey given a plate of the wrong shape, in the
/// wrong place, or a tenth of the area it should be passes every test here — 31093-01's twin 4-point
/// rectangles repeated on three storeys are exactly that fault and this check is blind to them.
/// Walls, columns, thicknesses and openings are outside it entirely.
///
/// A same-class fault it would NOT catch: a set whose ELEVATIONS are short — six storeys named on the
/// drawings of a twenty-storey building — reads a perfect 6 of 6 here while missing most of the
/// tower, because both sides of the comparison are read from the same drawings. Only the engineer's
/// own model catches that one, and it is the yardstick's job, not this one's.
/// </summary>
public class ASetWhoseSheetsWillNotSayWhichStoreyIsHandedBackTests
{
    private static E2kModelContents Model(IReadOnlyList<string> storeys, IReadOnlyDictionary<string, int> plates) =>
        E2kModelContents.Empty with { Storeys = storeys, PlatesByStorey = plates };

    private static Dictionary<string, int> Plated(params string[] storeys) =>
        storeys.ToDictionary(s => s, _ => 1, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void HalfTheLadderWithNoFloorIsSaidPlainlyAndTheStoreysAreNamed()
    {
        var said = DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["P1", "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "ROOF"],
            Plated("P1", "L1", "L2", "L3")));

        string note = Assert.Single(said);
        Assert.Contains("NAME 10 STOREYS AND ONLY 4 OF THEM RECEIVED A FLOOR", note, StringComparison.Ordinal);

        // The storeys she would go looking for, by name. A count on its own sends her to the file.
        foreach (string storey in new[] { "L4", "L5", "L6", "L7", "L8", "ROOF" })
            Assert.Contains(storey, note, StringComparison.Ordinal);

        // And not the ones that are fine.
        Assert.DoesNotContain("at all: P1", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelThatFlooredMostOfItsLadderSaysNothing()
    {
        // 7 of 10 floored: three storeys with no plate is a building with a question, which J1 and
        // F2 already ask. It is not a set that could not be read.
        Assert.Empty(DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["P1", "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "ROOF"],
            Plated("P1", "L1", "L2", "L3", "L4", "L5", "L6"))));
    }

    [Fact]
    public void ExactlyHalfTripsIt()
    {
        // The boundary, stated so a later edit to the comparison has to come through here.
        Assert.Single(DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["L1", "L2", "L3", "L4"], Plated("L1", "L2"))));
    }

    [Fact]
    public void ThreeStoreysIsNotEnoughToJudgeASet()
    {
        // FOUR, because the smallest sets in the corpus are parkade-only and would trip on one
        // storey. A three-storey ladder missing two is not evidence that the drawings are unreadable.
        Assert.Empty(DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["P1", "L1", "ROOF"], Plated("P1"))));
    }

    [Fact]
    public void AStoreyRecordedWithZeroPlatesCountsAsHavingNoFloor()
    {
        // PlatesByStorey is built by counting UP from plates, so a storey with none is normally
        // absent rather than zero. Both spellings have to mean the same thing, or the day the
        // dictionary starts being pre-seeded this check goes quiet and nothing turns red.
        var said = DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["L1", "L2", "L3", "L4"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["L1"] = 1, ["L2"] = 1, ["L3"] = 0, ["L4"] = 0,
            }));

        string note = Assert.Single(said);
        Assert.Contains("ONLY 2 OF THEM RECEIVED A FLOOR", note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuestionNamesTheStoreysWithNoFloorAndTheDrawingsSheCanPutALevelOn()
    {
        var flag = Assert.Single(DxfToEtabsService.MostOfTheLadderGotNoFloor(Model(
            ["L1", "L2", "L3", "L4", "L5", "L6"], Plated("L1", "L2"))));

        // ⚠ EVERY COUNT ON THESE TWO SHEETS IS ZERO, AND THAT IS NOT A SHORTCUT IN THE FIXTURE —
        // IT IS THE REAL SHAPE OF THE DATA, and the reason the first cut of this question was wrong.
        //
        // SheetOutcome's Walls/Columns/Slabs are read back from the finished file AFTER the cut, so
        // a sheet that reached no storey contributed no objects and every count on it is 0. A filter
        // of `Walls + Columns + Slabs > 0` therefore removed precisely the sheets this question
        // exists to name, and 31005-01 — the set it was written for — produced "None of this set's
        // drawings is waiting on a level" in the shipped workbook. This fixture is built so that
        // filter can never come back green.
        var sheets = new SheetOutcome[]
        {
            new("S2.05 LEVEL PLAN CONCRETE.dxf", "", null, [], [], 0, 0, 0, []) { NamedStories = [] },
            new("S2.06 OUTLINE PLAN CHANGE LEVEL.dxf", "", null, [], [], 0, 0, 0, []) { NamedStories = [] },
            // Named its storey: nothing about it is open, whatever else is wrong with the set.
            new("S2.01 LEVEL 1 PLAN.dxf", "", null, [], ["L1"], 30, 20, 1, []) { NamedStories = ["L1"] },
            // Read but never set on the grid — a details or sections sheet names no storey either,
            // and putting it in front of her as "which level is this" wastes the one thing being
            // asked of her.
            new("S5.01 TYPICAL DETAILS.dxf", "", null, [], [], 0, 0, 0, []) { NamedStories = [] },
        };

        var question = Assert.Single(
            ModelQuestionnaire.StandingQuestions(
                new PlanClassificationOptions(),
                new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 },
                Report(sheets, flag)),
            q => q.Code == "J8");

        Assert.Equal("a-set-whose-sheets-will-not-say-which-storey", question.RuleTopic);

        // It is a fault in what was built, not a preference she is being asked to state.
        Assert.True(question.Defect);
        Assert.False(question.Decided);

        Assert.Contains("L3, L4, L5, L6", question.Question, StringComparison.Ordinal);
        Assert.Contains("WHICH LEVEL DOES EACH OF THESE 2 DRAWINGS SHOW?", question.Question, StringComparison.Ordinal);

        // The two that were read, positioned, and could not be named. This is the assertion that
        // fails if the post-cut counts are ever filtered on again.
        Assert.Contains("S2.05 LEVEL PLAN CONCRETE.dxf", question.Question, StringComparison.Ordinal);
        Assert.Contains("S2.06 OUTLINE PLAN CHANGE LEVEL.dxf", question.Question, StringComparison.Ordinal);

        // The sheet that DID name its storey is not on the list: nothing about it is open.
        Assert.DoesNotContain("S2.01", question.Question, StringComparison.Ordinal);

        // Nor is the one that never reached the grid.
        Assert.DoesNotContain("S5.01", question.Question, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelThatBuiltItsLadderIsAskedNothing()
    {
        Assert.DoesNotContain(
            ModelQuestionnaire.StandingQuestions(
                new PlanClassificationOptions(),
                new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 },
                Report(Array.Empty<SheetOutcome>(), flag: null)),
            q => q.Code == "J8");
    }

    /// <summary>
    /// Every sheet but the details one reached the model's grid, which is what the run records in
    /// <see cref="DxfToEtabsReport.SheetsSetOnGridByName"/> — by file name, as the run writes it.
    /// </summary>
    private static DxfToEtabsReport Report(IReadOnlyList<SheetOutcome> sheets, string? flag) =>
        new(
            Path.Combine(Path.GetTempPath(), "31005-01.e2k"), sheets.Count, 1, 6,
            new ComposeSummary(1, 1, 1, 4, 6, [], flag is null ? [] : [flag]),
            (0, 0),
            sheets,
            [],
            new PlanClassificationOptions(),
            new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 })
        {
            SheetsSetOnGridByName = sheets
                .Select(s => s.File)
                .Where(f => !f.StartsWith("S5.", StringComparison.Ordinal))
                .ToList(),
        };
}
