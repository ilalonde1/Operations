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
        // ── Billed P&L (BilledPnLTool) ──
        // Canonical revenue-account list. Either side dropping one of these is
        // an incident (Daler's Crystal report defines them as the source of
        // truth for billed revenue).
        new("Billed_Revenue", "BilledPnLTool.cs",
            new[] { "4001", "4003", "4210", "4220", "4240", "LedgerAR" }),

        // KOR-specific operating-expense exclusions (Batch 73). Stripping any
        // of these regresses to the $103K/month over-report bug.
        new("Billed_Expenses", "BilledPnLTool.cs",
            new[] { "7290", "7970", "8200", "8300" }),

        // ── Firm Health — NSR/DLC pair (FirmHealthTool) ──
        // Net Multiplier shares the canonical revenue accounts + DLC predicate
        // with FirmHealthTool's methodology.
        new("Exec_NetMultiplier", "FirmHealthTool.cs",
            new[] { "4001", "4003", "4210", "4220", "4240", "LedgerAR", "NetMultiplier", "RegAmt" }),

        // Labor Margin shares the NSR/DLC abbreviations + the 12-month bucket
        // names with FirmHealthTool.
        new("Exec_NetProfit", "FirmHealthTool.cs",
            new[] { "NSR", "DLC", "NetServiceRevenue12Mo", "DirectLaborCost12Mo" }),

        // ── Cash (CashTool) ──
        // Cash position has a posted layer (cumulative GLSummary) plus an
        // unposted sub-ledger overlay (LedgerAR/AP/EX/Misc, TransDate > last
        // closed period). Removing the overlay on either side regresses the
        // headline by months of activity (Deltek's posting lag at KOR).
        new("Exec_CashPosition", "CashTool.cs",
            new[] { "CFGBanks", "GLSummary", "LedgerAR", "LedgerAP", "LedgerEX", "LedgerMisc" }),

        // ── AR (ArTool) ──
        // AR balance source is AR.InvBalanceSourceCurrency; the Definition and
        // tool must agree on the column name.
        new("Exec_ArOutstanding", "ArTool.cs",
            new[] { "InvBalanceSourceCurrency" }),

        // AR aging uses COALESCE(DueDate, InvoiceDate) as the anchor. Both
        // sides must reference both columns or the aging math drifts.
        new("Exec_ArOver60", "ArTool.cs",
            new[] { "DueDate", "InvoiceDate" }),

        // ── Backlog (BacklogTool) ──
        // Backlog scope = active projects (PR.Status='A'). TotalFee includes
        // T&M HourlyRevenue extras; FeeBilled includes LedgerAR unposted overlay
        // covering Deltek's posting lag.
        new("Exec_Backlog", "BacklogTool.cs",
            new[] { "PR.Status='A'", "HourlyRevenue", "PRSummaryMain", "LedgerAR" }),

        // ── Utilization (UtilizationTool) ──
        // Billable predicate: LaborCode NOT IN (70 Admin, 80 NonBillable) AND
        // WBS1 NOT LIKE overhead prefixes [A-Z]% / 9[A-Z]% / 99%. Source table
        // is tkDetail.
        new("Exec_Utilization30", "UtilizationTool.cs",
            new[] { "tkDetail", "LaborCode", "70", "80", "[A-Z]%", "99%" }),

        // ── Collection Exposure (CollectionExposureTool) ──
        // Billed90 denominator is period-anchored (last 3 closed PRSummaryMain
        // periods), NOT a 90-day calendar window. Drop "period-anchored" on
        // either side and you regress to ~$0 denominator near period-boundary.
        // Note: Definition writes "LATEST 3 CLOSED PERIODS" in uppercase but
        // the tool says "last 3 closed periods" lowercase, so the count
        // phrase isn't substring-matchable across both. "Billed90" and
        // "period-anchored" appear verbatim in both and cover the contract.
        new("Exec_CollectionExposure", "CollectionExposureTool.cs",
            new[] { "Billed90", "period-anchored" }),

        // ── Earned vs Invoiced (EarnedVsInvoicedTool) ──
        // Earned = PRSummaryMain.BilledFee (legacy Revenue fallback);
        // Invoiced = PRSummaryMain.Billed; UnbilledGap = Earned - Invoiced.
        new("Exec_Revenue3090", "EarnedVsInvoicedTool.cs",
            new[] { "BilledFee", "PRSummaryMain.Billed", "UnbilledGap" }),

        new("Exec_Billed3090", "EarnedVsInvoicedTool.cs",
            new[] { "PRSummaryMain.Billed" }),

        // ── WIP (WipTool) ──
        // Two paths: (a) Revenue Generation ON at KOR → use PRSummaryMain.Unbilled
        // directly; (b) OFF → proxy via Revenue - Billed.
        // Both sides must keep the Unbilled column name and the Overbilled
        // term so the contract-asset/liability split stays explicit.
        new("Exec_WipUnbilled", "WipTool.cs",
            new[] { "PRSummaryMain.Unbilled", "Overbilled" }),

        // ── GL P&L — Net Income (T12mo) (GlPnLTool) ──
        // GL bottom line is aggregated from GLSummary via GLTable section
        // grouping. Definition and tool both name those tables.
        new("Exec_GlNetIncome", "GlPnLTool.cs",
            new[] { "GLSummary", "GLTable" }),
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
