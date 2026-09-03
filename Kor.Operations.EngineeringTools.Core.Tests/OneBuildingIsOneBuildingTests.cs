using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Asking for one building must give the same model as asking for all of them and taking that one.
/// </summary>
/// <remarks>
/// It did not. The storey drop was derived inside the --per-building branch and nothing shared it,
/// so building C came out two ways from the same drawings: 14 storeys under --per-building, and 38
/// under --tower C — identical members, plus the towers' LEVEL 3..26 carried along empty. The
/// tower filter cuts members and never cut storeys, and nothing said the two disagreed.
/// </remarks>
public class OneBuildingIsOneBuildingTests
{
    /// <summary>The 31168 shape: C's own storeys, the towers' own, and the shared base under both.</summary>
    private static IReadOnlyList<PublishPlan.Model> Derived() =>
    [
        new("A", "A", new[] { "LEVEL 3", "C-ONLY-DECK" }),
        new("C", "C", new[] { "LEVEL 3", "LEVEL 4", "LEVEL 26" }),
    ];

    [Fact]
    public void AskingForOneBuildingDropsWhatAskingForAllOfThemDrops()
    {
        var one = Assert.Single(JobPublisher.ChoosePlans(Derived(), tower: "C", variant: null, perBuilding: false));

        Assert.Equal("C", one.Tower);
        Assert.Equal(new[] { "LEVEL 3", "LEVEL 4", "LEVEL 26" }, one.DropStoreys);
    }

    [Fact]
    public void ItIsTheSamePlanTheWholeSetWouldHaveUsed()
    {
        var alone = Assert.Single(JobPublisher.ChoosePlans(Derived(), "C", null, perBuilding: false));
        var inTheSet = Assert.Single(
            JobPublisher.ChoosePlans(Derived(), null, null, perBuilding: true), p => p.Tower == "C");

        Assert.Equal(inTheSet.DropStoreys, alone.DropStoreys);
    }

    [Fact]
    public void LowerCaseAsksForTheSameBuilding()
    {
        var one = Assert.Single(JobPublisher.ChoosePlans(Derived(), tower: "c", variant: null, perBuilding: false));

        Assert.Equal(new[] { "LEVEL 3", "LEVEL 4", "LEVEL 26" }, one.DropStoreys);
    }

    /// <summary>
    /// A variant is the whole site under another name — 31168-TOWERS — so it has no building to
    /// derive a footprint from and must keep every storey.
    /// </summary>
    [Fact]
    public void AVariantKeepsEveryStorey()
    {
        var one = Assert.Single(JobPublisher.ChoosePlans(Derived(), tower: null, variant: "TOWERS", perBuilding: false));

        Assert.Empty(one.DropStoreys);
        Assert.Equal(string.Empty, one.Tower);
        Assert.Equal("TOWERS", one.Building);
    }

    /// <summary>
    /// A tower tag the storeys do not carry has no footprint, and inventing a drop list from
    /// nothing would cut real structure. Cut members only.
    /// </summary>
    [Fact]
    public void ABuildingTheStoreysDoNotNameDropsNothing()
    {
        var one = Assert.Single(JobPublisher.ChoosePlans(Derived(), tower: "Z", variant: null, perBuilding: false));

        Assert.Equal("Z", one.Tower);
        Assert.Empty(one.DropStoreys);
    }

    /// <summary>With no building asked for at all, the run is the whole site.</summary>
    [Fact]
    public void NoBuildingAskedForIsTheWholeSite()
    {
        var one = Assert.Single(JobPublisher.ChoosePlans(Derived(), tower: null, variant: null, perBuilding: false));

        Assert.Equal(string.Empty, one.Tower);
        Assert.Empty(one.DropStoreys);
    }
}
