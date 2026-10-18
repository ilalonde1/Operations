#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.App.Tests;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

/// <summary>
/// Rejects a string literal in repository C# that selects MAX(Period) FROM PRSummaryMain anywhere but
/// <see cref="PrSummaryPeriod"/>: a bare read returned 202611 from placeholder 99999-00 on 17 Oct 2026.
/// It does not catch a latest period taken by ORDER BY Period DESC or TOP 1, a GLSummary or LedgerAR
/// latest period, a query assembled across several literals, or a .sql file outside the repo.
/// </summary>
public sealed class PrSummaryPeriodSourceTests
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [Fact]
    public void ProductionCSharp_TakesLatestPrSummaryPeriodOnlyThroughTheHelper()
    {
        var root = XamlStaticResourceOrderTests.GetRepoRoot();
        var violations = ProductionSources(root)
            .Where(path => !Path.GetFileName(path).Equals("PrSummaryPeriod.cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return BareReadOffsets(source).Select(offset =>
                    $"{Path.GetRelativePath(root, path)}:{1 + source.Take(offset).Count(c => c == '\n')}");
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(violations.Count == 0,
            "Use PrSummaryPeriod.MaxPostedPeriodSql so placeholder projects never set the posted period. Bare reads at:\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("cmd.CommandText = $@\"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain;\";")]
    [InlineData("var sql = \"select max ( Period ) from dbo.prsummarymain\";")]
    public void Detector_FindsBareReads(string source) => Assert.Single(BareReadOffsets(source));

    [Theory]
    [InlineData("Log.Warning(ex, \"Deltek PRSummaryMain MAX(Period) probe failed\");")]
    [InlineData("// First day of MAX(Period) in PRSummaryMain")]
    [InlineData("cmd.CommandText = \"SELECT MAX(Period) FROM dbo.GLSummary\";")]
    public void Detector_IgnoresMessagesCommentsAndOtherTables(string source) => Assert.Empty(BareReadOffsets(source));

    [Fact]
    public void Helper_ExcludesTheSnapshotsPlaceholderProjects()
    {
        var sql = PrSummaryPeriod.MaxPostedPeriodSql("CAT");
        Assert.Contains("[CAT].dbo.PRSummaryMain", sql, StringComparison.Ordinal);
        Assert.Contains("WBS1 NOT LIKE '[A-Z]%'", sql, StringComparison.Ordinal);
        Assert.Contains("WBS1 NOT LIKE '9[A-Z]%'", sql, StringComparison.Ordinal);
        Assert.Contains("WBS1 NOT LIKE '99%'", sql, StringComparison.Ordinal);
    }

    private static IEnumerable<int> BareReadOffsets(string source) =>
        Regex.Matches(source, @"""[^""\n]*\bMAX\s*\(\s*Period\s*\)\s*FROM\b[^""\n]*PRSummaryMain", MatchOptions)
            .Select(m => m.Index);

    private static IEnumerable<string> ProductionSources(string directory)
    {
        var name = Path.GetFileName(directory);
        if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || Directory.EnumerateFiles(directory, "*.csproj").Any(project =>
                Regex.IsMatch(File.ReadAllText(project), @"<IsTestProject>\s*true\s*</IsTestProject>", MatchOptions)))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            foreach (var file in ProductionSources(child))
                yield return file;
        }
    }
}
