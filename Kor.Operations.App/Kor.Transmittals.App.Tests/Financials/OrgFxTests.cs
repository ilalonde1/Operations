#nullable enable
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

public sealed class OrgFxTests
{
    [Fact]
    public void ParseUsdToCadRateTable_ParsesRatesAndProvisionalFlag()
    {
        var table = OrgFx.ParseUsdToCadRateTable("2024:~1.3698,2025:1.3985,2026:1.378457");

        Assert.Equal(3, table.Count);
        Assert.Equal(1.3698, table[2024].Rate, 4);
        Assert.True(table[2024].IsProvisional);
        Assert.Equal(1.3985, table[2025].Rate, 4);
        Assert.False(table[2025].IsProvisional);
        Assert.Equal(1.378457, table[2026].Rate, 6);
        Assert.False(table[2026].IsProvisional);
    }

    [Fact]
    public void ResolveUsdToCadRate_AbsentYearUsesFallbackAsProvisional()
    {
        var table = OrgFx.ParseUsdToCadRateTable("2025:1.3985");

        var resolved = OrgFx.ResolveUsdToCadRate(table, 2024, 1.36);

        Assert.Equal(1.36, resolved.Rate, 2);
        Assert.True(resolved.IsProvisional);
    }

    [Fact]
    public void ParseUsdToCadRateTable_SkipsMalformedZeroAndNegativeEntries()
    {
        var table = OrgFx.ParseUsdToCadRateTable("bad,2023:not-a-rate,2024:0,2025:-1.2,2026:1.378457");

        Assert.Single(table);
        Assert.True(table.ContainsKey(2026));
        Assert.Equal(1.378457, table[2026].Rate, 6);
    }
}