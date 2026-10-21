using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A TABLE ROW IS NOT A CALL-OUT (2026-10-20). The positioned reader took a bar size followed by any number as an
/// intensity call-out, so a splice-length table row "10M 500 19" (575)" on 31065 S1.02 read as 10M@575 - and the change
/// report put a red "removed" box on a table that was the same in both issues. The "@" is what makes it a call-out.
/// WHAT THIS COVERS: RebarPdfReader.Read on a real (generated) PDF page: a size-then-numbers table row gives no hit;
/// "15M @ 200", "15M@ 250", "15M @300" and "15M@450" each give one, with the right key. WHAT IT DOES NOT: plan
/// call-outs (36-15M4700@125, their own grammar, untouched); imperial "#5 @ 12" (same rule, not exercised here); a
/// drawing that writes intensity call-outs with no "@" at all - those are now NOT read, by design, and the measured
/// 31065 run is the check that no real call-out was lost.
/// </summary>
public sealed class ATableRowIsNotACalloutTests
{
    [Fact]
    public void ASizeFollowedByNumbersIsNotACalloutButEveryAtFormIs()
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var p = b.AddPage(800, 400);
        p.AddText("10M 500 19\" (575) 23\" (575)", 10, new PdfPoint(40, 340), f);   // a splice-length table row
        p.AddText("15M @ 200 T&B", 10, new PdfPoint(40, 300), f);
        p.AddText("15M@ 250 EW", 10, new PdfPoint(40, 260), f);
        p.AddText("15M @300 BOT", 10, new PdfPoint(40, 220), f);
        p.AddText("15M@450 TOP", 10, new PdfPoint(40, 180), f);

        using var doc = PdfDocument.Open(b.Build());
        var keys = RebarPdfReader.Read(doc, UnitSystem.Metric).Single().Callouts.Select(h => h.Key).OrderBy(k => k).ToList();

        Assert.Equal(new[] { "15M@200", "15M@250", "15M@300", "15M@450" }, keys);
    }
}
