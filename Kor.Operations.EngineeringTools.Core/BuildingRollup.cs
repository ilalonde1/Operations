#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The building-level bookkeeping that turns a pile of per-sheet slab readings into a takeoff that
    /// counts every physical floor exactly once. Per-sheet measurement is convention-agnostic, but the
    /// way a set encodes "which floors does this plan stand for" is not: Coronation uses clean
    /// non-overlapping bands ("LEVEL 17-28"), while Onyx draws one plan per level and tags it with
    /// several OVERLAPPING reinforcing layouts ("L4 (Layout 1: L3-10)", "L4 (Layout 2: L4,6,9…)"). A
    /// naïve sum of each plate's vision-given count multiply-counts the second style several-fold.
    ///
    /// The fix is global floor assignment: parse each slab's label into the set of physical floors it
    /// represents (the leading level token/range, ignoring parenthetical layout/plate-type notes), then
    /// give each floor to a single owning plate — the most specific one (smallest floor set), breaking
    /// ties by confidence then area. A plate's effective count is the number of floors it alone owns;
    /// duplicate and overlapping plates fall to zero and drop out. Reinforcing-only or concrete-outline
    /// copies of the same level collapse to one. Clean bands are unaffected (nothing else claims their
    /// floors). This never touches the measured area — only how many identical floors it stands in for.
    /// </summary>
    public static class BuildingRollup
    {
        // A plate represents the FULL floor only if its area is at least this fraction of the largest
        // plan competing for that same floor. A much smaller plate (an enlarged-core or partial-area
        // detail re-drawn at the same level as a typical-floor band) has its concrete already inside the
        // band's full-floor measurement, so it must not win the floor and price it as a fragment.
        private const double FullFloorAreaFraction = 0.6;

        // Leading level token: a 1–3 letter prefix + number, optionally a "-number" range end.
        private static readonly Regex LeadLevel = new(
            @"^\s*([A-Za-z]{1,3})\s*0*(\d+)\s*(?:-\s*[A-Za-z]{0,3}\s*0*(\d+))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Words that mark a distinct slab at the same level number (a mezzanine, roof, mechanical or
        // penthouse slab is NOT the same pour as the plain level) — tagged so it isn't deduped away.
        private static readonly Regex Modifier = new(
            @"\b(MEZZ|MEZZANINE|ROOF|MECH|MECHANICAL|AMENITY|PH|PENTHOUSE)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Canonical physical-floor keys a level label represents. "L17-28" → L17…L28; "P5-P1" →
        /// P5…P1; "L4 (Layout 1: L3-10)" → L4 (the leading token; the parenthetical is a layout note,
        /// not this sheet's floor); "L34 Roof" → L34·M (modifier-tagged, distinct from a plain L34).
        /// Empty when no level token can be read — the caller then treats the plate as its own unique
        /// floor (counted once, never merged with another).
        /// </summary>
        public static IReadOnlyList<string> ParseFloors(string? level)
        {
            if (string.IsNullOrWhiteSpace(level)) return Array.Empty<string>();

            int paren = level.IndexOf('(');
            string head = (paren >= 0 ? level.Substring(0, paren) : level);

            bool hasMod = Modifier.IsMatch(head);
            // Normalise spelled-out prefixes so the regex sees a single-letter prefix.
            string s = Regex.Replace(head, @"\bLEVELS?\b|\bLVL\b|\bLEV\b", "L", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPARKADE\b|\bPARKING\b", "P", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bBASEMENT\b", "B", RegexOptions.IgnoreCase);

            var m = LeadLevel.Match(s);
            if (!m.Success) return Array.Empty<string>();

            string prefix = m.Groups[1].Value.ToUpperInvariant();
            if (!int.TryParse(m.Groups[2].Value, out int a)) return Array.Empty<string>();
            string tag = hasMod ? "·M" : "";

            if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int b))
            {
                var span = new List<string>();
                int step = a <= b ? 1 : -1;
                // Guard against an absurd range (a mis-read) producing thousands of floors.
                if (Math.Abs(b - a) > 200) return new[] { prefix + a + tag };
                for (int f = a; ; f += step) { span.Add(prefix + f + tag); if (f == b) break; }
                return span;
            }
            return new[] { prefix + a + tag };
        }

        /// <summary>
        /// One slab plate as seen by the roll-up: its index, level label, area, confidence and read
        /// thickness. ThicknessIn is 0 when the callout could not be resolved — such a plate prices to no
        /// concrete, so it must never win a floor over a sibling whose thickness IS known.
        /// </summary>
        public readonly record struct SlabRef(int Index, string? Level, double AreaSqFt, double Confidence, double ThicknessIn = 0);

        /// <summary>
        /// Assigns every physical floor to exactly one slab plate and returns each plate's effective
        /// floor count (how many floors it alone owns). Plates that own no floors — duplicates, or the
        /// losing copies of an overlapping layout — map to 0 and should be dropped. A plate whose label
        /// yields no floor token is treated as a single unique floor (counted once).
        /// </summary>
        public static IReadOnlyDictionary<int, int> AssignSlabFloors(IReadOnlyList<SlabRef> slabs)
        {
            ArgumentNullException.ThrowIfNull(slabs);
            var owned = slabs.ToDictionary(s => s.Index, _ => 0);
            var floorSets = new Dictionary<int, IReadOnlyList<string>>();
            var candidates = new Dictionary<string, List<SlabRef>>(StringComparer.Ordinal);

            foreach (var s in slabs)
            {
                var floors = ParseFloors(s.Level);
                floorSets[s.Index] = floors;
                foreach (var f in floors)
                {
                    if (!candidates.TryGetValue(f, out var lst)) candidates[f] = lst = new();
                    lst.Add(s);
                }
            }

            foreach (var (_, lst) in candidates)
            {
                // Only plans that cover (most of) the floor are eligible to own it — a much smaller plate
                // is a partial/enlarged detail and is excluded so it can't price the floor as a fragment.
                // When every candidate is small (the floor only has partial plans), they all stay eligible.
                double maxArea = lst.Max(s => s.AreaSqFt);
                double floorThreshold = maxArea * FullFloorAreaFraction;
                var fullPlans = lst.Where(s => s.AreaSqFt >= floorThreshold).ToList();
                var pool = fullPlans.Count > 0 ? fullPlans : lst;

                // Among the full-floor plans: a resolved thickness wins first (a 0"/unresolved read prices
                // to no concrete and must never beat a usable sibling), then most specific (fewest floors),
                // then most confident, then largest area, then earliest — so a single-floor sheet still
                // beats a band when both genuinely measure the whole floor, but a fragment or a broken
                // thickness read can no longer outrank the plate that actually carries the floor's concrete.
                SlabRef best = pool
                    .OrderByDescending(s => s.ThicknessIn > 0 ? 1 : 0)
                    .ThenBy(s => floorSets[s.Index].Count)
                    .ThenByDescending(s => s.Confidence)
                    .ThenByDescending(s => s.AreaSqFt)
                    .ThenBy(s => s.Index)
                    .First();
                owned[best.Index]++;
            }

            // A plate with no parseable level is its own floor — count it once rather than drop it.
            foreach (var s in slabs)
                if (floorSets[s.Index].Count == 0)
                    owned[s.Index] = 1;

            return owned;
        }
    }
}
