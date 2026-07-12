using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins DisciplineClassifier behaviour. The adversarial review of the initial
// version found the OutOfScope branch mis-firing on substring collisions
// ("retrofit services" ⊃ "it services") and parent commodity codes flipping
// Structural→Mixed. The classifier now owns only structural relevance
// (Structural/Mixed/Inspections/Unknown) and never emits OutOfScope — hard
// relevance is StructuralRelevanceGate's job. These tests lock that in.
public sealed class DisciplineClassifierTests
{
    [Theory]
    [InlineData("Structural Engineering Services - New Fire Hall")]
    [InlineData("Seismic retrofit of City Hall")]              // bigram, and NOT OutOfScope via "it services"
    [InlineData("Seismic upgrade of elementary school")]
    [InlineData("Structural design for pedestrian bridge")]
    public void StructuralPhrases_ClassifyStructural(string title)
        => Assert.Equal(OpportunityDiscipline.Structural,
            DisciplineClassifier.Classify(null, title, null));

    [Fact]
    public void StructuralCommodityCode_ClassifiesStructural()
        => Assert.Equal(OpportunityDiscipline.Structural,
            DisciplineClassifier.Classify(new[] { "81101505 Structural engineering" }, "Engineering services", null));

    [Fact]
    public void ParentClassCode_DoesNotFlipStructuralToMixed()
        => Assert.Equal(OpportunityDiscipline.Structural,
            // 81101500 is the parent family code that co-lists with 81101505 on
            // pure-structural notices; it must not be read as "another discipline".
            DisciplineClassifier.Classify(new[] { "81101505", "81101500" }, "Structural engineering - pure scope", null));

    [Fact]
    public void RihMultiDiscipline_ClassifiesMixed()
        => Assert.Equal(OpportunityDiscipline.Mixed,
            DisciplineClassifier.Classify(
                new[] { "81101505 Structural engineering", "81101508 Architectural engineering", "81101600 Mechanical", "81101701 Electrical" },
                "RFP RIH ERCP Service Delivery - Architectural and Engineering Services",
                null));

    [Fact]
    public void StructuralPlusArchitectural_ClassifiesMixed()
        => Assert.Equal(OpportunityDiscipline.Mixed,
            DisciplineClassifier.Classify(new[] { "81101505", "81101508" }, "Structural and architectural engineering", null));

    [Theory]
    [InlineData("Building envelope condition assessment")]
    [InlineData("Structural inspection of parkade")]
    [InlineData("Restoration engineering - heritage facade")]
    public void InspectionWork_ClassifiesInspections(string title)
        => Assert.Equal(OpportunityDiscipline.Inspections,
            DisciplineClassifier.Classify(null, title, null));

    [Theory]
    [InlineData("General contractor prequalification")]
    [InlineData("Office building renovation")]                 // no structural signal
    [InlineData("")]                                           // empty blob
    public void NoSignal_ClassifiesUnknown(string title)
        => Assert.Equal(OpportunityDiscipline.Unknown,
            DisciplineClassifier.Classify(null, title, null));

    [Fact]
    public void NullCandidate_ClassifiesUnknown()
        => Assert.Equal(OpportunityDiscipline.Unknown, DisciplineClassifier.Classify((OpportunityCandidate)null!));

    // Regression guards for the substring false-positives that the old OutOfScope
    // branch produced. The classifier must NEVER return OutOfScope now.
    [Theory]
    [InlineData(null, "Seismic retrofit services - City Hall")]            // "retrofit services" ⊃ "it services"
    [InlineData(null, "Building permit services / expediting")]            // "permit services" ⊃ "it services"
    [InlineData(null, "Structural analysis and BIM software for tower")]   // ⊃ "software"
    [InlineData(null, "New hospital - medical equipment planning")]        // ⊃ "medical equipment"
    [InlineData(null, "Janitorial services for city facilities")]         // gate's job, not the classifier's
    [InlineData(null, "Landscaping and snow removal")]
    public void NeverEmitsOutOfScope(string? desc, string title)
        => Assert.NotEqual(OpportunityDiscipline.OutOfScope,
            DisciplineClassifier.Classify(null, title, desc));
}
