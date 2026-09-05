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
                $"{RulePrefix}.min-dimension-mm",
                $"{RulePrefix}.max-dimension-mm",
                $"{RulePrefix}.require-dimension-pair",
            ];
        }

        public readonly record struct ScheduleHeading(double X, double Y, bool IsTarget, string Title);

        public sealed record Row(
            string Mark,
            string RowText,
            IReadOnlyList<double> DimensionsMm,
            double? SingleLengthMm,
            double? StrengthMPa,
            VectorPageReader.TextToken MarkToken,
            ScheduleHeading? Heading,
            IReadOnlyList<string> SettingKeys);

        public static Options ColumnDefaults() => new(
            "dxf.schedule.column",
            ["COLUMN"],
            [@"^[A-Z]{1,3}\d{1,2}$"],
            [],
            RowWidthPts: 340,
            HeadingBandFraction: 0.18,
            HeadingSearchWidthPts: 260,
            MinDimensionMm: 150,
            MaxDimensionMm: 3000);

        public static Options FootingDefaults() => new(
            "dxf.schedule.footing",
            ["FOUNDATION", "FOOTING"],
            [@"^[A-Z]{1,3}\d{1,2}$"],
            ["DEEP", "DP"],
            RowWidthPts: 320,
            HeadingBandFraction: 0.18,
            HeadingSearchWidthPts: 260,
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

                candidates.Add(new Candidate(token, mark, rowText, dims ?? Array.Empty<double>(), single, strength));
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
                    settingKeys));
            }

            return rows;
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
            double? StrengthMPa);
    }
}
