#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kor.Operations.PMTools
{
    internal sealed class AnalyticsAiService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly string _apiKey;

        internal bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        internal AnalyticsAiService(string apiKey)
        {
            _apiKey = (apiKey ?? "").Trim();
        }

        internal async Task<string> ExplainAsync(string question, string dataContext, CancellationToken ct = default)
        {
            if (!IsConfigured) return "AI is not configured. Set the KOR_ANTHROPIC_KEY environment variable.";
            if (string.IsNullOrWhiteSpace(question)) return "";

            var systemPrompt =
                "You are an analytics assistant for KOR Structural, a structural engineering firm in Vancouver, BC. " +
                "You have access to the firm's complete project and employee performance data provided below.\n\n" +
                "Your audience is firm principals and project managers who are NOT data analysts. " +
                "Explain metrics, scores, and trends in plain, actionable language. Use specific names and numbers. " +
                "Be concise but thorough — answer in 3-6 sentences unless the question needs more.\n\n" +
                "You can compare employees, identify trends, flag concerns, and make recommendations based on the data. " +
                "If asked about something not in the data, say so clearly. Do NOT make up data or reference " +
                "information outside of what's provided below.\n\n" +
                "SCORING METHODOLOGY:\n" +
                "Employee Productivity Score (0-100, maps to A+ through F):\n" +
                "  Billable Rate (30%) — % of total hours on billable projects vs overhead/admin\n" +
                "  Efficiency (40%) — Fee/Hr percentile rank vs all employees. 50 = median.\n" +
                "  Project Health (30%) — % of hours on projects NOT over budget\n\n" +
                "PM/DM Performance Score (0-100, maps to A+ through F):\n" +
                "  Delivery Health (30%) — % of projects not over budget\n" +
                "  Estimation Accuracy (30%) — Budget delta percentile rank\n" +
                "  Revenue Efficiency (20%) — Fee/Hr percentile rank\n" +
                "  AR Management (20%) — % of AR not 90+ days overdue\n\n" +
                "Consistency: CV of hours across projects. Steady < 0.3, Variable < 0.6, Erratic ≥ 0.6\n" +
                "Peer Comparison: Fee/Hr compared against employees working on same construction type\n\n" +
                "FIRM DATA:\n" + dataContext;

            var requestBody = new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 800,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = question } }
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");

                using var response = await _http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            }
            catch (OperationCanceledException)
            {
                return "";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Analytics AI request failed.");
                return $"Unable to get AI response: {ex.Message}";
            }
        }

        internal static string BuildContext(HistoricalAnalyticsViewModel vm)
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

            // All employees
            if (vm.EmployeeSummaryRows.Count > 0)
            {
                sb.AppendLine("=== ALL EMPLOYEES ===");
                foreach (var e in vm.EmployeeSummaryRows)
                {
                    sb.Append($"  {e.EmployeeName} | {e.PrimaryRole} | {e.ProjectCount} projects | ");
                    sb.Append($"Score: {e.ProductivityScore:N0} ({e.ProductivityGrade}) | ");
                    sb.Append($"Billable: {e.BillableRateScore:N0} | Efficiency: {e.EfficiencyScore:N0} | Health: {e.ProjectHealthScore:N0} | ");
                    sb.Append($"Fee/Hr: ${e.FeePerHr:N0} | {e.ConsistencyLabel}");
                    if (e.TenureYears > 0) sb.Append($" | Tenure: {e.TenureYears:N1}yrs");
                    if (e.PeerCount >= 2) sb.Append($" | vs Peers: {e.VsPeerPct:N0}%");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // All PMs
            if (vm.PmSummaryRows.Count > 0)
            {
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

            // Currently selected project (Projects view)
            if (vm.SelectedRow is { } sel)
            {
                sb.AppendLine($"=== CURRENTLY SELECTED PROJECT: {sel.Wbs1} — {sel.Name} ===");
                sb.AppendLine($"  PM: {sel.Pm} | DM: {sel.DraftingManager} | Phase: {sel.Phase} | Status: {sel.Status}");
                sb.AppendLine($"  Type: {sel.ConstructionType} | Category: {sel.ProjectCategory} | Drafting: {sel.DraftingType}");
                sb.AppendLine($"  Duration: {sel.DurationDisplay} | Fee/Month: ${sel.FeePerMonth:N0}");
                sb.AppendLine($"  Fee: ${sel.Fee:N0} | Billed: ${sel.FeeBilled:N0} ({sel.PercentBilled:P0})");
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

            return sb.ToString();
        }
    }
}
