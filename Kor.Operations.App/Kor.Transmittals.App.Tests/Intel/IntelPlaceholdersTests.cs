#nullable enable
using Kor.Opportunities.Data.Intel;
using Xunit;

namespace Kor.Operations.App.Tests.Intel;

public sealed class IntelPlaceholdersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("None.")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("NA")]
    [InlineData("Unknown")]
    [InlineData("TBD")]
    [InlineData("Not applicable")]
    [InlineData("-")]
    [InlineData("—")]
    public void IsPlaceholder_CatchesDrainModelFillerText(string? value)
    {
        Assert.True(IntelPlaceholders.IsPlaceholder(value));
        Assert.Null(IntelPlaceholders.NullIfPlaceholder(value));
    }

    [Theory]
    [InlineData("Call Karen Marler before the Q3 RFP window.")]
    [InlineData("Nanaimo outreach")] // starts with 'na' but is real content
    [InlineData("None of the incumbents hold the Okanagan market — KOR opening.")] // 'None' as a sentence opener is real prose
    public void IsPlaceholder_KeepsRealContent(string value)
    {
        Assert.False(IntelPlaceholders.IsPlaceholder(value));
        Assert.Equal(value, IntelPlaceholders.NullIfPlaceholder(value));
    }
}
