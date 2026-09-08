#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Kor.Operations.StandardDetails;

/// <summary>One detail in a review set: its identity, what the catalog says about it, and its stored PDF (or null).</summary>
internal sealed record ReviewSetItem(
    string DetailNumber,
    string Title,
    string Discipline,
    string Kind,
    string Status,
    byte[]? Pdf);

internal sealed record ReviewSetSpec(
    string Title,
    string FilterLabel,
    string AuthorLabel,
    DateTime GeneratedUtc,
    IReadOnlyList<ReviewSetItem> Items);

/// <summary>
/// Assembles the details an engineer has on screen into ONE PDF for Bluebeam markup: a cover page
/// with the index, then a page per detail carrying its stored vector art under a header strip that
/// names it. Built on demand from the current store, so it is never a package to keep track of:
/// every engineer who clicks gets the set as it is right now, and nothing is written anywhere but a
/// temp file that Bluebeam opens.
///
/// A detail with no stored PDF gets a placeholder page so the set is still complete and the gap is
/// visible. The stamp on every page says what this is: a review set, not the governed master.
/// </summary>
internal static class ReviewSetComposer
{
    private const double PointsPerMillimeter = 72.0 / 25.4;
    private const double HeaderPt = 34;
    private const double LetterWidthPt = 792;   // 11 in, landscape
    private const double LetterHeightPt = 612;  // 8.5 in
    private const int IndexLinesPerPage = 38;

    internal static byte[] Build(ReviewSetSpec spec)
    {
        if (spec.Items.Count == 0)
        {
            throw new InvalidOperationException("A review set needs at least one detail.");
        }

        using var document = new PdfDocument();
        document.Info.Title = spec.Title;
        document.Info.Author = spec.AuthorLabel;

        DrawCoverAndIndex(document, spec);

        var ordinal = 0;
        foreach (var item in spec.Items)
        {
            ordinal++;
            DrawDetailPage(document, spec, item, ordinal);
        }

        using var ms = new MemoryStream();
        document.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    /// <summary>The number of pages a set of this shape produces: cover/index pages plus one per item.</summary>
    internal static int ExpectedPageCount(int itemCount)
        => IndexPageCount(itemCount) + itemCount;

    private static int IndexPageCount(int itemCount)
        => Math.Max(1, (int)Math.Ceiling(itemCount / (double)IndexLinesPerPage));

    private static void DrawCoverAndIndex(PdfDocument document, ReviewSetSpec spec)
    {
        var titleFont = new XFont("Arial", 20, XFontStyleEx.Bold);
        var subFont = new XFont("Arial", 10.5, XFontStyleEx.Regular);
        var lineFont = new XFont("Arial", 9, XFontStyleEx.Regular);
        var numFont = new XFont("Arial", 9, XFontStyleEx.Bold);
        var ink = new XSolidBrush(XColor.FromArgb(255, 34, 40, 46));
        var grey = new XSolidBrush(XColor.FromArgb(255, 108, 117, 128));

        var pages = IndexPageCount(spec.Items.Count);
        for (var p = 0; p < pages; p++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(LetterWidthPt);
            page.Height = XUnit.FromPoint(LetterHeightPt);
            using var gfx = XGraphics.FromPdfPage(page);

            var y = 40.0;
            if (p == 0)
            {
                gfx.DrawString(spec.Title, titleFont, ink, new XRect(40, y, LetterWidthPt - 80, 28), XStringFormats.TopLeft);
                y += 32;
                gfx.DrawString(
                    $"{spec.Items.Count} detail(s) — {spec.FilterLabel} — generated {spec.GeneratedUtc:yyyy-MM-dd HH:mm} UTC by {spec.AuthorLabel}",
                    subFont, grey, new XRect(40, y, LetterWidthPt - 80, 16), XStringFormats.TopLeft);
                y += 18;
                gfx.DrawString(
                    "Review set from the current KOR Standard Details store. Mark up in Bluebeam; record decisions in the app. Not the governed master.",
                    subFont, grey, new XRect(40, y, LetterWidthPt - 80, 16), XStringFormats.TopLeft);
                y += 30;
            }
            else
            {
                gfx.DrawString($"{spec.Title} — index (continued)", subFont, grey, new XRect(40, y, LetterWidthPt - 80, 16), XStringFormats.TopLeft);
                y += 26;
            }

            var first = p * IndexLinesPerPage;
            var slice = spec.Items.Skip(first).Take(IndexLinesPerPage).ToList();
            var half = (int)Math.Ceiling(slice.Count / 2.0);
            var colWidth = (LetterWidthPt - 80) / 2;
            for (var i = 0; i < slice.Count; i++)
            {
                var col = i < half ? 0 : 1;
                var row = i < half ? i : i - half;
                var x = 40 + (col * colWidth);
                var ly = y + (row * 12.5);
                var item = slice[i];
                var missing = item.Pdf is { Length: > 0 } ? "" : "  (no stored PDF)";
                gfx.DrawString($"{first + i + 1,3}.", lineFont, grey, new XRect(x, ly, 22, 12), XStringFormats.TopLeft);
                gfx.DrawString(item.DetailNumber, numFont, ink, new XRect(x + 24, ly, 74, 12), XStringFormats.TopLeft);
                gfx.DrawString(Truncate(item.Title, 60) + missing, lineFont, ink, new XRect(x + 100, ly, colWidth - 104, 12), XStringFormats.TopLeft);
            }

            DrawStamp(gfx, spec, LetterWidthPt, LetterHeightPt);
        }
    }

    private static void DrawDetailPage(PdfDocument document, ReviewSetSpec spec, ReviewSetItem item, int ordinal)
    {
        XPdfForm? form = null;
        MemoryStream? stream = null;
        if (item.Pdf is { Length: > 0 })
        {
            try
            {
                stream = new MemoryStream(item.Pdf);
                form = XPdfForm.FromStream(stream);
                if (form.PointWidth <= 0 || form.PointHeight <= 0)
                {
                    form.Dispose();
                    form = null;
                }
            }
            catch
            {
                // A bad stored PDF gets a placeholder page, not a broken set.
                form?.Dispose();
                form = null;
            }
        }

        var artWidth = form?.PointWidth ?? LetterWidthPt;
        var artHeight = form?.PointHeight ?? (LetterHeightPt - HeaderPt);

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(artWidth);
        page.Height = XUnit.FromPoint(artHeight + HeaderPt);

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            DrawHeader(gfx, item, ordinal, spec.Items.Count, artWidth);
            var box = new XRect(0, HeaderPt, artWidth, artHeight);
            if (form is not null)
            {
                gfx.DrawImage(form, box);
            }
            else
            {
                DrawPlaceholder(gfx, item, box);
            }

            DrawStamp(gfx, spec, artWidth, artHeight + HeaderPt);
        }

        form?.Dispose();
        stream?.Dispose();
    }

