#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Reconstructs a SHEAR WALL SCHEDULE grid from the native vector tokens read by
    /// <see cref="VectorPageReader"/>. A schedule is a 2-D table: the vertical axis is the level
    /// ladder (LEVEL 20 … P7), the horizontal axis is the element marks (W1…W5, Z1…), and the cells
    /// state a thickness over a band of levels. This turns positioned text into structure
    /// deterministically — no OCR, no per-drawing rules. It is a first pass: it recovers the level
    /// ladder and the thickness cells (each resolved to its level row); mark-column binding and
    /// band-spanning build on top of these axes.
    /// </summary>
    public static class ScheduleGridReader
    {
        /// <summary>A level row on the schedule's vertical axis: its label and y-centre (PDF points).</summary>
        public readonly record struct LevelRow(string RawLabel, string Normalized, double Y);

        /// <summary>A thickness cell: the value in inches and where it sits, with its resolved level row.</summary>
        public readonly record struct ThicknessCell(double ThicknessIn, double X, double Y, string Level);

        // 1–2 digits then at most two non-alphanumeric chars (the inch mark, whatever glyph it is).
        // Matches 30", 6", 30 — rejects rebar tokens like "30-45M", "8-30M" (they carry letters).
        private static readonly Regex InchValue = new(@"^(\d{1,2})\s*[^0-9A-Za-z]{0,2}$", RegexOptions.Compiled);

        // A shear-wall mark header: W1..W5 (optionally an A suffix, e.g. W2A).
        private static readonly Regex WallMark = new(@"^W\d{1,2}A?$", RegexOptions.Compiled);

        /// <summary>
        /// Recover the ordered level ladder (top of sheet → bottom). The level labels run down a single
        /// near-constant x; we pick the x-column carrying the most "LEVEL" tokens (the schedule mirrors
        /// the ladder left and right, so the busiest column is the axis), pair each with its number, and
        /// sort by y descending so the first entry is the topmost level.
        /// </summary>
        public static IReadOnlyList<LevelRow> ReadLevelLadder(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var levelTokens = page.Words.Where(w => string.Equals(w.Text, "LEVEL", StringComparison.OrdinalIgnoreCase)).ToList();
            if (levelTokens.Count == 0) return Array.Empty<LevelRow>();

            // Busiest x-column (rounded to 5pt buckets) = the level axis. ThenBy(Key) makes the choice
            // deterministic when mirrored ladders tie on count.
            double axisX = levelTokens
                .GroupBy(t => Math.Round(t.Cx / 5.0) * 5.0)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First().Key;

            var rows = new List<LevelRow>();
            foreach (var lt in levelTokens.Where(t => Math.Abs(t.Cx - axisX) <= 12))
            {
                // The level value is the nearest token just to the right on the same baseline.
                var num = page.Words
                    .Where(w => Math.Abs(w.Cy - lt.Cy) <= 10 && w.Cx > lt.Cx && w.Cx - lt.Cx <= 90)
                    .OrderBy(w => w.Cx - lt.Cx)
                    .Select(w => (string?)w.Text)
                    .FirstOrDefault();

                string raw = num is null ? "LEVEL" : $"LEVEL {num}";
                rows.Add(new LevelRow(raw, ScheduleTakeoff.NormalizeLevel(raw), lt.Cy));
            }

            // Collapse duplicate tokens on the same physical row (round Y), order top→bottom, then
            // collapse any repeated semantic level (e.g. a vector-duplicated "LEVEL P1") so the ladder
            // is a unique level list — the topmost occurrence wins.
            return rows
                .GroupBy(r => Math.Round(r.Y))
                .Select(g => g.First())
                .OrderByDescending(r => r.Y)
                .GroupBy(r => r.Normalized)
                .Select(g => g.First())
                .OrderByDescending(r => r.Y)
                .ToList();
        }

        /// <summary>
        /// Recover thickness cells: a numeric/inch token immediately left of a "WALL" token (e.g. the
        /// 30 of «30" WALL»), tagged with the nearest level row by y. Only the level axis gives the row;
        /// the mark column is bound in a later pass.
        /// </summary>
        public static IReadOnlyList<ThicknessCell> ReadThicknessCells(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var ladder = ReadLevelLadder(page);
            var wallTokens = page.Words.Where(w => string.Equals(w.Text, "WALL", StringComparison.OrdinalIgnoreCase)).ToList();
            var cells = new List<ThicknessCell>();

            foreach (var wall in wallTokens)
            {
                // The thickness value is the nearest INCH-pattern token just left of WALL on the SAME
                // baseline. A tight dy (<=6) is essential: rebar-note tokens (e.g. the "@" of
                // «20M @ 8" VERT») sit a line below and would otherwise win as nearest-left.
                var left = page.Words
                    .Where(w => Math.Abs(w.Cy - wall.Cy) <= 6 && w.Cx < wall.Cx && wall.Cx - w.Cx <= 140
                                && InchValue.IsMatch((w.Text ?? "").Trim()))
                    .OrderBy(w => wall.Cx - w.Cx)
                    .FirstOrDefault();
                if (left.Text is null) continue;

                double thk = double.Parse(InchValue.Match(left.Text.Trim()).Groups[1].Value);
                if (thk < 4 || thk > 60) continue;   // sane wall-thickness window (inches)

                string level = ladder.Count == 0 ? "" :
                    ladder.OrderBy(r => Math.Abs(r.Y - wall.Cy)).First().Normalized;

                cells.Add(new ThicknessCell(thk, wall.Cx, wall.Cy, level));
            }

            return cells.OrderByDescending(c => c.Y).ToList();
        }

        /// <summary>
        /// Bind thickness cells to their wall mark (W1…W5) and fill them down into per-mark level bands —
        /// the <see cref="ScheduleTakeoff.WallBand"/> records the takeoff math consumes. A schedule
        /// states a mark's thickness at the rows where it changes; between changes the cell is merged, so
        /// each detected thickness applies from its level DOWN to the row just above the next change for
        /// that mark (the last runs to the bottom of the ladder). Marks are bound by nearest header
        /// column (columns are ~80pt apart, so a ≤40pt match is unambiguous); cells with no nearby W mark
        /// (other element groups) are left out, not mis-bound.
        /// </summary>
        public static IReadOnlyList<ScheduleTakeoff.WallBand> ReadWallBands(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var ladder = ReadLevelLadder(page);
            if (ladder.Count == 0) return Array.Empty<ScheduleTakeoff.WallBand>();
            var idxByLevel = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ladder.Count; i++) idxByLevel.TryAdd(ladder[i].Normalized, i);

            // Mark header row = the y-row carrying the most DISTINCT W marks (the schedule mirrors marks
            // top and bottom; pick the busiest), then map each mark to its column x.
            var markTokens = page.Words.Where(w => WallMark.IsMatch((w.Text ?? "").Trim())).ToList();
            if (markTokens.Count == 0) return Array.Empty<ScheduleTakeoff.WallBand>();

            var headerRow = markTokens
                .GroupBy(t => Math.Round(t.Cy / 6.0) * 6.0)
                .OrderByDescending(g => g.Select(t => t.Text.Trim().ToUpperInvariant()).Distinct().Count())
                .ThenByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First();
            var markX = headerRow
                .GroupBy(t => t.Text.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Average(t => t.Cx));

            // Bind each thickness cell to the nearest mark column (≤40pt); collect per-mark anchors as
            // (ladder index, thickness).
            var anchors = new Dictionary<string, List<(int Idx, double Thk)>>(StringComparer.Ordinal);
            foreach (var c in ReadThicknessCells(page))
            {
                if (string.IsNullOrEmpty(c.Level) || !idxByLevel.TryGetValue(c.Level, out int li)) continue;

                string? mark = null; double best = 40;
                foreach (var kv in markX)
                {
                    double d = Math.Abs(kv.Value - c.X);
                    if (d < best) { best = d; mark = kv.Key; }
                }
                if (mark is null) continue;

                if (!anchors.TryGetValue(mark, out var lst)) anchors[mark] = lst = new();
                lst.Add((li, c.ThicknessIn));
            }

            // Fill-down: one thickness per row (thickest wins a tie), then span each to the next change.
            var bands = new List<ScheduleTakeoff.WallBand>();
            foreach (var (mark, lst) in anchors)
            {
                var ordered = lst
                    .GroupBy(a => a.Idx)
                    .Select(g => (Idx: g.Key, Thk: g.Max(x => x.Thk)))
                    .OrderBy(a => a.Idx)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    int topIdx = ordered[i].Idx;
                    int botIdx = (i + 1 < ordered.Count) ? ordered[i + 1].Idx - 1 : ladder.Count - 1;
                    if (botIdx < topIdx) botIdx = topIdx;
                    bands.Add(new ScheduleTakeoff.WallBand(
                        mark, ladder[topIdx].Normalized, ladder[botIdx].Normalized, ordered[i].Thk));
                }
            }

            return bands.OrderBy(b => b.Mark, StringComparer.Ordinal).ThenBy(b => b.LevelTop).ToList();
        }
    }
}
