#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class RebarOverlayGeneratorTests
{
    // Realistic 2-page sheet: page 1 carries the call-out line + a cross-ref to S5.03 AND its own
    // title-block number (bottom-right, large) — so own-sheet resolves by the title block, not by
    // token frequency. Page 2 is the S5.03 detail.
    private static byte[] Pdf(string callouts)
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var p1 = b.AddPage(600, 800);
        p1.AddText($"S2.01.1 SLAB REINFORCING {callouts} S5.03 typ", 12, new PdfPoint(50, 700), f);
        p1.AddText("S2.01.1", 20, new PdfPoint(500, 40), f); // title-block sheet number, bottom-right
        var p2 = b.AddPage(600, 800);
        p2.AddText("S5.03 S5.03 S5.03 typical detail referenced everywhere", 12, new PdfPoint(50, 700), f);
        p2.AddText("S5.03", 20, new PdfPoint(500, 40), f);
        return b.Build();
    }

    [Fact]
    public void MetricOverlayProducesCoverPlusIftIfcPair()
    {
        var bytes = RebarOverlayGenerator.Build(Pdf("15M @ 200"), Pdf("15M @ 150"), "31065", "IFT", "IFC");
        Assert.True(bytes.Length > 1000);
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(3, doc.NumberOfPages); // cover + IFT(S2.01.1) + IFC(S2.01.1)
    }

    [Fact]
    public void ImperialOverlayWorksToo()
    {
        var bytes = RebarOverlayGenerator.Build(Pdf("#5 @ 12"), Pdf("#5 @ 10"), "Lindley", "IFT", "IFC", UnitSystem.Imperial);
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(3, doc.NumberOfPages);
    }

    [Fact]
    public void AddedOnlySheetEmitsOnlyTheAfterPageNotABlankBeforePage()
    {
        // before has one 15M@200; after has two. Net change is +1 added, 0 removed — so there is
        // nothing to box in red. The before page must NOT be emitted (it would be a blank
        // "removed in RED" page). Expect cover + the green after page only.
        var before = Pdf("15M @ 200");
        var after = Pdf2x("15M @ 200");
        var bytes = RebarOverlayGenerator.Build(before, after, "31065", "IFT", "IFC");
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(2, doc.NumberOfPages); // cover + after; no blank before page
    }

    // Page 1 carries the call-out twice (count 2) so a single-count before page reads as +1 added.
    private static byte[] Pdf2x(string callouts)
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var p1 = b.AddPage(600, 800);
        p1.AddText($"S2.01.1 SLAB REINFORCING {callouts} and {callouts} S5.03 typ", 12, new PdfPoint(50, 700), f);
        p1.AddText("S2.01.1", 20, new PdfPoint(500, 40), f);
        var p2 = b.AddPage(600, 800);
        p2.AddText("S5.03 S5.03 S5.03 typical detail referenced everywhere", 12, new PdfPoint(50, 700), f);
        p2.AddText("S5.03", 20, new PdfPoint(500, 40), f);
        return b.Build();
    }

    [Fact]
    public void NoChangesProducesCoverOnly()
    {
        var bytes = RebarOverlayGenerator.Build(Pdf("15M @ 200"), Pdf("15M @ 200"), "31065", "IFT", "IFC");
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(1, doc.NumberOfPages); // cover only — nothing changed
    }

    // Regression guard for the own-sheet bug caught by visual verification: on a details sheet the
    // own number recurs in every detail bubble while a cross-reference appears once; the title-block
    // number (largest, bottom-right) must still win.
    [Fact]
    public void DetailsSheetOwnedByTitleBlockNotByRarestToken()
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var p1 = b.AddPage(600, 800);
        // S6.03 recurs (detail bubbles), S1.07 appears once (cross-ref). Frequency would pick S1.07.
        p1.AddText("S6.03 S6.03 S6.03 S6.03 DETAILS 15M @ 200 S1.07 see", 12, new PdfPoint(50, 700), f);
        p1.AddText("S6.03", 20, new PdfPoint(500, 40), f); // title block
        var before = b.Build();

        var b2 = new PdfDocumentBuilder();
        var f2 = b2.AddStandard14Font(Standard14Font.Helvetica);
        var p2 = b2.AddPage(600, 800);
        p2.AddText("S6.03 S6.03 S6.03 S6.03 DETAILS 15M @ 150 S1.07 see", 12, new PdfPoint(50, 700), f2);
        p2.AddText("S6.03", 20, new PdfPoint(500, 40), f2);
        var after = b2.Build();

        var bytes = RebarOverlayGenerator.Build(before, after, "31065", "IFT", "IFC");
        using var doc = PdfDocument.Open(bytes);
        // The page text must reference S6.03 (correct owner), not S1.07.
        var pageText = doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text);
        Assert.Contains("S6.03", pageText);
    }
}
