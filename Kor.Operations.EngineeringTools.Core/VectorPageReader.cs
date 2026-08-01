#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Graphics;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The takeoff tool's OWN front-end: reads the NATIVE vector content of a structural drawing page —
    /// the exact text (each word with its position) and the line/polygon geometry — straight out of the
    /// PDF, with no rasterization and no OCR. Numbers come out exact, not guessed from pixels.
    ///
    /// This is deliberately INDEPENDENT of the PdfToSafe app. PdfToSafe reads the engineer's Bluebeam
    /// MARKUP annotations (it intentionally discards the base drawing); a takeoff must read the native
    /// drawing itself. So this reader keeps the native page content and never depends on PdfToSafe.
    /// </summary>
    public static class VectorPageReader
    {
        /// <summary>One extracted word: its text and its bounding box, in PDF points (origin bottom-left).</summary>
        public readonly record struct TextToken(string Text, double Cx, double Cy, double MinX, double MinY, double MaxX, double MaxY)
        {
            public double Width  => MaxX - MinX;
            public double Height => MaxY - MinY;
        }

        /// <summary>One vector subpath: its points and bounding box, in PDF points.</summary>
        public readonly record struct GeomPath(
            IReadOnlyList<(double X, double Y)> Points,
            bool IsClosed, bool IsFilled, bool IsStroked,
            double MinX, double MinY, double MaxX, double MaxY)
        {
            public double Width       => MaxX - MinX;
            public double Height      => MaxY - MinY;
            public double DiagonalLen => Math.Sqrt(Width * Width + Height * Height);
        }

        public sealed record PageContent(
            int PageNumber,
            double WidthPts,
            double HeightPts,
            IReadOnlyList<TextToken> Words,
            IReadOnlyList<GeomPath> Paths);

        /// <summary>One reconstructed line of text: words sharing a baseline, joined left-to-right.</summary>
        public readonly record struct TextLine(string Text, double Y, double MinX, double MaxX);

        /// <summary>
        /// Group the page's words into readable lines (same baseline, ordered left→right, top→bottom).
        /// This is the estimator-readable form — callouts like «10" SLAB», sheet titles, schedule rows —
        /// far better fuel for synthesis than a bag of tokens. Baseline tolerance is ~6pt.
        /// </summary>
        public static IReadOnlyList<TextLine> ReadTextLines(PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);
            return page.Words
                .GroupBy(w => Math.Round(w.Cy / 6.0))
                .Select(g =>
                {
                    var ordered = g.OrderBy(w => w.Cx).ToList();
                    return new TextLine(
                        string.Join(" ", ordered.Select(w => w.Text)),
                        ordered[0].Cy, ordered.Min(w => w.MinX), ordered.Max(w => w.MaxX));
                })
                .OrderByDescending(l => l.Y)
                .ToList();
        }

        /// <summary>Open the PDF and read one page (1-based).</summary>
        public static PageContent ReadPage(string pdfPath, int pageNumber)
        {
            using var doc = PdfDocument.Open(pdfPath);
            return ReadPage(doc.GetPage(pageNumber));
        }

        public static PageContent ReadPage(Page page)
        {
            ArgumentNullException.ThrowIfNull(page);

            // ── Text: every word with its bounding box ──────────────────────────
            // CAD-exported drawings place each glyph individually with no inter-word spaces, so the
            // default whitespace word-splitter yields single characters. The nearest-neighbour
            // extractor groups glyphs by proximity + baseline (and handles rotated text), so we get
            // real tokens like "WALL", "LEVEL", "30", "MPa".
            var words = new List<TextToken>();
            foreach (var w in page.GetWords(NearestNeighbourWordExtractor.Instance))
            {
                if (string.IsNullOrWhiteSpace(w.Text)) continue;
                var bb = w.BoundingBox;
                double minX = Math.Min(bb.BottomLeft.X, bb.TopRight.X);
                double minY = Math.Min(bb.BottomLeft.Y, bb.TopRight.Y);
                double maxX = Math.Max(bb.BottomLeft.X, bb.TopRight.X);
                double maxY = Math.Max(bb.BottomLeft.Y, bb.TopRight.Y);
                words.Add(new TextToken(w.Text, (minX + maxX) / 2.0, (minY + maxY) / 2.0, minX, minY, maxX, maxY));
            }

            // ── Geometry: every vector subpath, points + bbox ───────────────────
            var paths = new List<GeomPath>();
            foreach (var pdfPath in page.ExperimentalAccess.Paths)
            {
                bool isFilled  = pdfPath.IsFilled;
                bool isStroked = pdfPath.IsStroked;

                foreach (var sub in pdfPath)
                {
                    var pts = new List<(double X, double Y)>();
                    foreach (var cmd in sub.Commands)
                    {
                        switch (cmd)
                        {
                            case PdfSubpath.Move m:               AddPoint(pts, m.Location); break;
                            case PdfSubpath.Line l:               AddPoint(pts, l.To);       break;
                            case PdfSubpath.CubicBezierCurve b:   AddPoint(pts, b.EndPoint); break;
                        }
                    }
                    if (pts.Count < 2) continue;

                    bool isClosed = sub.Commands.OfType<PdfSubpath.Close>().Any();
                    // Treat a polyline whose ends nearly meet as closed (common for slab outlines).
                    if (!isClosed && pts.Count >= 3 &&
                        Math.Abs(pts[0].X - pts[^1].X) < 0.5 && Math.Abs(pts[0].Y - pts[^1].Y) < 0.5)
                        isClosed = true;

                    double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
                    double maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
                    paths.Add(new GeomPath(pts, isClosed, isFilled, isStroked, minX, minY, maxX, maxY));
                }
            }

            return new PageContent(page.Number, page.Width, page.Height, words, paths);
        }

        private static void AddPoint(List<(double X, double Y)> pts, PdfPoint p) => pts.Add((p.X, p.Y));
    }
}
