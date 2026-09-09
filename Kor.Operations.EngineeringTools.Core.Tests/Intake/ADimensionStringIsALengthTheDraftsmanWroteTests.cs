#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A dimension string is a length the drafter wrote (brief 27); between two grid axes it is a claim
/// the drawing makes about itself, and the axes' spacing at the sheet's scale is the check.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the parse of feet-inches, bare inches and bare millimetres; the tightest axis
/// span that agrees with the value, the adjacent pair when none does, nothing outside the grid; tall
/// (rotated) text against the horizontal axes; words in furniture excluded. WHAT IT DOES NOT: real
/// sheets — on the five schedule pages the drafter dimensions members, not the grid, and
/// FiveStickFilesTests banks how many strings each page types; the dimension LINE, which is not read.
/// </remarks>
public sealed class ADimensionStringIsALengthTheDraftsmanWroteTests
{
    private static TT Tok(string text, double x, double y, double w = 20, double h = 8) => new(text, x, y, x - w / 2, y - h / 2, x + w / 2, y + h / 2);
    private static GridAxis X(string name, double atMm) => new(name, Vertical: true, AtMm: atMm);
    private static GridAxis Y(string name, double atMm) => new(name, Vertical: false, AtMm: atMm);
    private const double MmPerPt = 25.4 / 72 * 96;   // 1:96

    [Theory]
    [InlineData("12'-6\"", 3810, false)]
    [InlineData("12'-6 1/2\"", 3822.7, false)]
    [InlineData("0'-8\"", 203.2, false)]
    [InlineData("8\"", 203.2, false)]
    [InlineData("6 1/2\"", 165.1, false)]
    [InlineData("7200", 7200, true)]
    public void TheParseGivesMillimetresAndSaysWhetherTheNumberWasBare(string text, double mm, bool bare)
    {
        var parsed = DimensionStrings.Parse(text);
        Assert.NotNull(parsed);
        Assert.Equal(mm, parsed!.Value.Mm, 1);
        Assert.Equal(bare, parsed.Value.BareNumber);
    }

    [Theory]
    [InlineData("PC1")]
    [InlineData("12")]
    [InlineData("1/8\" = 1'-0\"")]
    [InlineData("123456")]
    public void WhatIsNotALengthDoesNotParse(string text) => Assert.Null(DimensionStrings.Parse(text));

    [Fact]
    public void TheTightestAgreeingSpanIsTheAxesTheTextSitsBetween()
    {
        // axes 1, 2, 3 at 0, 6,000 and 12,000 mm; text at x = 3,000 mm (between 1 and 2) and at 6,500 (between 2 and 3)
        var axes = new List<GridAxis> { X("1", 0), X("2", 6000), X("3", 12000), Y("A", 0), Y("B", 9000) };
        var page = new PC(1, 3024, 2160, new List<TT>
        {
            Tok("19'-8\"", 3000 / MmPerPt, 500),     // 5,994 mm: agrees with 1–2
            Tok("39'-4\"", 6500 / MmPerPt, 500),     // 11,989 mm: the wider span 1–3 agrees
            Tok("22\"", 3000 / MmPerPt, 300),         // 559 mm between 1 and 2: a member's size, not the grid
            Tok("29'-6\"", 4000 / MmPerPt, 200, 8, 40),   // tall text at y = 6,773 mm, between A and B; 8,992 mm: the horizontal axes A–B agree
            Tok("10'-0\"", 20000 / MmPerPt, 500),    // beyond the last axis: not on the grid
        }, new List<GP>());

        var dims = DimensionStrings.Read(page, axes, MmPerPt);
        Assert.Equal(5, dims.Count);
        var d12 = dims.Single(d => d.Text == "19'-8\"");
        Assert.True(d12.Agrees); Assert.Equal(("1", "2"), (d12.SpansFrom, d12.SpansTo));
        var d13 = dims.Single(d => d.Text == "39'-4\"");
        Assert.True(d13.Agrees); Assert.Equal(("1", "3"), (d13.SpansFrom, d13.SpansTo));
        var member = dims.Single(d => d.Text == "22\"");
        Assert.True(member.Disagrees); Assert.Equal(("1", "2"), (member.SpansFrom, member.SpansTo)); Assert.Equal(6000, member.AxisGapMm);
        var tall = dims.Single(d => d.Text == "29'-6\"");
        Assert.True(tall.Vertical); Assert.True(tall.Agrees); Assert.Equal(("A", "B"), (tall.SpansFrom, tall.SpansTo));
        var outside = dims.Single(d => d.Text == "10'-0\"");
        Assert.Null(outside.SpansFrom); Assert.False(outside.Agrees); Assert.False(outside.Disagrees);
    }

    [Fact]
    public void AWordInsideFurnitureIsNotADimension()
    {
        var axes = new List<GridAxis> { X("1", 0), X("2", 6000) };
        var furniture = new SheetFurniture.Set(
            new[] { new SheetFurniture.Region("schedule: COLUMN SCHEDULE", 100, 100, 200, 200) }, new List<double>(), new List<double>(), 1.5, new List<(double, double)>(), 1);
        var page = new PC(1, 3024, 2160, new List<TT> { Tok("12\"", 150, 150), Tok("12\"", 3000 / MmPerPt, 500) }, new List<GP>());
        var dims = DimensionStrings.Read(page, axes, MmPerPt, furniture);
        Assert.Single(dims);
        Assert.Equal(1, dims[0].WordIndex);
    }
}
