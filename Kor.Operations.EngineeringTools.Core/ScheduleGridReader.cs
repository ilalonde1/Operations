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

        // A column mark header: C1, C4B, PC1, ZC2 — letters then digits, optional letter suffix.
        private static readonly Regex ColMark = new(@"^[A-Z]{1,3}\d{1,2}[A-Z]?$", RegexOptions.Compiled);

        // Column SIZE cell glued into one token ("500x900"); the spaced form ("500 x 900") is
        // assembled from an x-token and its numeric neighbours. Millimetres; guarded to real columns.
        private static readonly Regex GluedSize = new(@"^(\d{3,4})\s*[xX×]\s*(\d{3,4})$", RegexOptions.Compiled);
        private const int ColDimMinMm = 200, ColDimMaxMm = 2000;

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
        /// DETERMINISTIC column-schedule read — the ladder-format convention (marks across the header,
        /// level ladder vertical, merged "W x D" size cells spanning level bands), which is how tower
        /// column schedules are drawn. Returns one <see cref="ScheduleTakeoff.ColumnBand"/> per
        /// (mark, ladder level), sizes filled DOWN from each stated cell to the next change — the same
        /// semantics the schedule itself means. This replaces the vision read for the priced number:
        /// the table is vector text, and text does not vary run to run. A page that is not a
        /// ladder-format column schedule yields no bands (callers fall back).
        /// </summary>
        public static IReadOnlyList<ScheduleTakeoff.ColumnBand> ReadColumnBands(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);
            var ladder = ReadLevelLadder(page);
            if (ladder.Count < 3) return Array.Empty<ScheduleTakeoff.ColumnBand>();

            // Mark header row: the y-row carrying the most distinct mark-shaped tokens. LEVEL-row words
            // and pure numbers never match ColMark, so the ladder cannot be picked.
            var markTokens = page.Words.Where(w => ColMark.IsMatch((w.Text ?? "").Trim())
                                                && !w.Text.StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase)).ToList();
            if (markTokens.Count == 0) return Array.Empty<ScheduleTakeoff.ColumnBand>();
            var headerRow = markTokens
                .GroupBy(t => Math.Round(t.Cy / 6.0) * 6.0)
                .OrderByDescending(g => g.Select(t => t.Text.Trim().ToUpperInvariant()).Distinct().Count())
                .ThenByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First();
            if (headerRow.Select(t => t.Text.Trim().ToUpperInvariant()).Distinct().Count() < 3)
                return Array.Empty<ScheduleTakeoff.ColumnBand>();   // a real schedule has several marks
            var markX = headerRow
                .GroupBy(t => t.Text.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Average(t => t.Cx));

            // SIZE cells: glued "500x900" tokens, plus "500 x 900" assembled around an x-token.
            var cells = new List<(double X, double Y, double Wmm, double Dmm)>();
            foreach (var w in page.Words)
            {
                var g = GluedSize.Match((w.Text ?? "").Trim());
                if (g.Success) { AddSize(cells, w.Cx, w.Cy, g.Groups[1].Value, g.Groups[2].Value); continue; }
                if (w.Text is not ("x" or "X" or "×")) continue;
                var left = Nearest(page, w, -1); var right = Nearest(page, w, +1);
                if (left is { } l && right is { } r) AddSize(cells, w.Cx, w.Cy, l, r);
            }
            if (cells.Count == 0) return Array.Empty<ScheduleTakeoff.ColumnBand>();

            // Bind each size cell to its mark column (≤40pt) and level row; fill down to the next change.
            var idxOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ladder.Count; i++) idxOf.TryAdd(ladder[i].Normalized, i);
            var anchors = new Dictionary<string, List<(int Idx, double Wmm, double Dmm)>>(StringComparer.Ordinal);
            foreach (var c in cells)
            {
                string? mark = null; double best = 40;
                foreach (var kv in markX)
                {
                    double d = Math.Abs(kv.Value - c.X);
                    if (d < best) { best = d; mark = kv.Key; }
                }
                if (mark is null || ladder.Count == 0) continue;
                var row = ladder.OrderBy(r => Math.Abs(r.Y - c.Y)).First();
                if (Math.Abs(row.Y - c.Y) > 60 || !idxOf.TryGetValue(row.Normalized, out int li)) continue;
                if (!anchors.TryGetValue(mark, out var lst)) anchors[mark] = lst = new();
                lst.Add((li, c.Wmm, c.Dmm));
            }

            var bands = new List<ScheduleTakeoff.ColumnBand>();
            foreach (var (mark, lst) in anchors)
            {
                var ordered = lst.GroupBy(a => a.Idx)
                    .Select(g => (Idx: g.Key, Wmm: g.Max(x => x.Wmm), Dmm: g.Max(x => x.Dmm)))
                    .OrderBy(a => a.Idx).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    int top = ordered[i].Idx;
                    int bot = i + 1 < ordered.Count ? ordered[i + 1].Idx - 1 : ladder.Count - 1;
                    if (bot < top) bot = top;
                    for (int li = top; li <= bot; li++)
                        bands.Add(new ScheduleTakeoff.ColumnBand(
                            mark, ladder[li].Normalized, ladder[li].Normalized,
                            ordered[i].Wmm / 25.4, ordered[i].Dmm / 25.4));
                }
            }
            return bands;

            static void AddSize(List<(double, double, double, double)> cells, double x, double y, string a, string b)
            {
                if (!int.TryParse(a.Replace(",", ""), out int w) || !int.TryParse(b.Replace(",", ""), out int d)) return;
                if (w < ColDimMinMm || w > ColDimMaxMm || d < ColDimMinMm || d > ColDimMaxMm) return;
                cells.Add((x, y, w, d));
            }
            static string? Nearest(VectorPageReader.PageContent page, VectorPageReader.TextToken w, int dir)
            {
                VectorPageReader.TextToken best = default; double bestDx = 30; bool found = false;
                foreach (var t in page.Words)
                {
                    double dx = (t.Cx - w.Cx) * dir;
                    if (dx <= 0 || dx > bestDx || Math.Abs(t.Cy - w.Cy) > 6) continue;
                    bestDx = dx; best = t; found = true;
                }
                return found ? best.Text.Trim() : null;
            }
        }

        /// <summary>
        /// Count each mark's KEY-PLAN placements: mark-shaped words outside the schedule grid (header
        /// marks + ladder rows region). One placement per drawn column, same convention as footing
        /// marks. Marks absent from the key plan return no entry (callers default to 1).
        /// </summary>
        public static Dictionary<string, int> CountColumnMarks(
            VectorPageReader.PageContent page, IReadOnlyCollection<string> marks)
        {
            ArgumentNullException.ThrowIfNull(page);
            var set = new HashSet<string>(marks, StringComparer.OrdinalIgnoreCase);
            var tokens = page.Words.Where(w => set.Contains((w.Text ?? "").Trim())).ToList();
            if (tokens.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

            // The schedule grid re-states every mark in its header (top and often bottom of each grid
            // column). The GRID region is the marks' x-range ∩ the ladder's y-span; the key plan sits
            // BESIDE or BELOW that box, so its mark labels — one per drawn column — survive. Excluding
            // the whole ladder band would swallow a key plan drawn beside the grid.
            var ladder = ReadLevelLadder(page);
            double yLo = ladder.Count > 0 ? ladder.Min(r => r.Y) - 80 : double.MaxValue;
            double yHi = ladder.Count > 0 ? ladder.Max(r => r.Y) + 80 : double.MinValue;
            // Grid x-range from the header row (the busiest distinct-marks row), same rule as the reader.
            var headerRow = tokens
                .GroupBy(t => Math.Round(t.Cy / 6.0) * 6.0)
                .OrderByDescending(g => g.Select(t => t.Text.Trim().ToUpperInvariant()).Distinct().Count())
                .ThenByDescending(g => g.Count())
                .First();
            double xLo = headerRow.Min(t => t.Cx) - 60, xHi = headerRow.Max(t => t.Cx) + 60;

            var outside = tokens.Where(t => !(ladder.Count > 0
                    && t.Cy >= yLo && t.Cy <= yHi
                    && t.Cx >= xLo && t.Cx <= xHi)).ToList();
            if (outside.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

            // The key plan is one tight cluster of mark labels; stray mentions (detail bubbles, notes)
            // sit elsewhere. Single-linkage cluster the outside tokens (≤150pt gap) and count only the
            // LARGEST cluster — the key plan itself.
            var cluster = new int[outside.Count];
            for (int i = 0; i < outside.Count; i++) cluster[i] = i;
            int Find(int a) { while (cluster[a] != a) { cluster[a] = cluster[cluster[a]]; a = cluster[a]; } return a; }
            for (int i = 0; i < outside.Count; i++)
                for (int j = i + 1; j < outside.Count; j++)
                    if (Math.Abs(outside[i].Cx - outside[j].Cx) <= 150 && Math.Abs(outside[i].Cy - outside[j].Cy) <= 150)
                    { int ri = Find(i), rj = Find(j); if (ri != rj) cluster[ri] = rj; }
            var byRoot = Enumerable.Range(0, outside.Count).GroupBy(Find).OrderByDescending(g => g.Count()).First();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in byRoot)
            {
                string k = outside[i].Text.Trim().ToUpperInvariant();
                counts[k] = counts.GetValueOrDefault(k) + 1;
            }
            return counts;
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
