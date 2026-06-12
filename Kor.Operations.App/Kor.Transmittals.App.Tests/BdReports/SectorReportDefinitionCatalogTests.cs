#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class SectorReportDefinitionCatalogTests
{
    [Fact]
    public void Catalog_HasExactlyTheShippedReports()
    {
        // Nine launch sectors + two region rows (okanagan, us-west).
        var expected = new[]
        {
            "hospitals", "schools", "indigenous", "bc-housing", "defense",
            "recreational", "residential", "commercial", "post-secondary",
            "okanagan", "us-west",
        };

        Assert.Equal(expected, SectorReportDefinitionCatalog.All.Select(d => d.Key).ToArray());
    }

    [Fact]
    public void EveryFilter_ConstrainsProvince()
    {
        // The inventory carries BC, AB, CA, WA, OR. Sector heuristics alone
        // leaked US rows into "BC + AB" reports (a $1.74B Seattle hospital
        // topped the hospitals report, caught 2026-06-12) — every fragment
        // must scope geography explicitly.
        foreach (var def in SectorReportDefinitionCatalog.All)
        {
            Assert.Contains("m.Province", def.MpiWhere, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Keys_AreUniqueCaseInsensitive()
    {
        var keys = SectorReportDefinitionCatalog.All.Select(d => d.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryDefinition_HasTitleReportTitleAndParenthesizedFilter()
    {
        foreach (var def in SectorReportDefinitionCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Key));
            Assert.False(string.IsNullOrWhiteSpace(def.Title));
            Assert.False(string.IsNullOrWhiteSpace(def.ReportTitle));
            Assert.False(string.IsNullOrWhiteSpace(def.MpiWhere));

            // Fragments are ANDed into a larger WHERE — they must be
            // self-contained parenthesized expressions.
            Assert.StartsWith("(", def.MpiWhere, StringComparison.Ordinal);
            Assert.EndsWith(")", def.MpiWhere, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Filters_ReferenceOnlyTheMpiAlias()
    {
        // Catalog fragments run against alias m (MajorProjectsInventory) only —
        // referencing e. (enrichment) would break the summary LEFT JOIN semantics.
        foreach (var def in SectorReportDefinitionCatalog.All)
        {
            Assert.Contains("m.", def.MpiWhere, StringComparison.Ordinal);
            Assert.DoesNotContain("e.", def.MpiWhere, StringComparison.Ordinal);
        }
    }
}
