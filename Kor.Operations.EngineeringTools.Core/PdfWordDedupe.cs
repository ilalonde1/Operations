#nullable enable
using System;
using System.Collections.Generic;
using UglyToad.PdfPig.Content;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Removes fake-bold DOUBLE-DRAWS from a page's word list: CAD PDF writers embolden text by
    /// painting the identical string a second time at a sub-point offset, which a text extractor
    /// reads as two words. That double word made the change-detector flag UNCHANGED call-outs as
    /// "added" whenever a revision merely bolded them (proven on 31065 IFC p14: every bolded
    /// schedule cell's tokens appear twice at identical coordinates in the text layer, once in the
    /// non-bold issue). Filtering is by DISTANCE, not text alone: two genuine occurrences of the
    /// same call-out are separate annotations at least a text-height apart, while a bold
    /// double-draw is offset by a fraction of a stroke width — so a same-text word within
    /// <see cref="TolerancePt"/> of an already-kept word is the second draw, never a real repeat.
    /// </summary>
    public static class PdfWordDedupe
    {
        /// <summary>Max offset (PDF points) at which a same-text word is a double-draw. Fake-bold
        /// offsets are ≲0.5pt (a stroke width); distinct annotations sit ≥ a text height (~5pt+) apart.</summary>
        public const double TolerancePt = 1.0;

        public static List<Word> Filter(IEnumerable<Word> words)
        {
            ArgumentNullException.ThrowIfNull(words);
            var kept = new List<Word>();
            var byText = new Dictionary<string, List<(double X, double Y)>>(StringComparer.Ordinal);
            foreach (var w in words)
            {
                var bb = w.BoundingBox;
                double x = bb.Left, y = bb.Bottom;
                if (byText.TryGetValue(w.Text, out var prior))
                {
                    bool dup = false;
                    foreach (var (px, py) in prior)
                        if (Math.Abs(px - x) <= TolerancePt && Math.Abs(py - y) <= TolerancePt) { dup = true; break; }
                    if (dup) continue;
                    prior.Add((x, y));
                }
                else byText[w.Text] = new List<(double, double)> { (x, y) };
                kept.Add(w);
            }
            return kept;
        }
    }
}
