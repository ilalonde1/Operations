#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): the canonical sheet identity an estimator reads off the title block —
    /// which physical floor this plan draws (<see cref="Level"/>) and, for match-line sets, which half
    /// (<see cref="Zone"/>, e.g. NORTH/SOUTH). This is what lets the takeoff count one slab per
    /// (level, zone): NORTH + SOUTH of a floor are distinct plates that SUM, but the same (level, zone)
    /// drawn on two sheets (framing plan vs reinforcing plan) is ONE slab — counting both double-counts.
    ///
    /// Read by POSITION, not by scanning every line. On these dense CAD sheets a page-wide text scan
    /// also catches north-arrows and cross-references to OTHER levels ("SEE LEVEL P4 PLAN"), which
    /// corrupts the identity. Instead we read the title from where it lives: the bottom-right title
    /// block. The sheet title is the largest "PLAN" on the right edge; its baseline spells "LEVEL &lt;x&gt;
    /// PLAN [&lt;zone&gt;]". The match-line half is taken from the SHEET NUMBER suffix ("S2.02-N"/"-S"),
    /// which is present and unambiguous even when the title text omits the half.
    /// </summary>
    public sealed record SheetTitle(string Level, string? Zone, string Raw)
    {
        /// <summary>Stable key for de-duplicating slab plates: "P4|NORTH" / "L12|" (no zone).</summary>
        public string Key => $"{Level}|{Zone}";

        /// <summary>Human label for the level row: "P4 NORTH" / "L12".</summary>
        public string Display => Zone is null ? Level : $"{Level} {Zone}";
    }

    public static class SheetTitleReader
    {
        // The (now position-isolated) title line: "LEVEL P4 PLAN NORTH", "LEVEL P1 MEZZ. PLAN",
        // "LEVEL 12 FLOOR PLAN", "2ND FLOOR PLAN". A P-level may carry a MEZZ(anine) modifier that keeps
        // a mezzanine distinct from its base level; the half (if present in the text) may follow a
        // separator ("PLAN - NORTH"). Safe to be liberal here because the input is the isolated title
        // band, not the whole page. Foundation plans are intentionally NOT matched — a mat is a different
        // element (different rebar density) and giving it a slab identity would mis-price it.
        private static readonly Regex TitleRx = new(
            @"(?:LEVEL\s+)?(?<lvl>P\d{1,2}(?:\s+MEZZ(?:ANINE)?)?|L\d{1,2}|B\d{1,2}|PH\d?|ROOF|MAIN|GROUND|\d{1,2}(?:ST|ND|RD|TH)?)\.?\s+(?:FLOOR\s+|FRAMING\s+|FORMWORK\s+)?PLAN(?:\s*[-–—]?\s*(?<zone>NORTH|SOUTH|EAST|WEST))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // A drawing sheet number like "S2.02-N", "S2.09.1-S": a letter+digit code, optional .sub parts,
        // optional cardinal-half suffix. The suffix is the reliable match-line zone.
        private static readonly Regex SheetNumRx = new(@"^[A-Z]{1,2}\d[\d.]*(?:-(?<half>[NSEW]))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Title block lives on the right edge; the sheet number sits in the very bottom-right corner.
        private const double TitleRegionMinFx = 0.80;   // right 20% of the sheet width
        private const double SheetNumMinFx    = 0.85;
        private const double SheetNumMaxFy    = 0.12;   // bottom ~12% (PDF y is up from the bottom)
        private const double SheetNumMinH     = 16.0;   // the number is set large
        private const double TitleMinH        = 11.0;   // the title is larger than note text (~10)

        /// <summary>
        /// Read the sheet title from the page's title block (by position), or null if the page has no
        /// plan title there. Level comes from the title band; the match-line half comes from the sheet
        /// number suffix when present, else from the title text.
        /// </summary>
        public static SheetTitle? FromPage(VectorPageReader.PageContent? page)
        {
            if (page is null || page.Words.Count == 0) return null;
            double w = page.WidthPts, h = page.HeightPts;
            if (w <= 0 || h <= 0) return null;

            var rightEdge = page.Words.Where(t => t.Cx / w >= TitleRegionMinFx).ToList();
            if (rightEdge.Count == 0) return null;

            // Anchor on the largest "PLAN" on the right edge — the sheet title's PLAN, not a note's.
            VectorPageReader.TextToken anchor = default;
            double anchorH = 0;
            foreach (var t in rightEdge)
            {
                if (t.Height < TitleMinH) continue;
                if (!t.Text.StartsWith("PLAN", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Height > anchorH) { anchor = t; anchorH = t.Height; }
            }
            if (anchorH <= 0) return null;

            // The title line = the right-edge words sharing the anchor's baseline, left to right.
            double tol = Math.Max(anchorH * 0.8, 6.0);
            string titleLine = string.Join(" ", rightEdge
                .Where(t => Math.Abs(t.Cy - anchor.Cy) <= tol)
                .OrderBy(t => t.Cx)
                .Select(t => t.Text));

            var parsed = ParseTitleLine(titleLine);
            if (parsed is null) return null;

            // Zone priority: the sheet-number suffix ("-N"/"-S") is the most reliable match-line half;
            // then a "NORTH/SOUTH TOWER" label in the title-block subtitle (two-tower sets like 31065, whose
            // title line carries no half); then any half embedded in the title text itself.
            string? zone = SheetNumberZone(page, w, h)
                        ?? TitleBlockTowerZone(page, w, anchor.Cy, anchorH)
                        ?? parsed.Value.Zone;
            return new SheetTitle(parsed.Value.Level, zone, titleLine.Trim());
        }

        /// <summary>
        /// Parse an already-isolated title line into (level, zone), or null if it is not a plan title.
        /// Exposed for testing the pure text parse independently of page geometry.
        /// </summary>
        public static (string Level, string? Zone)? ParseTitleLine(string? titleLine)
        {
            if (string.IsNullOrWhiteSpace(titleLine)) return null;
            var m = TitleRx.Match(titleLine);
            if (!m.Success) return null;
            string lvl = NormalizeLevel(m.Groups["lvl"].Value);
            string? zone = m.Groups["zone"].Success ? m.Groups["zone"].Value.ToUpperInvariant() : null;
            return (lvl, zone);
        }

        // The match-line half from the bottom-right sheet number's suffix ("S2.02-N" -> NORTH), or null.
        private static string? SheetNumberZone(VectorPageReader.PageContent page, double w, double h)
        {
            VectorPageReader.TextToken best = default;
            double bestH = 0;
            foreach (var t in page.Words)
            {
                if (t.Height < SheetNumMinH || t.Cx / w < SheetNumMinFx || t.Cy / h > SheetNumMaxFy) continue;
                var m = SheetNumRx.Match(t.Text);
                if (!m.Success || !m.Groups["half"].Success) continue;
                if (t.Height > bestH) { best = t; bestH = t.Height; }
            }
            if (bestH <= 0) return null;
            return SheetNumRx.Match(best.Text).Groups["half"].Value.ToUpperInvariant() switch
            {
                "N" => "NORTH",
                "S" => "SOUTH",
                "E" => "EAST",
                "W" => "WEST",
                _ => null,
            };
        }

        // The match-line tower from the sheet-title subtitle: "<CARDINAL> TOWER" anywhere in it.
        private static readonly Regex TowerZoneRx = new(@"\b(NORTH|SOUTH|EAST|WEST)\s+TOWER\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// The tower a two-tower set draws, from a "&lt;CARDINAL&gt; TOWER" label in the title-block subtitle
        /// (31065's "… - NORTH TOWER" / "… - SOUTH TOWER"), or null. The subtitle WRAPS in the title block —
        /// e.g. "LEVEL 2 PLAN - SLAB / REINFORCING - NORTH / TOWER" — so the cardinal and "TOWER" are on
        /// different baselines; we therefore read the whole subtitle in reading order (lines top→bottom,
        /// words left→right) and match the pair, rather than requiring a shared baseline. The window — the
        /// title region (right edge), title-size font, and only the few lines around the title anchor — keeps
        /// the key-plan and note "NORTH TOWER"/"SOUTH TOWER" mentions (smaller, further left) out, so it does
        /// not re-introduce the stray-label trap. Distinguishes the towers so their floors don't dedup together.
        /// </summary>
        private static string? TitleBlockTowerZone(VectorPageReader.PageContent page, double w, double anchorCy, double anchorH)
        {
            // The subtitle wraps up to ~4 lines below the title anchor ("LEVEL 2 PLAN - SLAB /
            // REINFORCING - NORTH / TOWER" spans ~3.8 line-heights on the real sheet); reach them, but stop
            // above the bottom-corner sheet number so it is never pulled in.
            double lo = anchorCy - 5.5 * anchorH, hi = anchorCy + 1.5 * anchorH;
            var sub = page.Words
                .Where(t => t.Cx / w >= TitleRegionMinFx && t.Height >= TitleMinH && t.Cy >= lo && t.Cy <= hi)
                .ToList();
            if (sub.Count == 0) return null;

            // Concatenate in reading order: lines top→bottom (Cy is up-from-bottom, so descending), then
            // words left→right within a line (grouped by a fraction of the line height).
            double lineH = Math.Max(anchorH * 0.6, 4.0);
            string text = string.Join(" ", sub
                .GroupBy(t => Math.Round(t.Cy / lineH))
                .OrderByDescending(g => g.Key)
                .SelectMany(g => g.OrderBy(t => t.Cx).Select(t => t.Text)));

            var m = TowerZoneRx.Match(text);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        // Canonicalise a level token: upper, single-spaced, ordinal suffix dropped ("2ND" -> "2"),
        // MEZZANINE -> MEZZ. Keeps "P1 MEZZ" distinct from "P1".
        private static string NormalizeLevel(string raw)
        {
            string s = Regex.Replace(raw.ToUpperInvariant().Trim(), @"\s+", " ");
            s = s.Replace("MEZZANINE", "MEZZ");
            s = Regex.Replace(s, @"^(\d{1,2})(?:ST|ND|RD|TH)\b", "$1");
            return s;
        }
    }
}
