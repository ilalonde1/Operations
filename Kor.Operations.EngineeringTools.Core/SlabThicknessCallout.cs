#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The slab-thickness call-out grammar: imperial <c>14" SLAB</c> / <c>SLAB 14"</c>
    /// and metric <c>350 SLAB</c> / <c>SLAB 350</c>. This is only the shape of the
    /// call-out; callers still own the physical bounds that match the question they are asking.
    ///
    /// One definition shared by the field reader, the positioned zoner and PdfToSafe's markup
    /// parser, so an inch/mm dialect fix cannot land in one reader while the others keep the old
    /// grammar. The Core takeoff readers intentionally scan only the number-first form today; the
    /// slab-first form is parsed here as the shared grammar, but not enabled at existing call sites
    /// because that would be a behavior change.
    /// </summary>
    public static class SlabThicknessCallout
    {
        // IMPERIAL: "<n>" SLAB" - a storey number, a 1-2 char inch mark (the glyph varies on CAD sheets),
        // then SLAB. The inch mark is REQUIRED, which is what keeps a metric "200 SLAB" out of this pool.
        private static readonly Regex NumberFirstImperialTextRe = new(@"(\d{1,2})\s*[^\w\s]{1,2}\s*SLAB",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // METRIC: "<mm> SLAB" - a 2-3 digit millimetre depth directly before SLAB (no inch mark). The
        // required adjacency keeps an imperial «10" SLAB» (inch mark intervenes) and note-numbering
        // («5. SLABS») out of this pool. Metric drawings (e.g. 5380 Heather) call out "200 SLAB".
        private static readonly Regex NumberFirstMetricTextRe = new(@"(\d{2,3})\s*SLAB",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Words immediately left of SLAB on the same baseline, ending in «<n>" » - the same tight shape
        // SlabThicknessReader uses, so "UNREINFORCED SLAB" / "PC3 ... SLAB" are skipped (a word intervenes).
        private static readonly Regex NumberFirstImperialTailRe = new(@"(\d{1,2})\s*[^\w\s]{1,2}\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Metric tail: a 2-3 digit millimetre depth directly before SLAB (no inch mark) - "200 SLAB",
        // "900 SLAB". Mirrors SlabThicknessReader's metric handling so a metric set zones too.
        private static readonly Regex NumberFirstMetricTailRe = new(@"(\d{2,3})\s*$", RegexOptions.Compiled);

        private static readonly Regex SlabFirstImperialTextRe = new(@"\bSLABS?\s*(\d{1,2})\s*(?!\.)[^\w\s]{1,2}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SlabFirstMetricTextRe = new(@"\bSLABS?\s*(\d{2,3})(?![\d.])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // PdfToSafe legacy annotation slab branch: kept byte-for-behavior compatible with the old parser,
        // including decimal annotations and its historical treatment of the optional unit on "N SLAB".
        private static readonly Regex PdfToSafeNumberFirstSlabRe = new(
            @"\b(\d+(?:\.\d+)?)\s*(mm|"")?(?:\s+SLAB)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Rebar corroboration evidence: the old report listed 3-4 digit metric slab callouts only.
        // It is not a field-slab decision, so there is no structural-thickness bound here.
        private static readonly Regex MetricCorroborationTextRe = new(@"(\d{3,4})\s*SLAB", RegexOptions.Compiled);

        public const int FieldMinIn = 4;     // thinner than this is a topping/SOG, not a structural slab
        public const int FieldMaxIn = 48;    // thicker is a transfer mat, not a field slab
        public const int FieldMinMm = 100;   // ~4"  - plausible structural slab in millimetres
        public const int FieldMaxMm = 600;   // ~24" - above this is a transfer/mat, not a field slab

        public const int ZonerMinIn = 4;        // thinner is a topping/SOG, not a structural slab
        public const int ZonerMaxIn = 48;       // capture up to a thickening/band so the decision can see it
        public const int ZonerFieldMaxIn = 16;  // a true field slab; above this is a localized thickening/mat
        public const int ZonerMinMm = 100;  // lower metric counterpart to the 4" topping/SOG cutoff
        public const int ZonerMaxMm = 1200; // lets a metric deep band/mat reach the zoner decision

        public const double MmPerInch = 25.4;

        public const double PdfToSafeMinMm = 50;   // PdfToSafe's existing recognisable annotation range
        public const double PdfToSafeMaxMm = 2000; // PdfToSafe's existing recognisable annotation range

        public readonly record struct Parsed(int Value, bool IsMetric)
        {
            public int ValueIn =>
                IsMetric ? (int)Math.Round(Value / MmPerInch, MidpointRounding.AwayFromZero) : Value;
        }

        public static IEnumerable<Parsed> MatchNumberFirstText(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            foreach (Match m in NumberFirstImperialTextRe.Matches(text))
                yield return new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: false);

            foreach (Match m in NumberFirstMetricTextRe.Matches(text))
                yield return new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: true);
        }

        public static Parsed? MatchNumberFirstTail(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var m = NumberFirstImperialTailRe.Match(text);
            if (m.Success)
                return new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: false);

            m = NumberFirstMetricTailRe.Match(text);
            return m.Success
                ? new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: true)
                : null;
        }

        public static Parsed? MatchAnyOrderText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            Parsed? first = null;
            foreach (var candidate in MatchNumberFirstText(text))
            {
                first = candidate;
                break;
            }
            if (first is not null && !LooksLikeNoteNumbering(text))
                return first;

            var m = SlabFirstImperialTextRe.Match(text);
            if (m.Success)
                return new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: false);

            m = SlabFirstMetricTextRe.Match(text);
            return m.Success
                ? new Parsed(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), IsMetric: true)
                : null;
        }

        public static double? MatchPdfToSafeNumberFirstSlabMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var m = PdfToSafeNumberFirstSlabRe.Match(text);
            if (!m.Success ||
                !double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mm))
                return null;

            return mm >= PdfToSafeMinMm && mm <= PdfToSafeMaxMm ? mm : null;
        }

        public static IEnumerable<int> MatchMetricCorroborationTextMm(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            foreach (Match m in MetricCorroborationTextRe.Matches(text))
                yield return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static bool LooksLikeNoteNumbering(string text) =>
            Regex.IsMatch(text, @"^\s*\d{1,2}\.\s*SLABS?\b", RegexOptions.IgnoreCase);

    }
}
