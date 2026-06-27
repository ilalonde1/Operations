#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlanVisionParserTests
{
    [Fact]
    public void Parse_FullSheet_ReadsKindScaleAndPlates()
    {
        const string json = """
        {
          "kind": "Framing",
          "scaleNote": "1/8\"=1'-0\"",
          "plates": [
            { "level": "L17-28", "count": 12, "element": "Slab", "thicknessIn": 8,
              "box": [0.08, 0.16, 0.46, 0.78], "confidence": 0.92 },
            { "level": "L16", "count": 1, "element": "Slab", "thicknessIn": 8,
              "box": [0.54, 0.16, 0.92, 0.78], "confidence": 0.9 }
          ]
        }
        """;

        var r = PlanVisionParser.Parse(json);

        Assert.Equal(SheetKind.Framing, r.Kind);
        Assert.Equal("1/8\"=1'-0\"", r.ScaleNote);
        Assert.Equal(2, r.Plates.Count);
        var p = r.Plates[0];
        Assert.Equal("L17-28", p.Level);
        Assert.Equal(12, p.Count);
        Assert.Equal(TakeoffElementType.Slab, p.Element);
        Assert.Equal(8, p.ThicknessIn);
        Assert.Equal(0.08, p.NormX0, 3);
        Assert.Equal(0.46, p.NormX1, 3);
        Assert.Equal(0.92, p.Confidence, 3);
    }

    [Fact]
    public void Parse_UnknownEnums_DegradeSafely()
    {
        const string json = """
        { "kind": "Squiggle", "plates": [ { "level": "X", "element": "Mystery", "box": [0,0,1,1], "confidence": 0.5 } ] }
        """;

        var r = PlanVisionParser.Parse(json);

        Assert.Equal(SheetKind.Other, r.Kind);
        Assert.Equal(TakeoffElementType.Slab, r.Plates[0].Element); // default
    }

    [Fact]
    public void Parse_OutOfRangeAndReversedBox_ClampedAndOrdered()
    {
        const string json = """
        { "kind": "Framing", "plates": [ { "level": "L", "element": "Slab", "box": [1.4, 0.9, -0.2, 0.1], "confidence": 2.0 } ] }
        """;

        var p = PlanVisionParser.Parse(json).Plates[0];

        Assert.Equal(0.0, p.NormX0, 6);   // -0.2 clamped to 0, then ordered to be the min
        Assert.Equal(1.0, p.NormX1, 6);   // 1.4 clamped to 1
        Assert.Equal(0.1, p.NormY0, 6);
        Assert.Equal(0.9, p.NormY1, 6);
        Assert.Equal(1.0, p.Confidence, 6); // 2.0 clamped
    }

    [Fact]
    public void Parse_CountBelowOne_BecomesOne_AndZeroThicknessBecomesNull()
    {
        const string json = """
        { "kind": "Foundation", "plates": [ { "level": "Mat", "count": 0, "element": "Foundation", "thicknessIn": 0, "box": [0,0,1,1], "confidence": 0.7 } ] }
        """;

        var p = PlanVisionParser.Parse(json).Plates[0];

        Assert.Equal(1, p.Count);
        Assert.Null(p.ThicknessIn);
        Assert.Equal(TakeoffElementType.Foundation, p.Element);
    }

    [Fact]
    public void Parse_NoPlates_ReturnsEmpty()
    {
        var r = PlanVisionParser.Parse("""{ "kind": "Detail" }""");
        Assert.Equal(SheetKind.Detail, r.Kind);
        Assert.Empty(r.Plates);
    }

    [Theory]
    [InlineData("[1,2,3]")]   // array root
    [InlineData("\"oops\"")]  // scalar root
    [InlineData("42")]
    public void Parse_NonObjectRoot_ReturnsEmptyInsteadOfThrowing(string json)
    {
        // A malformed-but-valid response must not abort a whole batch — it yields no plates.
        var r = PlanVisionParser.Parse(json);
        Assert.Equal(SheetKind.Other, r.Kind);
        Assert.Empty(r.Plates);
    }

    [Fact]
    public void Parse_MissingBox_IsDegenerate_SoCallerSkipsItNotWholeSheet()
    {
        // No box → a degenerate (zero-area) box, NOT the whole sheet. The caller detects
        // x1<=x0 / y1<=y0 and skips the plate rather than measuring across the entire page.
        const string json = """
        { "kind": "Framing", "plates": [ { "level": "L", "element": "Slab", "confidence": 0.8 } ] }
        """;

        var p = PlanVisionParser.Parse(json).Plates[0];

        Assert.True(p.NormX1 <= p.NormX0 || p.NormY1 <= p.NormY0, "missing box must be degenerate");
    }
}
