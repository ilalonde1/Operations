#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Kor.Operations.StandardDetails;

internal sealed record CustomSheetPlacementSpec(
    string DetailNumber,
    byte[]? DetailPdf,
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm);

internal sealed record CustomSheetSpec(
    double SheetWidthMm,
    double SheetHeightMm,
    string? SheetNumber,
    string? SheetName,
    string AuthorLabel,
    DateTime GeneratedUtc,
    IReadOnlyList<CustomSheetPlacementSpec> Placements);

internal static class CustomPdfSheetComposer
{
    private const double PointsPerMillimeter = 72.0 / 25.4;

    internal static byte[] Build(CustomSheetSpec spec)
    {
        if (spec.SheetWidthMm <= 0 || spec.SheetHeightMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "Sheet dimensions must be positive.");
        }

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(ToPoints(spec.SheetWidthMm));
        page.Height = XUnit.FromPoint(ToPoints(spec.SheetHeightMm));

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            DrawSheet(gfx, page.Width.Point, page.Height.Point);
            foreach (var placement in spec.Placements)
            {
                DrawPlacement(gfx, placement);
            }

            DrawFooter(gfx, spec, page.Width.Point, page.Height.Point);
        }

        using var ms = new MemoryStream();
        document.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static void DrawSheet(XGraphics gfx, double widthPt, double heightPt)
    {
        var borderPen = new XPen(XColor.FromArgb(130, 75, 84, 94), 0.75);
        gfx.DrawRectangle(borderPen, 0.5, 0.5, Math.Max(0, widthPt - 1), Math.Max(0, heightPt - 1));
    }

    private static void DrawPlacement(XGraphics gfx, CustomSheetPlacementSpec placement)
    {
        var box = new XRect(
            ToPoints(placement.LeftMm),
            ToPoints(placement.TopMm),
            ToPoints(placement.WidthMm),
            ToPoints(placement.HeightMm));

        if (placement.DetailPdf is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(placement.DetailPdf);
                using var form = XPdfForm.FromStream(stream);
                var target = FitToBox(form, box);
                gfx.DrawImage(form, target);
                return;
            }
            catch
            {
                // A bad captured PDF should not create a blank sheet or block a working copy.
            }
        }

        DrawPlaceholder(gfx, placement.DetailNumber, box);
    }

    private static XRect FitToBox(XPdfForm form, XRect box)
    {
        var formWidth = form.PointWidth;
        var formHeight = form.PointHeight;
        if (formWidth <= 0 || formHeight <= 0 || box.Width <= 0 || box.Height <= 0)
        {
            return box;
        }

        var formAspect = formWidth / formHeight;
        var boxAspect = box.Width / box.Height;
        if (Math.Abs(formAspect - boxAspect) / boxAspect <= 0.02)
        {
            return box;
        }

        var width = box.Width;
        var height = width / formAspect;
        if (height > box.Height)
        {
            height = box.Height;
            width = height * formAspect;
        }

        return new XRect(
            box.X + ((box.Width - width) / 2),
            box.Y + ((box.Height - height) / 2),
            width,
            height);
    }

    private static void DrawPlaceholder(XGraphics gfx, string detailNumber, XRect box)
    {
        var pen = new XPen(XColor.FromArgb(180, 145, 153, 163), 0.8);
        var brush = new XSolidBrush(XColor.FromArgb(245, 248, 250, 252));
        gfx.DrawRectangle(pen, brush, box);

        var titleFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var noteFont = new XFont("Arial", 8.5, XFontStyleEx.Regular);
        var textBrush = new XSolidBrush(XColor.FromArgb(255, 76, 86, 96));
        var noteBrush = new XSolidBrush(XColor.FromArgb(255, 116, 125, 136));
        var titleBox = new XRect(box.X + 4, box.Y + (box.Height / 2) - 14, Math.Max(0, box.Width - 8), 13);
        var noteBox = new XRect(box.X + 4, box.Y + (box.Height / 2) + 1, Math.Max(0, box.Width - 8), 12);
        gfx.DrawString(detailNumber, titleFont, textBrush, titleBox, XStringFormats.Center);
        gfx.DrawString("art not captured", noteFont, noteBrush, noteBox, XStringFormats.Center);
    }

    private static void DrawFooter(XGraphics gfx, CustomSheetSpec spec, double widthPt, double heightPt)
    {
        var footerHeight = ToPoints(10);
        var footerTop = heightPt - footerHeight - ToPoints(3);
        var brush = new XSolidBrush(XColor.FromArgb(255, 92, 101, 112));
        var font = new XFont("Arial", 7.5, XFontStyleEx.Regular);
        var label = BuildFooterLabel(spec);
        var rect = new XRect(ToPoints(8), footerTop, Math.Max(0, widthPt - ToPoints(16)), footerHeight);
        gfx.DrawString(label, font, brush, rect, XStringFormats.CenterLeft);
    }

    private static string BuildFooterLabel(CustomSheetSpec spec)
    {
        var sheetLabel = string.Join(
            " ",
            new[] { spec.SheetNumber?.Trim(), spec.SheetName?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(sheetLabel))
        {
            sheetLabel = "custom sheet";
        }

        var author = string.IsNullOrWhiteSpace(spec.AuthorLabel) ? Environment.UserName : spec.AuthorLabel.Trim();
        return $"UNCONTROLLED WORKING COPY — {sheetLabel} — generated {spec.GeneratedUtc:yyyy-MM-dd} by {author} from the KOR Standard Details. Not the governed standard.";
    }

    private static double ToPoints(double millimeters) => millimeters * PointsPerMillimeter;
}
