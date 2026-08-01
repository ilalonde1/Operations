#nullable enable
using System;
using System.Text;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Builds the dataContext block injected into AI calls from the Historical
    /// Analytics window. After Arcs 1-4 every signal in this context has a
    /// canonical MCP tool, so this method shrinks to scope-only data
    /// (filtered totals + currently selected item) plus a pointer block
    /// telling the LLM which tool serves which question. Off-screen data
    /// comes from tools, not pushed context.
    ///
    /// Originally a 344-line dump of every signal at every level (per the
    /// roadmap in docs/architecture/Kor.Operations.Ai.consolidation-roadmap.md);
    /// trimmed in Batch 99 as the Arc 5 completion.
    /// </summary>
    internal static class AnalyticsAiService
    {
        internal static string BuildContext(HistoricalAnalyticsViewModel vm)
        {
            var sb = new StringBuilder();

            // Portfolio scope — reflects the user's current filter, NOT a
            // firmwide rollup. AI uses this to know what the user is looking
            // at on screen; firmwide rollups come from the structured tools.
            sb.AppendLine("=== PORTFOLIO OVERVIEW (current filter) ===");
            sb.AppendLine($"Projects on screen: {vm.VisibleCount}");
            sb.AppendLine($"Total Fee (filtered): ${vm.TotalFee:N0}");
            sb.AppendLine($"Total Eng Hours: {vm.TotalEngHrs:N0}");
            sb.AppendLine($"Total Draft Hours: {vm.TotalDraftHrs:N0}");
            sb.AppendLine($"Weighted Billable %: {vm.WeightedBillablePct:P0}");
            sb.AppendLine($"Fee/Hr Distribution: P25=${vm.P25FeePerHr:N0}, Median=${vm.MedianFeePerHr:N0}, P75=${vm.P75FeePerHr:N0}");
            sb.AppendLine($"Budget Accuracy: {vm.BudgetAccuracyPct:P0} within threshold, Median Abs Error: {vm.MedianAbsError:N0} hrs");
            sb.AppendLine();

            // Currently selected project (Projects view)
            if (vm.SelectedRow is { } sel)
            {
                sb.AppendLine($"=== CURRENTLY SELECTED PROJECT: {sel.Wbs1} — {sel.Name} ===");
                sb.AppendLine($"  PM: {sel.Pm} | DM: {sel.DraftingManager} | Phase: {sel.Phase} | Status: {sel.Status}");
                sb.AppendLine($"  Type: {sel.ConstructionType} | Category: {sel.ProjectCategory} | Drafting: {sel.DraftingType}");
                sb.AppendLine($"  Duration: {sel.DurationDisplay} | Fee/Month: ${sel.FeePerMonth:N0}");
                var selBilled = sel.HasUnpostedBilling
                    ? $"Billed: ${sel.FeeBilled:N0} ({sel.PercentBilled:P0}) posted, ${sel.FeeBilledWithUnposted:N0} ({sel.PercentBilledWithUnposted:P0}) all-in (+${sel.UnpostedFeeBilled:N0} unposted)"
                    : $"Billed: ${sel.FeeBilled:N0} ({sel.PercentBilled:P0})";
                sb.AppendLine($"  Fee: ${sel.TotalFee:N0} (fixed ${sel.Fee:N0} + hourly ${sel.HourlyRevenue:N0}) | {selBilled}");
                sb.AppendLine($"  Subconsultant Cost: ${sel.SubCost:N0} | Sub %: {sel.SubPctOfFee:P0}");
                sb.AppendLine($"  Net Fee: ${sel.NetFee:N0} | Fee/Hr: ${sel.FeePerHr:N0} | Net $/Hr: ${sel.NetFeePerHr:N0}");
                sb.AppendLine($"  Eng Hours: {sel.EngHrs:N0} | Draft Hours: {sel.DraftHrs:N0} | Eng/Draft: {sel.EngPct:P0}/{sel.DraftPct:P0}");
                sb.AppendLine($"  Insp Hours: {sel.InspHrs:N0} | Total All Hours: {sel.TotalAllHrs:N0} | Billable %: {sel.BillablePct:P0}");
                sb.AppendLine($"  Est Eng Budget: {sel.EstEngBudget:N0} | Est Draft Budget: {sel.EstDraftBudget:N0} | Peers: {sel.BudgetPeerCount}");
                sb.AppendLine($"  Eng Delta: {sel.EngBudgetDelta:N0} hrs | Draft Delta: {sel.DraftBudgetDelta:N0} hrs");
                sb.AppendLine($"  AR Outstanding: ${sel.ArTotal:N0} | AR 90+: ${sel.Ar90Plus:N0}");
                sb.AppendLine($"  Inspections: {sel.TotalInspections} total, {sel.LastMonthInspections} last month");
                sb.AppendLine();
            }

            // Currently selected summary detail (PM/DM/Employee views).
            // Snapshot DetailMetrics — it's a BulkObservableCollection mutated
            // on the UI thread, and BuildContext runs on AppAiContextBuilder's
            // worker thread (Batch 102 audit pattern).
            if (!string.IsNullOrWhiteSpace(vm.DetailTitle))
            {
                sb.AppendLine($"=== CURRENTLY SELECTED: {vm.DetailTitle} ({vm.DetailSubtitle}) ===");
                var metrics = vm.DetailMetrics.ToArray();
                foreach (var m in metrics)
                {
                    if (m.IsHeader) sb.AppendLine($"\n  [{m.Label}]");
                    else if (!m.IsExplanation && !string.IsNullOrWhiteSpace(m.Value))
                        sb.AppendLine($"    {m.Label}: {m.Value}");
                }
                sb.AppendLine();
            }

            // Tool pointer footer. Off-screen / firmwide data comes from the
            // MCP catalog, not from this BuildContext. Match the tool names
            // exactly so PromptToolParityValidator catches any drift here.
            sb.AppendLine("=== OFF-SCREEN DATA — USE TOOLS ===");
            sb.AppendLine("For firmwide PM ranking: call get_pm_performance.");
            sb.AppendLine("For firmwide Drafting Manager ranking: call get_dm_performance.");
            sb.AppendLine("For firmwide employee productivity: call get_employee_performance.");
            sb.AppendLine("For firmwide last-12-week utilization per employee: call get_employee_utilization.");
            sb.AppendLine("For full per-project detail by WBS1: call get_project_detail.");
            sb.AppendLine("For the over-budget watchlist: call get_at_risk_projects.");
            sb.AppendLine("For year-over-year portfolio aggregates: call get_project_yoy_trend.");
            sb.AppendLine("For per-year firmwide billable%: call get_firm_utilization_by_year.");
            sb.AppendLine("For firmwide revenue by period: call get_revenue_timeline.");

            return sb.ToString();
        }
    }
}
