#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Arc 6 drift gate (Batch 101). Locks the canonical signals shared by
/// each tool-mapped FinancialMetricDefinition and its MCP tool source.
///
/// For every (definitionKey, toolFile, phrases) row below, asserts each
/// phrase appears in BOTH the Definitions.*.cs entry AND the matching
/// MCP tool's .cs file. If anyone strips a canonical signal (account
/// codes, exclusions, predicate names) from either side without updating
/// the other, the test fails — catching drift between the WPF Financial
/// Metric Dictionary and the MCP tool catalog that mirrors it.
///
/// Definition keys NOT listed here are treated as free-form (non-tooled);
/// the dictionary intentionally surfaces metrics that don't have an MCP
/// tool counterpart (UI-only summaries, derived ratios, etc.).
/// </summary>
public sealed class ToolDefinitionDriftTests
{
    public sealed record ToolDriftCase(string DefinitionKey, string ToolFile, string[] Phrases);

    private static readonly ToolDriftCase[] Cases =
    {
        // Canonical revenue-account list. Either side dropping one of these is
        // an incident (Daler's Crystal report defines them as the source of
        // truth for billed revenue).
        new("Billed_Revenue", "BilledPnLTool.cs",
            new[] { "4001", "4003", "4210", "4220", "4240", "LedgerAR" }),

        // KOR-specific operating-expense exclusions (Batch 73). Stripping any
        // of these regresses to the $103K/month over-report bug.
        new("Billed_Expenses", "BilledPnLTool.cs",
            new[] { "7290", "7970", "8200", "8300" }),

        // Net Multiplier shares the canonical revenue accounts + DLC predicate
        // with FirmHealthTool's methodology.
        new("Exec_NetMultiplier", "FirmHealthTool.cs",
            new[] { "4001", "4003", "4210", "4220", "4240", "LedgerAR", "NetMultiplier", "RegAmt" }),

        // Labor Margin shares the NSR/DLC abbreviations + the 12-month bucket
        // names with FirmHealthTool.
        new("Exec_NetProfit", "FirmHealthTool.cs",
            new[] { "NSR", "DLC", "NetServiceRevenue12Mo", "DirectLaborCost12Mo" }),
    };

    [Theory]
    [MemberData(nameof(CaseData))]
    public void Phrase_AppearsInBothDefinitionAndTool(string definitionKey, string toolFile, string phrase)
    {
        // Definition side: combine Description + Formula and check substring.
        Assert.True(
            AppFin.FinancialMetricDefinitions.Definitions.TryGetValue(definitionKey, out var def) && def != null,
            $"Definition key '{definitionKey}' is missing from FinancialMetricDefinitions — Arc 6 drift map is stale or the key was renamed.");

        var defText = (def!.Description ?? string.Empty) + "\n" + (def.Formula ?? string.Empty);
        Assert.True(
            defText.Contains(phrase, StringComparison.Ordinal),
            $"Canonical phrase '{phrase}' missing from Definitions entry '{definitionKey}'. " +
            "Either the dictionary lost the canonical signal (drift) or the Arc 6 phrase list needs updating.");

        // Tool side: read the .cs source file and check substring.
        var toolPath = Path.Combine(ResolveMcpToolsDir(), toolFile);
        Assert.True(File.Exists(toolPath), $"MCP tool source not found at {toolPath} — Arc 6 drift map references a missing file.");

        var toolText = File.ReadAllText(toolPath);
        Assert.True(
            toolText.Contains(phrase, StringComparison.Ordinal),
            $"Canonical phrase '{phrase}' missing from MCP tool '{toolFile}'. " +
            $"Definition '{definitionKey}' still names it but the tool no longer does — drift.");
    }

    [Fact]
    public void EveryDriftCase_HasNonEmptyPhraseList()
    {
        // Empty phrase list = no gate = pointless row. Guard the map shape.
        foreach (var c in Cases)
        {
            Assert.True(c.Phrases.Length > 0, $"Drift case for '{c.DefinitionKey}' has no phrases — every row must encode at least one canonical signal.");
        }
    }

    public static IEnumerable<object[]> CaseData()
    {
        foreach (var c in Cases)
            foreach (var phrase in c.Phrases)
                yield return new object[] { c.DefinitionKey, c.ToolFile, phrase };
    }

    private static string ResolveMcpToolsDir()
    {
        // Walk up from the test binary's BaseDirectory until we find the repo
        // root (the directory that contains the Kor.Operations.Mcp project).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.Mcp", "Tools")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException(
                $"Could not locate Kor.Operations.Mcp\\Tools from BaseDirectory '{AppContext.BaseDirectory}'.");

        return Path.Combine(dir.FullName, "Kor.Operations.Mcp", "Tools");
    }
}
