#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PdfReadabilityTests
{
    [Fact]
    public void Vector_set_is_readable()
    {
        // 31065-like: every page hundreds–thousands of words.
        var words = Enumerable.Repeat(1232, 73).ToList();
        var v = PdfReadabilityAssessor.Assess(words);
        Assert.True(v.Readable);
        Assert.Equal(0, v.ImageOnlyPages);
    }

    [Fact]
    public void Flattened_set_is_blind()
    {
        // Granville-like: 4 of 5 pages carry no text layer.
        var words = new List<int> { 0, 0, 0, 0, 1345 };
        var v = PdfReadabilityAssessor.Assess(words);
        Assert.False(v.Readable);
        Assert.Equal(4, v.ImageOnlyPages);
        Assert.Contains("no extractable text layer", v.Reason);
    }

    [Fact]
    public void A_few_image_pages_do_not_condemn_a_real_vector_set()
    {
        // A vector set with a rendered cover + a photo page, the rest real drawings — still readable.
        var words = new List<int> { 0, 0 }.Concat(Enumerable.Repeat(900, 10)).ToList();
        Assert.True(PdfReadabilityAssessor.Assess(words).Readable);
    }

    [Fact]
    public void Sparse_but_real_pages_stay_readable_above_the_image_floor()
    {
        // The sparsest genuine vector page measured (31065) was 104 words — above the 25 floor, so a set of
        // such pages is text, not image. Guards against a floor set so high it rejects light-text sheets.
        Assert.True(PdfReadabilityAssessor.Assess(Enumerable.Repeat(104, 6).ToList()).Readable);
        Assert.Equal(0, PdfReadabilityAssessor.Assess(Enumerable.Repeat(104, 6).ToList()).ImageOnlyPages);
    }

    [Fact]
    public void All_image_pages_are_blind()
    {
        var v = PdfReadabilityAssessor.Assess(Enumerable.Repeat(0, 12).ToList());
        Assert.False(v.Readable);
        Assert.Equal(12, v.ImageOnlyPages);
    }

    [Fact]
    public void Empty_range_is_not_readable()
    {
        Assert.False(PdfReadabilityAssessor.Assess(new List<int>()).Readable);
    }

    [Fact]
    public void AssessPageTexts_counts_words_per_page_string()
    {
        // Two rich text pages + three empty (image) pages → majority image-only → blind.
        var pages = new List<string>
        {
            string.Join(" ", Enumerable.Repeat("15M@200", 300)),
            string.Join(" ", Enumerable.Repeat("10M@300", 300)),
            "", "   ", "S-2",
        };
        var v = PdfReadabilityAssessor.AssessPageTexts(pages);
        Assert.False(v.Readable);
        Assert.Equal(3, v.ImageOnlyPages);
    }

    [Fact]
    public void AssessPageTexts_readable_when_pages_carry_text()
    {
        var pages = Enumerable.Range(0, 8)
            .Select(_ => string.Join(" ", Enumerable.Repeat("callout", 400))).ToList();
        Assert.True(PdfReadabilityAssessor.AssessPageTexts(pages).Readable);
    }
}
