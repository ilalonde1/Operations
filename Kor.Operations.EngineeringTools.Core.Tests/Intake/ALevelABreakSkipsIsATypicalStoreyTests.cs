#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The set's levels, chained from its lowest at 0 (intake step 25). A storey stated from one level
/// to another that skips names — LEVEL 13 drawn straight above LEVEL 3 with a break line between,
/// which is how a drafter draws a run of typical storeys once — is a break, not a height: the
/// levels it skips stand at the set's typical storey each. A level named twice keeps its first
/// statement. A storey from one building's level to another's is nobody's.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a plain chain from the base; a break filled at the typical storey with the
/// level above the break placed on the filled ones, not on the drawn gap; the typical storey taken
/// from the storeys that are not breaks; a level stated twice keeping its first statement; a
/// storey across two buildings left unchained while each building's own chain continues; a set
/// with a break and no typical storey to fill it, which leaves the levels above unchained and
/// says so. WHAT IT DOES NOT: reading the ladders (the ladder tests and the banked elevation
/// pages); the drawn gap of a break agreeing with anything — it is discarded by design; a break
/// across a parkade or a mezzanine, whose names carry no number to skip.
/// </remarks>
public sealed class ALevelABreakSkipsIsATypicalStoreyTests
{
    private static SetStoreys.Table Table(params (string Level, string Below, double Mm)[] storeys)
        => new(storeys.Select(s => new SetStoreys.SetStorey(s.Level, s.Below, s.Mm, 1, 0)).ToList(), 1, 1);

    private static double At(SetStoreys.Chain chain, string name) => chain.Levels.Single(l => l.Name == name).ElevationMm;

    [Fact]
    public void LevelsChainFromTheLowestStatedAtZero()
    {
        var chain = SetStoreys.Levels(Table(("L3", "L2", 3000), ("L2", "L1", 3000), ("L1", "P1", 4000)));
        Assert.Equal(new[] { "P1" }, chain.Bases);
        Assert.Equal(0, At(chain, "P1"));
        Assert.Equal(4000, At(chain, "L1"));
        Assert.Equal(10000, At(chain, "L3"));
        Assert.Empty(chain.Breaks);
        Assert.Empty(chain.Unchained);
    }

    [Fact]
    public void ABreakSkipsLevelsAndTheyStandAtTheTypicalStorey()
    {
        // LEVEL 13 drawn 921 mm above LEVEL 3 across a break line; the set's typical storey is 2,950
        var chain = SetStoreys.Levels(Table(
            ("L2", "L1", 2950), ("L3", "L2", 2950), ("L13", "L3", 921), ("L14", "L13", 2950), ("L15", "L14", 2950)));
        Assert.Equal(2950, chain.TypicalMm);
        Assert.Single(chain.Breaks);
        Assert.Equal(2950 * 2, At(chain, "L3"));
        for (int n = 4; n <= 13; n++) Assert.Equal(2950 * (n - 1), At(chain, $"L{n}"));                   // filled, not the drawn 921
        Assert.Equal(2950 * 14, At(chain, "L15"));                                                         // and chained on past the break
        Assert.Contains("not drawn", chain.Levels.Single(l => l.Name == "L8").From);
        Assert.Contains("drawn", chain.Levels.Single(l => l.Name == "L15").From);
        Assert.DoesNotContain("not drawn", chain.Levels.Single(l => l.Name == "L15").From);
    }

    [Fact]
    public void ALevelStatedTwiceKeepsItsFirstStatement()
    {
        var chain = SetStoreys.Levels(Table(("L2", "L1", 2800), ("L1", "P1", 4000), ("L2", "P1", 11460)));
        Assert.Equal(6800, At(chain, "L2"));
        Assert.Single(chain.NamedTwice);
    }

    [Fact]
    public void AStoreyAcrossTwoBuildingsIsNobodys()
    {
        // a strip's column on 31168's S3.11 read B-LEVEL 34 49 mm over A-LEVEL 34: two towers' labels
        // on one row; each tower chains on its own levels and the cross pair is left aside
        var chain = SetStoreys.Levels(Table(
            ("L26", "L25", 2950), ("A-L27", "L26", 2950), ("B-L27", "L26", 3255), ("A-L28", "A-L27", 2950), ("B-L28", "A-L27", 49), ("B-L28", "B-L27", 3556)));
        // L25 is the base at 0; L26 at 2950; A's and B's 27 and 28 each on their own tower's storeys
        Assert.Equal(2950 * 3, At(chain, "A-L28"));
        Assert.Equal(2950 + 3255 + 3556, At(chain, "B-L28"));
        Assert.Empty(chain.Unchained);
    }

    [Fact]
    public void ABreakWithNoTypicalStoreyLeavesTheLevelsAboveUnchainedAndSaysSo()
    {
        var chain = SetStoreys.Levels(Table(("L13", "L3", 921), ("L3", "L2", 3000), ("L2", "L1", 2800)));
        // the two drawn storeys disagree, 3000 and 2800: the mode is a tie broken low, still a typical
        Assert.NotNull(chain.TypicalMm);
        Assert.Single(chain.Breaks);
        // and a set with ONLY the break has nothing typical to fill it with
        var alone = SetStoreys.Levels(Table(("L13", "L3", 921)));
        Assert.Null(alone.TypicalMm);
        Assert.Contains(alone.Unchained, u => u.StartsWith("L13", StringComparison.Ordinal));
        Assert.Empty(alone.Breaks);
    }
}
