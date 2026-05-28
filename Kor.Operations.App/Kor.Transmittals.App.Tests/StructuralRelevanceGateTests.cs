#nullable enable
using Kor.Opportunities.Core.Ingestion;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class StructuralRelevanceGateTests
{
    [Theory]
    [InlineData("Road Paving Program")]
    [InlineData("IT Software Support Services")]
    [InlineData("Supply of Office Furniture")]
    [InlineData("Janitorial Services")]
    public void Evaluate_WhenClearlyNonBuilding_ReturnsReject(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.False(decision.Keep);
        Assert.NotNull(decision.RejectReason);
    }

    [Theory]
    [InlineData("New Elementary School Construction")]
    [InlineData("Pedestrian Bridge Replacement")]
    [InlineData("Hospital Seismic Upgrade")]
    [InlineData("Supply and Install Structural Steel - Community Centre")]
    [InlineData("Consulting Services")]
    public void Evaluate_WhenBuildingOrAmbiguous_ReturnsKeep(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.True(decision.Keep);
        Assert.Null(decision.RejectReason);
    }
}
