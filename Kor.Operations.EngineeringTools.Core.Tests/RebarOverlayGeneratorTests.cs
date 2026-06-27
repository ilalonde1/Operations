#nullable enable

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
    // Build a 2-page synthetic sheet: page 1 owns S2.01.1 and carries the call-out line;
    // page 2 is the cross-referenced S5.03 detail so the own-sheet rule resolves cleanly.
    private static byte[] Pdf(string sheetLine)
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var p1 = b.AddPage(600, 800);
        p1.AddText(sheetLine, 12, new PdfPoint(50, 700), f);
        var p2 = b.AddPage(600, 800);
        p2.AddText("S5.03 S5.03 S5.03 typical detail referenced everywhere", 12, new PdfPoint(50, 700), f);
        return b.Build();
    }

    [Fact]
    public void MetricOverlayProducesCoverPlusIftIfcPair()
    {
        var before = Pdf("S2.01.1 SLAB REINFORCING 15M @ 200 S5.03 typ");
        var after = Pdf("S2.01.1 SLAB REINFORCING 15M @ 150 S5.03 typ");

        var bytes = RebarOverlayGenerator.Build(before, after, "31065", "IFT", "IFC");

        Assert.True(bytes.Length > 1000);
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(3, doc.NumberOfPages); // cover + IFT(S2.01.1) + IFC(S2.01.1)
    }

    [Fact]
    public void ImperialOverlayWorksToo()
    {
        var before = Pdf("S2.01.1 SLAB REINF #5 @ 12 S5.03 typ");
        var after = Pdf("S2.01.1 SLAB REINF #5 @ 10 S5.03 typ");

        var bytes = RebarOverlayGenerator.Build(before, after, "Lindley", "IFT", "IFC", UnitSystem.Imperial);

        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(3, doc.NumberOfPages);
    }

    [Fact]
    public void NoChangesProducesCoverOnly()
    {
        var same = Pdf("S2.01.1 SLAB REINFORCING 15M @ 200 S5.03 typ");
        var bytes = RebarOverlayGenerator.Build(same, same, "31065", "IFT", "IFC");
        using var doc = PdfDocument.Open(bytes);
        Assert.Equal(1, doc.NumberOfPages); // cover only — nothing changed
    }
}
