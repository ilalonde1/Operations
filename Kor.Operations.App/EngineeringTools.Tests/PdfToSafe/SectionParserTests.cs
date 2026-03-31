using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class ColumnSectionParserTests
{
    //  TryParse: rectangular 

    [Theory]
    [InlineData("600x600", 600, 600)]
    [InlineData("600X800", 600, 800)]
    [InlineData("C1-600x800", 600, 800)]
    [InlineData("C2 450x600", 450, 600)]
    public void TryParse_Rectangular_ReturnsWidthDepth(string input, double w, double d)
    {
        var result = ColumnSectionParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(w, result!.Value.WidthMm, precision: 1);
        Assert.Equal(d, result!.Value.DepthMm, precision: 1);
    }

    [Fact]
    public void TryParse_ImperialRectangular_ConvertsToMm()
    {
        // 12"x18"  304.8 x 457.2
        var result = ColumnSectionParser.TryParse("12\"x18\"");
        Assert.NotNull(result);
        Assert.Equal(304.8, result!.Value.WidthMm, precision: 1);
        Assert.Equal(457.2, result!.Value.DepthMm, precision: 1);
    }

    //  TryParse: circular 

    [Theory]
    [InlineData("Ø500", 500)]
    [InlineData("D=600", 600)]
    [InlineData("dia=500", 500)]
    [InlineData("diameter=800", 800)]
    public void TryParse_Circular_ReturnsDiameterAsWidthAndDepth(string input, double dia)
    {
        var result = ColumnSectionParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(dia, result!.Value.WidthMm, precision: 1);
        Assert.Equal(dia, result!.Value.DepthMm, precision: 1);
    }

    [Fact]
    public void TryParse_Radius_DoublesValue()
    {
        // R=250  diameter=500  W=D=500
        var result = ColumnSectionParser.TryParse("R=250");
        Assert.NotNull(result);
        Assert.Equal(500, result!.Value.WidthMm, precision: 1);
        Assert.Equal(500, result!.Value.DepthMm, precision: 1);
    }

    //  TryParse: edge cases 

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    public void TryParse_NoMatch_ReturnsNull(string? input)
    {
        Assert.Null(ColumnSectionParser.TryParse(input!));
    }

    [Theory]
    [InlineData("50x50")]
    [InlineData("4000x4000")]
    [InlineData("Ø50")]
    public void TryParse_OutOfRange_ReturnsNull(string input)
    {
        Assert.Null(ColumnSectionParser.TryParse(input));
    }

    //  AssignToColumns 

    [Fact]
    public void AssignToColumns_NearestAnnotationWins()
    {
        var columns = new List<(double X, double Y)> { (1000, 1000) };
        var annotations = new List<(string Text, double X, double Y)>
        {
            ("600x600", 1100, 1100),
            ("800x800", 9000, 9000)
        };

        var result = ColumnSectionParser.AssignToColumns(columns, annotations);
        Assert.NotNull(result[0]);
        Assert.Equal(600, result[0]!.Value.WidthMm);
    }

    [Fact]
    public void AssignToColumns_NoAnnotations_AllNull()
    {
        var columns = new List<(double X, double Y)> { (0, 0) };
        var empty = new List<(string Text, double X, double Y)>();
        Assert.Null(ColumnSectionParser.AssignToColumns(columns, empty)[0]);
    }
}

public class BeamSectionParserTests
{
    //  TryParse: W=D format 

    [Fact]
    public void TryParse_ExplicitWD_ReturnsWidthDepth()
    {
        var result = BeamSectionParser.TryParse("W=300 D=600");
        Assert.NotNull(result);
        Assert.Equal(300, result!.Value.WidthMm, precision: 1);
        Assert.Equal(600, result!.Value.DepthMm, precision: 1);
    }

    //  TryParse: NxN format 

    [Fact]
    public void TryParse_WidthByDepth_ReturnsMinMax()
    {
        // 600x300  sorted to (300, 600)
        var result = BeamSectionParser.TryParse("600x300");
        Assert.NotNull(result);
        Assert.Equal(300, result!.Value.WidthMm, precision: 1);
        Assert.Equal(600, result!.Value.DepthMm, precision: 1);
    }

    [Fact]
    public void TryParse_LabelledBeam_ParsesCorrectly()
    {
        var result = BeamSectionParser.TryParse("B1-300x600");
        Assert.NotNull(result);
        Assert.Equal(300, result!.Value.WidthMm, precision: 1);
        Assert.Equal(600, result!.Value.DepthMm, precision: 1);
    }

    [Fact]
    public void TryParse_ImperialBeam_ConvertsToMm()
    {
        // 12"x24"  304.8 x 609.6
        var result = BeamSectionParser.TryParse("12\"x24\"");
        Assert.NotNull(result);
        Assert.Equal(304.8, result!.Value.WidthMm, precision: 1);
        Assert.Equal(609.6, result!.Value.DepthMm, precision: 1);
    }

    //  TryParse: wall patterns 

    [Theory]
    [InlineData("200 WALL", 200)]
    [InlineData("200THK WALL", 200)]
    [InlineData("200 RC WALL", 200)]
    [InlineData("200 RC", 200)]
    public void TryParse_Wall_ReturnsSquareSection(string input, double t)
    {
        var result = BeamSectionParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(t, result!.Value.WidthMm, precision: 1);
        Assert.Equal(t, result!.Value.DepthMm, precision: 1);
    }

    //  TryParse: edge cases 

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SLAB 200")]
    public void TryParse_NoMatch_ReturnsNull(string? input)
    {
        Assert.Null(BeamSectionParser.TryParse(input!));
    }

    //  AssignToLines 

    [Fact]
    public void AssignToLines_NearestAnnotationToMidpoint()
    {
        // Horizontal beam from (0,0) to (2000,0)  midpoint at (1000, 0)
        var lines = new List<List<(double X, double Y)>>
        {
            new() { (0, 0), (2000, 0) }
        };
        var annotations = new List<(string Text, double X, double Y)>
        {
            ("300x600", 1100, 100)
        };

        var result = BeamSectionParser.AssignToLines(lines, annotations);
        Assert.NotNull(result[0]);
        Assert.Equal(300, result[0]!.Value.WidthMm);
        Assert.Equal(600, result[0]!.Value.DepthMm);
    }

    [Fact]
    public void AssignToLines_NoAnnotations_AllNull()
    {
        var lines = new List<List<(double X, double Y)>>
        {
            new() { (0, 0), (2000, 0) }
        };
        var empty = new List<(string Text, double X, double Y)>();
        Assert.Null(BeamSectionParser.AssignToLines(lines, empty)[0]);
    }
}
