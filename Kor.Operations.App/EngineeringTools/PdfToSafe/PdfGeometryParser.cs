#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    // RawSubpath moved to Core/PdfToSafe/PdfGeometryModels.cs — same namespace, so this file
    // needed no other change.

    internal enum PdfScaleSource
    {
        None,
        TitleBlock,
        SheetCaption,
        ConflictingSheetNotes
    }

    internal sealed record PdfScaleDetection(
        PdfScaleSource Source,
        int? Denominator,
        string? Note,
        double? FractionX,
        double? FractionY,
        IReadOnlyList<string> Notes);

    internal static class PdfGeometryParser
    {
        /// <summary>
        /// Moved to <see cref="PdfPlanReader.ParsePage"/> in Core on 2 September so the CLI can
        /// read a drawing too. Kept as a forwarder: this file's other readers call it, and the
        /// point of the move was one implementation, not a second one.
        /// </summary>
        public static List<RawSubpath>
            ParsePage(Page page, double scale)
            => PdfPlanReader.ParsePage(page, scale);

        public static int? DetectScale(string filePath, int pageNumber = 1)
            => DetectScaleForLoad(filePath, pageNumber).Denominator;

        public static PdfScaleDetection DetectScaleForLoad(string filePath, int pageNumber = 1)
        {
            try
            {
                var page = VectorPageReader.ReadPage(filePath, pageNumber);
                string? titleNote = SheetScaleReader.FromPage(page);
                if (DenominatorFromScaleNote(titleNote) is int titleDenominator)
                {
                    return new PdfScaleDetection(
                        PdfScaleSource.TitleBlock,
                        titleDenominator,
                        titleNote,
                        null,
                        null,
                        Array.Empty<string>());
                }

                var notes = SheetScaleReader.ScaleNotesAnywhere(page).ToList();
                if (notes.Count == 1)
                {
                    var note = notes[0];
                    // FROM THE NOTE, NOT FROM ScaleNote.MetresPerPixel.
                    //
                    // That field is computed at 96 DPI, because SheetScaleReader only ever used it
                    // to compare notes against each other and says so — "DPI here is arbitrary;
                    // only parseability and the relative factor matter". Read as metres per PDF
                    // POINT it is out by 96/72, and Parcel 11 came back 1:72 against a sheet that
                    // plainly states 1/8" = 1'-0". Right note, right place, wrong by a third, and
                    // nothing about the number looks wrong.
                    return new PdfScaleDetection(
                        PdfScaleSource.SheetCaption,
                        DenominatorFromScaleNote(note.Note),
                        note.Note,
                        note.FractionX,
                        note.FractionY,
                        new[] { note.Note });
                }

                if (notes.Count > 1)
                {
                    var namedNotes = notes
                        .Select(n => DenominatorFromScaleNote(n.Note) is int d
                            ? $"{n.Note.Trim()} (1:{d})"
                            : n.Note.Trim())
                        .ToList();
                    return new PdfScaleDetection(
                        PdfScaleSource.ConflictingSheetNotes,
                        null,
                        null,
                        null,
                        null,
                        namedNotes);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("PdfGeometryParser: scale detection failed: " + ex.Message); }
            return new PdfScaleDetection(PdfScaleSource.None, null, null, null, null, Array.Empty<string>());
        }

        private static int? DenominatorFromScaleNote(string? note)
        {
            double? metresPerPoint = PlanGeometry.MetresPerPixel(note, renderDpi: 72);
            return metresPerPoint is double mpp ? DenominatorFromMetresPerPoint(mpp) : null;
        }

        private static int? DenominatorFromMetresPerPoint(double metresPerPoint)
        {
            if (metresPerPoint <= 0) return null;
            int denominator = (int)Math.Round(metresPerPoint * 1000.0 / PdfToSafeConstants.PointsToMm);
            return denominator > 0 ? denominator : null;
        }

        public static List<TextAnnotation> ExtractTextAnnotations(Page page, double scale)
        {
            var result = new List<TextAnnotation>();
            foreach (var word in page.GetWords())
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                    continue;

                var box = word.BoundingBox;
                double xMm = ((box.BottomLeft.X + box.TopRight.X) / 2.0) * scale;
                double yMm = ((box.BottomLeft.Y + box.TopRight.Y) / 2.0) * scale;
                double leftMm = box.BottomLeft.X * scale;
                double bottomMm = box.BottomLeft.Y * scale;
                double heightMm = (box.TopRight.Y - box.BottomLeft.Y) * scale;
                result.Add(new TextAnnotation(word.Text, xMm, yMm, leftMm, bottomMm, heightMm));
            }
            return result;
        }

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

        public static Dictionary<(byte R, byte G, byte B), double> ExtractThicknessHints(
            string filePath,
            int    pageNumber,
            int    scaleDenominator,
            ExtractedGeometry geometry)
        {
            var results = new Dictionary<(byte R, byte G, byte B), double>();
            if (geometry.Slabs.Count == 0 || geometry.SlabColors.Count == 0)
                return results;

            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;
            var candidatesByColor = new Dictionary<(byte R, byte G, byte B), List<int>>();

            static int? ParseThickness(string text)
            {
                string t = text.Trim().ToUpperInvariant();

                var m = Regex.Match(t, @"^(\d{2,4})\s*(THK|THICK|MM|T)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v1) && v1 is >= 50 and <= 1000) return v1;

                m = Regex.Match(t, @"^[TDH]=(\d{2,4})$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v2) && v2 is >= 50 and <= 1000) return v2;

                m = Regex.Match(t, @"^(\d{2,4})\s*(SLAB|FLAT|FS|PT|RC|TOPPING)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v3) && v3 is >= 50 and <= 1000) return v3;

                m = Regex.Match(t, @"^(RC|FS|PT|S|T|H|D|WT|FL)(\d{2,4})$");
                if (m.Success && int.TryParse(m.Groups[2].Value, out int v4) && v4 is >= 50 and <= 1000) return v4;

                m = Regex.Match(t, @"^(\d{2,4})(RC|PT|FS)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v5) && v5 is >= 50 and <= 1000) return v5;

                if (Regex.IsMatch(t, @"^\d{3}$") && int.TryParse(t, out int bare) && bare is >= 100 and <= 500)
                    return bare;

                return null;
            }

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(pageNumber);
            foreach (var word in page.GetWords())
            {
                int? thickness = ParseThickness(word.Text);
                if (!thickness.HasValue)
                    continue;

                var box = word.BoundingBox;
                double xMm = ((box.BottomLeft.X + box.TopRight.X) / 2.0) * scale;
                double yMm = ((box.BottomLeft.Y + box.TopRight.Y) / 2.0) * scale;
                var point = (X: xMm, Y: yMm);

                for (int i = 0; i < geometry.Slabs.Count && i < geometry.SlabColors.Count; i++)
                {
                    if (!PolygonProcessor.PointInPolygon(point, geometry.Slabs[i]))
                        continue;

                    var color = geometry.SlabColors[i];
                    if (!candidatesByColor.TryGetValue(color, out var list))
                    {
                        list = new List<int>();
                        candidatesByColor[color] = list;
                    }
                    list.Add(thickness.Value);
                    break;
                }
            }

            foreach (var (color, values) in candidatesByColor)
            {
                if (values.Count == 0) continue;
                double chosen = values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                results[color] = chosen;
            }

            return results;
        }

    }
}
