#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Pure logic: turns the per-page text of a structural drawing set into per-sheet
    /// reinforcing call-outs. No PDF dependency here so it is fast to unit-test - feed it
    /// page-text strings (PdfPageTextReader produces them from a real PDF).
    ///
    /// A rebar call-out is an intensity, e.g. "15M @ 200" (bar size + spacing). The page's
    /// OWN sheet number is the rarest sheet token on the page (cross-references like S5.01,
    /// S7.01 recur across many pages; a sheet's own number appears on essentially one page).
    /// </summary>
    public static class RebarCalloutExtractor
    {
        private static readonly Regex SheetRe =
            new(@"\bS\d{1,2}(?:\.\d{1,2}){1,3}[A-Z]?\b", RegexOptions.Compiled);

        private static readonly Regex CalloutRe =
            new(@"\b(\d{2})M\s*@\s*(\d{2,4})\b", RegexOptions.Compiled);

        private static readonly Regex TitleRe =
            new(@"(S\d{1,2}(?:\.\d{1,2}){1,3}[A-Z]?)\s*-\s*([A-Z0-9][A-Z0-9 ,.\-()/&']{4,60})",
                RegexOptions.Compiled);

        // Plausible rebar spacing (mm). Filters detail/reference numbers like "@ 16".
        private const int SpacingMin = 75;
        private const int SpacingMax = 750;

        // Imperial (US) call-out: "#5 @ 12" — bar number, spacing in inches. A PARALLEL path,
        // used only when UnitSystem.Imperial; it never runs on a metric set, and the metric
        // CalloutRe / 75–750 constants above are never touched (Rory's 31065 path stays identical).
        private static readonly Regex ImperialCalloutRe =
            new(@"#(\d{1,2})\s*@\s*(\d{1,2})(?!\d)", RegexOptions.Compiled);
        private const int SpacingMinIn = 3;
        private const int SpacingMaxIn = 48;

        private static (Regex Re, int Min, int Max, System.Func<int, int, string> Key) CalloutSpec(UnitSystem unit)
            => unit == UnitSystem.Imperial
                ? (ImperialCalloutRe, SpacingMinIn, SpacingMaxIn, (s, sp) => $"#{s}@{sp}")
                : (CalloutRe, SpacingMin, SpacingMax, (s, sp) => $"{s}M@{sp}");

        // KOR-Vancouver BAR-LIST call-out: "16-15M13.9", "6-C15M08.5", "C15M3.11" — an optional bar
        // QUANTITY, an optional C(ontinuous), a Canadian metric bar size, and a bar LENGTH (ft.in), glued
        // into one token (spacing, when shown, is a SEPARATE "@ 10\"" token and is deliberately not relied
        // on — word order from the text layer doesn't keep it adjacent). This is a THIRD grammar that runs
        // ALONGSIDE the metric/imperial intensity patterns above, never instead of them: it requires a bar
        // size GLUED to a decimal length (`15M13.9`), which the intensity callout "15M @ 200" never produces,
        // so it adds nothing on Rory's 31065 metric set (kept byte-identical) and only captures the bar-list
        // sets (e.g. Brewery District 30953) the intensity patterns read as empty. The full token is the key.
        private static readonly Regex BarListRe =
            new(@"\b(?:\d{1,3}-)?C?(\d{2})M\d{1,2}\.\d{1,2}\b", RegexOptions.Compiled);
        private const int BarSizeMin = 10, BarSizeMax = 55;   // Canadian bars: 10M..55M, all multiples of 5

        public static IReadOnlyList<SheetCallouts> Extract(IReadOnlyList<string> pages, UnitSystem unit = UnitSystem.Metric)
        {
            var tokens = pages
                .Select(p => SheetRe.Matches(p).Select(m => m.Value).ToList())
                .ToList();

            // Index/cover pages list every sheet -> many distinct tokens; exclude from frequency.
            var indexPages = new HashSet<int>();
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Distinct().Count() > 30) indexPages.Add(i);

            var freq = new Dictionary<string, int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (indexPages.Contains(i)) continue;
                foreach (var s in tokens[i].Distinct())
                    freq[s] = freq.GetValueOrDefault(s) + 1;
            }

            var titles = BuildTitles(pages);

            var (re, smin, smax, keyFmt) = CalloutSpec(unit);
            var byOwn = new Dictionary<string, Dictionary<string, int>>();
            for (int i = 0; i < pages.Count; i++)
            {
                if (indexPages.Contains(i) || tokens[i].Count == 0) continue;

                var uniq = tokens[i].Distinct().ToList(); // preserves first-appearance order
                string own = uniq
                    .OrderBy(s => freq.GetValueOrDefault(s, 0))
                    .ThenBy(s => uniq.IndexOf(s))
                    .First();

                if (!byOwn.TryGetValue(own, out var counter))
                    byOwn[own] = counter = new Dictionary<string, int>();

                foreach (Match m in re.Matches(pages[i]))
                {
                    int spacing = int.Parse(m.Groups[2].Value);
                    if (spacing < smin || spacing > smax) continue;
                    string key = keyFmt(int.Parse(m.Groups[1].Value), spacing);
                    counter[key] = counter.GetValueOrDefault(key) + 1;
                }

                // Bar-list grammar (additive, all modes): captures "16-15M13.9"-style call-outs the intensity
                // patterns above never see. Size must be a real Canadian bar (10..55M, multiple of 5) so a
                // stray "12M34.5"-shaped dimension can't masquerade as a bar. The full token is the key.
                foreach (Match m in BarListRe.Matches(pages[i]))
                {
                    int size = int.Parse(m.Groups[1].Value);
                    if (size < BarSizeMin || size > BarSizeMax || size % 5 != 0) continue;
                    string key = m.Value.ToUpperInvariant();
                    counter[key] = counter.GetValueOrDefault(key) + 1;
                }

                // PLAN-callout grammar (additive, all modes): "36-15M4700 @ 125" — count-size-LENGTH(mm)
                // [@ spacing], the form reinforcing is called up on the plans themselves. The glued mm
                // length keeps it disjoint from the intensity form ("15M @ 200", no glued digits) and the
                // bar-list form ("16-15M13.9", feet-inch dot), so this adds plan steel without disturbing
                // either. These are the changes that take the manual work: schedules are read in minutes,
                // the 20 scattered plan bars are not.
                foreach (Match m in RebarPlanCallout.TextRe.Matches(pages[i]))
                {
                    var pc = RebarPlanCallout.FromGroups(m);
                    if (pc is { } v) counter[v.Key] = counter.GetValueOrDefault(v.Key) + 1;
                }
            }

            return byOwn
                .Select(kv => new SheetCallouts(kv.Key, titles.GetValueOrDefault(kv.Key, ""), kv.Value))
                .ToList();
        }

        /// <summary>
        /// Groups the raw page text by each page's OWN sheet number (same ownership rule as
        /// Extract). Lets downstream logic - e.g. base-grid pricing - read a whole sheet's text.
        /// </summary>
        public static IReadOnlyList<(string Sheet, string Title, string Text)> GroupTextBySheet(
            IReadOnlyList<string> pages)
        {
            var tokens = pages
                .Select(p => SheetRe.Matches(p).Select(m => m.Value).ToList())
                .ToList();

            var indexPages = new HashSet<int>();
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Distinct().Count() > 30) indexPages.Add(i);

            var freq = new Dictionary<string, int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (indexPages.Contains(i)) continue;
                foreach (var s in tokens[i].Distinct())
                    freq[s] = freq.GetValueOrDefault(s) + 1;
            }

            var titles = BuildTitles(pages);
            var byOwn = new Dictionary<string, List<string>>();
            for (int i = 0; i < pages.Count; i++)
            {
                if (indexPages.Contains(i) || tokens[i].Count == 0) continue;
                var uniq = tokens[i].Distinct().ToList();
                string own = uniq
                    .OrderBy(s => freq.GetValueOrDefault(s, 0))
                    .ThenBy(s => uniq.IndexOf(s))
                    .First();
                if (!byOwn.TryGetValue(own, out var list)) byOwn[own] = list = new List<string>();
                list.Add(pages[i]);
            }

            return byOwn
                .Select(kv => (kv.Key, titles.GetValueOrDefault(kv.Key, ""), string.Join("\n", kv.Value)))
                .ToList();
        }

        private static Dictionary<string, string> BuildTitles(IReadOnlyList<string> pages)
        {
            var titles = new Dictionary<string, string>();

            // The drawing-index page has the most distinct sheet tokens.
            int best = -1, bestN = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                int n = SheetRe.Matches(pages[i]).Select(m => m.Value).Distinct().Count();
                if (n > bestN) { bestN = n; best = i; }
            }
            if (best < 0) return titles;

            foreach (Match m in TitleRe.Matches(pages[best]))
            {
                string num = m.Groups[1].Value;
                string title = m.Groups[2].Value.Trim();
                title = Regex.Split(title, @"\s{2,}")[0].Trim(' ', '-');
                // Drop any trailing cross-reference (e.g. "... - NORTH  S3.04 - SHEA") that bled in.
                title = Regex.Replace(title, @"\s+S\d{1,2}(?:\.\d{1,2}){1,3}.*$", "").Trim(' ', '-');
                if (!titles.ContainsKey(num) && title.Length >= 4)
                    titles[num] = title;
            }
            return titles;
        }
    }
}
