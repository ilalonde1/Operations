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
    [InlineData("Consulting Services")]
    [InlineData("Deposit Reconciliation Services")]
    [InlineData("ASL Interpretation Services")]
    [InlineData("Supply of Type III Ambulances")]
    [InlineData("Solar PV Installations")]
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
    [InlineData("Fleet Service Centre - Dawson Creek")]
    [InlineData("Prime Consultant Services (Architect)")]
    public void Evaluate_WhenBuildingOrAmbiguous_ReturnsKeep(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.True(decision.Keep);
        Assert.Null(decision.RejectReason);
    }

    [Theory]
    [InlineData("Red Chris Mine Expansion")]
    [InlineData("Cedar LNG")]
    [InlineData("Kitimat Clean Oil Refinery")]
    [InlineData("Prince Rupert Gas Transmission Project")]
    [InlineData("Baptiste Nickel Project")]
    [InlineData("Iona Island Wastewater Treatment Plant Upgrades")]
    [InlineData("Tilbury Phase 2 LNG expansion project")]
    public void Evaluate_WhenResourceEnergyOrHeavyIndustrial_ReturnsReject(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.False(decision.Keep);
        Assert.StartsWith("out-of-lane: ", decision.RejectReason);
    }

    [Theory]
    [InlineData("Langley Memorial Hospital New South Patient Tower")]
    [InlineData("Inglewood Care Centre")]
    [InlineData("Surrey Memorial New Acute Care Tower")]
    [InlineData("Anglemont Fire Hall Rebuild")]
    public void Evaluate_WhenBuildingTitleContainsAlwaysIrrelevantSubstring_ReturnsKeep(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.True(decision.Keep);
        Assert.Null(decision.RejectReason);
    }

    [Fact]
    public void Evaluate_WhenAuditServices_ReturnsRejectForAuditToken()
    {
        var decision = StructuralRelevanceGate.Evaluate("Financial Audit Services", null, null);

        Assert.False(decision.Keep);
        Assert.Equal("audit services", decision.RejectReason);
    }

    // Reject-table review 2026-07-01: each case below was a live false
    // negative captured in opportunities.RelevanceGateRejects.
    [Theory]
    [InlineData("Coal Harbour Phase 2 - Construction Manager Pre-Qualification")]
    [InlineData("Roof Replacement Sooke Municipal Hall")]
    [InlineData("ROTATIONAL ENGINEERING CONSULTING SERVICES ROSTER")]
    [InlineData("Contractor for Orpheum Service elevator modernization")]
    [InlineData("Contractor for Academy of Music: Patio Re-roof")]
    [InlineData("Design,Fabrication,Supply,Delivery of Pre-Engineered Firewall")]
    public void Evaluate_WhenPreviouslyFalseNegative_ReturnsKeep(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.True(decision.Keep);
        Assert.Null(decision.RejectReason);
    }

    [Theory]
    [InlineData("Agrandissement de l'école secondaire - services d'architecture")]
    [InlineData("Rénovation du bâtiment administratif")]
    [InlineData("Services de génie-conseil en structure")]
    [InlineData("Construction d'un nouvel hôpital régional")]
    public void Evaluate_WhenFrenchBuildingPosting_ReturnsKeep(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.True(decision.Keep);
        Assert.Null(decision.RejectReason);
    }

    [Theory]
    [InlineData("Quintette Coal Mine Restart")]
    [InlineData("Roberts Bank Coal Terminal Expansion")]
    [InlineData("Coal Handling Facility Upgrades")]
    public void Evaluate_WhenIndustrialCoal_StillReturnsReject(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.False(decision.Keep);
        Assert.StartsWith("out-of-lane: ", decision.RejectReason);
    }

    [Theory]
    [InlineData("Janitorial Services - Police Facilities")]
    [InlineData("Pre-Qualification for Snow & Ice Control Services")]
    [InlineData("Elevator Maintenance Services")]
    public void Evaluate_WhenServiceNoise_StillReturnsReject(string title)
    {
        var decision = StructuralRelevanceGate.Evaluate(title, null, null);

        Assert.False(decision.Keep);
        Assert.NotNull(decision.RejectReason);
    }
}
