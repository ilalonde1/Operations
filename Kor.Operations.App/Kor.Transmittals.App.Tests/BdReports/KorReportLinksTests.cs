#nullable enable
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class KorReportLinksTests
{
    [Fact]
    public void Mint_RoundTripsThroughTryParse()
    {
        Assert.Equal("kor://mpi/6585", KorReportLinks.Mpi(6585));
        Assert.Equal("kor://org/42", KorReportLinks.Org(42));
        Assert.Equal("kor://person/9", KorReportLinks.Person(9));
        Assert.Null(KorReportLinks.Org(null)); // unresolved graph edge -> no link
        Assert.Null(KorReportLinks.Person(null)); // unresolved person edge -> no link

        Assert.True(KorReportLinks.TryParse(KorReportLinks.Mpi(6585), out var kind, out var id));
        Assert.Equal("mpi", kind);
        Assert.Equal(6585, id);

        Assert.True(KorReportLinks.TryParse("KOR://ORG/42", out kind, out id)); // case-insensitive
        Assert.Equal("org", kind);
        Assert.Equal(42, id);

        Assert.True(KorReportLinks.TryParse("kor://person/9", out kind, out id));
        Assert.Equal("person", kind);
        Assert.Equal(9, id);
    }

    [Fact]
    public void Opportunity_MintsAndRoundTrips()
    {
        Assert.Equal("kor://opp/23643", KorReportLinks.Opportunity(23643));
        Assert.Null(KorReportLinks.Opportunity(null)); // no id -> no link

        Assert.True(KorReportLinks.TryParse(KorReportLinks.Opportunity(23643), out var kind, out var id));
        Assert.Equal("opp", kind);
        Assert.Equal(23643, id);

        Assert.True(KorReportLinks.TryParse("KOR://OPP/1", out kind, out id)); // case-insensitive
        Assert.Equal("opp", kind);
        Assert.Equal(1, id);
    }

    [Theory]
    [InlineData("kor://opp/")]               // missing id
    [InlineData("kor://opp/abc")]            // non-numeric
    [InlineData("kor://opp/0")]              // non-positive
    [InlineData("kor://opp/-5")]             // negative
    [InlineData("kor://opportunity/5")]      // long form is NOT the vocabulary
    public void TryParse_RejectsMalformedOpportunityUris(string uri)
    {
        Assert.False(KorReportLinks.TryParse(uri, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://mpi/123")]          // wrong scheme
    [InlineData("kor://bogus/9")]            // unknown kind
    [InlineData("kor://mpi/")]               // missing id
    [InlineData("kor://mpi/abc")]            // non-numeric
    [InlineData("kor://mpi/-5")]             // negative
    [InlineData("kor://mpi/0")]              // non-positive
    [InlineData("kor://mpi/123/extra")]      // trailing segment
    [InlineData("kor:mpi/123")]              // not authority form
    public void TryParse_RejectsMalformedUris(string? uri)
    {
        Assert.False(KorReportLinks.TryParse(uri, out _, out _));
    }
}
