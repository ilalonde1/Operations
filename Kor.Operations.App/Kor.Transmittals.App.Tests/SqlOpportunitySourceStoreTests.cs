#nullable enable
using System.Collections.Generic;
using Kor.Opportunities.Data.Sources;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class SqlOpportunitySourceStoreTests
{
    [Fact]
    public void AddConfigJsonMappings_AddsTopLevelStringProperties()
    {
        var target = new Dictionary<string, string>();

        SqlOpportunitySourceStore.AddConfigJsonMappings(
            target,
            """{"bcbid.keyword":"engineering","ignoredNumber":42,"ignoredObject":{"x":"y"}}""");

        Assert.Equal("engineering", target["bcbid.keyword"]);
        Assert.False(target.ContainsKey("ignoredNumber"));
        Assert.False(target.ContainsKey("ignoredObject"));
    }

    [Fact]
    public void AddConfigJsonMappings_WhenKeyAlreadyExists_KeepsExistingValue()
    {
        var target = new Dictionary<string, string>
        {
            ["bcbid.keyword"] = "architecture",
        };

        SqlOpportunitySourceStore.AddConfigJsonMappings(
            target,
            """{"bcbid.keyword":"engineering"}""");

        Assert.Equal("architecture", target["bcbid.keyword"]);
    }
}
