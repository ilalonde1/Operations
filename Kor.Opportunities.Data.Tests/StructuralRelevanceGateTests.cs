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

    [Theory]
    [InlineData("M-C1")]
    [InlineData("M-H1")]
    [InlineData("M-X2")]
    [InlineData("H-GO")]
    [InlineData("MU-1")]
    [InlineData("I-B")]
    [InlineData("I-C")]
    [InlineData("C-COR2")]
    [InlineData("CC-MHX")]
    [InlineData("CR20-C20/R20")]
    [InlineData("S-CI")]
    [InlineData("R-CG")]
    [InlineData("DC")]
    [InlineData("Direct Control District (DC)")]
    public void CalgaryDistrictInPlanningDescription_IsKept(string district)
    {
        Assert.True(StructuralRelevanceGate.Evaluate("111 27 AV NW",
            $"Land Use Amendment to accommodate {district}", "City of Calgary").Keep);
    }

    [Theory]
    [InlineData("R-C1")]
    [InlineData("R-C1s")]
    [InlineData("R-C2")]
    [InlineData("R-1s")]
    [InlineData("R-1N")]
    [InlineData("R-G")]
    [InlineData("R-Gm")]
    [InlineData("R-2M")]
    public void CalgaryLowDensityDistrict_AloneIsNotAKeepSignal(string district)
    {
        Assert.False(StructuralRelevanceGate.Evaluate("111 27 AV NW",
            $"Land Use Amendment to accommodate {district}", "City of Calgary").Keep);
    }

    [Theory]
    [InlineData("TA1882 - TPL - DC - Redwillow")]
    [InlineData("TA2951. TPL - DC, Kelly Lake")]
    [InlineData("PA(23)DCS-001 Site Operator for Harmon and Kane Lake Rec Site")]
    [InlineData("Supply of DC motors")]
    [InlineData("XM-C1 specification")]
    [InlineData("M-C10 specification")]
    [InlineData("MU-10 specification")]
    [InlineData("S-CIR specification")]
    public void CalgaryCodeLookalikes_AndBareDc_DoNotPass(string title)
    {
        Assert.False(StructuralRelevanceGate.Evaluate(title, null, null).Keep);
    }

    [Fact]
    public void CalgaryDistrict_DoesNotOverrideAlwaysIrrelevantPermit()
    {
        Assert.False(StructuralRelevanceGate.Evaluate("Plumbing permit",
            "Land Use Amendment to accommodate M-C1", null).Keep);
    }

    [Fact]
    public void CalgaryDistrict_IsCaseInsensitiveAndWordBoundedInTitle()
    {
        Assert.True(StructuralRelevanceGate.Evaluate("Amendment (m-c1).", null, null).Keep);
    }

    [Fact]
    public void CalgaryDistrictInBuyerOnly_DoesNotPass()
    {
        Assert.False(StructuralRelevanceGate.Evaluate("Policy Amendment", null, "M-C1").Keep);
    }
}
