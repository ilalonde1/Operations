#nullable enable
using Kor.Operations.Mcp.Smoke.Calibrators;

namespace Kor.Operations.Mcp.Smoke;

internal sealed record TestCase(
    string Id,
    string Question,
    string? CurrentlyViewing,
    Func<IServiceProvider, ICalibrator> CalibratorFactory,
    int MaxDurationMs = 30000);

internal static class TestCases
{
    public static IReadOnlyList<TestCase> All { get; } =
    [
        new(
            "CAD Apr-2024 expenses",
            "What were CAD-only Billed P&L expenses for April 2024? Give the total.",
            "Billed P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            sp => new BilledPnLCadCalibrator((SmokeServices)sp, "Total Expenses", "CAD Apr-2024 expenses")),
        new(
            "CAD Apr-2024 revenue",
            "What was CAD-only Billed P&L revenue for April 2024? Give the total.",
            "Billed P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            sp => new BilledPnLCadCalibrator((SmokeServices)sp, "Total Revenue", "CAD Apr-2024 revenue")),
        new(
            "Combined Apr-2024 expenses",
            "What were combined firm-wide Billed P&L expenses for April 2024? Give the total.",
            "Billed P&L screen. Org filter: all orgs, USA converted to CAD at 1.36. Range: 2024-04-01 to 2024-04-30.",
            sp => new BilledPnLCombinedCalibrator((SmokeServices)sp)),
        new(
            "Top 3 Apr-2024 CAD expense accounts",
            "List the top 3 CAD-only Billed P&L expense accounts for April 2024.",
            "Billed P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            sp => new BilledPnLTopExpenseCalibrator((SmokeServices)sp)),
        new(
            "Current cash position",
            "What is our current cash position?",
            null,
            sp => new CashCalibrator((SmokeServices)sp)),
        new(
            "AR over 90 days",
            "What is firmwide AR over 90 days? Give the 90+ aging total.",
            null,
            sp => new ArCalibrator((SmokeServices)sp)),
        new(
            "T12mo Net Multiplier",
            "What is our trailing-12-month Net Multiplier?",
            null,
            sp => new FirmHealthNetMultiplierCalibrator((SmokeServices)sp)),
        new(
            "Firmwide 30-day billable utilization",
            "What is firmwide 30-day billable utilization?",
            null,
            sp => new UtilizationCalibrator((SmokeServices)sp)),
        new(
            "Current backlog firmwide",
            "What is current firmwide backlog?",
            null,
            sp => new BacklogCalibrator((SmokeServices)sp)),
        new(
            "Latest 3-period billed total",
            "For earned vs invoiced, what is the invoiced total for the last 3 closed periods?",
            null,
            sp => new RecentBilledCalibrator((SmokeServices)sp)),
        new(
            "Collection exposure ratio",
            "What is our collection exposure ratio?",
            null,
            sp => new CollectionExposureCalibrator((SmokeServices)sp)),
        new(
            "Earned-vs-invoiced latest gap",
            "What is the earned-vs-invoiced gap for the latest closed period?",
            null,
            sp => new EarnedVsInvoicedCalibrator((SmokeServices)sp)),
        new(
            "Apr-2024 vs Feb-2026 CAD comparison",
            "Use the get_billed_pnl tool to fetch CAD-only Billed P&L expenses for BOTH April 2024 AND February 2026 (two separate tool calls, both with org='CAD'). Report both totals side by side. Do NOT ask the user to confirm any number — fetch every period.",
            null,
            sp => new BilledPnLComparisonCalibrator((SmokeServices)sp)),
        new(
            "CAD Apr-2024 GL P&L expenses",
            "Use the get_gl_pnl tool to fetch CAD-only GL P&L (posted) expenses for April 2024. Give the total. Pass org='CAD'.",
            "GL P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            sp => new GlPnLCadCalibrator((SmokeServices)sp)),
        new(
            "Firmwide WIP earned",
            "What is firmwide WIP (Work-In-Progress) earned at the latest posted period? Use the get_wip tool.",
            null,
            sp => new WipCalibrator((SmokeServices)sp)),
    ];
}
