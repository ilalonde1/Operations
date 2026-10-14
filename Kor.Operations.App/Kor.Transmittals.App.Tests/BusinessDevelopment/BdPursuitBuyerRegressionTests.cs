#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// EXPECTED canonical buyer fallback versus ACTUAL SQL source and row projection.
/// No SQL is executed: a wrong join key, or a DisplayName that is null for a merged
/// org, would escape. Claude proves the live rows on the running app and in the PNGs.
/// Write-output checks do not simulate a database reader or a complete UI save.
/// </summary>
public sealed class BdPursuitBuyerRegressionTests
{
    [Fact]
    public void BoardSql_ProjectsTheCanonicalBuyerFallback()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs");
        var board = Regex.Match(source, @"private const string BoardSql = @""(.*?)"";", RegexOptions.Singleline);
        Assert.True(board.Success, "EXPECTED BoardSql; ACTUAL no constant found");
        Assert.Contains("LEFT JOIN opportunities.CanonicalOrg", board.Value);
        Assert.Contains("COALESCE(o.BuyerName, co.DisplayName", board.Value);
        Assert.Contains("e.OwnerStaffId IS NOT NULL", board.Value);
        Assert.Contains("e.Stage IN (1, 3)", board.Value);
    }

    [Fact]
    public void CrmSelects_ProjectBuyerWithoutChangingWriteOutputs()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs");
        Assert.Contains("ListColumns = AllColumns", source);
        Assert.Contains("AS BuyerOrgName", source);
        Assert.Equal(4, Regex.Matches(source, @"SELECT \{ListColumns\}\s+FROM opportunities.CrmEngagements").Count);
        var outputs = Regex.Matches(source, @"^\s*OUTPUT\s*\r?\n(.*?)(?:^\s*VALUES\b|^\s*WHERE\b)",
            RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.Equal(2, outputs.Count);
        foreach (Match output in outputs) Assert.DoesNotContain("BuyerOrgName", output.Value);
        var mapStart = source.IndexOf("private static CrmEngagement MapReader", StringComparison.Ordinal);
        Assert.True(mapStart >= 0, "EXPECTED MapReader; ACTUAL missing");
        var map = source[mapStart..];
        Assert.DoesNotContain("GetOrdinal(\"BuyerOrgName\")", map);
        Assert.Contains("FieldCount", map);
        Assert.Contains("GetName(i)", map);
        Assert.Contains("BuyerOrgName =", map);
    }

    [Fact]
    public void RowBuyer_PrefersOpportunityThenProjectionThenPreviousRow()
    {
        var engagement = new CrmEngagement { BuyerOrgName = "X" };
        Assert.Equal("X", new CrmEngagementRowView(engagement, null).Buyer);
        Assert.Equal("Y", new CrmEngagementRowView(engagement, new Opportunity { BuyerName = "Y" }).Buyer);
        Assert.Equal("", new CrmEngagementRowView(new CrmEngagement(), null).Buyer);
        Assert.Equal("previous", new CrmEngagementRowView(new CrmEngagement(), null, "previous").Buyer);
        Assert.Equal("X", new CrmEngagementRowView(engagement, null, "previous").Buyer);
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
}
