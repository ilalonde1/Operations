#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Kor.Operations.StandardDetails;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails
{
    /// <summary>
    /// Locks the Revit-free "Create PDF sheet" output. The composer stamps captured detail PDFs onto a
    /// page laid out exactly as the on-screen canvas — a mm→pt, top-left-origin mapping that is easy to
    /// get subtly wrong (Y flip, scale, letterbox). These assert the invariants the engineer's eye
    /// depends on.
    ///
    /// COVERS: output is a single page of the requested sheet size (in points); a placement with no art
    /// still yields a page (placeholder path, not a throw); a corrupt PDF byte[] degrades to a
    /// placeholder rather than failing the whole sheet.
    /// DOES NOT COVER (by construction — no rasteriser here): that the stamped vector content lands at
    /// the correct pixels or that letterboxing is centred. That is verified by rendering the emitted
    /// PDF (written to %TEMP%\custompdf_verify.pdf) and LOOKING — a code test cannot see position.
    /// </summary>
    public class CustomPdfSheetComposerTests
    {
        private const double SheetWidthMm = 914.4;   // 36"
        private const double SheetHeightMm = 609.6;  // 24"
        private const double PtPerMm = 72.0 / 25.4;

        private static byte[] SyntheticDetailPdf(double widthPt, double heightPt, XColor fill, string label)
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(widthPt);
            page.Height = XUnit.FromPoint(heightPt);
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawRectangle(new XSolidBrush(fill), 0, 0, widthPt, heightPt);
                gfx.DrawString(label, new XFont("Arial", 24, XFontStyleEx.Bold), XBrushes.White,
                    new XRect(0, 0, widthPt, heightPt), XStringFormats.Center);
            }

            using var ms = new MemoryStream();
            doc.Save(ms, closeStream: false);
            return ms.ToArray();
        }

        [Fact]
        public void Build_ProducesOneSheetSizedPage_AndVisualDump()
        {
            var placements = new List<CustomSheetPlacementSpec>
            {
                // A — aspect-matched (400x300 => 1.333), box 300x225mm at top-left. Should FILL the box.
                new("KOR-D-A", SyntheticDetailPdf(400, 300, XColor.FromArgb(255, 200, 40, 40), "A"),
                    LeftMm: 50, TopMm: 40, WidthMm: 300, HeightMm: 225),
                // B — square art (1.0) in a WIDE 300x150mm box (2.0). Should LETTERBOX, centred.
                new("KOR-D-B", SyntheticDetailPdf(300, 300, XColor.FromArgb(255, 40, 150, 60), "B"),
                    LeftMm: 307, TopMm: 230, WidthMm: 300, HeightMm: 150),
                // C — tall art (0.5) matched box 150x300mm at bottom-right. Should FILL.
                new("KOR-D-C", SyntheticDetailPdf(200, 400, XColor.FromArgb(255, 40, 80, 200), "C"),
                    LeftMm: 645, TopMm: 300, WidthMm: 150, HeightMm: 300),
                // D — no art => placeholder box at bottom-left.
                new("KOR-D-D", null, LeftMm: 100, TopMm: 380, WidthMm: 200, HeightMm: 150),
                // E — corrupt bytes => must degrade to placeholder, not throw.
                new("KOR-D-E", new byte[] { 1, 2, 3, 4, 5 }, LeftMm: 380, TopMm: 430, WidthMm: 180, HeightMm: 130),
            };

            var spec = new CustomSheetSpec(
                SheetWidthMm, SheetHeightMm,
                SheetNumber: "SK-01", SheetName: "Verification",
                AuthorLabel: "verify-harness", GeneratedUtc: new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
                Placements: placements);

            var bytes = CustomPdfSheetComposer.Build(spec);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);

            using (var ms = new MemoryStream(bytes))
            using (var reopened = PdfReader.Open(ms, PdfDocumentOpenMode.Import))
            {
                Assert.Equal(1, reopened.PageCount);
                var page = reopened.Pages[0];
                Assert.Equal(SheetWidthMm * PtPerMm, page.Width.Point, 1.0);   // 2592 pt (36")
                Assert.Equal(SheetHeightMm * PtPerMm, page.Height.Point, 1.0); // 1728 pt (24")
            }

            // Visual-verification dump — render this and LOOK; a code test cannot judge placement.
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "custompdf_verify.pdf"), bytes);
        }
    }
}
