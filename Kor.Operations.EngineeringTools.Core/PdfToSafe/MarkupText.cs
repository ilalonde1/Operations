#nullable enable
using System.Collections.Generic;
using UglyToad.PdfPig.Content;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>Markup labels shared by the App and the drawing intake.</summary>
    public static class MarkupText
    {
        /// <summary>Roughly how wide a string sets at a given height, so a label can be centred on
        /// the shape it belongs to. A monospaced-ish 0.6 of the height per character is close enough
        /// for placing a tag; nothing downstream measures it.</summary>
        private static double EstimatedTextWidthMm(string text, double heightMm)
            => text.Length * heightMm * 0.6;

        public static List<TextAnnotation> ExtractMarkupTextAnnotations(Page page, double scale)
        {
            var result = new List<TextAnnotation>();
            foreach (var ann in page.ExperimentalAccess.GetAnnotations())
            {
                string text = ann.Content?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // AN ANNOTATION'S RECT IS THE SHAPE, NOT A TEXT BOX.
                //
                // A page WORD has a box and its height is the right text height. An annotation's
                // /Contents has no box at all — Bluebeam shows it in a popup, not on the drawing —
                // and ann.Rectangle is the extent of the SHAPE HE DREW. Taking the height from it
                // made every "12" x 30"" as tall as the wall it labels: 796 mm of text on a 796 mm
                // wall segment, 956 on the dimension. Opened in CAD it looked, accurately, like a
                // three-year-old had done it.
                //
                // So the contents get a drawing text height instead: 2.5 mm on paper, which is what
                // a drawing is annotated at, times the scale the sheet is drawn to. 240 mm at 1:96.
                var rect = ann.Rectangle;
                double heightMm = PdfToSafeConstants.PaperTextHeightMm * scale / PdfToSafeConstants.PointsToMm;

                // Above the shape, centred on it, clear of it — where a drafter puts a tag. Centred
                // ON the shape is where the annotation logically belongs, but it lays the text over
                // the linework it describes, and the label and the thing labelled then have to be
                // read through each other.
                double leftMm = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale
                                - EstimatedTextWidthMm(text, heightMm) / 2.0;
                double bottomMm = rect.TopRight.Y * scale + heightMm * 0.5;
                double xMm = leftMm + EstimatedTextWidthMm(text, heightMm) / 2.0;
                double yMm = bottomMm + heightMm / 2.0;
                result.Add(new TextAnnotation(text, xMm, yMm, leftMm, bottomMm, heightMm));
            }
            return result;
        }
    }
}
