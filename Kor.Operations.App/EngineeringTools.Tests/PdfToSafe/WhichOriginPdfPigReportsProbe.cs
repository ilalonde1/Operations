using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// WHY THE MEDIABOX FIX DID NOTHING, WHICH IS NOT THE SAME AS THE HYPOTHESIS BEING WRONG.
///
/// Codex's audit places the five misplaced Parcel 11 markups on the /Vertices and /L readers, both
/// of which multiply raw dictionary numbers by the scale and shift nothing. This page's raw
/// MediaBox is [-1728 -1296.12 1728 1296.12] — its origin is the middle of the sheet — so those
/// numbers are ~1728 pt left of where the page content sits.
///
/// I tested exactly that on 29 August and it changed nothing, and concluded the hypothesis was
/// wrong. This asks the narrower question I should have asked: what does PdfPig REPORT as the
/// origin? If its normalised Bounds already reads 0, my conversion was multiplying by zero and the
/// hypothesis was never tested at all.
/// </summary>
public sealed class WhichOriginPdfPigReportsProbe
{
    private readonly ITestOutputHelper _out;
    public WhichOriginPdfPigReportsProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void WhatPdfPigCallsTheOrigin()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var page = doc.GetPage(1);

        _out.WriteLine($"raw file MediaBox   : [-1728 -1296.12 1728 1296.12]   (read from the bytes)");
        _out.WriteLine($"PdfPig MediaBox     : left {page.MediaBox.Bounds.Left,10:N2}  bottom {page.MediaBox.Bounds.Bottom,10:N2}" +
                       $"  right {page.MediaBox.Bounds.Right,10:N2}  top {page.MediaBox.Bounds.Top,10:N2}");
        _out.WriteLine($"PdfPig CropBox      : left {page.CropBox.Bounds.Left,10:N2}  bottom {page.CropBox.Bounds.Bottom,10:N2}");
        _out.WriteLine($"PdfPig page size    : {page.Width:N2} x {page.Height:N2} pt");

        // Where does page CONTENT actually sit, and where do raw annotation Rects sit?
        var paths = page.ExperimentalAccess.Paths;
        if (paths.Count > 0)
        {
            var pts = paths.SelectMany(p => p.SelectMany(sp => sp.Commands
                    .OfType<UglyToad.PdfPig.Core.PdfSubpath.Line>().Select(l => l.To)))
                .ToList();
            if (pts.Count > 0)
                _out.WriteLine($"page content x      : {pts.Min(p => p.X),10:N1} .. {pts.Max(p => p.X),-10:N1} pt");
        }

        var annots = page.ExperimentalAccess.GetAnnotations().ToList();
        if (annots.Count > 0)
            _out.WriteLine($"annotation Rect x   : {annots.Min(a => a.Rectangle.Left),10:N1} .. " +
                           $"{annots.Max(a => a.Rectangle.Right),-10:N1} pt   ({annots.Count} annotations)");
    }
}
