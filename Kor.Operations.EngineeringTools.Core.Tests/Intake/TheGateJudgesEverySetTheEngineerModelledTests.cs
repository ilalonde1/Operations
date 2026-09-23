#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE SETS THE ENGINEER HAS A MODEL OF, JUDGED AGAINST THE LAST BANK (2026-09-22). On 2026-09-19 step 131 was
/// banked on a green six-set gate; the next full corpus run showed 31087 at 47% of her plates where run 41 had
/// 89% - forty-one storeys, found a day later by reading a diff. <see cref="CorpusGate"/> is the judgement that
/// sits between a rule and its bank: the fifty-odd sets she has modelled, on her plate area, her thicknesses and
/// her openings.
///
/// WHAT THIS COVERS: a set whose plate area falls past the tolerance is LOST and named (31087's shape, in its own
/// numbers); a fall under the tolerance is drift and is not; a fall in thickness agreement alone is a loss; a set
/// only one ledger judged is reported and left out of the totals; the totals are the sets judged both times; a
/// ledger banked before the figures existed (41 columns) reads as nulls and is judged on nothing.
/// WHAT IT DOES NOT: the walk (CorpusAnalyzer's own tests), whether a GAIN is right (the render and the six-set
/// gate), the 203 sets she has no model of. A same-class fault it would not catch: a rule that swaps her plate
/// area for someone else's on the same storey, area for area.
/// </summary>
public sealed class TheGateJudgesEverySetTheEngineerModelledTests
{
    private static CorpusAnalyzer.SetRow Row(string job, double? oursSqFt, double hersSqFt = 100_000,
        int? thicknessAgree = 8, int? thicknessStoreys = 10, int? openingsWeHave = 20, int? openingsHers = 30, string? yardstick = "hers.e2k")
        => new(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, job, "03 Residential", "structural", job + ".pdf",
            "2026-01-01", false, 1, 1, 1, 1, 0, 0, 0, 1, true, null, 1, 1, 0, 0, 0, 0, 1.0, null,
            Yardstick: yardstick,
            PlatesOursSqFt: oursSqFt, PlatesHersSqFt: oursSqFt is null ? null : hersSqFt, PlatesUnderHalf: 0, PlatesBeyondSqFt: 0,
            ThicknessStoreys: thicknessStoreys, ThicknessAgree: thicknessAgree, OpeningsHers: openingsHers, OpeningsHersWeHave: openingsWeHave);

    [Fact]
    public void ASetThatLosesHerPlateAreaIsNamedAndTheBankIsStopped()
    {
        // 31087's own shape: 89% of her 817,386 sq ft at the bank, 47% after - the loss the six-set gate could not see
        var before = new[] { Row("31087-01", 727_473, 817_386), Row("31138-01", 224_136, 309_507) };
        var after = new[] { Row("31087-01", 380_770, 817_386), Row("31138-01", 281_400, 309_507) };

        var report = CorpusGate.Judge(before, after);

        var lost = Assert.Single(report.Losses);
        Assert.Equal("31087-01", lost.Job);
        Assert.Equal(-346_703, lost.MovedSqFt!.Value, 0);
        Assert.Equal(0.47, lost.ShareNow!.Value, 2);
        Assert.Contains("31087-01", CorpusGate.Summary(report), StringComparison.Ordinal);
        Assert.Contains("LOST", CorpusGate.Summary(report), StringComparison.Ordinal);
        // the gain is reported and is not a loss; the totals are both sets
        Assert.Equal(951_609, report.BeforeSqFt, 0);
        Assert.Equal(662_170, report.AfterSqFt, 0);
        Assert.Equal(2, report.Judged.Count);
    }

    [Fact]
    public void AMoveInsideTheToleranceIsDriftAndASetJudgedOnceIsNeverInTheTotals()
    {
        // a corner moved: 150 sq ft of her 100,000 - under both the floor (200) and the share (0.5% = 500)
        var drift = CorpusGate.Judge([Row("31130-01", 250_000)], [Row("31130-01", 249_850)]);
        Assert.Empty(drift.Losses);
        Assert.Contains("no set lost", CorpusGate.Summary(drift), StringComparison.Ordinal);

        // her 700 sq ft on a set of 100,000 is past the share and is a loss
        var real = CorpusGate.Judge([Row("31130-01", 250_000)], [Row("31130-01", 249_200)]);
        Assert.Single(real.Losses);

        // a set the new ledger never judged (she has no model, or the run skipped it) is reported, never a loss, never a total
        var gone = CorpusGate.Judge([Row("31065-01", 246_673), Row("31130-01", 250_000)], [Row("31130-01", 250_000)]);
        Assert.Empty(gone.Losses);
        Assert.Equal(250_000, gone.BeforeSqFt, 0);
        Assert.Equal(250_000, gone.AfterSqFt, 0);
        Assert.Equal(2, gone.Verdicts.Count);
        Assert.Single(gone.Judged);
    }

    [Fact]
    public void ThicknessAgreementFallingIsALossOnItsOwnAndAnOldLedgerIsJudgedOnNothing()
    {
        var thinner = CorpusGate.Judge([Row("31202-01", 338_123, thicknessAgree: 10)], [Row("31202-01", 338_123, thicknessAgree: 8)]);
        var lost = Assert.Single(thinner.Losses);
        Assert.False(lost.LostPlate);
        Assert.True(lost.LostThickness);

        // a ledger banked before the steering columns existed: no figures, nothing judged, no false loss
        var old = CorpusGate.Judge([Row("31202-01", null, yardstick: "hers.e2k")], [Row("31202-01", 338_123)]);
        Assert.Empty(old.Losses);
        Assert.Empty(old.Judged);
        // and the jobs worth running are still known from the yardstick column alone
        Assert.Equal(["31202-01"], CorpusGate.JobsWithAYardstick([Row("31202-01", null)]));
    }
}
