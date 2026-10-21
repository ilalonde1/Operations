using Kor.Operations.EngineeringTools.Intake;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A LADDER MUST STATE THE HEIGHT IT ACTUALLY USED (2026-09-23).
///
/// A storey the plans name and no elevation places, standing between two STATED levels, is given an even share
/// of that gap — the assumed height never moves a fact. But it was then listed under the levels file's ASSUMED
/// header, which names the ASSUMED height, and on two sets of the corpus the two numbers were nothing like each
/// other. Both files contradicted themselves, and nothing was checking:
///
///   31017-01  twenty storeys "ASSUMED … taken as the set's typical storey, 3050 mm", standing  164 mm apart
///   31004-01  five   storeys claiming                                  2945 mm, standing   33 mm apart
///
/// Measured over the 278 sets built on disk: 204 ladders claim an assumed height, 3 stand a claimed storey less
/// than half of it — those two, and 31168-01 at 38%, whose one storey is a roof overrun and is plausible.
///
/// 31017-01 is the largest reading gap in the corpus, 120,656 sq ft of the engineer's own area, and its model is
/// a 24-storey tower 18 m tall. The cause is not the spacing rule. 31017-01 draws a tower (L1..L24) and a
/// commercial podium (C1..C4) on one set; the elevations state L4 at 15,057 mm and C4 at 18,512 mm; and twenty
/// tower storeys were shared out over the podium's last 3,455 mm because the ladder is one column of elevations
/// for two buildings.
///
/// WHAT THIS COVERS: that a spaced storey is not counted or listed under the ASSUMED header, which names a height
/// it does not stand at; that the levels file carries its own line naming the run, its two brackets and the step
/// actually used; and that a step far under the height the set's storeys stand at carries a warning that names
/// the two brackets to check.
///
/// WHAT IT DOES NOT: tell whose building a level belongs to in general. Step 169 (2026-10-21) puts the storeys
/// where they belong only where the gap gives it away — an even share under half the set's storey — and 31017-01's
/// tower now stands at 3,050 mm a storey. It does not fire where both brackets ARE the same building's and the
/// gap is genuinely small, because there is nothing wrong there. And the same-class fault it would NOT catch:
/// a run bracketed by another building's level whose gap happens to be plausible — two buildings with similar
/// storey heights would space evenly, wrongly, and silently.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class ALadderStatesTheHeightItActuallyUsedTests
{
    private readonly ITestOutputHelper _out;
    public ALadderStatesTheHeightItActuallyUsedTests(ITestOutputHelper output) => _out = output;

    private static SetStoreys.Chain Chain(double typicalMm, params (string Name, double ElevationMm)[] levels)
        => new(levels.Select(l => new SetStoreys.Level(l.Name, l.ElevationMm, "stated on S3.01")).ToList(),
               [levels[0].Name], [], [], [], typicalMm);

    /// <summary>31017-01's own shape: a tower and a podium on one set, the podium's C4 bracketing the tower.</summary>
    private static StoreysFromPlans.Ladder ThirtyOneThousandAndSeventeen()
    {
        var plans = new List<string>
        {
            "S2.01_1_FOUNDATION PLAN PARKING LEVEL P2.dxf", "S2.02_1_FOUNDATION PLAN PARKING LEVEL P1.dxf",
            "S2.05_1_LEVEL 1 PLAN.dxf", "S2.08_1_LEVEL 2 PLAN.dxf", "S2.10_1_LEVEL 3 PLAN.dxf",
            "S2.12_1_LEVEL 4 PLAN.dxf",
        };
        for (int n = 5; n <= 24; n++) plans.Add($"S2.{n + 9}_1_LEVEL {n} PLAN.dxf");   // L5..L24, named by plans only
        return StoreysFromPlans.Merge(
            Chain(3050, ("P2", 0), ("P1", 1544), ("L1", 4592), ("L2", 7640), ("L3", 10688), ("L4", 15057), ("C4", 18512)),
            plans, assumedHeightMm: 3050);
    }

    /// <summary>
    /// STEP 169 (2026-10-21): the larger job this class was the evidence for. An even share under half the set's storey
    /// means the stated level above is another building's, so 31017-01's L5..L24 walk up from L4 at 3,050 mm a storey,
    /// ASSUMED and listed so — the tower 76 m tall, as her model has it at 120 in a storey — and C4 keeps its stated 18,512.
    /// </summary>
    [Fact]
    public void ARunUnderHalfAStoreyAFloorWalksUpAtTheSetsStoreyAndIsListedAsAssumed()
    {
        var ladder = ThirtyOneThousandAndSeventeen();
        var lines = StoreysFromPlans.LevelsFileLines(ladder);
        foreach (string l in lines) _out.WriteLine(l);

        Assert.DoesNotContain(ladder.Storeys, s => s.Spaced);
        var at = ladder.Storeys.ToDictionary(s => s.Name, s => s.ElevationMm);
        for (int n = 5; n <= 24; n++) Assert.Equal(15057 + 3050 * (n - 4), at[$"L{n}"], 0);
        Assert.Equal(18512, at["C4"], 0);
        string assumed = Assert.Single(lines, l => l.StartsWith("# ASSUMED:", StringComparison.Ordinal));
        Assert.Contains("3050 mm", assumed, StringComparison.Ordinal);
        Assert.Contains(" L24", assumed, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.StartsWith("# SPACED EVENLY:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A ladder whose storeys WERE shared out far under the set's storey (as KOR_STEP169_OFF builds 31017-01) still
    /// says so: the run, its two brackets, the step, and the warning naming the two levels to check. Built directly,
    /// so no environment flag is set where a class running beside this one could read it.
    /// </summary>
    [Fact]
    public void TheLevelsFileNamesTheRunItsTwoBracketsAndTheStepItActuallyUsed()
    {
        var storeys = new List<StoreysFromPlans.Storey> { new("L4", 15057, "stated on S3.01", false) };
        for (int n = 5; n <= 24; n++) storeys.Add(new($"L{n}", 15057 + 164.5 * (n - 4), "spaced", false, Spaced: true));
        storeys.Add(new("C4", 18512, "stated on S3.01", false));
        var ladder = new StoreysFromPlans.Ladder(storeys, 2, 20, 0, 3050, "the set's typical storey, 3050 mm");

        var lines = StoreysFromPlans.LevelsFileLines(ladder);
        string note = Assert.Single(lines, l => l.StartsWith("# SPACED EVENLY:", StringComparison.Ordinal));
        _out.WriteLine(note);

        Assert.Contains("between L4 and C4", note, StringComparison.Ordinal);
        Assert.Contains("mm a storey", note, StringComparison.Ordinal);
        // and it warns, naming the two levels to check, because the step is nothing like the set's storey
        Assert.Contains("⚠", note, StringComparison.Ordinal);
        Assert.Contains("SAME building", note, StringComparison.Ordinal);
        Assert.Contains("⚠", StoreysFromPlans.Summary(ladder), StringComparison.Ordinal);
        // and a spaced storey is not listed under the ASSUMED header, which names a height it does not stand at
        Assert.DoesNotContain(lines, l => l.StartsWith("# ASSUMED:", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunBracketedByItsOwnBuildingsLevelsSaysSoAndDoesNotWarn()
    {
        // the elevations state L1, L2 and L5; the plans also name L3 and L4. 3,100 mm a storey against a typical
        // 3,000 is the spacing rule working, and there is nothing to warn about.
        var ladder = StoreysFromPlans.Merge(
            Chain(3000, ("L1", 0), ("L2", 3000), ("L5", 12300)),
            ["S2.01_1_LEVEL 1 PLAN.dxf", "S2.02_1_LEVEL 2 PLAN.dxf", "S2.03_1_LEVEL 3 PLAN.dxf",
             "S2.04_1_LEVEL 4 PLAN.dxf", "S2.05_1_LEVEL 5 PLAN.dxf"],
            assumedHeightMm: 3000);

        string note = Assert.Single(StoreysFromPlans.LevelsFileLines(ladder), l => l.StartsWith("# SPACED EVENLY:", StringComparison.Ordinal));
        _out.WriteLine(note);
        Assert.Contains("L3..L4, between L2 and L5", note, StringComparison.Ordinal);
        Assert.Contains("3100 mm a storey", note, StringComparison.Ordinal);
        Assert.DoesNotContain("⚠", note, StringComparison.Ordinal);
        Assert.DoesNotContain("⚠", StoreysFromPlans.Summary(ladder), StringComparison.Ordinal);
    }

    [Fact]
    public void ALadderWithNothingSpacedCarriesNoSuchLine()
    {
        var ladder = StoreysFromPlans.Merge(
            Chain(3000, ("P1", 0), ("L1", 3000), ("L2", 6000)),
            ["S2.01_1_LEVEL P1 PLAN.dxf", "S2.02_1_LEVEL 1 PLAN.dxf", "S2.03_1_LEVEL 2 PLAN.dxf"]);
        Assert.DoesNotContain(StoreysFromPlans.LevelsFileLines(ladder), l => l.StartsWith("# SPACED EVENLY:", StringComparison.Ordinal));
        Assert.Empty(StoreysFromPlans.SpacingNotes(ladder));
    }
}
