using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

/// <summary>
/// Rasterizes a PDF's pages to the <c>p-NN.png</c> files the slab takeoff measures off — so the tool renders
/// its own pages instead of dying when a folder of pre-made PNGs isn't supplied. Uses Docnet.Core (bundled
/// PDFium, no system install). Renders at the SAME dpi the engine's scale math assumes, so the pixel→area
/// conversion stays correct, and composites onto white so the poché flood reads filled regions, not
/// transparency. Idempotent: a page whose PNG already exists is left alone, so re-runs are instant.
/// </summary>
internal static class PlanPdfRenderer
{
    /// <summary>
    /// One page as an image at the dpi given, flattened onto white, for drawing an overlay on. The
    /// caller owns the image. Pixel (0,0) is the page's top-left; a PDF point (x, y) with origin
    /// bottom-left lands at (x · dpi/72, (H − y) · dpi/72).
    /// </summary>
    public static Image<Rgba32> RenderPage(string pdfPath, int page, double dpi)
    {
        double scaling = dpi / 72.0;
        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scaling));
        using var pageReader = docReader.GetPageReader(page - 1);
        int w = pageReader.GetPageWidth();
        int h = pageReader.GetPageHeight();
        byte[] bgra = pageReader.GetImage();
        var img = Image.LoadPixelData<Bgra32>(bgra, w, h);
        img.Mutate(c => c.BackgroundColor(Color.White));
        var rgba = img.CloneAs<Rgba32>();
        img.Dispose();
        return rgba;
    }

    public static int RenderMissing(string pdfPath, string pngDir, double dpi, int? firstPage, int? lastPage)
    {
        Directory.CreateDirectory(pngDir);
        double scaling = dpi / 72.0;   // PDFium's baseline is 72 dpi; scale up to the engine's render dpi.

        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scaling));
        int pageCount = docReader.GetPageCount();
        int first = Math.Max(1, firstPage ?? 1);
        int last = Math.Min(pageCount, lastPage ?? pageCount);

        int rendered = 0;
        for (int page = first; page <= last; page++)
        {
            string outPath = Path.Combine(pngDir, $"p-{page:D2}.png");
            if (File.Exists(outPath)) continue;

            using var pageReader = docReader.GetPageReader(page - 1);   // PDFium is 0-based
            int w = pageReader.GetPageWidth();
            int h = pageReader.GetPageHeight();
            byte[] bgra = pageReader.GetImage();                        // BGRA, w*h*4

            using var img = Image.LoadPixelData<Bgra32>(bgra, w, h);
            img.Mutate(c => c.BackgroundColor(Color.White));            // flatten transparency → white page
            img.SaveAsPng(outPath);
            rendered++;
        }
        return rendered;
    }
}
