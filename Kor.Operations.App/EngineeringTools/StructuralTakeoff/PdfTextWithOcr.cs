#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Kor.Operations.EngineeringTools.RebarChange;

namespace Kor.Operations.EngineeringTools.StructuralTakeoff
{
    /// <summary>Per-page text plus the 1-based numbers of any pages whose text was OCR-recovered
    /// (no embedded text layer) — those are flagged "verify" so a scanned sheet never silently
    /// reads as "no changes".</summary>
    public sealed record PdfReadResult(IReadOnlyList<string> Pages, IReadOnlyList<int> OcrPageNumbers);

    /// <summary>
    /// Reads a drawing PDF into one text string per page. Pages that carry an embedded text layer are
    /// read directly (PdfPig, exact). Pages with effectively no text — a scanned or flattened image
    /// sheet — are rendered and run through the built-in Windows OCR so reinforcing call-outs are
    /// recovered instead of silently lost. WinRT (render + OCR) keeps this in the App layer, alongside
    /// the existing <c>Windows.Data.Pdf</c> use in PdfToSafe; the Core extractor stays net8.0.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.10240.0")]
    public static class PdfTextWithOcr
    {
        public static async Task<PdfReadResult> ReadAsync(string pdfPath, int minWords = 5, uint renderWidth = 2200)
        {
            var pages = PdfPageTextReader.ReadPages(pdfPath).ToList();
            var ocrPages = new List<int>();

            // Only render+OCR if some page actually lacks a text layer (cheap common case: skip all).
            bool anyImageOnly = pages.Any(p => WordCount(p) < minWords);
            var engine = anyImageOnly ? OcrEngine.TryCreateFromUserProfileLanguages() : null;
            if (engine is null) return new PdfReadResult(pages, ocrPages);

            uint cap = OcrEngine.MaxImageDimension;
            uint width = Math.Min(renderWidth, cap);

            var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath);
            var pdf = await PdfDocument.LoadFromFileAsync(storageFile);

            for (int i = 0; i < pages.Count && (uint)i < pdf.PageCount; i++)
            {
                if (WordCount(pages[i]) >= minWords) continue;
                string recovered = await OcrPageAsync(pdf, (uint)i, engine, width, cap);
                if (!string.IsNullOrWhiteSpace(recovered))
                {
                    pages[i] = recovered;
                    ocrPages.Add(i + 1);
                }
            }
            return new PdfReadResult(pages, ocrPages);
        }

        private static int WordCount(string s) =>
            s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

        private static async Task<string> OcrPageAsync(PdfDocument pdf, uint index, OcrEngine engine, uint width, uint cap)
        {
            using var page = pdf.GetPage(index);
            using var stream = new InMemoryRandomAccessStream();

            // Keep the rendered bitmap within the OCR engine's max dimension on the long edge too.
            var size = page.Size;
            double aspect = size.Width > 0 ? size.Height / size.Width : 1.0;
            uint w = width;
            if (w * aspect > cap) w = (uint)Math.Max(1, cap / Math.Max(aspect, 0.0001));

            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = w });
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var raw = await decoder.GetSoftwareBitmapAsync();
            using var bmp = SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bmp);
            return result?.Text ?? string.Empty;
        }
    }
}
