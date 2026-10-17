#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.App.Tests;
using Xunit;

namespace Kor.Operations.Tests.Financials;

/// <summary>
/// Rejects MAX over phase balances throughout repository C# source, ignoring case
/// and aliases. This text gate does not catch deduplication done in C# or in a
/// .sql file outside the repo. The collections accumulator tests cover the former.
/// </summary>
public sealed class ArBalanceAggregationSourceTests
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [Fact]
    public void ProductionCSharp_DoesNotTakeMaxOfPhaseBalance()
    {
        var root = XamlStaticResourceOrderTests.GetRepoRoot();
        var violations = ProductionSources(root)
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return BalanceMaxOffsets(source).Select(offset =>
                    $"{Path.GetRelativePath(root, path)}:{1 + source.Take(offset).Count(c => c == '\n')}");
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(violations.Count == 0,
            "AR balances are phase shares and must be summed per invoice. MAX found at:\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("MAX(InvBalanceSourceCurrency)")]
    [InlineData("max(ar.invbalancesourcecurrency)")]
    [InlineData("MaX ( [ar].[InvBalanceSourceCurrency] )")]
    [InlineData("MAX(\nCOALESCE(ar.InvBalanceSourceCurrency, 0)\n)")]
    [InlineData("MAX(COALESCE(0, ar.InvBalanceSourceCurrency))")]
    public void Detector_FindsAliasedAndNestedArguments(string source)
    {
        Assert.Single(BalanceMaxOffsets(source));
    }

    [Theory]
    [InlineData("MAX(InvoiceDate), SUM(InvBalanceSourceCurrency)")]
    [InlineData("SUM(ar.InvBalanceSourceCurrency)")]
    [InlineData("MAX(COALESCE(InvoiceDate, DueDate)), ar.InvBalanceSourceCurrency")]
    public void Detector_DoesNotConfuseAdjacentExpressions(string source)
    {
        Assert.Empty(BalanceMaxOffsets(source));
    }

    private static IEnumerable<int> BalanceMaxOffsets(string source)
    {
        foreach (Match call in Regex.Matches(source, @"\bMAX\s*\(", MatchOptions))
        {
            // Balance parentheses so nested COALESCE/CAST arguments are included,
            // without consuming a subsequent SELECT expression after this MAX.
            var start = call.Index + call.Length;
            var end = start;
            var depth = 1;
            while (end < source.Length && depth > 0)
            {
                if (source[end] == '(') depth++;
                if (source[end] == ')') depth--;
                end++;
            }

            if (depth == 0 && Regex.IsMatch(source[start..(end - 1)], @"\bInvBalanceSourceCurrency\b", MatchOptions))
                yield return call.Index;
        }
    }

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
