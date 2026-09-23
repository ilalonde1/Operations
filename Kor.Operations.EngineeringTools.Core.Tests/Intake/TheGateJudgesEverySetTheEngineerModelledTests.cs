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
/// numbers); a fall under the tolerance is drift and is not; a fall in thickness agreement, or in her openings we
/// have, alone; EVIDENCE MISSING (the set gone from the new ledger, its yardstick failed, an older row standing in
/// for a newer failed one, a NaN, a tolerance inflated by her own figure changing - every one of them silent until
/// Codex's audit of 2026-09-22); an OVER-READ, because a rule that only adds area cannot lose; the totals are the
/// sets judged both times; a ledger banked before the figures existed (41 columns) reads as nulls, judged on nothing.
/// WHAT IT DOES NOT: the walk (CorpusAnalyzer's own tests), whether a GAIN is right (the render and the six-set
/// gate), the 203 sets she has no model of. A same-class fault it would not catch: a rule that swaps her plate
/// area for someone else's on the same storey, area for area.
/// </summary>
public sealed class TheGateJudgesEverySetTheEngineerModelledTests
{
    private static CorpusAnalyzer.SetRow Row(string job, double? oursSqFt, double hersSqFt = 100_000,
        int? thicknessAgree = 8, int? thicknessStoreys = 10, int? openingsWeHave = 20, int? openingsHers = 30, string? yardstick = "hers.e2k",
        int? overHalfAgain = 0)
        => new(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, job, "03 Residential", "structural", job + ".pdf",
            "2026-01-01", false, 1, 1, 1, 1, 0, 0, 0, 1, true, null, 1, 1, 0, 0, 0, 0, 1.0, null,
            Yardstick: yardstick,
            PlatesOursSqFt: oursSqFt, PlatesHersSqFt: oursSqFt is null ? null : hersSqFt, PlatesUnderHalf: 0,
            PlatesOverHalfAgain: oursSqFt is null ? null : overHalfAgain, PlatesBeyondSqFt: 0,
            ThicknessStoreys: oursSqFt is null ? null : thicknessStoreys, ThicknessAgree: oursSqFt is null ? null : thicknessAgree,
            OpeningsHers: oursSqFt is null ? null : openingsHers, OpeningsHersWeHave: oursSqFt is null ? null : openingsWeHave);

