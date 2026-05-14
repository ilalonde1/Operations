#nullable enable
using Kor.Operations.Mcp.Smoke;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Smoke;

public sealed class SmokeCoverageValidatorTests
{
    [Fact]
    public void Validate_AllRegisteredToolsHaveCalibrators_Passes()
    {
        SmokeCoverageValidator.Validate(
            ["get_cash_position", "get_ar"],
            ["get_ar", "get_cash_position"],
            []);
    }

    [Fact]
    public void Validate_RegisteredToolWithoutCalibratorAndNotExempt_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SmokeCoverageValidator.Validate(
                ["get_cash_position", "get_new_tool"],
                ["get_cash_position"],
                []));

        Assert.Contains("Smoke coverage gap", ex.Message, StringComparison.Ordinal);
        Assert.Contains("get_new_tool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ExemptToolWithoutCalibrator_Passes()
    {
        SmokeCoverageValidator.Validate(
            ["get_cash_position", "query_kor_data"],
            ["get_cash_position"]);
    }

    [Fact]
    public void Validate_CalibratorReferencesUnregisteredTool_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SmokeCoverageValidator.Validate(
                ["get_cash_position"],
                ["get_cash_position", "get_removed_tool"],
                []));

        Assert.Contains("Smoke coverage drift", ex.Message, StringComparison.Ordinal);
        Assert.Contains("get_removed_tool", ex.Message, StringComparison.Ordinal);
    }
}
