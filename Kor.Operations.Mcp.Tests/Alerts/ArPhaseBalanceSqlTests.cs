#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Alerts;

/// <summary>
/// Text-only guard for the three ARDeduped sites (two rules and the repository).
/// Does not execute SQL or validate live joins or data grain.
/// </summary>
public sealed class ArPhaseBalanceSqlTests
{
    [Theory]
    [InlineData("Alerts/Rules/Cash/ArAgingRule.cs")]
    [InlineData("Alerts/Rules/Legal/WrittenOffInvoicePaidRule.cs")]
    [InlineData("Collections/CollectionsRepository.cs")]
    public void ArDeduped_SumsPhaseBalances_AndKeepsLatestInvoiceDate(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.Mcp", relativePath));
        var cte = Regex.Match(source,
            @"\bARDeduped\s+AS\s*\((?<body>.*?)\bGROUP\s+BY\s+WBS1\s*,\s*Invoice\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(cte.Success, $"ARDeduped invoice grouping not found in {relativePath}.");
        var body = cte.Groups["body"].Value;
        Assert.Matches(@"(?i)\bSUM\s*\(\s*InvBalanceSourceCurrency\s*\)\s+AS\s+OutstandingBalance\b", body);
        Assert.Matches(@"(?i)\bMAX\s*\(\s*InvoiceDate\s*\)\s+AS\s+InvoiceDate\b", body);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kor.Operations.Mcp", "Kor.Operations.Mcp.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository containing Kor.Operations.Mcp.");
    }
}
