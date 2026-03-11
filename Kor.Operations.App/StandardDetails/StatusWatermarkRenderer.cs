using System;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kor.Operations.StandardDetails;

internal static class StatusWatermarkRenderer
{
    public static bool TryPrepareOpenCopy(
        string sourcePath,
        string watermarkText,
        out string launchPath,
        out string warningMessage)
    {
        launchPath = sourcePath;
        warningMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return false;

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Temp",
            "StandardDetailsWatermarked");

        Directory.CreateDirectory(outputDir);

        var safeBase = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var statusTag = SanitizeFileName(watermarkText);
        var outputName = $"{safeBase} [{statusTag}] {DateTime.Now:yyyyMMdd_HHmmss}{ext}";
        var outputPath = Path.Combine(outputDir, outputName);

        try
        {
            if (ext == ".pdf")
            {
                CreateWatermarkedPdf(sourcePath, outputPath, watermarkText);
            }
            else
            {
                // Non-PDF formats (e.g., DWG, DOCX) are copied to a status-tagged temp file.
                // We cannot safely draw in-file watermarks for these formats without format-specific libraries.
                File.Copy(sourcePath, outputPath, overwrite: false);
                warningMessage = "Visual watermark is currently applied to PDF only; opened a status-tagged copy for this file type.";
            }

            launchPath = outputPath;
            return true;
        }
        catch (Exception ex)
        {
            warningMessage = $"Could not prepare watermarked copy: {ex.Message}";
            launchPath = sourcePath;
            return false;
        }
    }

    private static void CreateWatermarkedPdf(string sourcePath, string outputPath, string watermarkText)
    {
        using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        using var target = new PdfDocument();

        foreach (var srcPage in source.Pages)
        {
            var page = target.AddPage(srcPage);
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var fontSize = Math.Max(36, Math.Min(96, page.Width.Point / 10.0));
            var font = new XFont("Arial", fontSize, XFontStyleEx.Bold);
            var brush = new XSolidBrush(XColor.FromArgb(56, 185, 0, 0));

            var state = gfx.Save();
            gfx.TranslateTransform(page.Width.Point / 2.0, page.Height.Point / 2.0);
            gfx.RotateTransform(-35);

            // Repeat text so it is obvious regardless of zoom/crop.
            for (double y = -page.Height.Point; y <= page.Height.Point; y += fontSize * 2.2)
            {
                for (double x = -page.Width.Point; x <= page.Width.Point; x += fontSize * 5.5)
                {
                    gfx.DrawString(watermarkText, font, brush, new XPoint(x, y), XStringFormats.Center);
                }
            }

            gfx.Restore(state);
        }

        target.Save(outputPath);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "file";

        var result = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(result) ? "file" : result;
    }
}
