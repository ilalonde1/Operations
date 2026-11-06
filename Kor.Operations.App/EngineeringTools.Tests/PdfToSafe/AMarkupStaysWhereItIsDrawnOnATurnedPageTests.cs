#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// A markup on a page stored with /Rotate, a markup turned by Bluebeam's /Rotation, and an "Ellipse Sketch to Scale"
/// (2026-11-06, the OAP arch package pp 28-29: columns thrown up to 150 ft, 45 degree columns upright, circles square).
/// </summary>
/// <remarks>
/// Compares: a polygon's box against PdfPig's own parsed /Rect at /Rotate 0, 90, 180 and 270, with the box on the page,
/// inherited from /Pages, and with a CropBox inside the MediaBox; a /Rotation sketch's
/// centre and bearing at 45 degrees; an ellipse sketch's roundness, cold and through the page cache; a turned markup
/// column's sides and turn. Synthetic pages, built here. Does not compare the real package (checked by rendering it over
/// the sheet), a /MediaBox on the page with its /CropBox inherited, a record left by earlier code (every PDF here has
/// fresh bytes, so nothing here would catch a walk change without a PageReadCache.Version bump), /Line or /InkList on a turned page, page CONTENT columns (step 181 stays parked), nor the app's window.
/// Same-class fault it would not catch: a /Rotation that is not a multiple of 45, or an appearance /Matrix that
/// disagrees with /Rotation: this reads /Rotation alone.
/// </remarks>
public sealed class AMarkupStaysWhereItIsDrawnOnATurnedPageTests
{
    private const string Rectangle = "/Subtype /Polygon /Rect [100 200 140 216] /Vertices [100 200 140 200 140 216 100 216] /C [1 0 0]";

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void AMarkupIsInsideItsOwnRectOnEveryPageTurn(int rotate)
    {
        WithPdf(rotate, Rectangle, path =>
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            var page = doc.GetPage(1);
            var rect = page.ExperimentalAccess.GetAnnotations().Single().Rectangle;
            var shape = VectorPageReader.ReadPage(page, includeAnnotations: true).Paths.Single(p => p.IsAnnotation);

            Assert.Equal(Math.Min(rect.BottomLeft.X, rect.TopRight.X), shape.MinX, 3);
            Assert.Equal(Math.Max(rect.BottomLeft.X, rect.TopRight.X), shape.MaxX, 3);
            Assert.Equal(Math.Min(rect.BottomLeft.Y, rect.TopRight.Y), shape.MinY, 3);
            Assert.Equal(Math.Max(rect.BottomLeft.Y, rect.TopRight.Y), shape.MaxY, 3);
        });
    }

    // Review of 97d07212: the box may be inherited from /Pages, or a CropBox may sit inside the MediaBox. Oracle as above: PdfPig's own Rect.
    [Theory]
    [InlineData(0, "", "/MediaBox [0 0 792 1224]")]
    [InlineData(90, "", "/MediaBox [0 0 792 1224]")]
    [InlineData(180, "", "/MediaBox [0 0 792 1224]")]
    [InlineData(270, "", "/MediaBox [0 0 792 1224]")]
    [InlineData(0, "/MediaBox [0 0 792 1224] /CropBox [50 60 700 1100]", "")]
    [InlineData(90, "/MediaBox [0 0 792 1224] /CropBox [50 60 700 1100]", "")]
    [InlineData(180, "/MediaBox [0 0 792 1224] /CropBox [50 60 700 1100]", "")]
    [InlineData(270, "/MediaBox [0 0 792 1224] /CropBox [50 60 700 1100]", "")]
    public void AMarkupIsInsideItsOwnRectWhenTheBoxIsInheritedOrCropped(int rotate, string pageBoxes, string parentBoxes)
    {
        WithPdf(rotate, Rectangle, path =>
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            var page = doc.GetPage(1);
            var rect = page.ExperimentalAccess.GetAnnotations().Single().Rectangle;
            var shape = VectorPageReader.ReadPage(page, includeAnnotations: true).Paths.Single(p => p.IsAnnotation);

            Assert.Equal(Math.Min(rect.BottomLeft.X, rect.TopRight.X), shape.MinX, 3);
            Assert.Equal(Math.Max(rect.BottomLeft.X, rect.TopRight.X), shape.MaxX, 3);
            Assert.Equal(Math.Min(rect.BottomLeft.Y, rect.TopRight.Y), shape.MinY, 3);
            Assert.Equal(Math.Max(rect.BottomLeft.Y, rect.TopRight.Y), shape.MaxY, 3);
        }, pageBoxes: pageBoxes, parentBoxes: parentBoxes);
    }

    // /Rotation 45 is 45 degrees clockwise; /Rotate 90 is a further 90 clockwise as the page is read.
    [Theory]
    [InlineData(0, 135)]
    [InlineData(90, 45)]
    public void ARotationTurnsClockwiseAboutItsOwnCentre(int rotate, double expectedBearing)
    {
        WithPdf(rotate, Rectangle + " /Rotation 45", path =>
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            var page = doc.GetPage(1);
            var rect = page.ExperimentalAccess.GetAnnotations().Single().Rectangle;
            var pts = VectorPageReader.ReadPage(page, includeAnnotations: true).Paths.Single(p => p.IsAnnotation).Points;

            Assert.Equal((rect.BottomLeft.X + rect.TopRight.X) / 2, pts.Average(p => p.X), 3);
            Assert.Equal((rect.BottomLeft.Y + rect.TopRight.Y) / 2, pts.Average(p => p.Y), 3);
            double bearing = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X) * 180 / Math.PI;
            Assert.Equal(expectedBearing, ((bearing % 180) + 180) % 180, 3);
        });
    }

    [Fact]
    public void AnEllipseSketchIsARoundColumnColdAndThroughThePageCache()
    {
        const string ellipse = "/Subtype /Polygon /IT /EllipseSketchToScale /Rect [100 200 120 220] /Vertices [110 200 120 210 110 220 100 210] /C [1 0 0]";
        WithPdf(90, ellipse, path =>
        {
            string first = path + ".first.dxf", second = path + ".second.dxf";
            Assert.Equal(1, PdfOnlyBuild.WriteSheets(path, first, 1, 1, 96, markup: true, korLayers: false, options: PdfIntakeOptions.Default).Written);
            Assert.Equal(1, PdfOnlyBuild.WriteSheets(path, second, 1, 1, 96, markup: true, korLayers: false, options: PdfIntakeOptions.Default).Written);

            foreach (string dxf in new[] { first, second })
            {
                var lines = File.ReadAllLines(dxf, DxfExporter.FileEncoding).Select(l => l.Trim()).ToList();
                int arc = lines.IndexOf("ARC");
                Assert.True(arc >= 0, "no ARC written");
                // start 0 and end 360 is a zero-length arc to AutoCAD: a dot, not a column
                int end = lines.FindIndex(arc, l => l == "51");
                double sweepEnd = double.Parse(lines[end + 1], CultureInfo.InvariantCulture);
                Assert.True(sweepEnd > 359 && sweepEnd < 360, $"the arc ends at {sweepEnd}");
            }
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        });
    }

    [Fact]
    public void AMarkupColumnDrawnAtAnAngleKeepsItsSidesAndItsTurn()
    {
        WithPdf(0, Rectangle + " /Rotation 45", path =>
        {
            var geometry = PdfGeometryExtractor.Extract(path, 96, pageNumber: 1, annotationsOnly: true);
            var size = Assert.Single(geometry.ColumnSizes);
            double perPoint = 96.0 / 72 * 25.4;
            Assert.Equal(40 * perPoint, size.Item1, 1);
            Assert.Equal(16 * perPoint, size.Item2, 1);
            Assert.Equal(135, ((Assert.Single(geometry.ColumnTurnDegrees) % 180) + 180) % 180, 1);
        });
    }

    [Fact]
    public void AMarkupColumnSquareToTheSheetIsUntouched()
    {
        WithPdf(0, Rectangle, path =>
        {
            var geometry = PdfGeometryExtractor.Extract(path, 96, pageNumber: 1, annotationsOnly: true);
            Assert.Equal(0, Assert.Single(geometry.ColumnTurnDegrees));
        });
    }

    private static void WithPdf(int rotate, string annotation, Action<string> test, string pageBoxes = "/MediaBox [0 0 792 1224]", string parentBoxes = "")
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-turned-markup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        // The bytes differ every run, so the page cache (keyed by content) has never seen this PDF: the first write in
        // a test really walks it, and the record it leaves is removed with it.
        byte[] bytes = Pdf(rotate, annotation, pageBoxes, parentBoxes);
        string cached = Path.Combine(DrawingMirror.Root, "pages", Convert.ToHexString(SHA256.HashData(bytes)));
        try
        {
            string path = Path.Combine(root, "markup.pdf");
            File.WriteAllBytes(path, bytes);
            test(path);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(cached)) Directory.Delete(cached, recursive: true);
        }
    }

    private static byte[] Pdf(int rotate, string annotation, string pageBoxes, string parentBoxes)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [3 0 R] /Count 1 {parentBoxes} >>",
            $"<< /Type /Page /Parent 2 0 R {pageBoxes} /Rotate {rotate} /Resources << >> /Annots [4 0 R] >>",
            $"<< /Type /Annot {annotation} >>",
        ];
        var pdf = new StringBuilder($"%PDF-1.4\n%{Guid.NewGuid():N}\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
