using Kor.Opportunities.Data.Awards;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// The BC registry name match (review row 509, board row 1636). COVERS: letters inside another word do not match; whole words in
/// either direction, legal suffixes and punctuation do. DOES NOT COVER: the provider's search and which hit it takes; rows already
/// written from the old match (row 1636 counts those).
/// Same-class fault it would miss: two different businesses whose names differ only by a word, "Team Construction" against
/// "Team Construction Management" when they are unrelated: whole words in one another still match.
/// </summary>
public class BcRegistryProviderMatchTests
{
    [Theory]
    [InlineData("Team Construction", "A-TEAM CONSTRUCTION")]      // org 68987, migration 382
    [InlineData("Team Construction", "DREAMTEAM CONSTRUCTION")]
    [InlineData("Wall Financial", "ACME BUILDERS")]
    [InlineData("KOR", "KORE CONSULTING")]
    public void AnotherBusinessDoesNotMatch(string query, string hit) => Assert.False(BcRegistryProvider.IsLikelyMatch(query, hit));

    [Theory]
    [InlineData("Team Construction", "TEAM CONSTRUCTION MANAGEMENT (1981) LTD.")]
    [InlineData("Team Construction Management (1981) Ltd.", "TEAM CONSTRUCTION MANAGEMENT (1981) LTD.")]
    [InlineData("Wall Financial Corporation", "WALL FINANCIAL")]
    [InlineData("Wall Financial", "WALL FINANCIAL CORPORATION")]
    [InlineData("BC Housing Management Commission", "BC HOUSING")]
    [InlineData("Smith & Jones Ltd", "SMITH AND JONES LIMITED")]
    [InlineData("Team-Construction", "TEAMCONSTRUCTION LTD.")]     // equal once normalized
    public void TheSameBusinessMatches(string query, string hit) => Assert.True(BcRegistryProvider.IsLikelyMatch(query, hit));
}
