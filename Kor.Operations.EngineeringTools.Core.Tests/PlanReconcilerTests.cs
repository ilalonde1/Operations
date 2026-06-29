#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlanReconcilerTests
{
    private static readonly PlanProfile Bc = PlanProfile.BcModerate;

    [Fact]
    public void NormalSuspendedSlab_IsHighConfidence_FieldSlabScopeOnly()
    {
        // A clean field measure stays High — but it carries the standing scope flag that the number is the
        // FIELD slab only, not a certified floor total. Info severity, so it does not lower confidence.
        var check = PlanReconciler.Check(TakeoffElementType.Slab, areaSqFt: 9597, thicknessIn: 8, Bc);
        Assert.Equal(TakeoffConfidence.High, check.Confidence);
        var only = Assert.Single(check.Flags);
        Assert.Equal("FIELD_SLAB_ONLY", only.Code);
        Assert.Equal(PlanFlagSeverity.Info, only.Severity);
    }

    [Fact]
    public void FieldSlabOnly_RidesEverySlab_ButNeverFoundation()
    {
        // The scope boundary belongs to plan-read suspended slabs (single-callout depth), not to mats —
        // a Foundation mat is priced as its own thing and does not carry the suspended-slab caveat.
        Assert.Contains(PlanReconciler.Check(TakeoffElementType.Slab, 9597, 8, Bc).Flags,
            f => f.Code == "FIELD_SLAB_ONLY");
        Assert.DoesNotContain(PlanReconciler.Check(TakeoffElementType.Foundation, 38_500, 130, Bc).Flags,
            f => f.Code == "FIELD_SLAB_ONLY");
    }

    [Fact]
    public void TransferThickSlab_IsFlaggedCritical_TheL01Lesson()
    {
        // L01: 13,913 sq.ft measured, QTO 2,059 cu.yd => ~48" => transfer slab, not suspended.
        var check = PlanReconciler.Check(TakeoffElementType.Slab, areaSqFt: 13_913, thicknessIn: 48, Bc);
        Assert.Equal(TakeoffConfidence.Review, check.Confidence);
        Assert.True(check.HasCritical);
        Assert.Contains(check.Flags, f => f.Code == "SLAB_TOO_THICK");
    }

    [Fact]
    public void FoundationMatMayBeVeryThick_NoSlabThicknessFlag()
    {
        // A 3300mm (130") mat is legitimate for a Foundation — the thick-slab rule must not fire.
        var check = PlanReconciler.Check(TakeoffElementType.Foundation, areaSqFt: 38_500, thicknessIn: 130, Bc);
        Assert.DoesNotContain(check.Flags, f => f.Code == "SLAB_TOO_THICK");
    }

    [Fact]
    public void NonPositiveArea_IsCritical()
    {
        var check = PlanReconciler.Check(TakeoffElementType.Slab, areaSqFt: 0, thicknessIn: 8, Bc);
        Assert.True(check.HasCritical);
        Assert.Contains(check.Flags, f => f.Code == "AREA_NONPOSITIVE");
    }

    [Fact]
    public void NonPositiveThickness_IsCritical()
    {
        var check = PlanReconciler.Check(TakeoffElementType.Slab, areaSqFt: 9597, thicknessIn: 0, Bc);
        Assert.True(check.HasCritical);
        Assert.Contains(check.Flags, f => f.Code == "THK_UNRESOLVED");
    }

    [Fact]
    public void UnconfirmedScale_LowersToReview()
    {
        var check = PlanReconciler.Check(TakeoffElementType.Slab, 9597, 8, Bc, scaleConfirmed: false);
        Assert.Equal(TakeoffConfidence.Review, check.Confidence);
        Assert.Contains(check.Flags, f => f.Code == "SCALE_UNCONFIRMED");
    }

    [Fact]
    public void RebarOverrideFarFromNorm_IsReviewed()
    {
        // BC column norm 385; a 900 lb/cy entry (SD-like) on a BC job should be questioned.
        var check = PlanReconciler.Check(TakeoffElementType.Column, 50, 24, Bc, rebarLbPerCyOverride: 900);
        Assert.Contains(check.Flags, f => f.Code == "RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void RebarOverrideNearNorm_IsAccepted()
    {
        var check = PlanReconciler.Check(TakeoffElementType.Column, 50, 24, Bc, rebarLbPerCyOverride: 400);
        Assert.DoesNotContain(check.Flags, f => f.Code == "RATIO_OUT_OF_BAND");
    }

    [Theory]
    [InlineData(TakeoffElementType.Slab, 199)]
    [InlineData(TakeoffElementType.Wall, 375)]
    [InlineData(TakeoffElementType.Column, 385)]
    [InlineData(TakeoffElementType.Foundation, 150)]
    public void Profile_ReturnsRegionalNorms(TakeoffElementType element, double expected)
    {
        Assert.Equal(expected, Bc.RebarLbPerCy(element));
    }

    [Fact]
    public void Profile_Beam_PricedAtSlabRatio_MatchesDensityTable()
    {
        // The plausibility band must use the same ratio the report prices the beam at (slab).
        Assert.Equal(Bc.SlabRebarLbPerCy, Bc.RebarLbPerCy(TakeoffElementType.Beam));
    }

    [Fact]
    public void Profile_ByName_PicksSanDiego()
    {
        Assert.Equal("SD-high-seismic", PlanProfile.ByName("sd-high-seismic").Name);
        Assert.Equal("BC-moderate", PlanProfile.ByName("anything-else").Name);
    }

    [Theory]
    [InlineData("L1 North", true)]
    [InlineData("L01", true)]
    [InlineData("Transfer", true)]
    [InlineData("P1 Parkade", true)]
    [InlineData("L1 Mezz South", true)]
    [InlineData("Podium", true)]
    [InlineData("L17-28", false)]   // must NOT trip the L1 rule
    [InlineData("L16", false)]
    [InlineData("L9-12", false)]
    [InlineData("Roof", false)]
    public void IsTransferProneLevel_MatchesLowAndTransferLevelsOnly(string label, bool expected)
    {
        Assert.Equal(expected, PlanReconciler.IsTransferProneLevel(label));
    }

    [Fact]
    public void TransferProneLevel_ThinSlab_IsReviewed_TheOtherL01Lesson()
    {
        // L1 reads a 12" P/T slab — under the 24" SLAB_TOO_THICK wire — but it's a transfer level,
        // so the built-up thick zones must be confirmed. This is the under-read the audit surfaced.
        var check = PlanReconciler.Check(TakeoffElementType.Slab, 9800, 12, Bc, levelLabel: "L1 North");
        Assert.Equal(TakeoffConfidence.Review, check.Confidence);
        Assert.Contains(check.Flags, f => f.Code == "TRANSFER_LEVEL_THK");
    }

    [Fact]
    public void TypicalTowerSlab_NotTransferLevel_NoTransferFlag()
    {
        var check = PlanReconciler.Check(TakeoffElementType.Slab, 9597, 8, Bc, levelLabel: "L17-28");
        Assert.DoesNotContain(check.Flags, f => f.Code == "TRANSFER_LEVEL_THK");
        Assert.Equal(TakeoffConfidence.High, check.Confidence);
    }

    [Fact]
    public void ScaleAgreement_AgreeingScales_NoFlag()
    {
        Assert.Null(PlanReconciler.ScaleAgreement(0.022167, 0.022400));
    }

    [Fact]
    public void ScaleAgreement_DisagreeingScales_Critical()
    {
        var flag = PlanReconciler.ScaleAgreement(0.022167, 0.044334); // 2x => half-size print
        Assert.NotNull(flag);
        Assert.Equal("SCALE_DISAGREE", flag!.Code);
    }
}
