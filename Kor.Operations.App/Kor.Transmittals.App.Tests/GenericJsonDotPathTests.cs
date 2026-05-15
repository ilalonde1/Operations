#nullable enable
using System.Text.Json;
using Kor.Opportunities.Data.Ingestion.Providers;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class GenericJsonDotPathTests
{
    [Fact]
    public void TryResolvePath_WithSimpleProperty_ReturnsValue()
    {
        using var document = JsonDocument.Parse("""{ "foo": "bar" }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "foo", out var value);

        Assert.True(resolved);
        Assert.Equal("bar", value.GetString());
    }

    [Fact]
    public void TryResolvePath_WithNestedProperty_ReturnsValue()
    {
        using var document = JsonDocument.Parse("""{ "a": { "b": { "c": 42 } } }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "a.b.c", out var value);

        Assert.True(resolved);
        Assert.Equal("42", value.GetRawText());
    }

    [Fact]
    public void TryResolvePath_WithArrayIndex_ReturnsValue()
    {
        using var document = JsonDocument.Parse("""{ "items": [ { "title": "x" } ] }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "items[0].title", out var value);

        Assert.True(resolved);
        Assert.Equal("x", value.GetString());
    }

    [Fact]
    public void TryResolvePath_WithDifferentPropertyCase_ReturnsValue()
    {
        using var document = JsonDocument.Parse("""{ "Foo": { "BAR": "baz" } }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "foo.bar", out var value);

        Assert.True(resolved);
        Assert.Equal("baz", value.GetString());
    }

    [Fact]
    public void TryResolvePath_WithOutOfBoundsArrayIndex_ReturnsFalse()
    {
        using var document = JsonDocument.Parse("""{ "items": [ { "title": "x" } ] }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "items[1].title", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolvePath_WithMissingProperty_ReturnsFalse()
    {
        using var document = JsonDocument.Parse("""{ "foo": "bar" }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "missing", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolvePath_WithEmptyPath_ReturnsRoot()
    {
        using var document = JsonDocument.Parse("""{ "foo": "bar" }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, string.Empty, out var value);

        Assert.True(resolved);
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.True(value.TryGetProperty("foo", out _));
    }

    [Fact]
    public void TryResolvePath_WithWhitespaceOnlyPath_ReturnsRoot()
    {
        using var document = JsonDocument.Parse("""{ "foo": "bar" }""");

        var resolved = GenericJsonOpportunityProvider.TryResolvePath(document.RootElement, "   ", out var value);

        Assert.True(resolved);
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.True(value.TryGetProperty("foo", out _));
    }
}
