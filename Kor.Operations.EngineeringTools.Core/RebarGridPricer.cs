#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Prices the changes that govern a known extent - the continuous "field" grids. A slab base
    /// grid covers the whole floor plate; a wall grid covers the wall face. So a change in one is
    /// ΔAs (kg/m², exact from the drawings) × area (m², from the Revit model or read off the plan).
    ///
    /// This is the "main focus for precise numbers" - the call-out change is exact, and the only
    /// input it needs is the area, which is the easy part to see.
    /// </summary>
    public static class RebarGridPricer
    {
        // "15M @ 350 EACH WAY BOT. CONT."  /  "... EACH WAY T&B"
        private static readonly Regex SlabGrid = new(
            @"(\d{2})M\s*@?\s*(\d{2,4})\s*EACH\s*WAY\.?\s*(T&B|TOP\s*&?\s*BOT\.?|BOT\.?|TOP\.?)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "15M @ 350 VERTS. EACH FACE"  /  "15M @ 200 HORIZ. EACH FACE"
        private static readonly Regex WallGrid = new(
            @"(\d{2})M\s*@?\s*(\d{2,4})\s*(VERT|HORIZ)[A-Z.]*\s*(EACH\s*FACE|E\.?F\.?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int SpacingMin = 75, SpacingMax = 750;

        public static RebarPricedResult Compare(
            IReadOnlyList<string> beforePages,
            IReadOnlyList<string> afterPages,
            IReadOnlyDictionary<string, double>? slabAreasM2 = null,
            IReadOnlyDictionary<string, double>? wallAreasM2 = null,
            string beforeLabel = "Before",
            string afterLabel = "After")
        {
            var bt = RebarCalloutExtractor.GroupTextBySheet(beforePages).ToDictionary(x => x.Sheet);
            var at = RebarCalloutExtractor.GroupTextBySheet(afterPages).ToDictionary(x => x.Sheet);
            var sheets = bt.Keys.Union(at.Keys).OrderBy(SortKey).ToList();

            var changes = new List<GridChange>();
            foreach (var s in sheets)
            {
                string bText = bt.TryGetValue(s, out var bb) ? bb.Text : "";
                string aText = at.TryGetValue(s, out var aa) ? aa.Text : "";
                string title = (at.TryGetValue(s, out var t1) ? t1.Title : null)
                               ?? (bt.TryGetValue(s, out var t2) ? t2.Title : "") ?? "";

                // Slab base grid (the dominant EACH WAY mat on the sheet).
                var sb = DominantSlab(bText);
                var sa = DominantSlab(aText);
                if (Differs(sb, sa))
                    changes.Add(MakeChange(s, title, "Slab grid", sb, sa,
                        slabAreasM2 != null && slabAreasM2.TryGetValue(s, out var av) ? av : (double?)null));

                // Wall grids (vertical and horizontal, each face) - priced if a wall area is given.
                foreach (var dir in new[] { "VERT", "HORIZ" })
                {
                    var wb = DominantWall(bText, dir);
                    var wa = DominantWall(aText, dir);
                    if (Differs(wb, wa))
                        changes.Add(MakeChange(s, title, $"Wall grid ({dir.ToLower()})", wb, wa,
                            wallAreasM2 != null && wallAreasM2.TryGetValue(s, out var wv) ? wv : (double?)null));
                }
            }

            // Biggest steel impact first; unpriced (no area yet) after priced.
            changes = changes
                .OrderByDescending(c => c.DeltaKg.HasValue)
                .ThenByDescending(c => Math.Abs(c.DeltaKg ?? 0))
                .ThenByDescending(c => Math.Abs(c.DeltaAsKgPerM2))
                .ToList();

            return new RebarPricedResult(
                changes,
                TotalKnownDeltaKg: changes.Where(c => c.DeltaKg.HasValue).Sum(c => c.DeltaKg!.Value),
                PricedCount: changes.Count(c => c.DeltaKg.HasValue),
                // Weight-neutral grids (ΔAs ≈ 0) don't need an area - excluded from the "needs input" count.
                UnpricedCount: changes.Count(c => !c.DeltaKg.HasValue && Math.Abs(c.DeltaAsKgPerM2) >= 0.02),
                BeforeLabel: beforeLabel,
                AfterLabel: afterLabel);
        }

        private static GridChange MakeChange(string sheet, string title, string kind,
            GridSpec? before, GridSpec? after, double? area)
        {
            double mass = (after ?? before)!.AsKgPerM2(RebarWeightEstimator.BarMassKgM); // touch to ensure table loaded
            double dAs = (after?.AsKgPerM2(RebarWeightEstimator.BarMassKgM) ?? 0)
                       - (before?.AsKgPerM2(RebarWeightEstimator.BarMassKgM) ?? 0);
            double? dKg = area.HasValue ? dAs * area.Value : (double?)null;
            return new GridChange(sheet, title, kind, before, after, dAs, area, dKg);
        }

        private static bool Differs(GridSpec? a, GridSpec? b)
        {
            if (a is null && b is null) return false;
            if (a is null || b is null) return false; // grid only on one issue -> treat as not a clean grid change
            return a.BarSize != b.BarSize || a.SpacingMm != b.SpacingMm || a.Layout != b.Layout;
        }

        private static GridSpec? DominantSlab(string text)
        {
            var hits = new List<GridSpec>();
            foreach (Match m in SlabGrid.Matches(text))
            {
                int sz = int.Parse(m.Groups[1].Value), sp = int.Parse(m.Groups[2].Value);
                if (sp < SpacingMin || sp > SpacingMax) continue;
                string suff = m.Groups[3].Value.ToUpperInvariant();
                var layout = suff.Contains("T&B") || (suff.Contains("TOP") && suff.Contains("BOT"))
                    ? GridLayout.EachWayTopBottom : GridLayout.EachWayBottom;
                hits.Add(new GridSpec(sz, sp, layout, m.Value.Trim()));
            }
            return Dominant(hits);
        }

        private static GridSpec? DominantWall(string text, string dir)
        {
            var hits = new List<GridSpec>();
            foreach (Match m in WallGrid.Matches(text))
            {
                if (!m.Groups[3].Value.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                int sz = int.Parse(m.Groups[1].Value), sp = int.Parse(m.Groups[2].Value);
                if (sp < SpacingMin || sp > SpacingMax) continue;
                hits.Add(new GridSpec(sz, sp, GridLayout.EachFace, m.Value.Trim()));
            }
            return Dominant(hits);
        }

        // Most frequent (size,spacing,layout); ties -> tighter spacing (more steel) wins.
        private static GridSpec? Dominant(List<GridSpec> hits)
        {
            if (hits.Count == 0) return null;
            return hits
                .GroupBy(h => (h.BarSize, h.SpacingMm, h.Layout))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.SpacingMm)
                .First().First();
        }

        private static string SortKey(string sheet) =>
            Regex.Replace(sheet, @"\d+", m => m.Value.PadLeft(4, '0'));
    }
}
