using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Deciding what to build, before anything is built.
///
/// These decisions lived in a PowerShell script — which folder is the job, which model in it is the
/// engineer's, how many buildings the drawings describe. Nothing in the suite could reach them, so
/// the way they were checked was to run a publish against the live project share and look at what
/// came out. That is how a model went to an engineer carrying eight storeys of a building she had
/// said was out of scope: the cut was passed in by hand, by someone who had to know the shape of
/// the job to get it right.
/// </summary>
public class PublishPlanTests
{
    // 31168's shape: a shared parkade and podium, a mid-rise on part of it, and two towers rising
    // past everything. Extents in inches, and they are the point — the tower floors carry no
    // prefix, so only where their structure stands says they are not the mid-rise's.
    private static readonly string[] Site =
    {
        "B-LEVEL 28", "A-LEVEL 28", "B-LEVEL 27", "A-LEVEL 27",
        "LEVEL 12", "LEVEL 11", "C-ROOF", "C-LEVEL 3",
        "LEVEL 2", "B-LEVEL 1", "A-LEVEL 1", "LEVEL P1", "Base",
    };

    private static PublishPlan.StoreyReach At(string storey, double x0, double y0, double x1, double y1)
        => new(storey, x0 * 12, y0 * 12, x1 * 12, y1 * 12);

    private static readonly PublishPlan.StoreyReach[] Reach =
    {
        At("A-LEVEL 28", -106, 213, -3, 308),      // tower A footprint
        At("A-LEVEL 27", -106, 213, -3, 308),
        At("B-LEVEL 28", 86, 213, 189, 308),       // tower B footprint
        At("B-LEVEL 27", 86, 213, 189, 308),
        At("LEVEL 11", -106, 213, 189, 308),       // both towers, no prefix
        At("LEVEL 12", -106, 213, 189, 308),
        At("C-ROOF", -60, 358, 143, 426),          // the mid-rise, well clear of the towers
        At("C-LEVEL 3", -60, 358, 143, 426),
        At("LEVEL 2", -115, 203, 217, 436),        // site-wide podium
        At("LEVEL P1", -115, 203, 217, 436),       // site-wide parkade
        At("A-LEVEL 1", -115, 203, 217, 436),
        At("B-LEVEL 1", -115, 203, 217, 436),
    };

    [Fact]
    public void EachBuildingGetsAModelAndTheSharedBaseIsInAllOfThem()
    {
        var plans = PublishPlan.ForBuildings(Site, Reach);

        Assert.Equal(new[] { "A", "B", "C" }, plans.Select(p => p.Building).ToArray());

        // The podium and parkade stand under every building, so nobody drops them.
        Assert.All(plans, p =>
        {
            Assert.DoesNotContain("LEVEL 2", p.DropStoreys);
            Assert.DoesNotContain("LEVEL P1", p.DropStoreys);
        });
    }

    /// <summary>
    /// The one that had to be passed in by hand. LEVEL 11 and LEVEL 12 are tower floors with no
    /// prefix, sitting at the mid-rise's own elevations, so neither a name filter nor an elevation
    /// cut can see them — an elevation cut keeps them, which is how eight storeys of somebody
    /// else's building went to the engineer. Where their structure stands says it plainly.
    /// </summary>
    [Fact]
    public void UnprefixedTowerFloorsAreNotTheMidRises()
    {
        var midRise = PublishPlan.ForBuildings(Site, Reach).Single(p => p.Building == "C");

        Assert.Contains("LEVEL 11", midRise.DropStoreys);
        Assert.Contains("LEVEL 12", midRise.DropStoreys);
    }

    [Fact]
    public void ATowerKeepsTheFloorsItSharesWithItsTwin()
    {
        var towerA = PublishPlan.ForBuildings(Site, Reach).Single(p => p.Building == "A");

        // LEVEL 11 and 12 carry both towers. Tower A's model keeps them: they are its floors too,
        // and which MEMBERS on them are its own is a different question from which storeys are.
        Assert.DoesNotContain("LEVEL 11", towerA.DropStoreys);
        Assert.DoesNotContain("LEVEL 12", towerA.DropStoreys);
    }

    /// <summary>
    /// A storey named for another building is left to the tower filter, which keeps the shared base
    /// BELOW this building rather than dropping it. Listing it here as well would take the mid-rise
    /// out from under its own ground floor — 31168's is drafted as A-LEVEL 1 and B-LEVEL 1, 1.7 in
    /// apart, and building C stands on it.
    /// </summary>
    [Fact]
    public void TheGroundFloorDraftedForAnotherBuildingIsNotDropped()
    {
        var midRise = PublishPlan.ForBuildings(Site, Reach).Single(p => p.Building == "C");

        Assert.DoesNotContain("A-LEVEL 1", midRise.DropStoreys);
        Assert.DoesNotContain("B-LEVEL 1", midRise.DropStoreys);
        Assert.Equal("C", midRise.Tower);
    }

    [Fact]
    public void AJobWithOneBuildingIsCutNoWayAtAll()
    {
        var plans = PublishPlan.ForBuildings(
            new[] { "ROOF", "L02", "L01", "Base" },
            new[] { At("L01", 0, 0, 100, 100), At("L02", 0, 0, 100, 100), At("ROOF", 0, 0, 100, 100) });

        var only = Assert.Single(plans);
        Assert.Empty(only.DropStoreys);
        Assert.Equal(string.Empty, only.Tower);
    }

    // ------------------------------------------------------------------------------------------

    [Fact]
    public void OurOwnOutputIsNeverTheReference()
    {
        string? chosen = PublishPlan.ChooseReference(new (string, Func<string>)[]
        {
            ("31168-FROM-DRAWINGS.e2k", () => "  AREA \"KW1\"  PANEL"),
            ("31168-reference.e2k", () => "  AREA \"W1\"  PANEL"),
        }, out string why);

        Assert.Equal("31168-reference.e2k", chosen);
        Assert.Equal(string.Empty, why);
    }

    /// <summary>
    /// A model round-tripped through ETABS keeps the names this tool stamped on it, so the file
    /// name alone does not say whose it is. That is how a generated model once got mistaken for an
    /// engineer's own and rebuilt from itself.
    /// </summary>
    [Fact]
    public void AModelRoundTrippedThroughEtabsIsStillOurs()
    {
        string? chosen = PublishPlan.ChooseReference(new (string, Func<string>)[]
        {
            ("Anrea_look.$et", () => "  LINE \"KC42\"  COLUMN"),
        }, out string why);

        Assert.Null(chosen);
        Assert.Contains("generated by this tool", why);
    }

    [Fact]
    public void TwoCandidatesAndNoWayToChooseIsRefusedRatherThanGuessed()
    {
        string? chosen = PublishPlan.ChooseReference(new (string, Func<string>)[]
        {
            ("31168-site.e2k", () => "  AREA \"W1\"  PANEL"),
            ("31168-towerB.e2k", () => "  AREA \"W2\"  PANEL"),
        }, out string why);

        Assert.Null(chosen);
        Assert.Contains("not", why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("31168-towerB.e2k", why);
    }
}
