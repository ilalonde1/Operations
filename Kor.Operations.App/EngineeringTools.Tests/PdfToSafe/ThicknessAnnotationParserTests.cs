using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class ThicknessAnnotationParserTests
{
    //  TryParse 

    [Theory]
    [InlineData("T=200", 200.0)]
    [InlineData("Ts=250", 250.0)]
    [InlineData("TS=300mm", 300.0)]
    [InlineData("200 THK", 200.0)]
    [InlineData("175 THK", 175.0)]
    [InlineData("300 SLAB", 300.0)]
    [InlineData("250.5 SLAB", 250.5)]
    [InlineData("200mm", 200.0)]
    [InlineData("250.5mm", 250.5)]
    public void TryParse_ValidMetric_ReturnsMillimetres(string input, double expected)
    {
        Assert.Equal(expected, ThicknessAnnotationParser.TryParse(input));
    }

    [Fact]
    public void TryParse_ImperialInches_ConvertsToMm()
    {
        // 8" THK  8  25.4 = 203.2mm
        double? result = ThicknessAnnotationParser.TryParse("8\" THK");
        Assert.NotNull(result);
        Assert.Equal(203.2, result!.Value, precision: 1);
    }

    [Theory]
    [InlineData("8\" SLAB", 203.2)]
    [InlineData("200\" SLAB", 200.0)]
    public void TryParse_SlabCalloutBranch_preserves_existing_annotation_behavior(string input, double expected)
    {
        Assert.Equal(expected, ThicknessAnnotationParser.TryParse(input)!.Value, precision: 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("BEAM 300x600")]
    [InlineData("SLAB 24\"")]
    public void TryParse_NoMatch_ReturnsNull(string? input)
    {
        Assert.Null(ThicknessAnnotationParser.TryParse(input!));
    }

    [Theory]
    [InlineData("30 THK")]
    [InlineData("2500mm")]
    [InlineData("T=10")]
    public void TryParse_OutOfRange_ReturnsNull(string input)
    {
        Assert.Null(ThicknessAnnotationParser.TryParse(input));
    }

    //  AssignToSlabs 

    [Fact]
    public void AssignToSlabs_NearestAnnotationWins()
    {
        // Slab centred at (500, 500)
        var slabs = new List<List<(double X, double Y)>>
        {
            new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }
        };
        var annotations = new List<(string Text, double X, double Y)>
        {
            ("200 THK", 600, 600),
            ("300 THK", 9000, 9000)
        };

        var result = ThicknessAnnotationParser.AssignToSlabs(slabs, annotations);
        Assert.Equal(200.0, result[0]);
    }

    [Fact]
    public void AssignToSlabs_AnnotationOutOfRadius_ReturnsNull()
    {
        var slabs = new List<List<(double X, double Y)>>
        {
            new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }
        };
        var annotations = new List<(string Text, double X, double Y)>
        {
            ("200 THK", 50000, 50000)
        };

        Assert.Null(ThicknessAnnotationParser.AssignToSlabs(slabs, annotations)[0]);
    }

    [Fact]
    public void AssignToSlabs_NoAnnotations_AllNull()
    {
        var slabs = new List<List<(double X, double Y)>>
        {
            new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) }
        };
        var empty = new List<(string Text, double X, double Y)>();

        Assert.Null(ThicknessAnnotationParser.AssignToSlabs(slabs, empty)[0]);
    }
}
