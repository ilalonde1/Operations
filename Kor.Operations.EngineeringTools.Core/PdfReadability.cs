#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>Verdict on whether a drawing set is even readable by the vector takeoff — assessed cheaply
    /// from the text layer alone, BEFORE any raster or AI spend. The takeoff reads vector TEXT (sheet titles,
    /// grid labels, thickness callouts); a scanned or CAD-flattened PDF whose drawings are images carries no
    /// such text and the tool is structurally blind to it. This refuses such a set loudly instead of bluffing
    /// a confident-looking number off the one or two pages that happen to have a text layer.</summary>
    public sealed record PdfReadability(
        int PagesInRange,
        int ImageOnlyPages,
        int TextPages,
        int MedianWordsPerTextPage,
        bool Readable,
        string Reason);

    /// <summary>Thrown when the takeoff refuses a set it cannot read (scanned/flattened, no vector text).
    /// A clean, recognizable failure the host turns into a clear message — not a crash or a bluffed number.</summary>
    public sealed class PdfNotReadableException : Exception
    {
        public PdfNotReadableException(string message) : base(message) { }
    }

    public static class PdfReadabilityAssessor
    {
        // A page below this many extractable words has no usable text layer — it is an image. The floor sits
        // in the wide empty gap measured across real sets: flattened pages carry ~0 words, the sparsest page
        // of a genuine vector set (31065) still carried 104. 25 is comfortably between the two, so a title-
        // block-only scan (a few stray tokens) still reads as image-only and a real drawing never does.
        public const int ImageOnlyWordFloor = 25;

        // A set is "blind" when at least this fraction of its pages are image-only. A vector set with an odd
        // rendered cover or photo page (a few image pages) is NOT refused; a scanned/flattened set (the great
        // majority of pages image-only) is. Granville's structural set ran 4 of 5 image-only → refused.
        public const double BlindPageFraction = 0.5;

        /// <summary>Readability from raw per-page text (one string per page, as the rebar/page-text reader
        /// yields it) — splits each page on whitespace and defers to <see cref="Assess(IReadOnlyList{int})"/>,
        /// so every caller counts words the same way.</summary>
        public static PdfReadability AssessPageTexts(IReadOnlyList<string> pageTexts)
        {
            ArgumentNullException.ThrowIfNull(pageTexts);
            var words = pageTexts
                .Select(t => (t ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length)
                .ToList();
            return Assess(words);
        }

        /// <summary>Decide readability from the per-page extractable-word counts. Pure and deterministic, so
        /// the threshold is unit-tested directly with synthetic counts — no PDF needed.</summary>
        public static PdfReadability Assess(IReadOnlyList<int> wordsPerPage)
        {
            ArgumentNullException.ThrowIfNull(wordsPerPage);
            int pages = wordsPerPage.Count;
            if (pages == 0)
                return new PdfReadability(0, 0, 0, 0, false,
                    "No pages in the selected range — nothing to read.");

            int imageOnly = wordsPerPage.Count(w => w < ImageOnlyWordFloor);
            int textPages = pages - imageOnly;
            var textWordCounts = wordsPerPage.Where(w => w >= ImageOnlyWordFloor).OrderBy(w => w).ToList();
            int median = textWordCounts.Count > 0 ? textWordCounts[textWordCounts.Count / 2] : 0;

            if (imageOnly >= BlindPageFraction * pages)
            {
                string reason =
                    $"{imageOnly} of {pages} pages have no extractable text layer — this is a scanned or " +
                    "CAD-flattened PDF whose drawings are images. The takeoff reads vector text (grid labels, " +
                    "slab callouts, sheet titles) and is blind to image-only drawings, so it will not guess a " +
                    "number off it. Re-export a true vector PDF from the authoring tool; OCR/vision is out of scope.";
                return new PdfReadability(pages, imageOnly, textPages, median, false, reason);
            }

            return new PdfReadability(pages, imageOnly, textPages, median, true,
                $"Vector text present on {textPages} of {pages} pages (median {median} words/page) — readable.");
        }
    }
}
