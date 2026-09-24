using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WHERE BOTH SHEETS OF ONE PLAN ARE KEPT, THE BRACKETS SAY WHICH IS THE PLAN (intake step 139, 2026-09-23).
///
/// Step 50 keeps a sheet a non-structural word would refuse when the name ALSO carries a structural-plan word:
/// 31130's "CONCRETE OUTLINE PLANS &amp; POST TENSION REINFORCING" is one drawing, the outline with the tendons on
/// it. Step 80 then stands a kept sheet down when the set issues the same plan PLAINLY as well - 30990's
/// "TOWER A - FOUNDATION PLAN PARKING LEVEL P3" beside "... - FOOTING REINFORCING".
///
/// 31017-01 issues neither plainly. It puts the kind in brackets on both:
///     S2.01.1_1_Foundation Plan Parking Level P2 - Tower A (Concrete Outline &amp; Shear Reinforcing)
///     S2.01.2_1_Foundation Plan Parking Level P2 - Tower A (Footing Reinforcing)
/// Both carry REINFORC, so step 50 keeps BOTH by FOUNDATION PLAN in the stem, and neither is in the plain list
/// step 80 matches against - so the rebar sheet was read as a floor. What it read is not subtle: a flood-filled
/// plate of 14,639,164 sq ft on P1 and 1,562,283 sq ft on P2, both thrown away downstream as "inside one already
/// written", and 240,992 drawing units of slab edge that would not close. 31017-01 is the largest reading gap in
/// the corpus - 120,656 sq ft of the engineer's own area.
///
/// So a kept sheet whose trailing "(...)" names a structural plan offers its STEM as a plan too, and a sibling on
/// that stem whose own brackets say reinforcing is about it.
///
/// WHAT THIS COVERS: the trailing "(...)" of a sheet name, against the office's own two banked word lists, and the
/// stem the two siblings share. WHAT IT DOES NOT: a set that issues ONLY the combined sheet keeps it, brackets or
/// not (<see cref="NonStructuralSheetsAreRefusedTests"/> banks that case, and it is asserted again below); a sheet
/// about a plan but titled unlike its plan is not seen; a parenthesis naming a building, a quadrant or a phase
/// says nothing here, because it carries no word from either list; where BOTH siblings' brackets say reinforcing
/// neither is the plan and neither stands down; and it cannot tell a rebar sheet from a plan when the kind is in
/// the stem of both - the brackets are the only evidence it reads.
///
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a set that issues "LEVEL 4 PLAN - CONCRETE OUTLINE" and "LEVEL 4 PLAN -
/// SLAB REINFORCING" with a dash instead of brackets and no plain "LEVEL 4 PLAN" sheet. Step 80 needs the plain
/// sheet, this needs the brackets, and neither fires.
///
/// REACH, measured over the run-44 sheet ledger before this was written: 5,018 sheet files across 265 sets; step
/// 80 alone stands down 2, step 139 stands down 2 more, both of them in 31017-01 and both named below.
/// </summary>
public sealed class TheParenthesisSaysWhatTheSheetIsTests
{
    private readonly ITestOutputHelper _out;
    public TheParenthesisSaysWhatTheSheetIsTests(ITestOutputHelper output) => _out = output;

    /// <summary>The banked dxf.non-structural-sheet-patterns row as migrations 053 + 091 + 092 leave it.</summary>
    private static readonly string[] Banked =
    [
        "REINFORC", "REBAR", "KEY PLAN", "CORE WALL", "MODEL SETTING", "DESIGN LOAD", "LOAD PLAN", "SHORING",
        "DEMO", "SITE PLAN", "INSTRUMENTATION", "SENSOR LAYOUT", "LOADING PLAN", "LOADING DIAGRAM",
        "LANDSCAPE LOADING", "DESIGN LOADING",
    ];

    private static PlanClassificationOptions Office() => new() { NonStructuralSheetPatterns = Banked };

