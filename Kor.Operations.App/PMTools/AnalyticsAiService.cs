#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Builds the analytics dataContext block injected into AI calls from the
    /// Historical Analytics window. Originally also held a direct
    /// api.anthropic.com call; that path is gone — analytics questions now
    /// flow through AppAiService → /ask gateway, which holds the API key
    /// server-side.
    /// </summary>
    internal static class AnalyticsAiService
    {
        private const int UtilizationTriggerThresholdPct = 65;
        private const int ProjectsSectionCap = 200;
        private const int OverBudgetFeeMinimum = 10_000;

        internal static string BuildContext(HistoricalAnalyticsViewModel vm,
            IReadOnlyList<EmployeeProjectHours>? employeeProjectHours = null,
            IReadOnlyList<HistoricalProjectRow>? allProjects = null,
            IReadOnlyList<EmployeeWeeklyHours>? employeeWeeklyHours = null,
            IReadOnlyList<EmployeeRate>? employeeRates = null,
            double? partnerImputedCostRate = null)
        {
            var sb = new StringBuilder();

            // Portfolio KPIs
            sb.AppendLine("=== PORTFOLIO OVERVIEW ===");
            sb.AppendLine($"Projects: {vm.VisibleCount}");
            sb.AppendLine($"Total Fee: ${vm.TotalFee:N0}");
            sb.AppendLine($"Total Eng Hours: {vm.TotalEngHrs:N0}");
            sb.AppendLine($"Total Draft Hours: {vm.TotalDraftHrs:N0}");
            sb.AppendLine($"Firm Billable %: {vm.WeightedBillablePct:P0}");
            sb.AppendLine($"Fee/Hr Distribution: P25=${vm.P25FeePerHr:N0}, Median=${vm.MedianFeePerHr:N0}, P75=${vm.P75FeePerHr:N0}");
            sb.AppendLine($"Budget Accuracy: {vm.BudgetAccuracyPct:P0} within threshold, Median Abs Error: {vm.MedianAbsError:N0} hrs");
            sb.AppendLine();

            // Year-over-year rollup (Batch 71). This is the same data the
            // YoY view in the Historicals window builds; surfacing it here
            // lets AI answer trend questions ("is fee-per-hour rising YoY?",
            // "did billable % drop in 2024?") from context, without firing
            // a tool call to recompute the rollup.
            if (vm.YearTrendRows.Count > 0)
            {
                sb.AppendLine("=== YEAR-OVER-YEAR TREND (rollup of visible projects, oldest → newest) ===");
                sb.AppendLine("  Year | Projects | Total Fee | Avg Fee/Hr | Eng% | Billable% | Sub% | AR Outstanding | Firm Billable%");
                foreach (var y in vm.YearTrendRows)
                {
                    sb.AppendLine(
                        $"  {y.Year} | {y.ProjectCount,8} | ${y.TotalFee,12:N0} | ${y.AvgFeePerHr,4:N0} | " +
                        $"{y.WeightedEngPct,4:P0} | {y.WeightedBillablePct,4:P0} | {y.AvgSubPct,4:P0} | " +
                        $"${y.TotalArOutstanding,12:N0} | {y.FirmBillablePct,4:P0}");
                }
                sb.AppendLine();
            }

            // All employees
            if (vm.EmployeeSummaryRows.Count > 0)
            {
                var rateLookup = employeeRates?
                    .GroupBy(r => r.EmployeeId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                sb.AppendLine("=== ALL EMPLOYEES ===");
                if (employeeRates != null && employeeRates.Count > 0)
                {
                    var partnerRateText = partnerImputedCostRate.HasValue
                        ? $"${partnerImputedCostRate.Value:N0}/hr"
                        : "the configured Partner imputed cost rate";
                    sb.AppendLine("Rate note: BillingRate from Deltek EMCompany. CostRate raw from EMCompany for non-Partners;");
                    sb.AppendLine($"for Partners (EmployeeId starting with 'P'), an imputed cost of {partnerRateText} is applied");
                    sb.AppendLine("because Partners are paid via distributions, not hours. Adjustable via");
                    sb.AppendLine("DeltekOdbcOptions.PartnerImputedCostRate.");
                }
                foreach (var e in vm.EmployeeSummaryRows)
                {
                    sb.Append($"  {e.EmployeeName} | {e.PrimaryRole} | {e.ProjectCount} projects | ");
                    sb.Append($"Score: {e.ProductivityScore:N0} ({e.ProductivityGrade}) | ");
                    sb.Append($"Billable: {e.BillableRateScore:N0} | Efficiency: {e.EfficiencyScore:N0} | Health: {e.ProjectHealthScore:N0} | ");
                    sb.Append($"Fee/Hr: ${e.FeePerHr:N0} | {e.ConsistencyLabel}");
                    if (e.TenureYears > 0) sb.Append($" | Tenure: {e.TenureYears:N1}yrs");
                    if (e.PeerCount >= 2) sb.Append($" | vs Peers: {e.VsPeerPct:N0}%");
                    if (rateLookup != null && rateLookup.TryGetValue(e.EmployeeId, out var rate))
                    {
                        sb.Append($" | Billing: ${rate.BillingRate:N0}/hr");
                        sb.Append($" | Cost: ${rate.EffectiveCostRate:N0}/hr{(rate.IsPartner ? " (imputed)" : "")}");
                        sb.Append($" | Margin/hr: ${rate.BillingRate - rate.EffectiveCostRate:N0}");
                    }
                    else
                    {
                        sb.Append(" | Billing: n/a | Cost: n/a | Margin/hr: n/a");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            if (employeeWeeklyHours != null && employeeWeeklyHours.Count > 0)
            {
                sb.AppendLine("=== EMPLOYEE WEEKLY UTILIZATION (last 12 weeks, most recent last) ===");
                sb.AppendLine($"  Name | W1% | W2% | W3% | W4% | W5% | W6% | W7% | W8% | W9% | W10% | W11% | W12% | Longest <{UtilizationTriggerThresholdPct}% streak | 3wk trigger");

                foreach (var employee in employeeWeeklyHours
                    .GroupBy(h => h.EmployeeId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.FirstOrDefault()?.EmployeeName ?? g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var weeklyData = employee
                        .OrderBy(h => h.WeekStart)
                        .TakeLast(12)
                        .ToList();

                    var displayWeeks = new List<string>();
                    var utilizationWeeks = new List<int>();
                    foreach (var week in weeklyData)
                    {
                        if (week.TotalHrs > 0)
                        {
                            var pct = (int)Math.Round((week.BillableHrs / week.TotalHrs) * 100, 0);
                            displayWeeks.Add($"{pct}%");
                            utilizationWeeks.Add(pct);
                        }
                        else
                        {
                            displayWeeks.Add("-");
                        }
                    }

                    while (displayWeeks.Count < 12)
                    {
                        displayWeeks.Insert(0, "-");
                    }

                    var longestStreak = 0;
                    var currentStreak = 0;
                    foreach (var pct in utilizationWeeks)
                    {
                        if (pct < UtilizationTriggerThresholdPct)
                        {
                            currentStreak++;
                            if (currentStreak > longestStreak) longestStreak = currentStreak;
                        }
                        else
                        {
                            currentStreak = 0;
                        }
                    }

                    var triggerActive = utilizationWeeks.Count >= 3 && utilizationWeeks.TakeLast(3).All(pct => pct < UtilizationTriggerThresholdPct) ? "Y" : "N";
                    sb.AppendLine($"  {employee.First().EmployeeName} | {string.Join(" | ", displayWeeks)} | {longestStreak} | {triggerActive}");
                }

                sb.AppendLine();
            }

            // Per-employee project breakdown (for bottom performers — shows WHICH projects are problematic)
            if (employeeProjectHours != null && allProjects != null && vm.EmployeeSummaryRows.Count > 0)
            {
                var projectLookup = new Dictionary<string, HistoricalProjectRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in allProjects) projectLookup.TryAdd(p.Wbs1, p);

                var bottom = vm.EmployeeSummaryRows
                    .Where(e => e.ProductivityScore < 60)
                    .OrderBy(e => e.ProductivityScore)
                    .Take(5);

                foreach (var emp in bottom)
                {
                    var projects = employeeProjectHours
                        .Where(h => h.EmployeeId.Equals(emp.EmployeeId, StringComparison.OrdinalIgnoreCase)
                            && projectLookup.ContainsKey(h.Wbs1))
                        .OrderByDescending(h => h.EngHrs + h.DraftHrs)
                        .Take(8)
                        .Select(h =>
                        {
                            var proj = projectLookup[h.Wbs1];
                            var overBudget = proj.EstEngBudget > 0 && proj.EngHrs > proj.EstEngBudget * Kor.Operations.Financials.AnalyticsThresholds.OverBudgetFactor;
                            return $"    {proj.Wbs1} {proj.Name}: {h.EngHrs + h.DraftHrs:N0}hrs, ${proj.TotalFee:N0} fee, " +
                                   $"$/Hr: ${proj.FeePerHr:N0}{(overBudget ? " [OVER BUDGET]" : "")}";
                        });

                    sb.AppendLine($"  --- {emp.EmployeeName}'s top projects (score {emp.ProductivityScore:N0}) ---");
                    foreach (var line in projects) sb.AppendLine(line);
                    sb.AppendLine();
                }
            }

            // All PMs
            if (vm.PmSummaryRows.Count > 0)
            {
                var asOf = DateTime.Today;
                var feeBookedT12 = allProjects?
                    .Where(p => p.OpenDate.HasValue && p.OpenDate.Value >= asOf.AddMonths(-12))
                    .GroupBy(p => p.Pm ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalFee), StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var feeBookedT24 = allProjects?
                    .Where(p => p.OpenDate.HasValue && p.OpenDate.Value >= asOf.AddMonths(-24))
                    .GroupBy(p => p.Pm ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalFee), StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var feeBookedT36 = allProjects?
                    .Where(p => p.OpenDate.HasValue && p.OpenDate.Value >= asOf.AddMonths(-36))
                    .GroupBy(p => p.Pm ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalFee), StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                sb.AppendLine("=== PROJECT MANAGERS ===");
                foreach (var p in vm.PmSummaryRows)
                {
                    sb.Append($"  {p.Pm} | {p.ProjectCount} projects | ${p.TotalFee:N0} fee | ");
                    sb.Append($"Grade: {p.PerformanceGrade} ({p.PerformanceScore:N0}) | ");
                    sb.Append($"Delivery: {p.DeliveryHealthScore:N0} | Estimation: {p.EstimationAccuracyScore:N0} | ");
                    sb.Append($"Revenue: {p.RevenueEfficiencyScore:N0} | AR: {p.ArManagementScore:N0} | ");
                    sb.Append($"Clients: {p.UniqueClients} ({p.RepeatClients} repeat, {p.RepeatRate:P0}) | ");
                    sb.Append($"Billing: {p.AvgMonthsToFirstBill:N1}mo to first bill, {p.PctBilledWithin6Months:P0} in 6mo");
                    if (p.TotalAr90Plus > 0) sb.Append($" | AR 90+: ${p.TotalAr90Plus:N0}");
                    if (allProjects != null && allProjects.Count > 0)
                    {
                        feeBookedT12.TryGetValue(p.Pm, out var t12);
                        feeBookedT24.TryGetValue(p.Pm, out var t24);
                        feeBookedT36.TryGetValue(p.Pm, out var t36);
                        sb.Append($" | Booked T12: ${t12:N0} | Booked T24: ${t24:N0} | Booked T36: ${t36:N0}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // All DMs
            if (vm.DmSummaryRows.Count > 0)
            {
                sb.AppendLine("=== DRAFTING MANAGERS ===");
                foreach (var d in vm.DmSummaryRows)
                {
                    sb.Append($"  {d.Pm} | {d.ProjectCount} projects | ${d.TotalFee:N0} fee | ");
                    sb.Append($"Grade: {d.PerformanceGrade} ({d.PerformanceScore:N0})");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // At-risk / over-budget projects
            if (allProjects != null)
            {
                var atRisk = allProjects
                    .Where(p => p.EstEngBudget > 0 && p.EngHrs > p.EstEngBudget * Kor.Operations.Financials.AnalyticsThresholds.OverBudgetFactor && p.TotalFee > OverBudgetFeeMinimum)
                    .OrderByDescending(p => p.EngHrs - p.EstEngBudget)
                    .Take(10);

                var riskList = atRisk.ToList();
                if (riskList.Count > 0)
                {
                    sb.AppendLine("=== OVER-BUDGET PROJECTS (eng hrs > 135% of estimate) ===");
                    foreach (var p in riskList)
                    {
                        sb.AppendLine($"  {p.Wbs1} {p.Name} | PM: {p.Pm} | ${p.TotalFee:N0} | Eng: {p.EngHrs:N0}/{p.EstEngBudget:N0} ({p.EngHrs / p.EstEngBudget:P0}) | AR 90+: ${p.Ar90Plus:N0}");
                    }
                    sb.AppendLine();
                }
            }

            if (allProjects != null && allProjects.Count > 0)
            {
                var projects = allProjects
                    .OrderByDescending(p => p.TotalFee)
                    .Take(ProjectsSectionCap)
                    .ToList();

                sb.AppendLine("=== PROJECTS (historical + active) ===");
                sb.AppendLine("  Wbs1 | Name | PM | ClientId | Type | Open | Close | Fee | Hrs | Fee/Hr");
                foreach (var proj in projects)
                {
                    var open = proj.OpenDate?.ToString("yyyy-MM-dd") ?? "";
                    var close = proj.CloseDate?.ToString("yyyy-MM-dd") ?? "active";
                    var hours = proj.TotalEngDraft;
                    var feePerHr = hours > 0 ? $"${proj.TotalFee / hours:N0}/hr" : "-";
                    sb.AppendLine($"  {proj.Wbs1} | {proj.Name} | {proj.Pm} | {proj.ClientId} | {proj.ConstructionType} | {open} | {close} | ${proj.TotalFee:N0} | {hours:N0} | {feePerHr}");
                }
                if (allProjects.Count > ProjectsSectionCap)
                {
                    sb.AppendLine($"  (Showing top {ProjectsSectionCap} of {allProjects.Count} by fee.)");
                }
                sb.AppendLine();
            }

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

            // Currently selected summary detail (PM/DM/Employee views)
            if (!string.IsNullOrWhiteSpace(vm.DetailTitle))
            {
                sb.AppendLine($"=== CURRENTLY SELECTED: {vm.DetailTitle} ({vm.DetailSubtitle}) ===");
                foreach (var m in vm.DetailMetrics)
                {
                    if (m.IsHeader) sb.AppendLine($"\n  [{m.Label}]");
                    else if (!m.IsExplanation && !string.IsNullOrWhiteSpace(m.Value))
                        sb.AppendLine($"    {m.Label}: {m.Value}");
                }
            }

            // KPI methodology (Batch 71). The Historicals window exposes a
            // lot of project-level computed columns (%Billed, Fee/Hr, Eng%,
            // SubPctOfFee, etc.). Surface the dictionary entries so AI
            // explains them in KOR's voice — billable-hour exclusions,
            // labor-code mapping, FX bucketing — rather than guessing.
            var methodology = Kor.Operations.Financials.FinancialMetricDefinitions.BuildAiMethodologyBlock(new[]
            {
                "Hist_PctBilled", "Hist_FeePerHr", "Hist_EngPct", "Hist_DraftPct",
                "Hist_BillablePct", "Hist_SubPctOfFee", "Hist_EngDelta", "Hist_DraftDelta",
            });
            if (methodology != null)
            {
                sb.AppendLine();
                sb.AppendLine("KPI methodology (so you can explain how each number is calculated):");
                sb.Append(methodology);
            }

            return sb.ToString();
        }
    }
}
