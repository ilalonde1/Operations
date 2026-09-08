#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Reads schedules whose stable convention is a mark column under a schedule heading.
    /// Cells are identified by what they contain, not by their ordinal position in the row.
    /// </summary>
    public static class MarkRowScheduleReader
    {
        // A strength as a schedule prints it: "45 MPa", or "7.0 ksi" on a set drawn in US units
        // (31202). The unit is on the sheet, so it is read, not assumed; ksi is returned as MPa.
        private static readonly Regex StrengthRe = new(
            @"(?<v>\d{1,3}(?:\.\d+)?)\s*(?<u>MPa|ksi)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const double MPaPerKsi = 6.894757;

        private static double? StrengthMPa(string rowText)
        {
            var s = StrengthRe.Match(rowText);
            if (!s.Success) return null;
            double v = double.Parse(s.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return s.Groups["u"].Value.Equals("ksi", StringComparison.OrdinalIgnoreCase) ? v * MPaPerKsi : v;
        }

        // A size cell that says the size is on the plan: "<varies> x <varies>" (31168's C03-B), or
        // VARIES on its own. The row is a real mark with no size, not a row that failed to parse.
        private static readonly Regex VariesRe = new(
            @"\bVARIES\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public sealed record Options(
            string RulePrefix,
            IReadOnlyList<string> HeadingWords,
            IReadOnlyList<string> MarkPatterns,
            IReadOnlyList<string> RequiredRowWords,
            double RowWidthPts,
            double HeadingBandFraction,
            double HeadingSearchWidthPts,
            double MarkColumnTolerancePts,
            double MinDimensionMm,
            double MaxDimensionMm,
            bool RequireDimensionPair = true)
        {
            /// <summary>
            /// Whether a row of THIS schedule states a size (a x b), or a single length.
            /// </summary>
            /// <remarks>
            /// ⚠ THIS IS WHAT KEEPS PLAN LABELS OUT OF A TABLE. The same marks the schedule declares
            /// are printed all over the drawing, and a mark on the plan usually has SOMETHING
            /// length-shaped on its baseline — a dimension string, a grid callout. Accepting a single
            /// length lets one of those become a candidate.
            ///
            /// That is not hypothetical: on 31138 page 9 the plan label SF1 sits at x=1916 and the
            /// schedule's mark column at x=1915 — the same 15pt bucket. Ownership is settled from the
            /// column's TOPMOST row, SF1 is far above the COLUMN SCHEDULE heading, so it anchored the
            /// column, owned no column schedule, and all ELEVEN schedule rows were dropped. The verb
            /// reported "0 marks", which reads exactly like a sheet with no column schedule.
            ///
            /// A column or footing row always states two or three dimensions, so requiring a pair
            /// costs nothing and excludes plan labels almost entirely. A flat shear-wall row states
            /// one thickness, so it must set this false and accept the weaker filter.
            /// </remarks>
            public bool RequireDimensionPair { get; init; } = RequireDimensionPair;

            public IReadOnlyList<string> SettingKeys =>
            [
                $"{RulePrefix}.heading-words",
                $"{RulePrefix}.mark-patterns",
                $"{RulePrefix}.required-row-words",
                $"{RulePrefix}.row-width-pts",
                $"{RulePrefix}.heading-band-fraction",
                $"{RulePrefix}.heading-search-width-pts",
                $"{RulePrefix}.mark-column-tolerance-pts",
                $"{RulePrefix}.min-dimension-mm",
                $"{RulePrefix}.max-dimension-mm",
                $"{RulePrefix}.require-dimension-pair",
            ];
        }

        public readonly record struct ScheduleHeading(double X, double Y, bool IsTarget, string Title)
        {
            /// <summary>The title's left edge — where its table's top rule is looked for.</summary>
            public double TitleMinX { get; init; }

            /// <summary>The title's bottom edge (y-up: its smallest y); the top rule sits just under it.</summary>
            public double TitleMinY { get; init; }

            /// <summary>The title's right edge. Zero with the others when the heading was not read off a page.</summary>
            public double TitleMaxX { get; init; }

            /// <summary>The title's text height: the unit every reach around the table is measured in.</summary>
            public double TitleHeight { get; init; }
        }

        /// <summary>How a row's mark was identified, and — for the two schedule routes — what bounded its table.</summary>
        public enum MarkRoute
        {
            /// <summary>Read off the table's own mark column, rows bounded by a band around the heading.</summary>
            ScheduleColumn,

            /// <summary>Matched a mark pattern anywhere on the page, then attributed to a heading.</summary>
            PatternFallback,

            /// <summary>Read off the table's own mark column, rows being the cells inside the border the table draws.</summary>
            ScheduleBorder,
        }

        public sealed record Row(
            string Mark,
            string RowText,
            IReadOnlyList<double> DimensionsMm,
            double? SingleLengthMm,
            double? StrengthMPa,
            VectorPageReader.TextToken MarkToken,
            ScheduleHeading? Heading,
            IReadOnlyList<string> SettingKeys,
            MarkRoute Route,
            bool SizeVaries = false);

        public static Options ColumnDefaults() => new(
            "dxf.schedule.column",
            ["COLUMN"],
            // PC1, TC02, C4 — and the SUFFIXED forms KOR's own drawings use: C02-A, C03-B, PC03-A,
            // GC11-C are all declared on 31168 S2.02, and the unsuffixed pattern read six of that
            // sheet's ~15 marks. A suffix is how a practice distinguishes two columns of the same
            // number, so it is the norm, not an oddity.
            [@"^[A-Z]{1,3}\d{1,2}(?:-[A-Z0-9]{1,3})?$"],
            [],
            RowWidthPts: 340,
            HeadingBandFraction: 0.18,
            HeadingSearchWidthPts: 260,
            MarkColumnTolerancePts: 20,
            MinDimensionMm: 150,
            MaxDimensionMm: 3000);

        public static Options FootingDefaults() => new(
            "dxf.schedule.footing",
            ["FOUNDATION", "FOOTING"],
            // PC1, TC02, C4 — and the SUFFIXED forms KOR's own drawings use: C02-A, C03-B, PC03-A,
            // GC11-C are all declared on 31168 S2.02, and the unsuffixed pattern read six of that
            // sheet's ~15 marks. A suffix is how a practice distinguishes two columns of the same
            // number, so it is the norm, not an oddity.
            [@"^[A-Z]{1,3}\d{1,2}(?:-[A-Z0-9]{1,3})?$"],
            ["DEEP", "DP"],
            RowWidthPts: 320,
            HeadingBandFraction: 0.18,
            HeadingSearchWidthPts: 260,
            MarkColumnTolerancePts: 20,
            MinDimensionMm: 200,
            MaxDimensionMm: 6000);

        public static Options ShearWallDefaults() => new(
            "dxf.schedule.shear-wall",
            ["SHEAR WALL", "WALL"],
            [@"^SW[A-Z0-9]{0,2}$", @"^W\d{1,2}[A-Z]?$"],
            [],
            RowWidthPts: 360,
            HeadingBandFraction: 0.18,
            HeadingSearchWidthPts: 320,
            MarkColumnTolerancePts: 20,
            MinDimensionMm: 100,
            MaxDimensionMm: 1500,
            // a flat wall row states one thickness, not a size, so it must accept a lone length
            RequireDimensionPair: false);

        public static Options ApplyRules(
            Options options,
            IReadOnlyDictionary<string, RuleSetting> settings)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(settings);

            return options with
            {
                HeadingWords = settings.ListOr($"{options.RulePrefix}.heading-words", options.HeadingWords),
                MarkPatterns = settings.ListOr($"{options.RulePrefix}.mark-patterns", options.MarkPatterns),
                RequiredRowWords = settings.ListOr($"{options.RulePrefix}.required-row-words", options.RequiredRowWords),
                RowWidthPts = settings.ValueOr($"{options.RulePrefix}.row-width-pts", options.RowWidthPts),
                HeadingBandFraction = settings.ValueOr($"{options.RulePrefix}.heading-band-fraction", options.HeadingBandFraction),
                HeadingSearchWidthPts = settings.ValueOr($"{options.RulePrefix}.heading-search-width-pts", options.HeadingSearchWidthPts),
                MarkColumnTolerancePts = settings.ValueOr($"{options.RulePrefix}.mark-column-tolerance-pts", options.MarkColumnTolerancePts),
                MinDimensionMm = settings.ValueOr($"{options.RulePrefix}.min-dimension-mm", options.MinDimensionMm),
                MaxDimensionMm = settings.ValueOr($"{options.RulePrefix}.max-dimension-mm", options.MaxDimensionMm),
                RequireDimensionPair = settings.FlagOr($"{options.RulePrefix}.require-dimension-pair", options.RequireDimensionPair),
            };
        }

        public static IReadOnlyList<Row> ReadSchedule(
            VectorPageReader.PageContent page,
            Options options)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(options);

            var markPatterns = options.MarkPatterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();
            if (markPatterns.Count == 0) return Array.Empty<Row>();

            var headings = SchedulesOn(page, options);
            double band = page.WidthPts * options.HeadingBandFraction;
            var settingKeys = options.SettingKeys;

            // ⭐ A BORDERED TABLE IS READ FROM ITS BORDER, AND THAT IS THE ANSWER. The rows are its
            // cells, read literally, so there is nothing a pattern could add and one thing it could
            // do: re-admit a token from outside the table. Where no target heading draws a border,
            // BOTH remaining routes run and the one that reads more wins.
            //
            // The band and the pattern fail in opposite directions, so choosing one up front loses
            // whichever job the other suited. Reading marks off the table's own column is right when
            // the table is clean — 31168 went from 6 marks to 12, picking up C02-A, PC03-A and the
            // other suffixed forms. But the band anchors on the topmost row under a heading, and
            // where a sheet's rows wrap or a note sits above them it reads far too few: 31065 dropped
            // from 7 marks to 2, losing PC1..PC5 and taking its footing total from 1,174 cy to 855,
            // and 31138 fell from 10 to 7. That regression shipped because the commit that introduced
            // the structural route was verified with column counts measured one commit earlier.
            var targetHeadings = headings.Where(h => h.IsTarget).ToList();
            IReadOnlyList<Row> structural = targetHeadings.Count > 0
                ? ReadFromScheduleColumns(page, options, targetHeadings, band, settingKeys)
                : Array.Empty<Row>();
            if (structural.Any(r => r.Route == MarkRoute.ScheduleBorder))
                return structural;

            if (targetHeadings.Count == 0 && headings.Count > 0)
                return Array.Empty<Row>();

            var candidates = new List<Candidate>();
            foreach (var token in page.Words)
            {
                string mark = token.Text.Trim();
                if (!markPatterns.Any(p => p.IsMatch(mark))) continue;

                var cells = page.Words
                    .Where(w => Math.Abs(w.Cy - token.Cy) <= 6
                                && w.Cx > token.Cx
                                && w.Cx - token.Cx <= options.RowWidthPts)
                    .OrderBy(w => w.Cx)
                    .ToList();
                if (cells.Count == 0) continue;

                string rowText = string.Join(" ", cells.Select(w => w.Text)).Replace(",", "");
                if (!HasRequiredWord(rowText, options.RequiredRowWords)) continue;

                var dims = PrintedLength.TryFindSizeMm(rowText);
                if (dims is not null && !Plausible(dims, options)) dims = null;

                // A schedule whose rows state a size PAIR never accepts a lone length: that is the
                // filter that keeps plan labels out of the table. See Options.RequireDimensionPair.
                double? single = dims is null && !options.RequireDimensionPair
                    ? FirstPlausibleLength(cells.Select(c => c.Text).ToList(), options)
                    : null;
                if (dims is null && single is null) continue;

                candidates.Add(new Candidate(token, mark, rowText, dims ?? Array.Empty<double>(), single, StrengthMPa(rowText), null));
            }

            var owners = new Dictionary<double, ScheduleHeading?>();
            foreach (var column in candidates.GroupBy(c => ColumnKey(c.Token)))
            {
                var top = column.OrderByDescending(c => c.Token.Cy).First();
                owners[column.Key] = headings.Count == 0 ? null : OwnerOf(top.Token.Cx, top.Token.Cy, headings, band);
            }

            var rows = new List<Row>();
            foreach (var c in candidates)
            {
                var owner = owners[ColumnKey(c.Token)];
                if (headings.Count > 0 && owner is not { IsTarget: true }) continue;

                rows.Add(new Row(
                    c.Mark,
                    c.RowText,
                    c.DimensionsMm,
                    c.SingleLengthMm,
                    c.StrengthMPa,
                    c.Token,
                    owner,
                    settingKeys,
                    MarkRoute.PatternFallback,
                    c.SizeVaries));
            }

            // whichever route saw more of the table; ties go to the structural one, which knows
            // which heading each row sits under rather than inferring it
            return structural.Count >= rows.Count ? structural : rows;
        }

        private static IReadOnlyList<Row> ReadFromScheduleColumns(
            VectorPageReader.PageContent page,
            Options options,
            IReadOnlyList<ScheduleHeading> targetHeadings,
            double band,
            IReadOnlyList<string> settingKeys)
        {
            var rows = new List<Row>();
            var rules = ScheduleTableBorder.RulesOn(page);

            // ⭐ THE TABLE'S OWN BORDER FIRST. Where the schedule draws its rules — every FOUNDATION
            // and COLUMN table on four of the five KOR jobs — its rows are the cells between them,
            // nothing outside the border joins a row, and a wrapped cell's second line stays in its
            // row. The band is the fallback for a sheet that draws no border under any target
            // heading, and MarkRoute says which one read each row.
            //
            // ⚠ On a sheet whose tables ARE bordered, a target heading with no border under it is a
            // sentence, not a table: 31065 p15's notes say "4. IF NOTED IN THE COLUMN SCHEDULE", and
            // the band under that line read the strip footings SF1 and SF2 as columns.
            var borders = targetHeadings.Select(heading =>
            {
                bool titleRead = heading.TitleMaxX > heading.TitleMinX;
                return (Heading: heading, Border: ScheduleTableBorder.Under(
                    page,
                    titleRead ? heading.TitleMinX : heading.X,
                    titleRead ? heading.TitleMaxX : heading.X + 60,
                    titleRead ? heading.TitleMinY : heading.Y,
                    heading.TitleHeight,
                    rules));
            }).ToList();
            bool ruledSheet = borders.Any(b => b.Border is not null);

            foreach (var (heading, border) in borders)
            {
                if (border is not null)
                {
                    rows.AddRange(ReadRowsInBorder(page, options, heading, border, settingKeys));
                    continue;
                }
                if (ruledSheet) continue;

                var candidates = new List<Candidate>();
                foreach (var rowGroup in page.Words
                    .Where(w => w.Cy < heading.Y && Math.Abs(w.Cx - heading.X) <= band)
                    .GroupBy(w => Math.Round(w.Cy / 6.0) * 6.0)
                    .OrderByDescending(g => g.Key))
                {
                    var token = rowGroup.OrderBy(w => w.Cx).First();

                    // ⚠ NO SHAPE TEST HERE, DELIBERATELY. Reading the mark literally off the table's
                    // own column is the whole point of this route — it is what lets C02-A, PC03-A and
                    // GC11-C be read at all — and LocatedScheduleReadsLiteralFirstColumnMarksInstead
                    // OfGuessingTheirShape exists to defend that.
                    //
                    // Where a sheet's rows wrap, this band does pick up a continuation line's first
                    // token (31065 p14 yielded "8-35M" and "BOT." before its border was read), and
                    // it cannot tell a neighbouring table's cell from its own. That is why it is the
                    // fallback and the border is the route; filtering here would trade a real
                    // capability for a symptom.
                    if (TryCandidate(token, token.Text, BandCells(page, token, options), options, heading) is { } candidate)
                        candidates.Add(candidate);
                }

                if (candidates.Count == 0)
                    continue;

                var anchor = candidates.OrderByDescending(c => c.Token.Cy).First();
                double anchorLeft = anchor.Token.MinX;
                foreach (var c in candidates
                    .Where(c => Math.Abs(c.Token.MinX - anchorLeft) <= options.MarkColumnTolerancePts)
                    .OrderByDescending(c => c.Token.Cy))
                {
                    rows.Add(new Row(
                        c.Mark,
                        c.RowText,
                        c.DimensionsMm,
                        c.SingleLengthMm,
                        c.StrengthMPa,
                        c.Token,
                        heading,
                        settingKeys,
                        MarkRoute.ScheduleColumn,
                        c.SizeVaries));
                }
            }

            return rows;
        }

        /// <summary>
        /// The rows of one table, read from the cells inside the border it draws.
        /// </summary>
        /// <remarks>
        /// A row is the band between two rules crossing the mark column; its mark is the text of its
        /// first cell; the other cells follow in column order, each cell's lines top to bottom, so a
        /// wrapped reinforcing cell reads as one cell and never as a second row. A row whose first
        /// cell is empty has no mark and is not a row — 31168's FOUNDATION SCHEDULE is a placeholder
        /// table whose only text is one reinforcing note, and it reads as nothing, correctly.
        ///
        /// There is no anchor here. Every mark is in the first column by construction, so the
        /// topmost-candidate rule that dropped F1 on 31065 and SF2 on 31138 has nothing left to do.
        /// </remarks>
        private static IEnumerable<Row> ReadRowsInBorder(
            VectorPageReader.PageContent page,
            Options options,
            ScheduleHeading heading,
            ScheduleTableBorder.Border border,
            IReadOnlyList<string> settingKeys)
        {
            var inside = page.Words.Where(w => border.Contains(w.Cx, w.Cy)).ToList();
            double? markCellRight = border.ColumnRuleXs.Count > 0 ? border.ColumnRuleXs[0] : null;

            foreach (var (top, bottom) in border.RowBands())
            {
                // a band no column rule runs through is a merged full-width cell — the boxed NOTES
                // under the last row — and its first token is not a mark, whatever it says
                if (!border.IsRuledRow(top, bottom)) continue;

                var band = inside.Where(w => w.Cy < top && w.Cy > bottom).ToList();
                if (band.Count == 0) continue;

                var markCell = markCellRight is double right
                    ? band.Where(w => w.Cx < right).ToList()
                    : [band.OrderBy(w => w.MinX).ThenByDescending(w => w.Cy).First()];
                if (markCell.Count == 0) continue;

                foreach (var (mark, markToken, subTop, subBottom) in MarksIn(markCell, top, bottom))
                {
                    var cells = border.InReadingOrder(
                        band.Where(w => w.Cy < subTop && w.Cy > subBottom && !markCell.Contains(w)));

                    if (TryCandidate(markToken, mark, cells, options, heading) is { } c)
                    {
                        yield return new Row(
                            c.Mark,
                            c.RowText,
                            c.DimensionsMm,
                            c.SingleLengthMm,
                            c.StrengthMPa,
                            c.Token,
                            heading,
                            settingKeys,
                            MarkRoute.ScheduleBorder,
                            c.SizeVaries);
                    }
                }
            }
        }

        /// <summary>
        /// The marks in one row's first cell, each with the y-range it owns. One line is one mark; a
        /// line ending in a hyphen continues on the next (31168 wraps GC11-C as "GC11-" over "C");
        /// two lines that do not join are two marks sharing a cell — a separator the drafter left
        /// out — and the row is split between them.
        /// </summary>
        private static IEnumerable<(string Mark, VectorPageReader.TextToken Token, double Top, double Bottom)> MarksIn(
            IReadOnlyList<VectorPageReader.TextToken> markCell,
            double top,
            double bottom)
        {
            var lines = new List<List<VectorPageReader.TextToken>>();
            foreach (var t in markCell.OrderByDescending(t => t.Cy).ThenBy(t => t.MinX))
            {
                if (lines.Count > 0 && Math.Abs(lines[^1][0].Cy - t.Cy) <= 4) lines[^1].Add(t);
                else lines.Add([t]);
            }

            var marks = new List<(string Mark, VectorPageReader.TextToken Token)>();
            for (int i = 0; i < lines.Count; i++)
            {
                var first = lines[i].OrderBy(t => t.MinX).First();
                string text = first.Text.Trim();
                while (text.EndsWith('-') && i + 1 < lines.Count)
                {
                    i++;
                    text += lines[i].OrderBy(t => t.MinX).First().Text.Trim();
                }
                marks.Add((text, first));
            }

            if (marks.Count == 1)
            {
                yield return (marks[0].Mark, marks[0].Token, top, bottom);
                yield break;
            }

            for (int i = 0; i < marks.Count; i++)
            {
                double t = i == 0 ? top : (marks[i - 1].Token.Cy + marks[i].Token.Cy) / 2;
                double b = i == marks.Count - 1 ? bottom : (marks[i].Token.Cy + marks[i + 1].Token.Cy) / 2;
                yield return (marks[i].Mark, marks[i].Token, t, b);
            }
        }

        /// <summary>The cells of a band-route row: the tokens on the mark's baseline, within RowWidthPts to its right.</summary>
        private static List<VectorPageReader.TextToken> BandCells(
            VectorPageReader.PageContent page,
            VectorPageReader.TextToken token,
            Options options)
            => page.Words
                .Where(w => Math.Abs(w.Cy - token.Cy) <= 6
                            && w.Cx > token.Cx
                            && w.Cx - token.Cx <= options.RowWidthPts)
                .OrderBy(w => w.Cx)
                .ToList();

        private static Candidate? TryCandidate(
            VectorPageReader.TextToken token,
            string mark,
            IReadOnlyList<VectorPageReader.TextToken> cells,
            Options options,
            ScheduleHeading? heading)
        {
            mark = mark.Trim();
            if (mark.Length == 0) return null;
            if (cells.Count == 0) return null;

            string rowText = string.Join(" ", cells.Select(w => w.Text)).Replace(",", "");
            if (!HasRequiredWord(rowText, options.RequiredRowWords)) return null;

            var dims = PrintedLength.TryFindSizeMm(rowText);
            if (dims is not null && !Plausible(dims, options)) dims = null;

            double? single = dims is null && !options.RequireDimensionPair
                ? FirstPlausibleLength(cells.Select(c => c.Text).ToList(), options)
                : null;

            // a row whose size cell says VARIES is a mark whose size is stated on the plan, and it is
            // returned as one rather than dropped as a row that did not parse
            bool varies = dims is null && single is null && options.RequireDimensionPair && VariesRe.IsMatch(rowText);
            if (dims is null && single is null && !varies) return null;

            return new Candidate(token, mark, rowText, dims ?? Array.Empty<double>(), single, StrengthMPa(rowText), heading, varies);
        }

        public static IReadOnlyList<ScheduleHeading> SchedulesOn(
            VectorPageReader.PageContent page,
            Options options)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(options);

            var found = new List<ScheduleHeading>();
            foreach (var w in page.Words)
            {
                if (!w.Text.StartsWith("SCHEDULE", StringComparison.OrdinalIgnoreCase)) continue;

                var beforeTokens = page.Words
                    .Where(s => Math.Abs(s.Cy - w.Cy) <= 6
                                && s.Cx < w.Cx
                                && w.Cx - s.Cx <= options.HeadingSearchWidthPts)
                    .OrderBy(s => s.Cx)
                    .ToList();

                var before = beforeTokens.Select(s => s.Text).ToList();
                string title = string.Join(" ", before.Append("SCHEDULE"));
                bool target = options.HeadingWords.Count == 0 ||
                              options.HeadingWords.Any(h => IsHeadedBy(before, h));

                double x = beforeTokens.Count > 0 ? beforeTokens.Min(s => s.Cx) : w.Cx;

                // The title's EXTENT runs past the word SCHEDULE: "COLUMN SCHEDULE - LEVEL 1 TO
                // LEVEL 3" is one title, and 31202 underlines all of it. The qualifier is not part
                // of what is scheduled, so it does not join the words above, but it is part of where
                // the title is. Tokens after SCHEDULE belong to it while they run on, on the same
                // baseline, with no gap wider than two of their heights.
                var titleTokens = beforeTokens.Append(w).ToList();
                double reachRight = w.MaxX;
                foreach (var after in page.Words
                    .Where(s => Math.Abs(s.Cy - w.Cy) <= 6 && s.Cx > w.Cx)
                    .OrderBy(s => s.MinX))
                {
                    if (after.MinX - reachRight > 2 * Math.Max(after.Height, w.Height)) break;
                    titleTokens.Add(after);
                    reachRight = Math.Max(reachRight, after.MaxX);
                }

                found.Add(new ScheduleHeading(x, w.Cy, target, title)
                {
                    TitleMinX = titleTokens.Min(s => s.MinX),
                    TitleMinY = titleTokens.Min(s => s.MinY),
                    TitleMaxX = titleTokens.Max(s => s.MaxX),
                    TitleHeight = titleTokens.Max(s => s.Height),
                });
            }

            return found;
        }

        public static ScheduleHeading? OwnerOf(
            double x,
            double y,
            IReadOnlyList<ScheduleHeading> headings,
            double band)
        {
            ArgumentNullException.ThrowIfNull(headings);

            ScheduleHeading? best = null;
            double bestScore = double.MaxValue;

            foreach (var h in headings)
            {
                if (h.Y <= y) continue;
                double dx = Math.Abs(x - h.X);
                if (dx > band) continue;

                double score = (h.Y - y) + dx;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }

            return best;
        }

        /// <summary>
        /// Whether a title names THIS kind of schedule: the words before SCHEDULE end with the
        /// heading phrase.
        /// </summary>
        /// <remarks>
        /// ⭐ THE LAST WORD BEFORE "SCHEDULE" IS WHAT IS SCHEDULED. English compounds are head-final:
        /// a SHEAR WALL ZONE SCHEDULE schedules zones, a COLUMN STIRRUP SCHEDULE schedules stirrups,
        /// a PARKADE COLUMN SCHEDULE schedules columns. "Contains the word anywhere" read the zone
        /// table as walls (ZA..ZD with a rebar grade for a strength) and the stirrup table as
        /// columns. A parenthetical after the head — "STEEL BEAM (SB) &amp; COLUMN (SC) SCHEDULE" — is
        /// an abbreviation, not the head, and is stepped over.
        /// </remarks>
        public static bool IsHeadedBy(IReadOnlyList<string> titleWordsBeforeSchedule, string headingPhrase)
        {
            ArgumentNullException.ThrowIfNull(titleWordsBeforeSchedule);
            if (string.IsNullOrWhiteSpace(headingPhrase)) return false;

            var words = titleWordsBeforeSchedule
                .Select(t => t.Trim().Trim(':', '-', '–', ','))
                .Where(t => t.Length > 0 && !(t.StartsWith('(') && t.EndsWith(')')))
                .ToList();
            var phrase = headingPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (phrase.Length == 0 || words.Count < phrase.Length) return false;

            for (int i = 0; i < phrase.Length; i++)
            {
                if (!words[words.Count - phrase.Length + i].Equals(phrase[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static bool HasRequiredWord(string rowText, IReadOnlyList<string> required)
        {
            if (required.Count == 0) return true;
            return required.Any(word =>
                Regex.IsMatch(rowText, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        private static bool Plausible(IReadOnlyList<double> dims, Options options)
            => dims.All(d => d >= options.MinDimensionMm && d <= options.MaxDimensionMm);

        private static double? FirstPlausibleLength(IReadOnlyList<string> cells, Options options)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                string previous = i > 0 ? cells[i - 1].Trim() : "";
                if (previous == "@") continue;

                string cell = cells[i];
                string text = cell.Replace(",", "").Trim();
                if (PrintedLength.TryParseMm(text) is not double mm) continue;
                if (mm >= options.MinDimensionMm && mm <= options.MaxDimensionMm) return mm;
            }

            return null;
        }

        private static double ColumnKey(VectorPageReader.TextToken token)
            => Math.Round(token.Cx / 15.0);

        private sealed record Candidate(
            VectorPageReader.TextToken Token,
            string Mark,
            string RowText,
            IReadOnlyList<double> DimensionsMm,
            double? SingleLengthMm,
            double? StrengthMPa,
            ScheduleHeading? Heading,
            bool SizeVaries = false);
    }
}