    /// <summary>31017-01's thirty plan sheets, exactly as the run-44 ledger records their DXF names.</summary>
    private static readonly string[] ThirtyOneThousandAndSeventeen =
    [
        "S1.21_1_Design Load Plan Level 4 Tower A",
        "S1.22_1_Design Load Plan Level 4 Tower A",
        "S2.01.1_1_Foundation Plan Parking Level P2 - Tower A (Concrete Outline & Shear Reinforcing)",
        "S2.01.2_1_Foundation Plan Parking Level P2 - Tower A (Footing Reinforcing)",
        "S2.02_1_Foundation Plan Parking Level P1 Tower A",
        "S2.03.1_1_Foundation Plan Parking Level P1 - Tower B (Concrete Outline & Shear Reinforcing)",
        "S2.03.2_1_Foundation Plan Parking Level P1 - Tower B (Footing Reinforcing)",
        "S2.04_1_Foundation Plan Parking Level P1 Commercial",
        "S2.05_1_Level 1 Plan Tower A",
        "S2.06_1_Level 1 Plan Tower B",
        "S2.07_1_Level 1 Plan Commercial",
        "S2.08_1_Level 2 Plan Tower A",
        "S2.09_1_Level 2 Plan Tower B",
        "S2.10_1_Level 3 Plan Tower A",
        "S2.11_1_Level 3 Plan Tower B",
        "S2.12.1_1_Level 4 Plan Tower A (Concrete Outline & Shear Reinforcing)",
        "S2.12.2_1_Level 4 Plan Tower A (Slab Reinforcing)",
        "S2.13.1_1_Level 4 Plan Tower B (Concrete Outline & Shear Reinforcing)",
        "S2.13.2_1_Level 4 Plan Tower B (Slab Reinforcing)",
        "S2.14_1_Level 5 Plan Tower A",
        "S2.15_1_Level 6 Plan Tower A",
        "S2.16_1_LEVEL 8 TO 22 PLANS",
        "S2.16_2_LEVEL 7 PLAN",
        "S2.17_1_Level 23 Plan Tower A",
        "S2.18_1_Level 24 Plan Tower A",
        "S2.19_1_ELEVATOR OVERRUN ROOF PLAN",
        "S2.19_2_ROOF SLAB REINFORCING PLAN",
        "S2.20_1_Level 2 Plan Commercial",
        "S2.21_1_Level 3 Plan Commercial",
        "S2.22_1_Level 4 Plan Commercial",
        "S2.23_1_Main Roof Plan Commercial",
        "S2.24_1_Level 6 Plan Commercial",
    ];

    [Fact]
    public void ThirtyOneThousandAndSeventeensTwoFootingSheetsStandDownAndNothingElseOnTheSetMoves()
    {
        var office = Office();
        var about = office.SheetsAboutAnotherPlan(ThirtyOneThousandAndSeventeen);
        foreach (var (sheet, plan) in about) _out.WriteLine($"{sheet}\n    is about \"{plan}\"");

        Assert.Equal(
        [
            ("S2.01.2_1_Foundation Plan Parking Level P2 - Tower A (Footing Reinforcing)",
             "Foundation Plan Parking Level P2 - Tower A"),
            ("S2.03.2_1_Foundation Plan Parking Level P1 - Tower B (Footing Reinforcing)",
             "Foundation Plan Parking Level P1 - Tower B"),
        ], about);

        // the four outline sheets are the plans, and stand: a sheet whose own brackets say it is the outline is
        // not about anything, least of all itself.
        foreach (string outline in ThirtyOneThousandAndSeventeen.Where(n => n.Contains("(Concrete Outline")))
            Assert.DoesNotContain(about, x => x.SheetName == outline);

        // the slab-reinforcing pair never reaches this rule at all - no structural word anywhere in the name, so
        // step 50 never kept them and RefusedBy turns them away.
        foreach (string slab in new[] { "S2.12.2_1_Level 4 Plan Tower A (Slab Reinforcing)", "S2.13.2_1_Level 4 Plan Tower B (Slab Reinforcing)" })
        {
            Assert.NotNull(office.RefusedBy(slab));
            Assert.Null(office.KeptBy(slab));
        }
    }

