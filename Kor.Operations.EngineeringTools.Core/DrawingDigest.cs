#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 of the synthesis-led takeoff: turn a vector drawing into a compact, EXACT, per-page
    /// digest — readable text lines, the largest closed geometry regions (candidate slab/zone outlines)
    /// with true polygon areas, the detected drawing scale, and any deterministically-reconstructed
    /// wall-schedule bands. This is the trustworthy fuel the Layer-2 estimator-synthesis reasons over;
    /// it contains NO guesses — only what the vector actually says.
    /// </summary>
    public sealed record GeomRegion(double WidthPt, double HeightPt, double AreaPt2, bool Filled);

    public sealed record PageDigest(
        int Page,
        double WidthPt,
        double HeightPt,
        string? ScaleNote,
        SheetTitle? Title,
        IReadOnlyList<string> Lines,
        IReadOnlyList<GeomRegion> ClosedRegions,
        IReadOnlyList<ScheduleTakeoff.WallBand> WallBands);

    public sealed record DrawingDigest(string Pdf, int PageCount, IReadOnlyList<PageDigest> Pages);

    public static class DrawingDigestBuilder
    {
        // A drawing scale note like 1/8"=1'-0" — tolerant of the glyph-jumbling seen on dense sheets,
        // where the "=" can be missing/scrambled. Anchor on a fraction-inch followed soon by a foot mark.
        private static readonly Regex ScaleLine = new(@"\d\s*/\s*\d.{0,18}\d\s*['’]", RegexOptions.Compiled);

        public static DrawingDigest Build(string pdfPath, int? firstPage = null, int? lastPage = null)
        {
            using var doc = PdfDocument.Open(pdfPath);
            int lo = Math.Max(1, firstPage ?? 1);
            int hi = Math.Min(doc.NumberOfPages, lastPage ?? doc.NumberOfPages);

            var pages = new List<PageDigest>();
            for (int p = lo; p <= hi; p++)
            {
                // Isolate per page: a single malformed vector page must not abort the whole set. Emit an
                // empty digest for it (classification will skip it) and carry on.
                try { pages.Add(BuildPage(VectorPageReader.ReadPage(doc.GetPage(p)))); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  ! page {p}: digest failed ({ex.Message}); emitting empty digest.");
                    pages.Add(new PageDigest(p, 0, 0, null, null, Array.Empty<string>(),
                        Array.Empty<GeomRegion>(), Array.Empty<ScheduleTakeoff.WallBand>()));
                }
            }

            return new DrawingDigest(pdfPath, doc.NumberOfPages, pages);
        }

        public static PageDigest BuildPage(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var lines = VectorPageReader.ReadTextLines(page)
                .Select(l => l.Text.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            string? scale = lines.FirstOrDefault(l => ScaleLine.IsMatch(l));

            // Largest closed regions by TRUE polygon area — candidate slab/zone outlines.
            var regions = page.Paths
                .Where(g => g.IsClosed && g.Points.Count >= 3)
                .Select(g => new GeomRegion(g.Width, g.Height, PolygonArea(g.Points), g.IsFilled))
                .Where(r => r.AreaPt2 > 1.0)
                .OrderByDescending(r => r.AreaPt2)
                .Take(16)
                .ToList();

            var bands = ScheduleGridReader.ReadWallBands(page);   // empty unless this sheet is a wall schedule
            var title = SheetTitleReader.FromLines(lines);        // canonical (level, zone) off the title block

            return new PageDigest(page.PageNumber, page.WidthPts, page.HeightPts, scale, title, lines, regions, bands);
        }

        private static double PolygonArea(IReadOnlyList<(double X, double Y)> pts)
        {
            double a = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return Math.Abs(a) / 2.0;
        }
    }
}
