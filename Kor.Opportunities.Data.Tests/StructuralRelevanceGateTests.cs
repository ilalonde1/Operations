using Kor.Opportunities.Core.Ingestion;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Audit-v2 #14: the keep-lists matched raw substrings ('infrastructure' ⊃
// 'structure', 'additional' ⊃ 'addition') while reject-lists were word-bounded,
// letting out-of-lane work pass the gate. These tests pin the word-bounded
// behaviour AND the morphological-suffix coverage that keeps verb stems working.
public sealed class StructuralRelevanceGateTests
{
    [Theory]
    [InlineData("Sanitary Sewer Infrastructure Upgrades")]          // 'infrastructure' must not satisfy 'structure'
    [InlineData("Additional Janitorial Services 2026")]             // 'additional' must not satisfy 'addition'
    public void SubstringLookalikes_DoNotPassTheGate(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, description: null, buyer: null);
        Assert.False(decision.Keep, $"'{title}' should be rejected; reason={decision.RejectReason}");
    }

    [Theory]
    [InlineData("Seismic retrofit of existing building")]
    [InlineData("Constructing a new community arena")]              // verb-stem suffix coverage
    [InlineData("Residential towers development")]                  // plural coverage
    [InlineData("School addition and renovation")]                  // the real 'addition' still passes
    public void GenuineBuildingWork_StillPasses(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, description: null, buyer: null);
        Assert.True(decision.Keep, $"'{title}' should pass; reason={decision.RejectReason}");
    }
}
