#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Kor.Operations.Services;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// <see cref="IAiContextProvider"/> implementation for the Staff
    /// Utilization window. Pushes the currently-loaded per-employee rows +
    /// the active search filter so AI sees what the user is looking at when
    /// they ask "who is underutilized" / "who is at risk of burnout".
    ///
    /// Pre-Batch 102 this window hosted an AiQueryPanel without ever
    /// registering a provider — Claude received only the global firmwide
    /// context (FirmContextProvider) and silently answered staff-utilization
    /// questions blind. The AiPanelContextProviderTests static gate now
    /// catches this category of gap so it cannot recur.
    ///
    /// Methodology stays on the MCP server (system prompt + get_utilization
    /// / get_employee_utilization tool descriptions) per the Batch 92c
    /// trim — this provider only pushes ON-SCREEN DATA.
    /// </summary>
    public partial class StaffUtilizationWindow : IAiContextProvider
    {
        string IAiContextProvider.ProviderName => "Staff Utilization (last 12 weeks)";

        bool IAiContextProvider.HasData => _rows.Count > 0;

        string IAiContextProvider.BuildContext()
        {
            // Snapshot _rows once. BuildContext runs on AppAiContextBuilder's
            // worker thread; LoadAsync's Clear/AddRange runs on the UI thread.
            // Without this snapshot a refresh mid-Ask races prompt construction
            // and AppAiContextBuilder's try/catch silently drops the section
            // (Codex audit Batch 102, finding #2).
            var rows = _rows.ToArray();
            if (rows.Length == 0) return "No staff utilization rows loaded.";

            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(capacity: 4096);

            // ── Firmwide summary ────────────────────────────────────────
            var headcount = rows.Length;
            var avgUtil = rows.Average(r => r.UtilizationPct);
            var avgBillable = rows.Average(r => r.BillablePct);
            var totalOt = rows.Sum(r => r.OvtHrs12Wk);
            var lowCount = rows.Count(r => r.Status == "Low");
            var highCount = rows.Count(r => r.Status == "High");

            sb.AppendFormat(ic,
                "Window: trailing 12 weeks (tkDetail). Headcount in view: {0}. " +
                "Avg utilization {1:P0}, avg billable {2:P0}. Status mix: {3} High / {4} Normal / {5} Low. " +
                "Total OT hours across the team: {6:N1}.",
                headcount, avgUtil, avgBillable,
                highCount, headcount - highCount - lowCount, lowCount, totalOt).AppendLine();
            sb.AppendLine();

            // ── Per-employee detail ─────────────────────────────────────
            // Cap to keep prompt budget bounded; sort by utilization desc so
            // the highest-loaded staff appear first.
            sb.AppendLine("--- PER-EMPLOYEE (sorted by Utilization% desc) ---");
            var ordered = rows
                .OrderByDescending(r => r.UtilizationPct)
                .Take(150)
                .ToList();

            foreach (var r in ordered)
            {
                sb.AppendFormat(ic,
                    "  {0} | 12wk avg {1:N1} hrs/wk | Util {2:P0} ({3}) | Billable {4:P0} ({5}) | " +
                    "OT 12wk {6:N1} | Projects {7} | Cost/billable-hr ${8:N0} | Trend {9}",
                    r.EmployeeName,
                    r.TwelveWkAvg, r.UtilizationPct, r.Status,
                    r.BillablePct, r.BillableStatus,
                    r.OvtHrs12Wk, r.ProjectCount,
                    r.CostPerBillableHr, r.Trend).AppendLine();
            }

            if (rows.Length > ordered.Count)
            {
                sb.AppendFormat(ic, "  …and {0} more not listed (cap = 150).", rows.Length - ordered.Count)
                  .AppendLine();
            }

            return sb.ToString();
        }

        string IAiContextProvider.BuildLocalContext()
        {
            // The window has a single user-controllable scope knob — the search
            // box, which filters the visible rows. Surface it so AI's "look at
            // who's underutilized" answer reflects the slice, not the full list.
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return "";
            return $"User is filtering the Staff Utilization grid by: \"{q}\". " +
                   "Only rows matching that text on EmployeeName / Status / Trend are visible.";
        }
    }
}
