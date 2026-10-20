#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Tokens;

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

        /// <summary>Helvetica's capital height as a fraction of its em (Adobe AFM: CapHeight 718).</summary>
        private const double HelveticaCapHeight = 0.718;

        /// <summary>Helvetica advance widths in ems, by StandardEncoding code — PdfPig's own AFM.</summary>
        private static readonly Lazy<Dictionary<int, double>> HelveticaWidths = new(() =>
            Standard14.GetAdobeFontMetrics(Standard14Font.Helvetica).CharacterMetrics.Values
                .Where(m => m.CharacterCode >= 0)
                .GroupBy(m => m.CharacterCode)
                .ToDictionary(g => g.Key, g => g.First().Width.X / 1000.0));

        /// <summary>How many ems a string sets in Helvetica; an unknown character counts as a digit.</summary>
        private static double HelveticaEms(string text)
            => text.Sum(c => HelveticaWidths.Value.TryGetValue(c, out var w) ? w : 0.556);

        public static List<TextAnnotation> ExtractMarkupTextAnnotations(PdfDocument document, Page page, double scale)
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
                // /Contents has no box at all, and ann.Rectangle is the extent of the SHAPE HE DREW.
                // Taking the height from it made every "12" x 30"" as tall as the wall it labels: 796 mm
                // of text on a 796 mm wall segment, 956 on the dimension. Opened in CAD it looked,
                // accurately, like a three-year-old had done it.
                //
                // So the contents get a drawing text height instead: 2.5 mm on paper, which is what
                // a drawing is annotated at, times the scale the sheet is drawn to. 240 mm at 1:96.
                var rect = ann.Rectangle;
                double heightMm = PdfToSafeConstants.PaperTextHeightMm * scale / PdfToSafeConstants.PointsToMm;
                double widthMm = EstimatedTextWidthMm(text, heightMm);

                // WHERE THE SHEET DRAWS IT, WINS.
                //
                // The annotation's normal appearance (/AP /N) is what Bluebeam paints on the sheet, so it
                // says whether the label is visible at all, where, and at what angle. On Omar's A105 the
                // "4,436 sf" of a whole-floor area sits mid-corridor, "14" wall" and the dimensions are
                // turned 90°, and the twelve "504 sq in" column notes are not drawn — they live in the
                // popup. Placing every label above its shape put the floor area on the top edge, crowded
                // the plan with twelve notes he never saw, and laid vertical text flat.
                //
                // A note the sheet does not draw is kept, flagged hidden: Parcel 11 shipped its 27
                // "12" x 30"" notes as text, and the DXF puts them on a layer that opens switched off.
                var drawn = ReadDrawnText(document, ann, out bool hasAppearance);
                if (drawn is { } d)
                {
                    // A drawn label takes the size the sheet draws it at. 2.5 mm on paper was set for
                    // notes with no size of their own; on A105 it made the 8 pt stair dimensions taller
                    // than the sheet's, and in AutoCAD's wider font "3'-4"" and "3'-0"" ran together.
                    // A DXF text height is a capital height: 0.718 of the em for Helvetica, which is what
                    // Bluebeam sets its markups in.
                    //
                    // And it keeps the LENGTH the sheet gives it. AutoCAD's default font is wider than
                    // Helvetica — "4,436 sf" measured about one height per character in Ian's screenshot —
                    // so at the sheet's height the stacked pair still touched. The DXF fits each drawn
                    // label to its Helvetica width, so CAD's font cannot make it longer than the sheet's.
                    double fitWidthMm = 0;
                    if (d.EmPt > 0)
                    {
                        heightMm = d.EmPt * HelveticaCapHeight * scale;
                        fitWidthMm = HelveticaEms(text) * d.EmPt * scale;
                        widthMm = fitWidthMm;
                    }
                    double leftMm = d.X * scale, bottomMm = d.Y * scale;
                    double rad = d.AngleDeg * Math.PI / 180.0;
                    double hx = widthMm / 2.0, hy = heightMm / 2.0;
                    double xMm = leftMm + hx * Math.Cos(rad) - hy * Math.Sin(rad);
                    double yMm = bottomMm + hx * Math.Sin(rad) + hy * Math.Cos(rad);
                    result.Add(new TextAnnotation(text, xMm, yMm, leftMm, bottomMm, heightMm, d.AngleDeg,
                        FitWidthMm: fitWidthMm));
                    continue;
                }

                // The fallback, for a label the sheet does not draw or a PDF with no appearance: above
                // the shape, centred on it, clear of it — where a drafter puts a tag. Centred ON the
                // shape lays the text over the linework it describes.
                double leftFallback = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale - widthMm / 2.0;
                double bottomFallback = rect.TopRight.Y * scale + heightMm * 0.5;
                result.Add(new TextAnnotation(text, leftFallback + widthMm / 2.0, bottomFallback + heightMm / 2.0,
                    leftFallback, bottomFallback, heightMm, 0.0, HiddenOnSheet: hasAppearance));
            }
            return result;
        }

        /// <summary>A PDF matrix [a b c d e f], applied to row vectors as the spec does.</summary>
        private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
        {
            public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

            /// <summary>This, then <paramref name="m"/>.</summary>
            public Matrix Then(Matrix m) => new(
                A * m.A + B * m.C, A * m.B + B * m.D,
                C * m.A + D * m.C, C * m.B + D * m.D,
                E * m.A + F * m.C + m.E, E * m.B + F * m.D + m.F);

            public (double X, double Y) Apply(double x, double y) => (x * A + y * C + E, x * B + y * D + F);
        }

        /// <summary>
        /// The first text the annotation's appearance paints: its baseline origin on the page, in points,
        /// in the same frame as <c>ann.Rectangle</c>, and its angle. Null when the appearance paints no
        /// text (<paramref name="hasAppearance"/> true) or there is no readable appearance (false).
        /// </summary>
        private static (double X, double Y, double AngleDeg, double EmPt)? ReadDrawnText(
            PdfDocument document, Annotation ann, out bool hasAppearance)
        {
            hasAppearance = false;
            try
            {
                if (!ann.AnnotationDictionary.Data.TryGetValue("AP", out var apToken)) return null;
                if (Resolve(document, apToken) is not DictionaryToken ap) return null;
                if (!ap.Data.TryGetValue("N", out var nToken)) return null;
                if (Resolve(document, nToken) is not StreamToken stream) return null;
                hasAppearance = true;

                var dict = stream.StreamDictionary;
                var rect = ann.Rectangle;
                double[] bbox = Numbers(document, dict, "BBox") is { Length: 4 } b
                    ? b : new[] { rect.Left, rect.Bottom, rect.Right, rect.Top };
                var form = Numbers(document, dict, "Matrix") is { Length: 6 } m
                    ? new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]) : Matrix.Identity;

                string content = Encoding.Latin1.GetString(VectorPageReader.DecodeStream(stream));
                if (FirstText(content) is not { } first) return null;
                var textMatrix = first.Placed;

                // PDF 32000 §12.5.5: the form's BBox, carried through its Matrix, is fitted onto the Rect.
                var corners = new[] { form.Apply(bbox[0], bbox[1]), form.Apply(bbox[2], bbox[1]),
                                      form.Apply(bbox[2], bbox[3]), form.Apply(bbox[0], bbox[3]) };
                double tMinX = corners.Min(c => c.X), tMaxX = corners.Max(c => c.X);
                double tMinY = corners.Min(c => c.Y), tMaxY = corners.Max(c => c.Y);
                if (tMaxX - tMinX <= 0 || tMaxY - tMinY <= 0) return null;
                double sx = rect.Width / (tMaxX - tMinX), sy = rect.Height / (tMaxY - tMinY);

                var placed = textMatrix.Then(form);
                var (ox, oy) = placed.Apply(0, 0);
                double x = rect.Left + (ox - tMinX) * sx;
                double y = rect.Bottom + (oy - tMinY) * sy;
                double angle = Math.Atan2(placed.B * sy, placed.A * sx) * 180.0 / Math.PI;
                // The size the sheet sets it at: Tf × the text's vertical scale on the page, in points.
                double emPt = first.FontSize * Math.Sqrt(Math.Pow(placed.C * sx, 2) + Math.Pow(placed.D * sy, 2));
                return (x, y, Math.Round(angle, 6), emPt);
            }
            catch (Exception)
            {
                hasAppearance = false;
                return null;
            }
        }

        private static IToken? Resolve(PdfDocument document, IToken token)
            => token is IndirectReferenceToken r ? document.Structure.GetObject(r.Data)?.Data : token;

        private static double[]? Numbers(PdfDocument document, DictionaryToken dict, string key)
            => dict.Data.TryGetValue(key, out var t) && Resolve(document, t) is ArrayToken arr
                ? arr.Data.Select(x => Resolve(document, x)).OfType<NumericToken>().Select(n => n.Double).ToArray()
                : null;

        /// <summary>
        /// Walks the content stream for the first text-showing operator with a non-empty string and
        /// returns Tm × CTM at that point. Only what a markup appearance uses: q/Q, cm, BT/ET, Tm, Td,
        /// Tj, TJ, ' and ".
        /// </summary>
        private static (Matrix Placed, double FontSize)? FirstText(string content)
        {
            double fontSize = 0;
            var ctm = Matrix.Identity;
            var stack = new Stack<Matrix>();
            var tm = Matrix.Identity;
            var line = Matrix.Identity;
            var operands = new List<string>();
            int i = 0;
            while (i < content.Length)
            {
                char ch = content[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '%') { while (i < content.Length && content[i] != '\n' && content[i] != '\r') i++; continue; }
                if (ch == '(') { operands.Add(ReadString(content, ref i)); continue; }
                if (ch == '[')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < content.Length && content[i] != ']')
                    {
                        if (content[i] == '(') sb.Append(ReadString(content, ref i));
                        else i++;
                    }
                    i++;
                    operands.Add(sb.ToString());
                    continue;
                }
                if (ch == '<' && i + 1 < content.Length && content[i + 1] == '<') { i += 2; continue; }
                if (ch == '>' && i + 1 < content.Length && content[i + 1] == '>') { i += 2; continue; }
                if (ch == '<') { int end = content.IndexOf('>', i); operands.Add("<hex>"); i = end < 0 ? content.Length : end + 1; continue; }

                int start = i;
                while (i < content.Length && !char.IsWhiteSpace(content[i]) && "()[]<>/%".IndexOf(content[i]) < 0) i++;
                if (i == start) { i++; continue; }
                string word = content.Substring(start, i - start);
                if (content[start] == '/' || double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    operands.Add(word);
                    continue;
                }

                double N(int k) => double.Parse(operands[operands.Count - 6 + k], CultureInfo.InvariantCulture);
                switch (word)
                {
                    case "q": stack.Push(ctm); break;
                    case "Q": if (stack.Count > 0) ctm = stack.Pop(); break;
                    case "cm" when operands.Count >= 6: ctm = new Matrix(N(0), N(1), N(2), N(3), N(4), N(5)).Then(ctm); break;
                    case "BT": tm = line = Matrix.Identity; break;
                    case "Tf" when operands.Count >= 1:
                        double.TryParse(operands[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize);
                        break;
                    case "Tm" when operands.Count >= 6: tm = line = new Matrix(N(0), N(1), N(2), N(3), N(4), N(5)); break;
                    case "Td" or "TD" when operands.Count >= 2:
                        double tx = double.Parse(operands[^2], CultureInfo.InvariantCulture);
                        double ty = double.Parse(operands[^1], CultureInfo.InvariantCulture);
                        tm = line = new Matrix(1, 0, 0, 1, tx, ty).Then(line);
                        break;
                    case "Tj" or "TJ" or "'" or "\"" when operands.Count > 0 && operands[^1].Trim().Length > 0:
                        return (tm.Then(ctm), fontSize);
                }
                operands.Clear();
            }
            return null;
        }

        /// <summary>A literal string from '(' to its matching ')', escapes undone; leaves i after it.</summary>
        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                if (c == '(') { if (depth++ > 0) sb.Append(c); continue; }
                if (c == ')') { if (--depth == 0) { i++; break; } sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
