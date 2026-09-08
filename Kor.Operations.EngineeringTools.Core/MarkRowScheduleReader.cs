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
        private static readonly Regex StrengthRe = new(
            @"(?<v>\d{2,3}(?:\.\d+)?)\s*MPa",
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

        public readonly record struct ScheduleHeading(double X, double Y, bool IsTarget, string Title);
        public enum MarkRoute { ScheduleColumn, PatternFallback }

        public sealed record Row(
            string Mark,
            string RowText,
            IReadOnlyList<double> DimensionsMm,
            double? SingleLengthMm,
            double? StrengthMPa,
            VectorPageReader.TextToken MarkToken,
            ScheduleHeading? Heading,
            IReadOnlyList<string> SettingKeys,
            MarkRoute Route);

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

            // ⭐ BOTH ROUTES RUN, AND THE ONE THAT READS MORE WINS.
            //
            // They fail in opposite directions, so choosing one up front loses whichever job the
            // other suited. Reading marks off the table's own column is right when the table is
            // clean — 31168 went from 6 marks to 12, picking up C02-A, PC03-A and the other suffixed
            // forms. But it anchors on the topmost row under a heading, and where a sheet's rows wrap
            // or a note sits above them it reads far too few: 31065 dropped from 7 marks to 2, losing
            // PC1..PC5 and taking its footing total from 1,174 cy to 855, and 31138 fell from 10 to 7.
            //
            // That regression shipped because the commit that introduced the structural route was
            // verified with column counts measured one commit earlier. Both routes are heading-scoped
            // and both validate marks, so running both costs one pass and cannot invent a row —
            // whichever sees more of the table is the one that read it.
            var targetHeadings = headings.Where(h => h.IsTarget).ToList();
            IReadOnlyList<Row> structural = targetHeadings.Count > 0
                ? ReadFromScheduleColumns(page, options, targetHeadings, band, settingKeys)
                : Array.Empty<Row>();

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

                double? strength = null;
                var s = StrengthRe.Match(rowText);
                if (s.Success)
                    strength = double.Parse(s.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

                candidates.Add(new Candidate(token, mark, rowText, dims ?? Array.Empty<double>(), single, strength, null));
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
                    MarkRoute.PatternFallback));
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

            foreach (var heading in targetHeadings)
            {
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
                    // Where a sheet's rows wrap, this route does pick up a continuation line's first
                    // token (31065 p14 yields "8-35M" and "BOT."), but that sheet is also where this
                    // route reads FEWEST rows, so the pattern route wins the count and the garbage
                    // never reaches a caller. Filtering here instead would trade a real capability
                    // for a symptom. The actual cure is knowing where the table ENDS, which is still
                    // open — see the seed doc.
                    if (TryCandidate(page, token, options, heading) is { } candidate)
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
                        MarkRoute.ScheduleColumn));
                }
            }

            return rows;
        }

        private static Candidate? TryCandidate(
            VectorPageReader.PageContent page,
            VectorPageReader.TextToken token,
            Options options,
            ScheduleHeading? heading)
        {
            string mark = token.Text.Trim();
            if (mark.Length == 0) return null;

            var cells = page.Words
                .Where(w => Math.Abs(w.Cy - token.Cy) <= 6
                            && w.Cx > token.Cx
                            && w.Cx - token.Cx <= options.RowWidthPts)
                .OrderBy(w => w.Cx)
                .ToList();
            if (cells.Count == 0) return null;

            string rowText = string.Join(" ", cells.Select(w => w.Text)).Replace(",", "");
            if (!HasRequiredWord(rowText, options.RequiredRowWords)) return null;

            var dims = PrintedLength.TryFindSizeMm(rowText);
            if (dims is not null && !Plausible(dims, options)) dims = null;

            double? single = dims is null && !options.RequireDimensionPair
                ? FirstPlausibleLength(cells.Select(c => c.Text).ToList(), options)
                : null;
            if (dims is null && single is null) return null;

            double? strength = null;
            var s = StrengthRe.Match(rowText);
            if (s.Success)
                strength = double.Parse(s.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

            return new Candidate(token, mark, rowText, dims ?? Array.Empty<double>(), single, strength, heading);
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
                string compactTitle = Compact(title);
                bool target = options.HeadingWords.Count == 0 ||
                              options.HeadingWords.Any(h => compactTitle.Contains(Compact(h), StringComparison.OrdinalIgnoreCase));

                double x = beforeTokens.Count > 0 ? beforeTokens.Min(s => s.Cx) : w.Cx;
                found.Add(new ScheduleHeading(x, w.Cy, target, title));
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

        private static string Compact(string text)
            => string.Concat((text ?? "").ToUpperInvariant().Where(ch => !char.IsWhiteSpace(ch)));

        private static double ColumnKey(VectorPageReader.TextToken token)
            => Math.Round(token.Cx / 15.0);

        private sealed record Candidate(
            VectorPageReader.TextToken Token,
            string Mark,
            string RowText,
            IReadOnlyList<double> DimensionsMm,
            double? SingleLengthMm,
            double? StrengthMPa,
            ScheduleHeading? Heading);
    }
}
