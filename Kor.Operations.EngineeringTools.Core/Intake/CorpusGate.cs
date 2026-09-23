#nullable enable
using System.Globalization;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// THE SETS THE ENGINEER HAS A MODEL OF, JUDGED AGAINST THE LAST BANK (2026-09-22).
///
/// The six-set gate proves a rule changes nothing it was not meant to change; it cannot say whether the rule is
/// GOOD, because six sets are not the corpus. On 2026-09-19 step 131 was banked on a green six-set gate and the
/// next full run - read a day later, by eye - showed 31087 at 47% of her plates where run 41 had 89%: forty-one
/// storeys, lost to a class the six do not draw. A full run is 3 h 17 m and cannot sit between a rule and its
/// bank; the fifty-odd sets she has modelled can, and they are the only sets in the corpus that can be judged
/// RIGHT rather than merely unchanged.
///
/// This is the judgement, not the walk: <c>CorpusAnalyzer</c> reads and composes those jobs (it already takes
/// <c>--jobs</c>), and this compares the ledger it writes against a banked one, set by set, on the figures the
/// work is steered by - her plate area, her slab thicknesses, her openings.
///
/// WHAT IT COVERS: a set whose plate area falls (the loss that must stop a bank), a set whose thickness
/// agreement falls, and the totals both ways. WHAT IT DOES NOT: sets she has no model of (203 of 293 - their
/// only judge is the ledger's own counts and the render); whether a GAIN is real (a plate that grew over a
/// stair well is a gain here and a fault on the page - the render and the six-set gate answer that); the
/// openings she flags as slivers. A same-class fault it would not catch: a rule that trades her plate area for
/// someone else's on the same storey, area for area.
/// </summary>
public static class CorpusGate
{
    /// <summary>
    /// How far a set's plate area may fall and still be drift rather than a loss: the larger of 200 sq ft and half a
    /// percent of hers. A rule that moves a corner moves a few square feet; the losses worth stopping for are storeys.
    /// </summary>
    public const double PlateLossFloorSqFt = 200;

    /// <summary>The share of her plate area a set may lose before the gate calls it a loss (0.005 = half a percent).</summary>
    public const double PlateLossShare = 0.005;

    /// <summary>
    /// How far past her own plate area on a storey a set may go before the gate calls it an over-read: half again.
    /// A rule that only ADDS area (step 136 removed a refusal) cannot lose, and a gate that only watches losses
    /// would pass it however much rubbish it added - a courtyard void read as a floor is a gain here (Codex's
    /// audit of 2026-09-22, finding 4, which named the U-shaped void that passes every other gate).
    /// </summary>
    public const double OverReadShare = 1.5;

