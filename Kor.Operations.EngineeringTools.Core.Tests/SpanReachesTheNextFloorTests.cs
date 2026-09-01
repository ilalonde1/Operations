using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A member reaches the next floor it stands on and stops there.
/// </summary>
/// <remarks>
/// Span says how far ONE member reaches; the assigns say which storeys it stands on. Carrying past
/// a storey the member is itself assigned to is a column running through its own floor — what the
/// engineer photographed and called an overlap.
///
/// This exists because a span reset that forced every span to 1 hung 290 of building C's columns
/// off the towers' floor, and NOTHING saw it. The wrong member is a full storey tall, so
/// NoColumnIsShorterThanAPerson reads a perfectly ordinary column; every count is unchanged. Only
/// the storey list can tell.
/// </remarks>
public class SpanReachesTheNextFloorTests
{
    private static string[] Model(int span, params string[] standsOn)
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"C-ROOF\"  HEIGHT 120",
            "  STORY \"LEVEL 10\"  HEIGHT 120",
            "  STORY \"C-LEVEL 9\"  HEIGHT 120",
            "  STORY \"C-LEVEL 8\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0  0",
            "$ LINE CONNECTIVITIES",
            $"  LINE  \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  {span}",
            "$ LINE ASSIGNS",
        };
        lines.AddRange(standsOn.Select(s => $"  LINEASSIGN  \"KC1\"  \"{s}\"  SECTION \"KOR-C24x24\""));
        return lines.ToArray();
    }

    /// <summary>
    /// The 31168 shape: an unprefixed tower level sits between two of building C's storeys, so a C
    /// column must step over it. Span 2 here is correct and must not be refused.
    /// </summary>
    [Fact]
    public void SteppingOverAStoreyTheMemberDoesNotStandOnIsAllowed()
    {
        var v = ShippedModelInvariants.Check(Model(2, "C-ROOF", "C-LEVEL 9"));

        Assert.DoesNotContain(v, x => x.Rule == "member-spans-through-its-own-floor");
    }

    /// <summary>
    /// The same column with a span that carries it past C-LEVEL 9, which it stands on, and down to
    /// C-LEVEL 8. That is a member through its own floor.
    /// </summary>
    [Fact]
    public void CarryingPastAFloorItStandsOnIsRefused()
    {
        var v = ShippedModelInvariants.Check(Model(3, "C-ROOF", "C-LEVEL 9", "C-LEVEL 8"));

        var caught = Assert.Single(v, x => x.Rule == "member-spans-through-its-own-floor"
                                           && x.What.Contains("C-ROOF", StringComparison.Ordinal));
        Assert.Contains("C-LEVEL 9", caught.What, StringComparison.Ordinal);
        Assert.True(caught.BlocksPublishing, "a member through its own floor must not be publishable");
    }

    /// <summary>Her own convention: one label, span 1, an assign on each floor.</summary>
    [Fact]
    public void OneLabelWithSpanOneOnEveryFloorIsClean()
    {
        var v = ShippedModelInvariants.Check(Model(1, "C-LEVEL 9", "C-LEVEL 8"));

        Assert.DoesNotContain(v, x => x.Rule == "member-spans-through-its-own-floor");
    }
}
