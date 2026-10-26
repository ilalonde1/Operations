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
        int? overHalfAgain = 0, double? medianMm = 20, int? onlyOurs = null, double? onlyOursSqFt = null)
        => new(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, job, "03 Residential", "structural", job + ".pdf",
            "2026-01-01", false, 1, 1, 1, 1, 0, 0, 0, 1, true, null, 1, 1, 0, 0, 0, 0, 1.0, null,
            Yardstick: yardstick, OursMedianMm: oursSqFt is null ? null : medianMm,
            PlatesOursSqFt: oursSqFt, PlatesHersSqFt: oursSqFt is null ? null : hersSqFt, PlatesUnderHalf: 0,
            PlatesOverHalfAgain: oursSqFt is null ? null : overHalfAgain, PlatesBeyondSqFt: 0,
            ThicknessStoreys: oursSqFt is null ? null : thicknessStoreys, ThicknessAgree: oursSqFt is null ? null : thicknessAgree,
            OpeningsHers: oursSqFt is null ? null : openingsHers, OpeningsHersWeHave: oursSqFt is null ? null : openingsWeHave,
            StoreysPlatedOnlyOurs: onlyOurs, PlatesOnlyOursSqFt: onlyOursSqFt);

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

    /// <summary>
    /// A FRAME THAT DOES NOT REGISTER IS NOT JUDGED BY POSITION, AND SAYS SO (2026-10-26). 30986-02 stopped the
    /// 21fb2b10 gate - her plate area fell 3,728 sq ft, her thicknesses we agree with 2 -> 1 - and the group bisect
    /// named step 179, which, looked at, had read P1 as a whole floor: better, judged through a frame whose columns
    /// stand a median 3.1 m then 3.3 m off hers. These are its own figures from the two ledgers.
    ///
    /// COVERS: plate, thickness, openings and over-read all refused where either run's frame is out; the set named in
    /// the Summary and kept out of the totals; a frame that registered at the bank and does not now stops the bank;
    /// the line itself sits at the gap. DOES NOT: whether 1,000 mm is the right line for sets not yet in the corpus.
    /// </summary>
    [Fact]
    public void ASetWhoseFrameDoesNotRegisterIsNotJudgedByPositionAndIsNamed()
    {
        var before = new[] { Row("30986-02", 19_710, 57_527, thicknessAgree: 2, thicknessStoreys: 2, openingsWeHave: 7, medianMm: 3_057), Row("31138-01", 224_136, 309_507) };
        var after = new[] { Row("30986-02", 15_982, 57_527, thicknessAgree: 1, thicknessStoreys: 2, openingsWeHave: 11, overHalfAgain: 1, medianMm: 3_317), Row("31138-01", 224_136, 309_507) };

        var report = CorpusGate.Judge(before, after);

        Assert.Empty(report.Losses);
        var v = Assert.Single(report.Verdicts, x => x.Job == "30986-02");
        Assert.True(v.NotJudgedByPosition);
        Assert.False(v.LostPlate);
        Assert.False(v.LostThickness);
        Assert.False(v.OverRead);
        Assert.Equal(224_136, report.BeforeSqFt, 0);                           // the totals are 31138's alone
        string summary = CorpusGate.Summary(report);
        Assert.Contains("1 of the 2 set(s) judged both times are NOT JUDGED on plate, thickness, openings or over-read", summary, StringComparison.Ordinal);
        Assert.Contains("30986-02: median 3,057 -> 3,317 mm", summary, StringComparison.Ordinal);

        // the same set with a frame that registered at the bank and does not now: a loss, not a refusal
        var broke = CorpusGate.Judge(
            [Row("30986-02", 19_710, 57_527, medianMm: 150)],
            [Row("30986-02", 19_710, 57_527, medianMm: 3_317)]);
        var lost = Assert.Single(broke.Losses);
        Assert.True(lost.LostFrame);
        Assert.Contains("the frame stopped registering", lost.Why, StringComparison.Ordinal);

        // the line sits in the corpus's gap: 781 mm (31093, the worst that registers) judged; 1,364 (31048) not
        Assert.False(CorpusGate.Judge([Row("31093-01", 10_000, medianMm: 781)], [Row("31093-01", 9_000, medianMm: 781)]).Verdicts[0].NotJudgedByPosition);
        Assert.True(CorpusGate.Judge([Row("31048-01", 10_000, medianMm: 1_364)], [Row("31048-01", 9_000, medianMm: 1_364)]).Verdicts[0].NotJudgedByPosition);
    }

    /// <summary>
    /// THE STOREYS ONLY WE PLATE ARE NAMED, AND MORE OF THEM STOPS THE BANK (item 5, 2026-10-26). 31162 plated its wood
    /// storeys L3 to L5 in concrete - the sheet's legend box, 7,068 sq ft each - and no figure judged them, because every
    /// figure is over the storeys both models name. Its own figures; the frame is out on purpose, to show a storey's name
    /// is judged where its position is not.
    ///
    /// COVERS: a rise against the bank stops it, even through a frame that does not register; the AFTER is named set by
    /// set whatever the bank holds; a bank that predates the figure judges no rise and says so. DOES NOT: whether a storey
    /// she does not model SHOULD be plated (a tower she left out is ours to read).
    /// </summary>
    [Fact]
    public void TheStoreysOnlyWePlateAreNamedAndMoreOfThemStopsTheBank()
    {
        var rose = CorpusGate.Judge(
            [Row("31162-01", 46_045, 39_961, onlyOurs: 0, onlyOursSqFt: 0, medianMm: 1_200)],
            [Row("31162-01", 46_045, 39_961, onlyOurs: 3, onlyOursSqFt: 21_204, medianMm: 1_200)]);
        var lost = Assert.Single(rose.Losses);
        Assert.True(lost.NotJudgedByPosition);
        Assert.True(lost.MoreOnlyOurs);
        Assert.Contains("storeys we plate that her model does not name 0 -> 3", lost.Why, StringComparison.Ordinal);
        Assert.Contains("31162-01: 3 storey(s), 21,204 sq ft (was 0)", CorpusGate.Summary(rose), StringComparison.Ordinal);

        // a bank from before the figure: named, not judged
        var old = CorpusGate.Judge([Row("31162-01", 46_045, 39_961)], [Row("31162-01", 46_045, 39_961, onlyOurs: 3, onlyOursSqFt: 21_204)]);
        Assert.Empty(old.Losses);
        string summary = CorpusGate.Summary(old);
        Assert.Contains("1 of 1 set(s) measured now, 3 storey(s), 21,204 sq ft - the bank predates this figure", summary, StringComparison.Ordinal);

        // and fewer is not a loss: step 184 takes them away
        Assert.Empty(CorpusGate.Judge([Row("31162-01", 46_045, 39_961, onlyOurs: 3, onlyOursSqFt: 21_204)],
                                      [Row("31162-01", 46_045, 39_961, onlyOurs: 0, onlyOursSqFt: 0)]).Losses);
    }

    /// <summary>
    /// THE SAME STICK FILE READ FROM A MIRROR IS JUDGED, NOT EXCUSED (2026-10-23). The gate's first run over the VPN
    /// read every set from a local mirror of the share, and because the bank's paths begin with \\Kor-fs01 and the
    /// mirror's with a Temp folder, all 52 judged sets were classed RE-ISSUED and nothing stopped the bank - a loss of
    /// 240,000 sq ft would have been waved through. Identity is the file's name and size (CorpusDiff.SameStickFile).
    /// </summary>
    [Fact]
    public void ASetReadFromAMirrorOfTheSameStickFileIsJudged()
    {
        var before = new[] { Row("31039-01", 300_000, 400_000) with { Pdf = @"\\Kor-fs01\Projects\Projects\03 Residential\31039-01 (x)\05 Stickfile\31039-01 2026-09-14 Stickfile.pdf" } };
        var after = new[] { Row("31039-01", 60_000, 400_000) with { Pdf = @"C:\Users\x\AppData\Local\Temp\kor-corpus-mirror\Projects\03 Residential\31039-01 (x)\05 Stickfile\31039-01 2026-09-14 Stickfile.pdf" } };

        var report = CorpusGate.Judge(before, after);

        var v = Assert.Single(report.Verdicts);
        Assert.False(v.ReIssued, "the same name at the same size is the same drawings, wherever they were read from");
        Assert.True(v.Lost, "it fell 240,000 sq ft from the same file: a loss the rule must answer for");
        Assert.Single(report.Losses);
    }
}
