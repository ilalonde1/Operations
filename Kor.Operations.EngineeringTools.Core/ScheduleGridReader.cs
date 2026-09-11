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

        /// <summary>One flat mark-row shear-wall schedule entry: mark, thickness, strength and notes.</summary>
        public sealed record FlatWallScheduleRow(
            string Mark,
            double ThicknessIn,
            double? StrengthMPa,
            string RowText,
            MarkRowScheduleReader.MarkRoute Route = MarkRowScheduleReader.MarkRoute.ScheduleColumn);

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
        /// <summary>A level's value: a number, P2, L0/P1, 1M, 1A — not a word that follows the level on the next line.</summary>
        private static readonly Regex LevelShaped = new(@"^[A-Z]?\d{1,3}[A-Z]?(?:/[A-Z0-9]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>The level-shaped start of a token a tag was glued onto: the 12 of "12-TN".</summary>
        private static readonly Regex LevelLeading = new(@"^([A-Z]?\d{1,3}[A-Z]?)[-–]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

            return LadderAt(page, levelTokens.Where(t => Math.Abs(t.Cx - axisX) <= LadderColumnPts).ToList());
        }

        /// <summary>A level label with a building in front of it: "B-LEVEL", "A-LEVEL". The letters are the building.</summary>
        private static readonly Regex BuildingLevelToken = new(@"^([A-Z]{1,2})-(LEVEL|LVL|LEV)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// THE WORDS A DRAWING NAMES A LEVEL WITH (intake step 30). KOR's own elevations write
        /// "LEVEL 5"; the architect's set for 31170 (Vectorworks, 2026-09-10) writes "Top of Slab-L5"
        /// down every section and elevation, with the geodetic height in feet and metres under it —
        /// the cleanest storey ladder yet seen, and this reader found none of it because it looked
        /// for the one word LEVEL. Every new practice will bring its own phrase, so the phrase is a
        /// vocabulary: these are the compiled defaults, true of drawings generally, and the
        /// KorStandards row <c>dxf.level.label-words</c> extends them without a build.
        ///
        /// A phrase is matched against the words to a token's LEFT on its own baseline, so
        /// "Top of Slab" is found from its last word; and the value may be glued to the phrase with a
        /// dash ("Slab-L5" is the phrase TOP OF SLAB and the level L5).
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultLevelLabelWords =
        [
            "LEVEL", "LVL", "LEV",
            "TOP OF SLAB", "T.O. SLAB", "T.O.SLAB", "T/O SLAB", "TO SLAB", "T.O.S.", "TOS",
            "TOP OF CONCRETE", "T.O. CONCRETE", "T/O CONCRETE", "T.O.C.",
            "FIN. FLOOR", "FINISHED FLOOR", "F.F.L.", "FFL",
        ];

        /// <summary>
        /// A LEVEL MAY BE NAMED BY A WORD ALONE (intake step 41). "LEVEL 19" is a label and a value;
        /// "ROOF", "HIGH ROOF", "PENTHOUSE", "ELEVATOR ROOF" are a level's whole name — a ladder row
        /// with nothing to its right but the elevation. The KOR sets' sections name their roof levels
        /// this way (31065: ROOF LEVEL, ELEVATOR ROOF; 31202: ROOF, HIGH ROOF, LOW ROOF, PENTHOUSE;
        /// 31138: ROOF), and the ladder reader, wanting a value after a label, read none of them —
        /// so every roof plan landed on the top numbered storey, stacked on its plate. These are the
        /// compiled defaults; the KorStandards row <c>dxf.level.name-words</c> extends them.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultLevelNameWords =
        [
            "ROOF", "ROOF LEVEL", "MAIN ROOF", "HIGH ROOF", "LOW ROOF", "UPPER ROOF", "LOWER ROOF", "MECH ROOF", "MECH. ROOF",
            "ELEVATOR ROOF", "ELEV ROOF", "ELEV. ROOF", "PENTHOUSE", "PENTHOUSE ROOF", "T/O PARAPET", "TOP OF PARAPET",
        ];

        /// <summary>Words on one baseline closer than this are one phrase (a space, not a column gap).</summary>
        private const double PhraseGapPts = 14.0;

        /// <summary>
        /// The level label a token ends, if it ends one: the phrase from the vocabulary that the token
        /// and the words to its left on the same baseline spell, and the level value glued to it if any.
        /// Null when the token is not the end of a level phrase.
        /// </summary>
        internal static (string Phrase, string? GluedValue, string? Building)? LevelPhraseEndingAt(
            VectorPageReader.PageContent page, VectorPageReader.TextToken token, IReadOnlyList<string> labelWords)
            => LevelPhraseEndingAt(page, token, labelWords, DefaultLevelNameWords);

        /// <summary>As above, with the words a level may be named by alone (step 41): a hit on one of those carries the phrase itself as the value.</summary>
        internal static (string Phrase, string? GluedValue, string? Building)? LevelPhraseEndingAt(
            VectorPageReader.PageContent page, VectorPageReader.TextToken token, IReadOnlyList<string> labelWords, IReadOnlyList<string> nameWords)
        {
            // the words to the left on this baseline, nearest first, each within a space of the one
            // after it — a phrase is a chain of neighbours, not everything within reach of its last word
            var left = new List<VectorPageReader.TextToken>();
            double edge = token.MinX;
            foreach (var w in page.Words
                         .Where(w => Math.Abs(w.Cy - token.Cy) <= 3 && w.MaxX <= token.MinX + 1)
                         .OrderByDescending(w => w.MaxX))
            {
                if (edge - w.MaxX > PhraseGapPts) break;
                left.Add(w);
                edge = w.MinX;
                if (left.Count == 3) break;
            }

            // split a glued value off the token: "Slab-L5" → "Slab", "L5"; "LEVEL-3" → "LEVEL", "3"
            string last = token.Text.Trim();
            string? glued = null;
            int dash = last.LastIndexOfAny(['-', '–', ':']);
            if (dash > 0 && dash < last.Length - 1 && LevelShaped.IsMatch(last[(dash + 1)..]))
            {
                glued = last[(dash + 1)..];
                last = last[..dash];
            }

            // the token with three, two, one, no words to its left — the LONGEST phrase first, so "ROOF
            // LEVEL" is the level named ROOF LEVEL before "LEVEL" is a label wanting a value (step 41)
            var chain = new List<string> { last };
            foreach (var w in left) chain.Insert(0, w.Text.Trim());
            for (int take = left.Count; take >= 0; take--)
            {
                var words = chain.Skip(left.Count - take).ToList();
                string phrase = Regex.Replace(string.Join(" ", words), @"\s+", " ").Trim();
                string bare = phrase;
                string? building = null;
                var b = Regex.Match(phrase, @"^([A-Z]{1,2})-(.+)$", RegexOptions.IgnoreCase);
                if (b.Success) { building = b.Groups[1].Value.ToUpperInvariant(); bare = b.Groups[2].Value; }
                // a name only where NO level value follows on the baseline: "ROOF LEVEL 3" is LEVEL 3 with a word
                // in front, not the level ROOF LEVEL (Codex audit 2026-09-11, F12)
                bool valueFollows = page.Words.Any(w => Math.Abs(w.Cy - token.Cy) <= 4 && w.MinX >= token.MaxX - 1 && w.MinX - token.MaxX <= PhraseGapPts && LevelShaped.IsMatch(w.Text.Trim()));
                foreach (var nw in nameWords)
                    if (glued is null && !valueFollows && string.Equals(bare, nw, StringComparison.OrdinalIgnoreCase))
                        return (nw.ToUpperInvariant(), nw.ToUpperInvariant(), building);      // the name is the value
                foreach (var lw in labelWords)
                    if (string.Equals(bare, lw, StringComparison.OrdinalIgnoreCase))
                        return (lw.ToUpperInvariant(), glued, building);
            }
            return null;
        }

        /// <summary>Level labels within this of one x are one column of the ladder, in points.</summary>
        public const double LadderColumnPts = 12.0;

        /// <summary>
        /// EVERY ladder on the sheet, one per column of level labels (intake step 25). An elevation
        /// taller than its sheet is drawn in strips side by side, each strip a column of level
        /// labels with its own level lines; and above the storeys the buildings share, each tower's
        /// levels are labelled for it — 31168's S3.12 carries LEVEL 2–19 in one column and B-LEVEL
        /// 27–41 in others. The busiest column alone (<see cref="ReadLevelLadder"/>) read one strip
        /// and merged the others' labels into its rows by y, so B-LEVEL 37 sat on LEVEL 16's row and
        /// was lost, and the towers above L19 had no storey to land on. A column with fewer labels
        /// than a ladder needs is a caption, not a strip.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<LevelRow>> ReadLevelLadders(VectorPageReader.PageContent page, int minRows = 3)
            => ReadLevelLadders(page, minRows, DefaultLevelLabelWords);

        /// <summary>As above, with the words a level is named by (step 30): the compiled defaults, or the KorStandards row.</summary>
        public static IReadOnlyList<IReadOnlyList<LevelRow>> ReadLevelLadders(VectorPageReader.PageContent page, int minRows, IReadOnlyList<string> labelWords)
            => ReadLevelLadders(page, minRows, labelWords, DefaultLevelNameWords);

        /// <summary>As above, with the words a level may be named by alone (step 41): the compiled defaults, or the KorStandards row <c>dxf.level.name-words</c>.</summary>
        public static IReadOnlyList<IReadOnlyList<LevelRow>> ReadLevelLadders(VectorPageReader.PageContent page, int minRows, IReadOnlyList<string> labelWords, IReadOnlyList<string> nameWords)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(labelWords);
            ArgumentNullException.ThrowIfNull(nameWords);

            // a level label is any word that ENDS a phrase from the vocabulary: LEVEL, B-LEVEL, the
            // "Slab" of "Top of Slab-L5". The label's position is that last word's.
            var levelTokens = new List<LevelLabel>();
            foreach (var w in page.Words)
            {
                var hit = LevelPhraseEndingAt(page, w, labelWords, nameWords);
                if (hit is null) continue;
                levelTokens.Add(new LevelLabel(w, hit.Value.GluedValue, hit.Value.Building));
            }
            // A NAME WRITTEN ON TWO LINES IS ONE NAME (step 41): "PENTHOUSE" over "ROOF" at the same x,
            // a line apart, is the level PENTHOUSE ROOF, not two levels 300 mm apart (31202's sections).
            // A name label with a name label directly above it, within a line and a half, takes the
            // upper one's words in front of its own and stands at its own line.
            var stacked = new HashSet<int>();
            for (int i = 0; i < levelTokens.Count; i++)
            {
                var lower = levelTokens[i];
                if (lower.GluedValue is null || !nameWords.Contains(lower.GluedValue, StringComparer.OrdinalIgnoreCase)) continue;
                for (int j = 0; j < levelTokens.Count; j++)
                {
                    if (j == i || stacked.Contains(j)) continue;
                    var upper = levelTokens[j];
                    if (upper.GluedValue is null || !nameWords.Contains(upper.GluedValue, StringComparer.OrdinalIgnoreCase)) continue;
                    double lineHeight = Math.Max(upper.Token.MaxY - upper.Token.MinY, 4);
                    double above = upper.Token.Cy - lower.Token.Cy;      // PdfPig's y runs up the page
                    if (Math.Abs(upper.Token.MinX - lower.Token.MinX) > 3 * lineHeight || above <= 0 || above > 1.5 * lineHeight) continue;
                    string joined = $"{upper.GluedValue} {lower.GluedValue}";
                    if (!nameWords.Contains(joined, StringComparer.OrdinalIgnoreCase)) continue;
                    levelTokens[i] = lower with { GluedValue = joined };
                    stacked.Add(j);
                    break;
                }
            }
            levelTokens = levelTokens.Where((_, k) => !stacked.Contains(k)).OrderBy(l => l.Token.Cx).ToList();
            if (levelTokens.Count == 0) return Array.Empty<IReadOnlyList<LevelRow>>();

            // columns: labels within LadderColumnPts of the column's first label, left to right
            var columns = new List<List<LevelLabel>>();
            foreach (var t in levelTokens)
            {
                if (columns.Count > 0 && Math.Abs(t.Token.Cx - columns[^1][0].Token.Cx) <= LadderColumnPts) columns[^1].Add(t);
                else columns.Add(new List<LevelLabel> { t });
            }

            var ladders = new List<IReadOnlyList<LevelRow>>();
            foreach (var column in columns)
            {
                var rows = LadderAt(page, column, nameWords);
                if (rows.Count >= minRows) ladders.Add(rows);
            }
            return ladders;
        }

        /// <summary>A level label as found: the word that ends its phrase, a value glued to it, and the building in front of it.</summary>
        internal readonly record struct LevelLabel(VectorPageReader.TextToken Token, string? GluedValue, string? Building);

        /// <summary>The ladder one column of level labels makes: each label paired with its level, one row per line, top to bottom.</summary>
        private static List<LevelRow> LadderAt(VectorPageReader.PageContent page, IReadOnlyList<VectorPageReader.TextToken> labels)
            => LadderAt(page, labels.Select(t => new LevelLabel(t, null, BuildingLevelToken.Match(t.Text.Trim()) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null)).ToList(), DefaultLevelNameWords);

        private static List<LevelRow> LadderAt(VectorPageReader.PageContent page, IReadOnlyList<LevelLabel> labels, IReadOnlyList<string> nameWords)
        {
            var rows = new List<LevelRow>();
            foreach (var label in labels)
            {
                var lt = label.Token;
                // a value glued to the phrase is the level: "Top of Slab-L5" needs no word to its right
                if (label.GluedValue is string gluedValue)
                {
                    string gluedPrefix = label.Building is null ? "" : label.Building + "-";
                    // a level named by a word alone (step 41) is that word: ROOF, not LEVEL ROOF
                    string gluedRaw = nameWords.Any(nw => string.Equals(nw, gluedValue, StringComparison.OrdinalIgnoreCase))
                        ? $"{gluedPrefix}{gluedValue}"
                        : $"{gluedPrefix}LEVEL {gluedValue}";
                    rows.Add(new LevelRow(gluedRaw, ScheduleTakeoff.NormalizeLevel(gluedRaw), lt.Cy));
                    continue;
                }
                // The level value is the token just to the right: on the label's own baseline before the
                // line wrapped under it, a level-shaped token (22, P2, L0/P1, 1M) before a word, then the
                // nearest. Nearest alone read "LEVEL 1 - CONCRETE" as a level named CONCRETE and
                // "LEVEL 22 / MECH." as MECH. (2026-09-08, 31130 p53 and 31138 p53).
                var num = page.Words
                    .Where(w => Math.Abs(w.Cy - lt.Cy) <= 10 && w.Cx > lt.Cx && w.Cx - lt.Cx <= 90)
                    .OrderBy(w => Math.Abs(w.Cy - lt.Cy) <= 4 ? 0 : 1)
                    .ThenBy(w => LevelShaped.IsMatch(w.Text.Trim()) ? 0 : 1)
                    .ThenBy(w => w.Cx - lt.Cx)
                    .Select(w => (string?)w.Text)
                    .FirstOrDefault();

                // a label "B-LEVEL" names building B's level: the building rides with the name
                string prefix = label.Building is null ? "" : label.Building + "-";
                // and the level is the level-shaped start of its token: "12-TN" on 31065's south
                // tower elevations is level 12 with the view's tag glued on, not a level named 12-TN
                if (num is not null && !LevelShaped.IsMatch(num.Trim()))
                {
                    var leading = LevelLeading.Match(num.Trim());
                    if (leading.Success) num = leading.Groups[1].Value;
                }
                string raw = num is null ? $"{prefix}LEVEL" : $"{prefix}LEVEL {num}";
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
                                                && !(w.Text ?? "").StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase)).ToList();
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

            // The key plan region is ANCHORED by its own title — a "KEY" word with "PLAN" beside it —
            // which is the drawing convention, not a guess. Cluster the outside mark tokens
            // (single-linkage, ≤200pt gaps) and count the cluster NEAREST a KEY-PLAN anchor; with no
            // anchor on the page, fall back to the largest cluster. Stray mentions in details and
            // notes live in other clusters and never count.
            var anchors = new List<(double X, double Y)>();
            foreach (var w in page.Words)
            {
                if (!w.Text.Equals("KEY", StringComparison.OrdinalIgnoreCase)) continue;
                if (page.Words.Any(p2 => p2.Text.StartsWith("PLAN", StringComparison.OrdinalIgnoreCase)
                                      && Math.Abs(p2.Cy - w.Cy) <= 8 && Math.Abs(p2.Cx - w.Cx) <= 90))
                    anchors.Add((w.Cx, w.Cy));
            }

            var cluster = new int[outside.Count];
            for (int i = 0; i < outside.Count; i++) cluster[i] = i;
            int Find(int a) { while (cluster[a] != a) { cluster[a] = cluster[cluster[a]]; a = cluster[a]; } return a; }
            for (int i = 0; i < outside.Count; i++)
                for (int j = i + 1; j < outside.Count; j++)
                    if (Math.Abs(outside[i].Cx - outside[j].Cx) <= 200 && Math.Abs(outside[i].Cy - outside[j].Cy) <= 200)
                    { int ri = Find(i), rj = Find(j); if (ri != rj) cluster[ri] = rj; }

            var groups = Enumerable.Range(0, outside.Count).GroupBy(Find).ToList();
            IGrouping<int, int> chosen;
            if (anchors.Count > 0)
                chosen = groups.OrderBy(g => g.Min(i => anchors.Min(a =>
                             Math.Max(Math.Abs(outside[i].Cx - a.X), Math.Abs(outside[i].Cy - a.Y)))))
                         .First();
            else
                chosen = groups.OrderByDescending(g => g.Count()).First();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in chosen)
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

        /// <summary>
        /// Reads a non-ladder shear-wall schedule shaped as one mark row per wall type:
        /// MARK | THICKNESS | STRENGTH | REINFORCING. The level-banded grid reader above still handles
        /// tower schedules; this handles podium tables such as SWA | 12" | 35 MPa | 15M @ 14".
        /// </summary>
        public static IReadOnlyList<FlatWallScheduleRow> ReadFlatWallRows(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var rows = new List<FlatWallScheduleRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in MarkRowScheduleReader.ReadSchedule(page, MarkRowScheduleReader.ShearWallDefaults()))
            {
                if (row.SingleLengthMm is not double thicknessMm) continue;
                if (!seen.Add(row.Mark)) continue;

                rows.Add(new FlatWallScheduleRow(
                    row.Mark,
                    thicknessMm / PrintedLength.MmPerInch,
                    row.StrengthMPa,
                    row.RowText,
                    row.Route));
            }

            return rows;
        }
    }
}
