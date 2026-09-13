using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A drawing set holds more than the plans a model is built from.
///
/// 31168's Revit export offers 139 plan views and 57 are reinforcing plans, core-wall key plans,
/// uncropped working views and a design load plan — drawings whose linework is a schematic OF the
/// building rather than the building. Until 2026-08-26 the generator read every .dxf in the
/// folder it was given and the filtering happened by hand, in a script outside the tool, which
/// protected exactly one job.
///
/// It also failed once, the way an out-of-band filter eventually does: a DESIGN LOAD PLAN slipped
/// through, its zone boundary was read as slab edge, and 10,245 sq ft came out of B-LEVEL 1's mat
/// as an opening — 93 per cent of the plate. Nothing turned red.
/// </summary>
public class NonStructuralSheetsAreRefusedTests
{
    private readonly ITestOutputHelper _out;

    public NonStructuralSheetsAreRefusedTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The rule as KorStandards holds it, so this test and the run agree on what the words are.
    /// Named here rather than read from the database because the point is the BEHAVIOUR: a
    /// pattern in the list refuses, one outside it does not.
    /// </summary>
    private static readonly string[] Patterns =
        "REINFORC;REBAR;KEY PLAN;CORE WALL;MODEL SETTING;DESIGN LOAD;LOAD PLAN;SHORING;DEMO".Split(';');

    [Theory]
    // Refused: every one of these is a real 31168 view name.
    [InlineData("--Structural Plan - S1.11_1_LEVEL 1 PLAN - DESIGN LOAD PLAN", false)]
    [InlineData("--Structural Plan - S3.01_3_KEY PLAN - CORE WALLS (LEVEL 02) - BLDG A", false)]
    [InlineData("--Structural Plan - LEVEL 2 PLAN - for reinforcing plan", false)]
    [InlineData("--Structural Plan - LEVEL 3 - MODEL SETTING", false)]
    // Read: the plans a model is built from, including the one whose name contains "PLAN" twice
    // and the tower floors that are named for nothing but their level.
    [InlineData("--Structural Plan - S2.40.1_1_LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C", true)]
    [InlineData("--Structural Plan - LEVEL 2 PLAN - CONCRETE OUTLINE", true)]
    [InlineData("--Structural Plan - A-LEVEL 28", true)]
    [InlineData("--Structural Plan - LEVEL P3 PLAN - FOUNDATION", true)]
    // Read, step 50: a sheet that says what it is, is that. 31130's fourteen tower storeys are drawn on a
    // concrete outline that carries the tendons, and REINFORC refused it (7 storeys with columns where the
    // engineer's model has 20); a foundation plan with the footing reinforcing on it is the foundation plan.
    [InlineData("S2.06.1_1_LEVEL 3 - 16 CONCRETE OUTLINE PLANS & POST TENSION REINFORCING - WEST TOWER", true)]
    [InlineData("S2.00.2 - FOUNDATION PLAN / PARKING LEVEL P6 -SOUTH (FOOTING REINFORCING)", true)]
    // ...but a reinforcing plan of a slab is a reinforcing plan, and a design load plan says nothing structural
    [InlineData("S2.04.2_1_LEVEL 1 PLAN SLAB REINFORCING WEST - TOWER", false)]
    [InlineData("S2.06.2_1_LEVEL 3 - 16 REINFORCING PLANS WEST TOWER -", false)]
    public void OnlyThePlansThatDrawTheStructureAreRead(string fileName, bool shouldBeRead)
    {
        var options = new PlanClassificationOptions { NonStructuralSheetPatterns = Patterns };
        string? hit = options.RefusedBy(fileName);

        _out.WriteLine($"{fileName}  ->  {(hit is null ? "read" + (options.KeptBy(fileName) is { } w ? $" (kept by {w})" : "") : "refused by " + hit)}");

        Assert.Equal(shouldBeRead, hit is null);
    }

    /// <summary>
    /// The option exists, defaults to reading everything, and carries the rule when given one.
    ///
    /// Defaulting to EMPTY is deliberate. A folder somebody has already curated — drafting's own
    /// export, the set on the share since June — must not have sheets taken out of it by a rule
    /// written for a different set, and a firm that names its plans nothing like KOR's would
    /// otherwise lose floors to a pattern that means nothing to them.
    /// </summary>
    [Fact]
    public void TheRuleIsAnOptionAndReadsEverythingUntilItIsGivenOne()
    {
        Assert.Empty(new PlanClassificationOptions().NonStructuralSheetPatterns);

        var withRule = new PlanClassificationOptions { NonStructuralSheetPatterns = Patterns };
        Assert.Contains("DESIGN LOAD", withRule.NonStructuralSheetPatterns);

        // And it is a required rule, so a run cannot quietly proceed without it.
        Assert.Contains("dxf.non-structural-sheet-patterns", DxfToEtabsService.RequiredRuleKeys);
    }
}
