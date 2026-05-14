#nullable enable
using Kor.Operations.Mcp.Smoke.Calibrators;

namespace Kor.Operations.Mcp.Smoke;

internal sealed record TestCase(
    string Id,
    string Question,
    string? CurrentlyViewing,
    IReadOnlyList<string> ExpectedToolNames,
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
            ["get_billed_pnl"],
            sp => new BilledPnLCadCalibrator((SmokeServices)sp, "Total Expenses", "CAD Apr-2024 expenses")),
        new(
            "CAD Apr-2024 revenue",
            "What was CAD-only Billed P&L revenue for April 2024? Give the total.",
            "Billed P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            ["get_billed_pnl"],
            sp => new BilledPnLCadCalibrator((SmokeServices)sp, "Total Revenue", "CAD Apr-2024 revenue")),
        new(
            "Combined Apr-2024 expenses",
            "What were combined firm-wide Billed P&L expenses for April 2024? Give the total.",
            "Billed P&L screen. Org filter: all orgs, USA converted to CAD at 1.36. Range: 2024-04-01 to 2024-04-30.",
            ["get_billed_pnl"],
            sp => new BilledPnLCombinedCalibrator((SmokeServices)sp)),
        new(
            "Top 3 Apr-2024 CAD expense accounts",
            "List the top 3 CAD-only Billed P&L expense accounts for April 2024.",
            "Billed P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            ["get_billed_pnl"],
            sp => new BilledPnLTopExpenseCalibrator((SmokeServices)sp)),
        new(
            "Current cash position",
            "What is our current cash position?",
            null,
            ["get_cash_position"],
            sp => new CashCalibrator((SmokeServices)sp)),
        new(
            "AR over 90 days",
            "What is firmwide AR over 90 days? Give the 90+ aging total.",
            null,
            ["get_ar"],
            sp => new ArCalibrator((SmokeServices)sp)),
        new(
            "T12mo Net Multiplier",
            "What is our trailing-12-month Net Multiplier?",
            null,
            ["get_firm_health"],
            sp => new FirmHealthNetMultiplierCalibrator((SmokeServices)sp)),
        new(
            "Firmwide 30-day billable utilization",
            "What is firmwide 30-day billable utilization?",
            null,
            ["get_utilization"],
            sp => new UtilizationCalibrator((SmokeServices)sp)),
        new(
            "Current backlog firmwide",
            "What is current firmwide backlog?",
            null,
            ["get_backlog"],
            sp => new BacklogCalibrator((SmokeServices)sp)),
        new(
            "Latest 3-period billed total",
            "For earned vs invoiced, what is the invoiced total for the last 3 closed periods?",
            null,
            ["get_earned_vs_invoiced"],
            sp => new RecentBilledCalibrator((SmokeServices)sp)),
        new(
            "Collection exposure ratio",
            "What is our collection exposure ratio?",
            null,
            ["get_collection_exposure"],
            sp => new CollectionExposureCalibrator((SmokeServices)sp)),
        new(
            "Earned-vs-invoiced latest gap",
            "What is the earned-vs-invoiced gap for the latest closed period?",
            null,
            ["get_earned_vs_invoiced"],
            sp => new EarnedVsInvoicedCalibrator((SmokeServices)sp)),
        new(
            "Apr-2024 vs Feb-2026 CAD comparison",
            "Use the get_billed_pnl tool to fetch CAD-only Billed P&L expenses for BOTH April 2024 AND February 2026 (two separate tool calls, both with org='CAD'). Report both totals side by side. Do NOT ask the user to confirm any number — fetch every period.",
            null,
            ["get_billed_pnl"],
            sp => new BilledPnLComparisonCalibrator((SmokeServices)sp)),
        new(
            "CAD Apr-2024 GL P&L expenses",
            "Use the get_gl_pnl tool to fetch CAD-only GL P&L (posted) expenses for April 2024. Give the total. Pass org='CAD'.",
            "GL P&L screen. Org filter: CAD. Range: 2024-04-01 to 2024-04-30.",
            ["get_gl_pnl"],
            sp => new GlPnLCadCalibrator((SmokeServices)sp)),
        new(
            "Firmwide WIP earned",
            "What is firmwide WIP (Work-In-Progress) earned at the latest posted period? Use the get_wip tool.",
            null,
            ["get_wip"],
            sp => new WipCalibrator((SmokeServices)sp)),
        new(
            "Top PM by Performance Score",
            "Who is the top-ranked Project Manager by Performance Score? Use the get_pm_performance tool and report the PM's name and their PerformanceScore (0-100).",
            null,
            ["get_pm_performance"],
            sp => new PmPerformanceCalibrator((SmokeServices)sp)),
        new(
            "Top DM by Performance Score",
            "Who is the top-ranked Drafting Manager by Performance Score? Use the get_dm_performance tool and report the DM's name and their PerformanceScore (0-100).",
            null,
            ["get_dm_performance"],
            sp => new DmPerformanceCalibrator((SmokeServices)sp)),
        new(
            "Top employee by Productivity Score",
            "Which employee has the highest Productivity Score? Use the get_employee_performance tool and report the employee's name and their ProductivityScore (0-100).",
            null,
            ["get_employee_performance"],
            sp => new EmployeePerformanceCalibrator((SmokeServices)sp)),
        new(
            "Firmwide 12-week billable utilization",
            "What is firmwide billable utilization over the last 12 weeks? Use the get_employee_utilization tool. Give the firmwideBillablePct as a percentage.",
            null,
            ["get_employee_utilization"],
            sp => new EmployeeUtilizationCalibrator((SmokeServices)sp)),
    ];
}
