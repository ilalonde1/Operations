using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

public sealed class PlanningScoringTests
{
    private static readonly RuleBasedOpportunityScoringService Scorer = new(new Options(), new NoFacts());
    private static Opportunity Application(string description) => new()
    {
        Name = "Land use redesignation", BuyerName = "Calgary", ProjectCity = "Calgary",
        ScoringContext = new(OpportunityKind.Application, description),
    };

    [Theory]
    [InlineData("Applicant: HORIZON LAND SURVEYS. H-GO")]
    [InlineData("M-CG")]
    [InlineData("MU-2")]
    [InlineData("multi-residential")]
    [InlineData("Mixed uses with apartments and townhouses")]
    public void LatestDescription_NamedForm_OutscoresSingleFamilyCeiling(string description)
    {
        var result = Scorer.Explain(Application(description));
        Assert.True(result.Score > 26m);
        Assert.Equal(RelevanceTier.High, result.Tier);
        Assert.Single(result.Factors, f => f.Label.StartsWith("Planning form:"));
    }

    [Theory]
    [InlineData("Single family dwelling, two storeys, secondary suite and setback variance")]
    [InlineData("R-C1 R-C2 R-CG DC")]
    [InlineData("M-C10 M-C1s XM-C1 apartmental")]
    [InlineData("")]
    public void BroadAdmissionWords_AndPartialDistricts_DoNotLiftPermits(string description)
    {
        var row = Application(description);
        Assert.Equal(Scorer.Score(row with { ScoringContext = null }), Scorer.Score(row));
    }

    [Theory]
    [InlineData(OpportunityKind.Bid)]
    [InlineData(OpportunityKind.Award)]
    [InlineData(OpportunityKind.Other)]
    public void NonApplications_KeepLegacyScoreAndFactors(OpportunityKind kind)
    {
        var row = Application("H-GO apartment structural engineering China") with
        {
            Name = "Structural engineering mixed-use", ScoringContext = new(kind, "H-GO apartment structural engineering China")
        };
        var before = Scorer.Explain(row with { ScoringContext = null });
        var after = Scorer.Explain(row);
        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.Tier, after.Tier);
        Assert.Equal(before.Factors, after.Factors);
    }

    [Fact]
    public void Synonyms_DoNotStack_AndApplicantCountryIsNotProjectCountry()
    {
        var one = Scorer.Explain(Application("H-GO"));
        var many = Scorer.Explain(Application("Applicant: China Construction. H-GO MU-1 mixed use apartments townhouses"));
        Assert.Equal(one.Score, many.Score);
        Assert.False(many.HardRejected);
    }

    [Fact]
    public void ExistingHardRejectStillWins()
    {
        var row = Application("H-GO") with { ProjectCity = "Germany" };
        Assert.True(Scorer.Explain(row).HardRejected);
    }

    private sealed class Options : IScoringOptionsAccessor
    {
        public ScoringOptions GetCurrent() => ScoringOptions.KorDefaults();
        public ScoringOptions Update(ScoringOptions updated) => throw new NotSupportedException();
    }
    private sealed class NoFacts : IDeltekClientFactsAccessor
    {
        public DeltekClientFactsSnapshot? GetFacts(string? clientId) => null;
    }
}
