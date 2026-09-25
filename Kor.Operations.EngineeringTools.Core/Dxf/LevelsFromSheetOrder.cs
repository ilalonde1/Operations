using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// The level a plan sheet draws, taken from WHERE IT SITS IN THE SHEET SEQUENCE, for a sheet whose
/// own title would not say (intake step 144, 2026-09-24).
///
/// THE TOOL GUESSES RATHER THAN SKIPS. Ian, 2026-09-24: "rather than skipping an unknown completely
/// (like a slab) or not running the thing at all if there's a question (there will ALWAYS be
/// questions) - can AI please make a best guess, then LOOK at the result as a sanity check, then
/// proceed - whilst showing clearly in the question workbook that this was an assumption."
///
/// Step 143 found the sets this is for: 20 of 190 leave half their own ladder with no floor, and the
/// worst of them, 31005-01, reads 10.6% of the engineer's plate area. Its title block writes the
/// title UP the page in three columns and the reader assembles it across, so five sheets come out
/// called "OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION" with the level lost. 143 asks her which
/// level each one is. This answers what it can before asking.
///
/// ⚠ THE STEM CARRIES THE STOREY, NOT THE SHEET. Read from 31005-01's own ledger rather than
/// assumed: its sheets come in pairs — S2.05.1 and S2.05.2 — which are two areas of ONE drawing, and
/// only the first one's title parsed. Ordering sheet by sheet therefore offers three candidates
/// (S2.05.2, S2.06.1, S2.06.2) for the single unclaimed level between L4 and L6, and determines
/// nothing. Grouping by the stem S2.05 / S2.06 / S2.07 gives one gap and one unclaimed level, and
/// L5 follows by arithmetic.
///
/// The set as its ledger has it, which is what this was designed against:
///
///     S2.01  P1        anchor
///     S2.02  -         gap
///     S2.03  -         gap        three gaps, and the levels between P1 and L3 are not a
///     S2.04  -         gap        sequence anyone can count — NOT determined, left to her
///     S2.05  L3, L4    anchor
///     S2.06  -         gap        one gap, one unclaimed level  -> L5
///     S2.07  L6, L7    anchor
///     S2.08  -         gap        one gap, one unclaimed level  -> L8
///     S2.09  L9 - L16  anchor
///     S2.11  ROOF      anchor
///
/// ⚠ WHAT THIS DOES NOT DO, said here so the next reader does not stop at the name (rule 11).
///
/// It infers a LEVEL NUMBER and nothing else. It does not read a title, does not repair the title
/// block, and cannot tell a mezzanine from a storey — a set that numbers L1, L1M, L2 has a level
/// between two anchors that no integer names, and this will either decline it or place it one out.
/// It says nothing about which BUILDING a sheet serves, so on a multi-building site a gap sheet
/// still reaches the wrong stack if its building tag is missing too.
///
/// It is also blind to a set whose sheet numbers do not ascend with the building. Nothing here
/// checks that the drawings were numbered bottom-up; the anchors are simply assumed to be in order,
/// and where an office numbers its plans some other way every inference between two anchors is
/// wrong together. <see cref="Infer"/> refuses when the COUNT disagrees, never when the ordering
/// itself is the lie — only the back-test below can see that.
///
/// ⭐ AND THE BACK-TEST IS WHY THIS ONLY ANSWERS WHERE THE COUNT IS EXACT. Leave-one-out over run
/// 45's sheet ledger — hide the level of a drawing whose title DID parse, infer it from the order,
/// compare — across 1,776 drawings:
///
///     exactly determined   480 right, 1 wrong   99.8%      <- shipped
///     spread over the gap   56 right, 39 wrong  58.9%      <- NOT shipped, asked instead
///
/// And the one "wrong" is not wrong. 30892-01's S2.53 is titled "LEVEL 19-20 PLAN &amp; LEVEL 21
/// PLAN": the drawing covers 19, 20 and 21, the title reader recorded only 19 and 21, and the order
/// supplied the 20 it had missed. So the exact tier is 481 of 481 against a ground truth that was
/// itself one short.
///
/// The spread tier was written, measured and deleted. It is the "make a best guess anyway" case,
/// and at 59% it would put a floor on the wrong storey more than a third of the time — which, as
/// this tool's own question J8 says, looks exactly like a floor the engineer drew. A wrong floor is
/// worse than an absent one because nothing about it asks to be checked. Those gaps go to her as
/// step 143's question instead, which is the honest place for a coin flip.
///
/// ⚠ One caveat on the number, because it flatters slightly: the back-test hides drawings whose
/// titles DID parse, and the drawings this runs on in anger are ones that did NOT. They are not
/// guaranteed to be the same population — a title that would not parse may also be a sheet that is
/// not a plan. It is the best estimate available and it is a strong one, but it is an estimate.
/// </summary>
public static class LevelsFromSheetOrder
{
    /// <summary>
    /// A level this tool worked out for a sheet, and the sentence that says how.
    ///
    /// Every one of these is EXACTLY DETERMINED by construction — the gap held as many sheets as
    /// there were levels unaccounted for, so the answer is arithmetic. It is still an assumption,
    /// because the arithmetic rests on the drawings being numbered in storey order, and
    /// <paramref name="Because"/> is what goes in front of the engineer so she can see the working
    /// rather than the conclusion.
    /// </summary>
    public sealed record Inferred(
        string FileName,
        IReadOnlyList<int> Levels,
        string Because);