    /// <summary>One set's verdict: what it was at the bank, what it is now, and whether that is a loss.</summary>
    public sealed record Verdict(
        string Job, double? BeforeSqFt, double? AfterSqFt, double? HersSqFt,
        int? ThicknessAgreeBefore, int? ThicknessAgreeAfter, int? ThicknessStoreys,
        int? OpeningsWeHaveBefore, int? OpeningsWeHaveAfter, int? OpeningsHers,
        // WHAT THE BANK MUST NOT BE ABLE TO MISS (Codex's audit, 2026-09-22): a set that was judged at the bank and
        // is not judged now - it failed to build, its yardstick would not load, its row is gone - is not "unchanged",
        // it is EVIDENCE MISSING, and evidence missing can conceal any loss at all. It stops the bank like a loss.
        bool WasJudged = false, bool IsJudged = false,
        double? BeforeHersSqFt = null, int? OverHalfAgainBefore = null, int? OverHalfAgainAfter = null)
    {
        /// <summary>The change in her square feet we read, or null where the set was not judged both times.</summary>
        public double? MovedSqFt => BeforeSqFt is { } b && AfterSqFt is { } a && !double.IsNaN(b) && !double.IsNaN(a) ? a - b : null;

        /// <summary>How much of her plate area we read now, as a share (null where she plates nothing on the shared storeys).</summary>
        public double? ShareNow => HersSqFt is { } h && h > 0 && AfterSqFt is { } a ? a / h : null;

        /// <summary>
        /// The tolerance, in her square feet, taken from what SHE had AT THE BANK. Reading it from the newer run lets a
        /// yardstick that changed between runs inflate it - 10,000/10,000 becoming 9,000/1,000,000 bought a 5,000 sq ft
        /// tolerance and concealed a 1,000 sq ft loss (Codex's audit, finding 1). A NaN or missing figure buys nothing.
        /// </summary>
        public double Tolerance
        {
            get
            {
                double hers = BeforeHersSqFt ?? HersSqFt ?? 0;
                if (double.IsNaN(hers) || double.IsInfinity(hers)) hers = 0;
                return Math.Max(PlateLossFloorSqFt, PlateLossShare * hers);
            }
        }

        /// <summary>A fall past the tolerance: the thing that stops a bank.</summary>
        public bool LostPlate => MovedSqFt is { } m && m < -Tolerance;

        /// <summary>Her thicknesses we agreed with and no longer do - a figure that goes missing counts as gone.</summary>
        public bool LostThickness => ThicknessAgreeBefore is { } b && (ThicknessAgreeAfter ?? 0) < b;

        /// <summary>Her openings we had and no longer have (the gate computed them and judged nothing by them until now).</summary>
        public bool LostOpenings => OpeningsWeHaveBefore is { } b && (OpeningsWeHaveAfter ?? 0) < b;

        /// <summary>A figure we cannot compare because the set stopped being judged: it hides everything else.</summary>
        public bool EvidenceMissing => WasJudged && !IsJudged;

        /// <summary>More storeys carrying half again her area than before: a rule adding what she does not have.</summary>
        public bool OverRead => (OverHalfAgainAfter ?? 0) > (OverHalfAgainBefore ?? 0);

        /// <summary>The set read a DIFFERENT stick file in the two runs, so no rule is answerable for what moved.</summary>
        public bool ReIssued { get; init; }

        /// <summary>What would have stopped the bank had the drawings not changed under it.</summary>
        public bool WouldHaveLost => LostPlate || LostThickness || LostOpenings || EvidenceMissing || OverRead;

        public bool Lost => WouldHaveLost && !ReIssued;

        /// <summary>Why this set stops the bank, in the engineer's own terms.</summary>
        public string Why => !WouldHaveLost ? "" : (ReIssued ? "RE-ISSUED, so no rule is answerable: " : "") + string.Join(", ", new[]
        {
            EvidenceMissing ? "judged at the bank, not judged now" : null,
            LostPlate ? $"her plate area fell {-(MovedSqFt ?? 0):N0} sq ft (tolerance {Tolerance:N0})" : null,
            LostThickness ? $"her thicknesses we agree with fell {ThicknessAgreeBefore} -> {ThicknessAgreeAfter?.ToString(CultureInfo.InvariantCulture) ?? "none"}" : null,
            LostOpenings ? $"her openings we have fell {OpeningsWeHaveBefore} -> {OpeningsWeHaveAfter?.ToString(CultureInfo.InvariantCulture) ?? "none"}" : null,
            OverRead ? $"storeys over half again her area {OverHalfAgainBefore ?? 0} -> {OverHalfAgainAfter ?? 0}" : null,
        }.Where(s => s is not null));
    }

    /// <summary>Every set judged, the losses first, then the largest gains; with the totals across the sets judged both times.</summary>
    public sealed record Report(IReadOnlyList<Verdict> Verdicts, double BeforeSqFt, double AfterSqFt, double HersSqFt,
        int ThicknessAgreeBefore, int ThicknessAgreeAfter, int ThicknessStoreys, int OpeningsWeHaveBefore, int OpeningsWeHaveAfter, int OpeningsHers)
    {
        public IReadOnlyList<Verdict> Losses => Verdicts.Where(v => v.Lost).ToList();

        /// <summary>The sets judged both times, by name - a set missing from either ledger is reported, never silently dropped.</summary>
        public IReadOnlyList<Verdict> Judged => Verdicts.Where(v => v.BeforeSqFt is not null && v.AfterSqFt is not null).ToList();
    }