    /// <summary>
    /// A SET WHOSE DRAWINGS CHANGED UNDER THE GATE DOES NOT STOP THE BANK, AND IS SAID OUT LOUD (2026-09-23).
    ///
    /// The gate judges a RULE. A re-issued stick file moves for reasons no rule is answerable for: 31039-01 fell
    /// from 35 plates to 7 between runs 43 and 44 and had simply been re-drawn that week, 77 pages becoming 68.
    /// Eleven of the 292 sets changed file in that fortnight, so this is too common to leave to judgement — and a
    /// gate that goes red for the drafting office's week is a gate that gets ignored.
    /// </summary>
    [Fact]
    public void ASetThatReadADifferentStickFileIsReportedAndDoesNotStopTheBank()
    {
        var before = new[] { Row("31039-01", 300_000, 400_000), Row("31138-01", 224_136, 309_507) };
        var after = new[]
        {
            Row("31039-01", 60_000, 400_000) with { Pdf = "31039-01 2026-09-22.pdf" },
            Row("31138-01", 281_400, 309_507),
        };

        var report = CorpusGate.Judge(before, after);

        var reissued = Assert.Single(report.Verdicts, v => v.ReIssued);
        Assert.Equal("31039-01", reissued.Job);
        Assert.True(reissued.WouldHaveLost, "it fell 240,000 sq ft; the point is not that it was fine");
        Assert.False(reissued.Lost);
        Assert.Empty(report.Losses);
        string summary = CorpusGate.Summary(report);
        Assert.Contains("READ A DIFFERENT STICK FILE", summary, StringComparison.Ordinal);
        Assert.Contains("would otherwise have stopped the bank", summary, StringComparison.Ordinal);
        Assert.Contains("RE-ISSUED, so no rule is answerable", reissued.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// A BASELINE THAT CARRIES NO FIGURES IS A BROKEN INSTRUMENT, NOT A VERDICT (2026-09-23).
    ///
    /// The first real run of this gate reported "0 set(s) judged" and then six sets LOST for over-reading. Both
    /// came from one cause: run 44's ledger was written by a mirror published before plates_over_half_again
    /// existed, so it carried 49 columns, the parser refused her figures whole, and every "before" was null —
    /// against which any "after" above zero is an over-read. It shouted, which beats a green, but it shouted a
    /// fault that was not there and hid the one that was.
    /// </summary>
    [Fact]
    public void ABaselineWithNoFiguresAtAllIsCalledVoidRatherThanReadAsOverReading()
    {
        var before = new[] { Row("31087-01", null), Row("31138-01", null) };
        var after = new[] { Row("31087-01", 380_770, 817_386, overHalfAgain: 2), Row("31138-01", 281_400, 309_507) };

        var report = CorpusGate.Judge(before, after);

        Assert.Empty(report.Judged);
        string summary = CorpusGate.Summary(report);
        Assert.Contains("THE BASELINE CARRIES NO FIGURES AND THIS JUDGEMENT IS VOID", summary, StringComparison.Ordinal);
        Assert.Contains("a short row is refused whole", summary, StringComparison.Ordinal);
    }

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

        // a set judged at the bank and NOT judged now is evidence missing: out of the totals, and it stops the bank.
        // (This read "never a loss" when it was written, which is the silence Codex's audit found the same night.)
        var gone = CorpusGate.Judge([Row("31065-01", 246_673), Row("31130-01", 250_000)], [Row("31130-01", 250_000)]);
        Assert.Equal("31065-01", Assert.Single(gone.Losses).Job);
        Assert.Equal(250_000, gone.BeforeSqFt, 0);
        Assert.Equal(250_000, gone.AfterSqFt, 0);
        Assert.Equal(2, gone.Verdicts.Count);
        Assert.Single(gone.Judged);

        // a set NEW in this run (never judged at the bank) is not a loss: there is nothing it could have lost
        Assert.Empty(CorpusGate.Judge([Row("31130-01", 250_000)], [Row("31130-01", 250_000), Row("31065-01", 246_673)]).Losses);
    }

    /// <summary>
    /// EVIDENCE MISSING IS A LOSS (Codex's audit of the gate, 2026-09-22, finding 1). Every one of these read GREEN
    /// before: a set that vanished from the new run, a set whose yardstick failed to load so its figures are null, an
    /// older successful row standing in for a newer failed one, a NaN that made the comparison false, and a yardstick
    /// that changed between runs and bought a tolerance big enough to hide a real fall. A gate that goes quiet when
    /// the evidence goes missing is worse than no gate, because it is believed.
    /// </summary>
    [Fact]
    public void AGateIsNeverSilentWhenTheEvidenceGoesMissing()
    {
        var bank = new[] { Row("31087-01", 727_473, 817_386), Row("31138-01", 224_136, 309_507) };

        // (a) the set is not in the new ledger at all - it failed to build, or the run skipped it
        var gone = CorpusGate.Judge(bank, [Row("31138-01", 224_136, 309_507)]);
        var lostGone = Assert.Single(gone.Losses);
        Assert.Equal("31087-01", lostGone.Job);
        Assert.Contains("not judged now", lostGone.Why, StringComparison.Ordinal);

        // (b) the set is there and its yardstick would not load: figures null
        var failed = CorpusGate.Judge(bank, [Row("31087-01", null), Row("31138-01", 224_136, 309_507)]);
        Assert.Equal("31087-01", Assert.Single(failed.Losses).Job);

        // (c) an OLDER successful row and a NEWER failed one in the same ledger: the newest decides
        var older = Row("31087-01", 727_473, 817_386) with { RunAtUtc = DateTime.UtcNow.AddHours(-2) };
        var newerFailed = Row("31087-01", null) with { RunAtUtc = DateTime.UtcNow };
        Assert.Equal("31087-01", Assert.Single(CorpusGate.Judge(bank, [older, newerFailed, Row("31138-01", 224_136, 309_507)]).Losses).Job);

        // (d) NaN: half the area gone and the comparison quietly false
        Assert.Single(CorpusGate.Judge([Row("31202-01", 10_000, 10_000)], [Row("31202-01", 5_000, double.NaN)]).Losses);

        // (e) her figure changing between runs must not buy a bigger tolerance: 1,000 sq ft lost is still a loss
        var inflated = CorpusGate.Judge([Row("31202-01", 10_000, 10_000)], [Row("31202-01", 9_000, 1_000_000)]);
        var lostInflated = Assert.Single(inflated.Losses);
        Assert.Equal(200, lostInflated.Tolerance, 0);                          // hers AT THE BANK, not hers now
    }

    /// <summary>
    /// A RULE THAT ONLY ADDS AREA CANNOT LOSE, SO THE GATE WATCHES WHAT IT ADDS (2026-09-22). Step 136 removes a
    /// refusal: every set can only gain, and a gate that watched losses alone would pass it however much rubbish it
    /// added - Codex's U-shaped courtyard void, read as a floor, is a GAIN here. More storeys carrying half again her
    /// own area than before is the shape of that fault, and it stops the bank.
    /// </summary>
    [Fact]
    public void ASetThatStartsReadingHalfAgainHerAreaOnAStoreyStopsTheBank()
    {
        var before = new[] { Row("31130-01", 250_000, 277_000, overHalfAgain: 0) };
        var after = new[] { Row("31130-01", 262_000, 277_000, overHalfAgain: 2) };

        var lost = Assert.Single(CorpusGate.Judge(before, after).Losses);
        Assert.True(lost.OverRead);
        Assert.False(lost.LostPlate);                                          // it GAINED 12,000 sq ft
        Assert.Contains("over half again her area 0 -> 2", lost.Why, StringComparison.Ordinal);

        // a gain that lands where she has area too is not an over-read
        Assert.Empty(CorpusGate.Judge(before, [Row("31130-01", 262_000, 277_000, overHalfAgain: 0)]).Losses);
    }

    /// <summary>Her openings falling was computed and judged by nothing until Codex's audit named it.</summary>
    [Fact]
    public void HerOpeningsFallingStopsTheBankToo()
    {
        var lost = Assert.Single(CorpusGate.Judge(
            [Row("31065-01", 246_673, 291_705, openingsWeHave: 62)],
            [Row("31065-01", 246_673, 291_705, openingsWeHave: 40)]).Losses);
        Assert.True(lost.LostOpenings);
        Assert.Contains("her openings we have fell 62 -> 40", lost.Why, StringComparison.Ordinal);
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
