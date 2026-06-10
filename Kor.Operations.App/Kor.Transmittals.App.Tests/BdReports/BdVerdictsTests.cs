#nullable enable
using Kor.Opportunities.Data.BdReports;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class BdVerdictsTests
{
    [Fact]
    public void Rank_OrdersVerdictsForReports()
    {
        Assert.True(BdVerdicts.Rank(BdVerdicts.PursueUrgent) < BdVerdicts.Rank(BdVerdicts.Pursue));
        Assert.True(BdVerdicts.Rank(BdVerdicts.Pursue) < BdVerdicts.Rank(BdVerdicts.Monitor));
        Assert.True(BdVerdicts.Rank(BdVerdicts.Monitor) < BdVerdicts.Rank(BdVerdicts.Discover));
        Assert.True(BdVerdicts.Rank(BdVerdicts.Discover) < BdVerdicts.Rank(BdVerdicts.Dead));
        Assert.True(BdVerdicts.Rank(BdVerdicts.Dead) < BdVerdicts.Rank(BdVerdicts.Duplicate));
        Assert.True(BdVerdicts.Rank(BdVerdicts.Duplicate) < BdVerdicts.Rank(null));
        Assert.Equal(BdVerdicts.Rank(null), BdVerdicts.Rank("SOMETHING_ELSE"));
    }

    [Theory]
    [InlineData("PURSUE_URGENT", null, true)]
    [InlineData("PURSUE_URGENT", "no urgency in text", true)]
    [InlineData("PURSUE", "URGENT — teams forming now", true)]
    [InlineData("PURSUE", "shortlist is urgent, engage now", true)] // case-insensitive, parity with PS -match
    [InlineData("PURSUE", "open structural seat", false)]
    [InlineData("PURSUE", null, false)]
    [InlineData("MONITOR", "URGENT future phase", false)]
    [InlineData("DEAD", "URGENT", false)]
    [InlineData(null, "URGENT", false)]
    public void IsUrgent_ParityWithPowerShellBuilders(string? verdict, string? korAngle, bool expected)
    {
        Assert.Equal(expected, BdVerdicts.IsUrgent(verdict, korAngle));
    }
}
