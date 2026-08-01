#nullable enable
using System.Text.Json;
using Kor.Operations.Mcp.Tools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Tools;

public sealed class ToolErrorEnvelopeTests
{
    [Fact]
    public void Build_EmitsCanonicalEnvelopeFields()
    {
        var json = ToolErrorEnvelope.Build("get_billed_pnl", "boom", "DataAccess", recoverable: false, durationMs: 123);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("get_billed_pnl", root.GetProperty("tool").GetString());
        Assert.Equal("boom", root.GetProperty("error").GetString());
        Assert.Equal("DataAccess", root.GetProperty("errorClass").GetString());
        Assert.False(root.GetProperty("recoverable").GetBoolean());
        Assert.Equal(123, root.GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public void Validation_ClassifiesAsRecoverableValidation()
    {
        var json = ToolErrorEnvelope.Validation("get_at_risk_projects", "wbs1 is required.", durationMs: 5);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Validation", root.GetProperty("errorClass").GetString());
        Assert.True(root.GetProperty("recoverable").GetBoolean());
        Assert.Equal(5, root.GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public void Cancelled_ClassifiesAsRecoverableCancelled()
    {
        var json = ToolErrorEnvelope.Cancelled("get_wip", durationMs: 42);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Cancelled", root.GetProperty("errorClass").GetString());
        Assert.True(root.GetProperty("recoverable").GetBoolean());
        Assert.Equal("Query cancelled.", root.GetProperty("error").GetString());
    }

    // OdbcException and SqlException cannot be instantiated from test code
    // (no public/protected ctor accessible), so their DataAccess+nonrecoverable
    // classification is exercised at runtime by the tools' catch blocks
    // rather than in this unit test.
    [Theory]
    [InlineData(typeof(System.ArgumentException), "Validation", true)]
    [InlineData(typeof(System.ArgumentNullException), "Validation", true)]
    [InlineData(typeof(System.FormatException), "Validation", true)]
    [InlineData(typeof(System.TimeoutException), "Timeout", true)]
    [InlineData(typeof(System.OperationCanceledException), "Cancelled", true)]
    [InlineData(typeof(System.InvalidOperationException), "InvalidOperationException", true)]
    public void FromException_ClassifiesByExceptionType(System.Type exType, string expectedClass, bool expectedRecoverable)
    {
        var ex = (Exception)Activator.CreateInstance(exType, "msg")!;

        var json = ToolErrorEnvelope.FromException("get_x", ex, durationMs: 1);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(expectedClass, root.GetProperty("errorClass").GetString());
        Assert.Equal(expectedRecoverable, root.GetProperty("recoverable").GetBoolean());
    }
}
