#nullable enable
using Kor.Operations.Mcp.Ai;
using Xunit;

namespace Kor.Operations.Tests.Mcp;

public sealed class PromptToolParityValidatorTests
{
    [Fact]
    public void Validate_BacktickedPhantomReference_Fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PromptToolParityValidator.Validate(
                ["get_cash_position"],
                "Use `get_cash_position` for cash. Use `get_phantom` for drift."));

        Assert.Contains("get_phantom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_BulletStylePhantomReference_Fails()
    {
        var prompt = """
==== TOOL CATALOG (use these FIRST) ====
  - get_cash_position         cash balance / liquidity questions
  - get_phantom               this tool does not exist

KOR KPI METHODOLOGY
- Cash Position: USE THE `get_cash_position` TOOL.
""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PromptToolParityValidator.Validate(["get_cash_position"], prompt));

        Assert.Contains("get_phantom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TerminalBareBulletPhantomReference_Fails()
    {
        var prompt =
            "==== TOOL CATALOG (use these FIRST) ====\n" +
            "  - get_cash_position         cash balance / liquidity questions\n" +
            "\n" +
            "KOR KPI METHODOLOGY\n" +
            "- Cash Position: USE THE `get_cash_position` TOOL.\n" +
            "- get_phantom";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PromptToolParityValidator.Validate(["get_cash_position"], prompt));

        Assert.Contains("get_phantom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_IncidentalProseMentionWithoutBackticksOrBullet_Passes()
    {
        var prompt = """
==== TOOL CATALOG (use these FIRST) ====
  - get_cash_position         cash balance / liquidity questions

KOR KPI METHODOLOGY
- Cash Position: USE THE `get_cash_position` TOOL.

This prose mentions get_phantom as an example of a typo, but it is not a
catalog bullet and not a backticked tool reference.
""";

        PromptToolParityValidator.Validate(["get_cash_position"], prompt);
    }
}
