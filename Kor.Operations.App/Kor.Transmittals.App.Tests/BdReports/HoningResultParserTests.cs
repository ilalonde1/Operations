#nullable enable
using Kor.Opportunities.Data.BdReports;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class HoningResultParserTests
{
    [Fact]
    public void ContractShape_FieldsUnderHoningPass_AreRead()
    {
        const string json = """
        {
          "_providerName": "ProjectBriefHoning",
          "id": 12345,
          "projectName": "Example Secondary School Replacement",
          "honingPass": {
            "verdict": "PURSUE",
            "overallConfidence": 0.8,
            "description": "desc text",
            "status": "RFP stage",
            "korAngle": "angle text"
          }
        }
        """;

        var result = HoningResultParser.Parse(json);

        Assert.Equal("PURSUE", result.Verdict);
        Assert.Equal("angle text", result.KorAngle);
        Assert.Equal("RFP stage", result.Status);
        Assert.Equal("desc text", result.Description);
        Assert.Equal(0.8, result.OverallConfidence);
    }

    [Fact]
    public void LegacyShapeA_FirstPassLike_NoVerdict_RootFieldsRead()
    {
        const string json = """
        {
          "overallConfidence": 0.7,
          "description": "first-pass description",
          "korAngle": "first-pass angle",
          "status": "Planning"
        }
        """;

        var result = HoningResultParser.Parse(json);

        Assert.Null(result.Verdict);
        Assert.Equal("first-pass angle", result.KorAngle);
        Assert.Equal("Planning", result.Status);
        Assert.Equal("first-pass description", result.Description);
        Assert.Equal(0.7, result.OverallConfidence);
    }

    [Fact]
    public void LegacyShapeB_TopLevelVerdict_IsRead()
    {
        const string json = """{ "verdict": "DEAD", "korAngle": "Stantec-captive" }""";

        var result = HoningResultParser.Parse(json);

        Assert.Equal("DEAD", result.Verdict);
        Assert.Equal("Stantec-captive", result.KorAngle);
    }

    [Fact]
    public void LegacyShapeC_NestedHoningPass_WithRootMetadata_HoningPassWins()
    {
        const string json = """
        {
          "id": 99,
          "projectName": "X",
          "proponentName": "Y",
          "province": "BC",
          "status": "root status",
          "honingPass": { "verdict": "MONITOR", "status": "honing status" }
        }
        """;

        var result = HoningResultParser.Parse(json);

        Assert.Equal("MONITOR", result.Verdict);
        Assert.Equal("honing status", result.Status);
    }

    [Fact]
    public void PerFieldFallback_HoningPassMissingField_FallsBackToRoot()
    {
        const string json = """
        {
          "korAngle": "root angle",
          "honingPass": { "verdict": "PURSUE" }
        }
        """;

        var result = HoningResultParser.Parse(json);

        Assert.Equal("PURSUE", result.Verdict);
        Assert.Equal("root angle", result.KorAngle);
    }

    [Fact]
    public void EmptyOrWhitespaceFieldInHoningPass_FallsBackToRoot()
    {
        const string json = """
        {
          "status": "root status",
          "honingPass": { "verdict": "PURSUE", "status": "  " }
        }
        """;

        var result = HoningResultParser.Parse(json);

        Assert.Equal("root status", result.Status);
    }

    [Fact]
    public void OverallConfidenceAsString_IsParsedInvariant()
    {
        const string json = """{ "honingPass": { "verdict": "PURSUE", "overallConfidence": "0.65" } }""";

        var result = HoningResultParser.Parse(json);

        Assert.Equal(0.65, result.OverallConfidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ truncated")]
    [InlineData("[ { \"verdict\": \"PURSUE\" } ]")]
    [InlineData("\"just a string\"")]
    public void MalformedOrNonObjectJson_ReturnsEmpty_NoThrow(string? json)
    {
        var result = HoningResultParser.Parse(json);

        Assert.Null(result.Verdict);
        Assert.Null(result.KorAngle);
        Assert.Null(result.Status);
        Assert.Null(result.Description);
        Assert.Null(result.OverallConfidence);
    }

    [Fact]
    public void HoningPassNotAnObject_IsIgnored_RootUsed()
    {
        const string json = """{ "verdict": "DISCOVER", "honingPass": "weird" }""";

        var result = HoningResultParser.Parse(json);

        Assert.Equal("DISCOVER", result.Verdict);
    }
}
