#nullable enable
using System;
using System.Collections.Generic;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Data.Ingestion.Providers;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class CsvFilterHelperTests
{
    private const string CategoryColumnsKey = "csv.filter.categoryColumns";
    private const string CategoryKeepValuesKey = "csv.filter.categoryKeepValues";
    private const string RegionColumnsKey = "csv.filter.regionColumns";
    private const string RegionKeepTokensKey = "csv.filter.regionKeepTokens";

    [Fact]
    public void PassesSubstringFilter_WhenColumnConfigAbsent_ReturnsTrue()
    {
        var headers = Headers("procurementCategory");
        var row = Row("CNST");

        var passes = GenericCsvOpportunityProvider.PassesSubstringFilter(
            row,
            headers,
            new Dictionary<string, string>(),
            CategoryColumnsKey,
            CategoryKeepValuesKey);

        Assert.True(passes);
    }

    [Fact]
    public void PassesSubstringFilter_WhenConfiguredColumnMissing_ReturnsFalse()
    {
        var headers = Headers("otherColumn");
        var row = Row("CNST");
        var config = CategoryConfig("procurementCategory", "CNST");

        var passes = GenericCsvOpportunityProvider.PassesSubstringFilter(
            row,
            headers,
            config,
            CategoryColumnsKey,
            CategoryKeepValuesKey);

        Assert.False(passes);
    }

    [Fact]
    public void PassesSubstringFilter_WhenValueContainsKeepToken_ReturnsTrue()
    {
        var headers = Headers("procurementCategory");
        var row = Row("CNST - Construction Services");
        var config = CategoryConfig("procurementCategory", "CNST,SRVTGD");

        var passes = GenericCsvOpportunityProvider.PassesSubstringFilter(
            row,
            headers,
            config,
            CategoryColumnsKey,
            CategoryKeepValuesKey);

        Assert.True(passes);
    }

    [Fact]
    public void PassesSubstringFilter_WhenValueDoesNotContainKeepToken_ReturnsFalse()
    {
        var headers = Headers("procurementCategory");
        var row = Row("Goods");
        var config = CategoryConfig("procurementCategory", "CNST,SRVTGD");

        var passes = GenericCsvOpportunityProvider.PassesSubstringFilter(
            row,
            headers,
            config,
            CategoryColumnsKey,
            CategoryKeepValuesKey);

        Assert.False(passes);
    }

    [Fact]
    public void PassesWordBoundaryFilter_WhenLabradorContainsAb_ReturnsFalse()
    {
        var headers = Headers("regionsOfDelivery");
        var row = Row("Newfoundland and Labrador");
        var config = RegionConfig("regionsOfDelivery", "AB");

        var passes = GenericCsvOpportunityProvider.PassesWordBoundaryFilter(
            row,
            headers,
            config,
            RegionColumnsKey,
            RegionKeepTokensKey);

        Assert.False(passes);
    }

    [Fact]
    public void PassesWordBoundaryFilter_WhenBcOrAlbertaRegionContainsIsolatedToken_ReturnsTrue()
    {
        var headers = Headers("regionsOfDelivery");
        var config = RegionConfig("regionsOfDelivery", "BC,BRITISH COLUMBIA,AB,ALBERTA");

        var bcPasses = GenericCsvOpportunityProvider.PassesWordBoundaryFilter(
            Row("British Columbia (BC)"),
            headers,
            config,
            RegionColumnsKey,
            RegionKeepTokensKey);

        var abPasses = GenericCsvOpportunityProvider.PassesWordBoundaryFilter(
            Row("Alberta (AB)"),
            headers,
            config,
            RegionColumnsKey,
            RegionKeepTokensKey);

        Assert.True(bcPasses);
        Assert.True(abPasses);
    }

    [Fact]
    public void PassesWordBoundaryFilter_WhenBcsContainsBc_ReturnsFalse()
    {
        var headers = Headers("regionsOfDelivery");
        var row = Row("BCS, Mexico");
        var config = RegionConfig("regionsOfDelivery", "BC");

        var passes = GenericCsvOpportunityProvider.PassesWordBoundaryFilter(
            row,
            headers,
            config,
            RegionColumnsKey,
            RegionKeepTokensKey);

        Assert.False(passes);
    }

    [Fact]
    public void ParseMappingCsv_WithCategoryTokens_ReturnsExpectedValues()
    {
        var tokens = GenericCsvOpportunityProvider.ParseMappingCsv("CNST,SRVTGD");

        Assert.Equal(new[] { "CNST", "SRVTGD" }, tokens);
    }

    [Fact]
    public void ParseMappingCsv_WithRegionTokensAndBlankValues_ReturnsExpectedValues()
    {
        var tokens = GenericCsvOpportunityProvider.ParseMappingCsv("BC,BRITISH COLUMBIA,AB,ALBERTA");

        Assert.Equal(4, tokens.Count);
        Assert.Contains("BRITISH COLUMBIA", tokens);
        Assert.Empty(GenericCsvOpportunityProvider.ParseMappingCsv(null));
        Assert.Empty(GenericCsvOpportunityProvider.ParseMappingCsv(string.Empty));
        Assert.Empty(GenericCsvOpportunityProvider.ParseMappingCsv("   "));
    }

    [Fact]
    public void PassesWordBoundaryFilter_WithLiteralCanadaBuysKeepTokensAgainstAlberta_ReturnsTrue()
    {
        var headers = Headers("regionsOfDelivery");
        var row = Row("ALBERTA (AB)");
        var config = RegionConfig("regionsOfDelivery", "BC,BRITISH COLUMBIA,AB,ALBERTA");

        var passes = GenericCsvOpportunityProvider.PassesWordBoundaryFilter(
            row,
            headers,
            config,
            RegionColumnsKey,
            RegionKeepTokensKey);

        Assert.True(passes);
    }

    private static IReadOnlyList<string> Headers(params string[] headers) =>
        CsvParser.NormalizeHeaderRow(headers);

    private static IReadOnlyList<string> Row(params string[] values) => values;

    private static Dictionary<string, string> CategoryConfig(string columns, string keepValues) => new(StringComparer.OrdinalIgnoreCase)
    {
        [CategoryColumnsKey] = columns,
        [CategoryKeepValuesKey] = keepValues,
    };

    private static Dictionary<string, string> RegionConfig(string columns, string keepTokens) => new(StringComparer.OrdinalIgnoreCase)
    {
        [RegionColumnsKey] = columns,
        [RegionKeepTokensKey] = keepTokens,
    };
}