    /// <summary>
    /// The verdict for every job either ledger judged against her model. A job in one ledger and not the other is kept
    /// with a null on the missing side (it shows in the table as new or gone), and never counts toward the totals.
    /// </summary>
    public static Report Judge(IReadOnlyList<CorpusAnalyzer.SetRow> before, IReadOnlyList<CorpusAnalyzer.SetRow> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var b = Newest(before);
        var a = Newest(after);
        var verdicts = new List<Verdict>();
        foreach (string job in b.Keys.Concat(a.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(j => j, StringComparer.Ordinal))
        {
            b.TryGetValue(job, out var was); a.TryGetValue(job, out var now);
            bool wasJudged = Figured(was), isJudged = Figured(now);
            verdicts.Add(new Verdict(job,
                wasJudged ? was!.PlatesOursSqFt : null, isJudged ? now!.PlatesOursSqFt : null,
                (isJudged ? now!.PlatesHersSqFt : null) ?? (wasJudged ? was!.PlatesHersSqFt : null),
                wasJudged ? was!.ThicknessAgree : null, isJudged ? now!.ThicknessAgree : null,
                (isJudged ? now!.ThicknessStoreys : null) ?? (wasJudged ? was!.ThicknessStoreys : null),
                wasJudged ? was!.OpeningsHersWeHave : null, isJudged ? now!.OpeningsHersWeHave : null,
                (isJudged ? now!.OpeningsHers : null) ?? (wasJudged ? was!.OpeningsHers : null),
                wasJudged, isJudged,
                wasJudged ? was!.PlatesHersSqFt : null,
                wasJudged ? was!.PlatesOverHalfAgain : null, isJudged ? now!.PlatesOverHalfAgain : null)
            {
                // A SET THAT READ A DIFFERENT STICK FILE IS NOT A LOSS (2026-09-23). The gate judges a RULE, and a
                // re-issued drawing set moves for reasons no rule can be blamed for: 31039-01 fell from 35 plates
                // to 7 between runs 43 and 44 and had simply been re-drawn that week, 77 pages becoming 68. Eleven
                // of 292 sets changed file in that fortnight, so this is not rare enough to leave to judgement. It
                // is REPORTED, loudly, and it does not stop the bank - because the alternative is a gate that goes
                // red for the drafting office's work and gets ignored.
                ReIssued = was is not null && now is not null
                    && (!string.Equals(was.Pdf, now.Pdf, StringComparison.OrdinalIgnoreCase) || was.Bytes != now.Bytes),
            });
        }
        // the losses first and worst-first; then everything else by the SIZE of its move, either way (a gain of 1,000
        // before a gain of 100 - ascending put them the other way round, Codex's audit 2026-09-22)
        var ordered = verdicts.OrderByDescending(v => v.Lost).ThenBy(v => v.Lost ? v.MovedSqFt ?? double.MinValue : -Math.Abs(v.MovedSqFt ?? 0)).ToList();
        var both = ordered.Where(v => v.BeforeSqFt is not null && v.AfterSqFt is not null).ToList();
        return new Report(ordered,
            both.Sum(v => v.BeforeSqFt ?? 0), both.Sum(v => v.AfterSqFt ?? 0), both.Sum(v => v.HersSqFt ?? 0),
            both.Sum(v => v.ThicknessAgreeBefore ?? 0), both.Sum(v => v.ThicknessAgreeAfter ?? 0), both.Sum(v => v.ThicknessStoreys ?? 0),
            both.Sum(v => v.OpeningsWeHaveBefore ?? 0), both.Sum(v => v.OpeningsWeHaveAfter ?? 0), both.Sum(v => v.OpeningsHers ?? 0));
    }

    /// <summary>
    /// THE NEWEST ROW PER JOB, WHETHER OR NOT IT CARRIES FIGURES (Codex's audit, 2026-09-22, finding 1). Filtering to
    /// rows that HAVE the figures and then taking the newest lets an older successful row stand in for a newer failed
    /// one - the set reads as unchanged while the run that matters measured nothing at all.
    /// </summary>
    private static Dictionary<string, CorpusAnalyzer.SetRow> Newest(IReadOnlyList<CorpusAnalyzer.SetRow> rows)
        => rows.GroupBy(r => r.Job, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RunAtUtc).First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a row was actually measured against her model: a figure, and a figure that is a number.</summary>
    private static bool Figured(CorpusAnalyzer.SetRow? r)
        => r is not null
           && (r.PlatesOursSqFt is not null || r.PlatesHersSqFt is not null)
           && !double.IsNaN(r.PlatesOursSqFt ?? 0) && !double.IsNaN(r.PlatesHersSqFt ?? 0)
           && !double.IsInfinity(r.PlatesOursSqFt ?? 0) && !double.IsInfinity(r.PlatesHersSqFt ?? 0);

    /// <summary>The jobs worth running: every set of a ledger that has the engineer's model, newest row per job.</summary>
    public static IReadOnlyList<string> JobsWithAYardstick(IReadOnlyList<CorpusAnalyzer.SetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        // the NEWEST row per job decides, as everywhere else here: an older row naming a yardstick does not keep a job
        // in the gate after a later run found none (Codex's audit, 2026-09-22)
        return Newest(rows).Values.Where(r => !string.IsNullOrWhiteSpace(r.Yardstick))
                   .Select(r => r.Job).Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(j => j, StringComparer.Ordinal).ToList();
    }

    /// <summary>The report as the engineer's own units, losses first; the last line is the verdict a bank reads.</summary>
    public static string Summary(Report r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"the gate: {r.Judged.Count} set(s) the engineer has a model of, judged against the bank");

        // ⚠ A BASELINE WITH NO FIGURES AT ALL IS NOT A JUDGEMENT, AND MUST NOT READ LIKE ONE (2026-09-23).
        //
        // The first real run of this gate reported "0 set(s) judged" and then SIX SETS LOST for over-reading.
        // Both came from the same cause: run 44's ledger was written by a mirror published before
        // plates_over_half_again existed, so it carried 49 columns, the parser refused the last ten figures, and
        // every "before" was null - against which any "after" above zero is an over-read. The gate shouted, which
        // is better than a green, but it shouted a fault that was not there and hid the one that was.
        //
        // So the condition is NAMED, first, and the verdicts that follow are declared void. A baseline nobody can
        // read is a broken instrument, not evidence about a rule.
        if (r.Judged.Count == 0 && r.Verdicts.Any(v => v.IsJudged))
        {
            sb.AppendLine("  ⚠ THE BASELINE CARRIES NO FIGURES AND THIS JUDGEMENT IS VOID. Every 'before' is blank while");
            sb.AppendLine("    the new ledger has them, so nothing below compares anything - a LOST line here means only that");
            sb.AppendLine("    a figure was read now and none was read at the bank. The usual cause is a ledger banked by an");
            sb.AppendLine("    older build: her figures are the last ten columns and a short row is refused whole. Re-bank the");
            sb.AppendLine("    baseline with this build, or judge two arms of ONE build against each other (KOR_STEP<n>_OFF).");
        }
        sb.AppendLine("  job         her plates we read: was -> now (of hers)        thickness agree   her openings we have");
        foreach (var v in r.Verdicts)
        {
            string was = v.BeforeSqFt is { } b ? b.ToString("N0", CultureInfo.InvariantCulture) : "-";
            string now = v.AfterSqFt is { } a ? a.ToString("N0", CultureInfo.InvariantCulture) : "-";
            string share = v.ShareNow is { } s ? $"{100 * s:F0}%" : "-";
            string moved = v.MovedSqFt is { } m ? (m >= 0 ? "+" : "") + m.ToString("N0", CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {(v.Lost ? "LOST " : v.ReIssued ? "REISS" : "     ")}{v.Job,-13}{was,12} -> {now,12} ({share,4}) {moved,10}   {v.ThicknessAgreeBefore,3} -> {v.ThicknessAgreeAfter,3} of {v.ThicknessStoreys,3}   {v.OpeningsWeHaveBefore,4} -> {v.OpeningsWeHaveAfter,4} of {v.OpeningsHers,4}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"  totals: her plates we read {r.BeforeSqFt:N0} -> {r.AfterSqFt:N0} sq ft of {r.HersSqFt:N0} " +
            $"({(r.HersSqFt > 0 ? 100 * r.BeforeSqFt / r.HersSqFt : 0):F0}% -> {(r.HersSqFt > 0 ? 100 * r.AfterSqFt / r.HersSqFt : 0):F0}%); " +
            $"thickness {r.ThicknessAgreeBefore} -> {r.ThicknessAgreeAfter} of {r.ThicknessStoreys}; her openings we have {r.OpeningsWeHaveBefore} -> {r.OpeningsWeHaveAfter} of {r.OpeningsHers}");
        // Said whether or not anything lost: a set whose drawings changed under the gate is not evidence either way,
        // and a reader who does not know that will read its movement as the rule's doing.
        var reissued = r.Verdicts.Where(v => v.ReIssued).ToList();
        if (reissued.Count > 0)
        {
            int wouldHave = reissued.Count(v => v.WouldHaveLost);
            string head = $"  {reissued.Count} set(s) READ A DIFFERENT STICK FILE than the bank did, so no rule is answerable for them"
                + (wouldHave > 0 ? $" ({wouldHave} of them would otherwise have stopped the bank)" : string.Empty) + ":";
            sb.AppendLine(head);
            foreach (var v in reissued) sb.AppendLine("    " + v.Job + (v.Why.Length > 0 ? ": " + v.Why : string.Empty));
        }
        if (r.Losses.Count == 0)
        {
            sb.AppendLine("  no set lost: the bank may take this.");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {r.Losses.Count} set(s) STOP THE BANK - each with its reason:");
            foreach (var v in r.Losses) sb.AppendLine(CultureInfo.InvariantCulture, $"    {v.Job}: {v.Why}");
        }
        return sb.ToString();
    }
}
