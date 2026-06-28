#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): the slab's field thickness, read off the drawing's own callouts ("10&quot;
    /// SLAB", "12&quot; SLABS") instead of from a synthesised image read — so it is STABLE run-to-run, not
    /// the ±noise an image read gives. The callout is a storey number immediately followed by an inch
    /// mark and the word SLAB; that tight shape naturally skips the distractors an estimator also skips:
    /// "4&quot; UNREINFORCED SLAB ON GRADE" (a word intervenes) and "12&quot; PC3 … SLAB" (a column). Where a
    /// sheet states more than one (the field slab plus thicker slab-bands/drops), the MOST COMMON wins,
    /// and ties go to the thinner value — the field slab is called out far more than its thickenings.
    /// </summary>
    public static class SlabThicknessReader
    {
        // "<n>" SLAB" — a storey number, a 1–2 char inch mark (the glyph varies on CAD sheets), then SLAB.
        private static readonly Regex CalloutRx = new(@"(\d{1,2})\s*[^\w\s]{1,2}\s*SLAB",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int MinIn = 4;    // thinner than this is a topping/SOG, not a structural slab
        private const int MaxIn = 48;   // thicker is a transfer mat, not a field slab

        /// <summary>The dominant field-slab thickness (inches) called out across the lines, or null.</summary>
        public static int? DominantThicknessIn(IEnumerable<string>? lines)
        {
            if (lines is null) return null;
            var votes = new List<int>();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (Match m in CalloutRx.Matches(line))
                {
                    int n = int.Parse(m.Groups[1].Value);
                    if (n >= MinIn && n <= MaxIn) votes.Add(n);
                }
            }
            if (votes.Count == 0) return null;
            return votes.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }
    }
}