    [Fact]
    public void TheKnobPutsTheOldBehaviourBack()
    {
        Assert.Equal(2, Office().SheetsAboutAnotherPlan(ThirtyOneThousandAndSeventeen).Count);
        string? was = Environment.GetEnvironmentVariable("KOR_STEP139_OFF");
        try
        {
            Environment.SetEnvironmentVariable("KOR_STEP139_OFF", "1");
            Assert.Empty(Office().SheetsAboutAnotherPlan(ThirtyOneThousandAndSeventeen));
        }
        finally { Environment.SetEnvironmentVariable("KOR_STEP139_OFF", was); }
    }

    [Fact]
    public void ASetThatIssuesOnlyTheCombinedSheetStillKeepsIt()
    {
        // the case NonStructuralSheetsAreRefusedTests banks, asserted here too because step 139 is the rule that
        // could take it away: one foundation sheet with its footing bars on it, and no outline sibling to rank it
        // against, is the foundation plan.
        Assert.Empty(Office().SheetsAboutAnotherPlan(
        [
            "S2.00.2 - FOUNDATION PLAN / PARKING LEVEL P6 -SOUTH (FOOTING REINFORCING)",
            "S2.02.1_1_TOWER A - PARKING LEVEL P2",
        ]));

        // and 31130's outline-with-tendons, which has no brackets at all
        Assert.Empty(Office().SheetsAboutAnotherPlan(
            ["S2.06.1_1_LEVEL 3 - 16 CONCRETE OUTLINE PLANS & POST TENSION REINFORCING - WEST TOWER"]));
    }

    [Fact]
    public void WhereBothSiblingsSayReinforcingNeitherIsThePlanAndNeitherStandsDown()
        => Assert.Empty(Office().SheetsAboutAnotherPlan(
        [
            "S2.01.1_1_Foundation Plan Level P2 (Shear Reinforcing)",
            "S2.01.2_1_Foundation Plan Level P2 (Footing Reinforcing)",
        ]));

    [Theory]
    // a parenthesis that names a building, a quadrant or a phase carries no word from either list, so the stem
    // still decides and nothing stands down
    [InlineData("S2.13.1_1_Foundation Plan Level P2 (NW Quadrant)", "S2.13.2_1_Foundation Plan Level P2 (SE Quadrant)")]
    [InlineData("S2.13.1_1_Foundation Plan Level P2 (Tower A)", "S2.13.2_1_Foundation Plan Level P2 (Tower B)")]
    public void BracketsThatNameAPartOfTheBuildingSayNothingAboutTheDrawingsKind(string a, string b)
        => Assert.Empty(Office().SheetsAboutAnotherPlan([a, b]));

    [Fact]
    public void TheStemIsMatchedOnWordsNotLetters()
        => Assert.Empty(Office().SheetsAboutAnotherPlan(
        [
            "S2.01_1_Foundation Plan P3 (Concrete Outline)",
            "S2.02_1_Foundation Plan P30 (Footing Reinforcing)",
        ]));

    [Fact]
    public void StepEightysOwnCasesAreUntouchedByThis()
    {
        var office = new PlanClassificationOptions { NonStructuralSheetPatterns = Banked };

        // 30990 - the plan issued plainly, its rebar beside it, no brackets anywhere
        var thirtyThousandNineHundredAndNinety = office.SheetsAboutAnotherPlan(
        [
            "S2.01.1.1_1_TOWER A - FOUNDATION PLAN PARKING LEVEL P3",
            "S2.01.1.2_1_TOWER A - FOUNDATION PLAN PARKING LEVEL P3 - FOOTING REINFORCING",
            "S2.02.1_1_TOWER A - PARKING LEVEL P2",
        ]);
        Assert.Equal(
            [("S2.01.1.2_1_TOWER A - FOUNDATION PLAN PARKING LEVEL P3 - FOOTING REINFORCING", "TOWER A - FOUNDATION PLAN PARKING LEVEL P3")],
            thirtyThousandNineHundredAndNinety);

        // 31202 - the foundation plan and its loading diagram
        Assert.Equal(
            [("S2.01.5_1_FOUNDATION PLAN -LOADING DIAGRAM", "FOUNDATION PLAN")],
            office.SheetsAboutAnotherPlan(
                ["S2.01.1_1_FOUNDATION PLAN", "S2.01.5_1_FOUNDATION PLAN -LOADING DIAGRAM", "S2.03.1_1_LEVEL 2 PLAN"]));
    }
}