    private static void DrawHeader(XGraphics gfx, ReviewSetItem item, int ordinal, int total, double widthPt)
    {
        var band = new XSolidBrush(XColor.FromArgb(255, 238, 241, 244));
        gfx.DrawRectangle(band, 0, 0, widthPt, HeaderPt);
        var rule = new XPen(XColor.FromArgb(255, 200, 206, 212), 0.6);
        gfx.DrawLine(rule, 0, HeaderPt - 0.5, widthPt, HeaderPt - 0.5);

        var numFont = new XFont("Arial", 12, XFontStyleEx.Bold);
        var titleFont = new XFont("Arial", 10, XFontStyleEx.Regular);
        var metaFont = new XFont("Arial", 8, XFontStyleEx.Regular);
        var ink = new XSolidBrush(XColor.FromArgb(255, 34, 40, 46));
        var grey = new XSolidBrush(XColor.FromArgb(255, 108, 117, 128));

        gfx.DrawString(item.DetailNumber, numFont, ink, new XRect(10, 5, 110, 16), XStringFormats.TopLeft);
        gfx.DrawString(Truncate(item.Title, 110), titleFont, ink, new XRect(120, 6, Math.Max(40, widthPt - 330), 14), XStringFormats.TopLeft);
        var meta = string.Join("  ·  ", new[] { item.Discipline, item.Kind, item.Status }.Where(x => !string.IsNullOrWhiteSpace(x)));
        gfx.DrawString(meta, metaFont, grey, new XRect(120, 20, Math.Max(40, widthPt - 330), 11), XStringFormats.TopLeft);
        gfx.DrawString($"{ordinal} of {total}", metaFont, grey, new XRect(widthPt - 200, 12, 190, 11), XStringFormats.TopRight);
    }

    private static void DrawPlaceholder(XGraphics gfx, ReviewSetItem item, XRect box)
    {
        var pen = new XPen(XColor.FromArgb(180, 145, 153, 163), 0.8);
        var brush = new XSolidBrush(XColor.FromArgb(245, 248, 250, 252));
        gfx.DrawRectangle(pen, brush, new XRect(box.X + 20, box.Y + 20, Math.Max(0, box.Width - 40), Math.Max(0, box.Height - 40)));
        var font = new XFont("Arial", 12, XFontStyleEx.Bold);
        var note = new XFont("Arial", 9.5, XFontStyleEx.Regular);
        var ink = new XSolidBrush(XColor.FromArgb(255, 76, 86, 96));
        gfx.DrawString(item.DetailNumber, font, ink, new XRect(box.X, box.Y + (box.Height / 2) - 16, box.Width, 16), XStringFormats.Center);
        gfx.DrawString("no PDF captured for this detail yet", note, ink, new XRect(box.X, box.Y + (box.Height / 2) + 2, box.Width, 14), XStringFormats.Center);
    }

    private static void DrawStamp(XGraphics gfx, ReviewSetSpec spec, double widthPt, double heightPt)
    {
        var font = new XFont("Arial", 6.5, XFontStyleEx.Regular);
        var brush = new XSolidBrush(XColor.FromArgb(255, 120, 128, 138));
        // A narrow detail page cannot carry the full line; it keeps the part that matters.
        var text = widthPt < 560
            ? $"REVIEW SET — not the governed master — {spec.GeneratedUtc:yyyy-MM-dd}"
            : $"REVIEW SET — {spec.Title} — generated {spec.GeneratedUtc:yyyy-MM-dd} by {spec.AuthorLabel} from the KOR Standard Details store. Not the governed master.";
        gfx.DrawString(text, font, brush, new XRect(8, heightPt - 11, Math.Max(0, widthPt - 16), 9), XStringFormats.CenterLeft);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..(max - 1)] + "…";

    internal static double ToPoints(double millimeters) => millimeters * PointsPerMillimeter;
}