    /// <summary>The sheet number at the head of a view's file name: S2.05.1 from S2.05.1_1_….dxf.</summary>
    private static readonly Regex SheetNumber =
        new(@"^([A-Za-z]{1,3}\d+(?:\.\d+)*)_", RegexOptions.Compiled);

    /// <summary>
    /// The drawing a view belongs to. S2.05.1 and S2.05.2 are two areas of sheet S2.05; S2.01 on its
    /// own is already a drawing. Only a number with three or more parts loses its last one.
    /// </summary>
    internal static string? StemOf(string fileName)
    {
        // ⚠ THE BASE NAME, because PlanSheetInfo.FileName is whatever Parse was handed, and in the
        // pipeline that is a FULL PATH. This pattern is anchored at ^, so against
        // "C:\…\dxf\S2.06.1_1_….dxf" it matched nothing and the whole rule silently did nothing on
        // every real set while all eight unit tests — written with bare names — stayed green.
        // Caught by rebuilding 31005-01 and reading the report, not by the tests.
        // PlanSheetNaming.Parse and TitleOf both take the base name first; so does this now.
        var m = SheetNumber.Match(Path.GetFileName(fileName));
        if (!m.Success) return null;

        string[] parts = m.Groups[1].Value.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts[..^1]) : m.Groups[1].Value;
    }

    /// <summary>Sheet numbers sort by their parts as NUMBERS: S2.9 comes before S2.10.</summary>
    private static IComparable[] SortKey(string stem) =>
        stem.Split('.')
            .Select(p => (IComparable)(int.TryParse(
                new string(p.Where(char.IsDigit).ToArray()),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0))
            .ToArray();

    /// <summary>
    /// The sheet SERIES: S2 from S2.05, S0 from S0.00.
    ///
    /// ⚠ THE SERIES IS THE WHOLE FIRST COMPONENT, NOT JUST THE LETTER, and 31005-01 is why. That set
    /// holds two views of every drawing: S0.00_3 "LEVEL L01 PLAN CONCRETE OUTLINE", whose title reads
    /// perfectly but which carries NO STRUCTURAL OUTLINES and never places, and S2.02.1, which
    /// carries the structure under a title the block scrambled. Taking only the letter put both in
    /// one sequence, so the empty S0 views CLAIMED every level in the building and the S2 gaps had
    /// nothing left to be — the rule found nothing on the set it was written for.
    ///
    /// A level claimed by a view that draws nothing is not claimed. Scoping ordering AND claims to
    /// the series keeps an index or general-notes series from speaking for the plan series, which is
    /// also how the drawings are actually organised: S0 general, S1 notes and diagrams, S2 plans.
    /// </summary>
    internal static string SeriesOf(string stem)
    {
        int dot = stem.IndexOf('.');
        return dot < 0 ? stem : stem[..dot];
    }

    /// <summary>
    /// Whether a drawing already says what it is by some means other than a level number: a parkade
    /// level, the roof, the lift-overrun roof, the foundation, or a top floor named by a word.
    ///
    /// Each of these leaves <see cref="PlanSheetInfo.Levels"/> empty and is emphatically NOT an
    /// unnamed drawing. <see cref="PlanSheetNaming"/> keeps them as separate flags rather than as
    /// levels, so anything reasoning about "did this sheet say where it goes" has to ask all of
    /// them — the Codex audit's finding, and the repo's own <c>HasPlacement</c> omits
    /// <c>IsTopFloor</c>, which is why this asks directly rather than borrowing it.
    /// </summary>
    private static bool NamesSomethingElse(IEnumerable<PlanSheetInfo> views) =>
        views.Any(v => v.ParkadeLevels.Count > 0 || v.IsRoof || v.IsElevatorRoof
                       || v.IsFoundation || v.IsTopFloor);

    private static int Compare(string a, string b)
    {
        int byPrefix = string.Compare(SeriesOf(a), SeriesOf(b), StringComparison.OrdinalIgnoreCase);
        if (byPrefix != 0) return byPrefix;

        var ka = SortKey(a);
        var kb = SortKey(b);
        for (int i = 0; i < Math.Min(ka.Length, kb.Length); i++)
        {
            int c = ka[i].CompareTo(kb[i]);
            if (c != 0) return c;
        }

        return ka.Length.CompareTo(kb.Length);
    }

    /// <summary>
    /// The levels each unnamed sheet draws, worked out from the sheets around it.
    ///
    /// Only ordinary numbered levels take part. A parkade level counts in its own sequence and the
    /// roof is not a number at all, so a gap that sits between P1 and L3 — 31005-01's S2.02 to
    /// S2.04 — is left alone: there is no arithmetic that says how many storeys are in it, and
    /// inventing one would put a floor on a storey she never drew.
    /// </summary>
    public static IReadOnlyList<Inferred> Infer(IReadOnlyList<PlanSheetInfo> sheets)
    {
        var byStem = new Dictionary<string, List<PlanSheetInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
            if (StemOf(sheet.FileName) is { } stem)
            {
                if (!byStem.TryGetValue(stem, out var list)) byStem[stem] = list = new List<PlanSheetInfo>();
                list.Add(sheet);
            }

        var allStems = byStem.Keys.ToList();
        allStems.Sort(Compare);

        // A stem's levels are its views' together: the half that parsed speaks for the drawing.
        var levelsOf = allStems.ToDictionary(
            s => s,
            s => byStem[s].SelectMany(v => v.Levels).Distinct().OrderBy(n => n).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var found = new List<Inferred>();

        // ONE SERIES AT A TIME. See SeriesOf: an index series whose views draw nothing must not claim
        // the levels the plan series is trying to account for.
        foreach (var series in allStems.GroupBy(SeriesOf, StringComparer.OrdinalIgnoreCase))
            InferWithin(series.ToList(), byStem, levelsOf, found);

        return found;
    }

    private static void InferWithin(
        List<string> order,
        IReadOnlyDictionary<string, List<PlanSheetInfo>> byStem,
        IReadOnlyDictionary<string, List<int>> levelsOf,
        List<Inferred> found)
    {
        // ANY sheet's claim counts here, not just the anchors either side. A level drawn on a sheet
        // somewhere else in the SERIES is not unaccounted for, and handing it to a gap would put two
        // drawings on one storey.
        var claimed = new HashSet<int>(order.SelectMany(s => levelsOf[s]));

        // A stem that names a parkade level, the roof, the foundation or nothing but a word is not an
        // anchor in the numbered sequence and cannot bound a run.
        bool IsNumberedAnchor(string stem) =>
            levelsOf[stem].Count > 0
            && byStem[stem].All(v => v.ParkadeLevels.Count == 0 && !v.IsRoof && !v.IsFoundation);

        for (int i = 0; i < order.Count; i++)
        {
            if (!IsNumberedAnchor(order[i])) continue;

            // The next numbered anchor, and the gap stems between the two.
            int j = i + 1;
            while (j < order.Count && !IsNumberedAnchor(order[j])) j++;
            if (j >= order.Count) break;

            // ⚠ A GAP IS A DRAWING THAT NAMES NOTHING — NOT MERELY ONE WITH NO LEVEL NUMBER.
            //
            // Found by the Codex audit, 2026-09-24: "Preserve every already parsed placement,
            // including parkade, roof, foundation, and top-floor meanings; checking only
            // Levels.Count is insufficient." A ROOF plan has no Levels. So does a foundation, a
            // parkade plan and a loft. One of those sitting BETWEEN two numbered anchors read as a
            // gap and was handed a storey number, which is the precise fault this rule was written
            // to avoid committing — a floor on a storey the engineer never drew.
            //
            // The run then fails the "every stem between the anchors is a gap" test below and is
            // declined entirely, which is right: a stretch with a roof in the middle is not a clean
            // sequence of numbered storeys and nothing about it is arithmetic.
            var gaps = order[(i + 1)..j]
                .Where(s => levelsOf[s].Count == 0 && !NamesSomethingElse(byStem[s]))
                .ToList();

            // A stem in the run that DOES name something — a parkade level, the roof — is not a gap,
            // and its presence means the run is not a clean stretch of numbered storeys. Leave it.
            if (gaps.Count == 0 || gaps.Count != j - i - 1) { i = j - 1; continue; }

            int lo = levelsOf[order[i]].Max();
            int hi = levelsOf[order[j]].Min();

            var unclaimed = Enumerable.Range(lo + 1, Math.Max(0, hi - lo - 1))
                .Where(n => !claimed.Contains(n))
                .ToList();

            // ONE LEVEL PER GAP SHEET, OR NOTHING. Measured at 99.8% when the counts agree and 58.9%
            // when they do not, so the second case is not answered at all — it goes to the engineer
            // as step 143's question. See the back-test in this class's summary.
            if (unclaimed.Count != gaps.Count) { i = j - 1; continue; }

            for (int g = 0; g < gaps.Count; g++)
            {
                string because =
                    $"sheet {gaps[g]} sits between {order[i]} (level {lo}) and {order[j]} (level {hi}), " +
                    $"and level {unclaimed[g]} is the only one between them that no drawing claims";

                // Both areas of the drawing get the answer: they are one sheet in two halves.
                foreach (var view in byStem[gaps[g]])
                    found.Add(new Inferred(view.FileName, [unclaimed[g]], because));
            }

            i = j - 1;
        }
    }
}
