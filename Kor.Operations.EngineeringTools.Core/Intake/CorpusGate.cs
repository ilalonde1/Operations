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

    /// <summary>One set's verdict: what it was at the bank, what it is now, and whether that is a loss.</summary>
    public sealed record Verdict(
        string Job, double? BeforeSqFt, double? AfterSqFt, double? HersSqFt,
        int? ThicknessAgreeBefore, int? ThicknessAgreeAfter, int? ThicknessStoreys,
        int? OpeningsWeHaveBefore, int? OpeningsWeHaveAfter, int? OpeningsHers)
    {
        /// <summary>The change in her square feet we read, or null where the set was not judged both times.</summary>
        public double? MovedSqFt => BeforeSqFt is { } b && AfterSqFt is { } a ? a - b : null;

        /// <summary>How much of her plate area we read now, as a share (null where she plates nothing on the shared storeys).</summary>
        public double? ShareNow => HersSqFt is { } h && h > 0 && AfterSqFt is { } a ? a / h : null;

        /// <summary>A fall past the tolerance: the thing that stops a bank.</summary>
        public bool LostPlate => MovedSqFt is { } m && m < -Math.Max(PlateLossFloorSqFt, PlateLossShare * (HersSqFt ?? 0));

        /// <summary>Her thicknesses we agreed with and no longer do.</summary>
        public bool LostThickness => ThicknessAgreeBefore is { } b && ThicknessAgreeAfter is { } a && a < b;

        public bool Lost => LostPlate || LostThickness;
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
        var b = Judgeable(before);
        var a = Judgeable(after);
        var verdicts = new List<Verdict>();
        foreach (string job in b.Keys.Concat(a.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(j => j, StringComparer.Ordinal))
        {
            b.TryGetValue(job, out var was); a.TryGetValue(job, out var now);
            verdicts.Add(new Verdict(job,
                was?.PlatesOursSqFt, now?.PlatesOursSqFt, now?.PlatesHersSqFt ?? was?.PlatesHersSqFt,
                was?.ThicknessAgree, now?.ThicknessAgree, now?.ThicknessStoreys ?? was?.ThicknessStoreys,
                was?.OpeningsHersWeHave, now?.OpeningsHersWeHave, now?.OpeningsHers ?? was?.OpeningsHers));
        }
        // the losses first (worst square feet first), then everything else by the size of its move
        var ordered = verdicts.OrderByDescending(v => v.Lost).ThenBy(v => v.MovedSqFt ?? 0).ToList();
        var both = ordered.Where(v => v.BeforeSqFt is not null && v.AfterSqFt is not null).ToList();
        return new Report(ordered,
            both.Sum(v => v.BeforeSqFt ?? 0), both.Sum(v => v.AfterSqFt ?? 0), both.Sum(v => v.HersSqFt ?? 0),
            both.Sum(v => v.ThicknessAgreeBefore ?? 0), both.Sum(v => v.ThicknessAgreeAfter ?? 0), both.Sum(v => v.ThicknessStoreys ?? 0),
            both.Sum(v => v.OpeningsWeHaveBefore ?? 0), both.Sum(v => v.OpeningsWeHaveAfter ?? 0), both.Sum(v => v.OpeningsHers ?? 0));
    }

    /// <summary>The jobs of a ledger that carry a yardstick figure: the newest row per job (a ledger may hold several runs).</summary>
    private static Dictionary<string, CorpusAnalyzer.SetRow> Judgeable(IReadOnlyList<CorpusAnalyzer.SetRow> rows)
        => rows.Where(r => r.PlatesOursSqFt is not null || r.PlatesHersSqFt is not null)
               .GroupBy(r => r.Job, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RunAtUtc).First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The jobs worth running: every set of a ledger that has the engineer's model, newest row per job.</summary>
    public static IReadOnlyList<string> JobsWithAYardstick(IReadOnlyList<CorpusAnalyzer.SetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Yardstick))
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
        sb.AppendLine("  job         her plates we read: was -> now (of hers)        thickness agree   her openings we have");
        foreach (var v in r.Verdicts)
        {
            string was = v.BeforeSqFt is { } b ? b.ToString("N0", CultureInfo.InvariantCulture) : "-";
            string now = v.AfterSqFt is { } a ? a.ToString("N0", CultureInfo.InvariantCulture) : "-";
            string share = v.ShareNow is { } s ? $"{100 * s:F0}%" : "-";
            string moved = v.MovedSqFt is { } m ? (m >= 0 ? "+" : "") + m.ToString("N0", CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {(v.Lost ? "LOST " : "     ")}{v.Job,-13}{was,12} -> {now,12} ({share,4}) {moved,10}   {v.ThicknessAgreeBefore,3} -> {v.ThicknessAgreeAfter,3} of {v.ThicknessStoreys,3}   {v.OpeningsWeHaveBefore,4} -> {v.OpeningsWeHaveAfter,4} of {v.OpeningsHers,4}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"  totals: her plates we read {r.BeforeSqFt:N0} -> {r.AfterSqFt:N0} sq ft of {r.HersSqFt:N0} " +
            $"({(r.HersSqFt > 0 ? 100 * r.BeforeSqFt / r.HersSqFt : 0):F0}% -> {(r.HersSqFt > 0 ? 100 * r.AfterSqFt / r.HersSqFt : 0):F0}%); " +
            $"thickness {r.ThicknessAgreeBefore} -> {r.ThicknessAgreeAfter} of {r.ThicknessStoreys}; her openings we have {r.OpeningsWeHaveBefore} -> {r.OpeningsWeHaveAfter} of {r.OpeningsHers}");
        sb.AppendLine(r.Losses.Count == 0
            ? "  no set lost: the bank may take this."
            : $"  {r.Losses.Count} set(s) LOST against the bank - look at each before banking: {string.Join(", ", r.Losses.Select(v => v.Job))}");
        return sb.ToString();
    }
}
