#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SlabThicknessCalloutTests
{
    [Fact]
    public void Shared_grammar_reads_number_first_slab_first_and_metric_forms()
    {
        var numberFirst = SlabThicknessCallout.MatchAnyOrderText("14\" SLAB");
        Assert.NotNull(numberFirst);
        Assert.False(numberFirst!.Value.IsMetric);
        Assert.Equal(14, numberFirst.Value.ValueIn);

        var slabFirst = SlabThicknessCallout.MatchAnyOrderText("SLAB 14\"");
        Assert.NotNull(slabFirst);
        Assert.False(slabFirst!.Value.IsMetric);
        Assert.Equal(14, slabFirst.Value.ValueIn);

        var metric = SlabThicknessCallout.MatchAnyOrderText("350 SLAB");
        Assert.NotNull(metric);
        Assert.True(metric!.Value.IsMetric);
        Assert.Equal(350, metric.Value.Value);
        Assert.Equal(14, metric.Value.ValueIn);
    }

    [Theory]
    [InlineData("4\" UNREINFORCED SLAB ON GRADE")]
    [InlineData("12\" PC3 x 8. FOR ALL SLAB ON GRADE STEPS")]
    [InlineData("5. SLABS TO BE CAMBERED")]
    public void Shared_grammar_rejects_known_non_callout_shapes(string text)
    {
        Assert.Null(SlabThicknessCallout.MatchAnyOrderText(text));
    }
}
