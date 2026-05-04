#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Project-based "P&amp;L" derived from the same Deltek project KPI sources used by the Financials dashboard.
    /// This is not a true GL-based income statement; it is intended as an operational view.
    /// </summary>
public sealed class ProfitLossReportService
    {
        private readonly FinancialsService _financialsService;

        public ProfitLossReportService(FinancialsService financialsService)
        {
            _financialsService = financialsService ?? throw new ArgumentNullException(nameof(financialsService));
        }

        public async Task<IReadOnlyList<ProfitLossReportRow>> LoadAsync(bool forceRefresh, CancellationToken cancelToken)
        {
            var snap = await _financialsService.GetSnapshotAsync(forceRefresh, cancelToken, watchlistOnly: false).ConfigureAwait(false);

            var revenue = (decimal)snap.Rows.Sum(r => r.FeeBilled);

            // Direct labor cost: Deltek-computed labor cost from tkDetail
            // (RegAmt + OvtAmt + SpecialOvtAmt = hours × employee's ProvCostRate at timesheet-entry time).
            var engLaborCost     = (decimal)snap.Rows.Sum(r => r.EngLaborCost);
            var draftLaborCost   = (decimal)snap.Rows.Sum(r => r.DraftLaborCost);
            var otherDirectCost  = (decimal)snap.Rows.Sum(r => r.InspLaborCost + r.DocPrepLaborCost);
            var directLabor      = engLaborCost + draftLaborCost + otherDirectCost;

            // Overhead: General + Admin + NonBillable labor cost on these projects.
            var overhead = (decimal)snap.Rows.Sum(r => r.GenLaborCost + r.AdminLaborCost + r.NonBillLaborCost);

            var grossProfit = revenue - directLabor;
            var netIncome = grossProfit - overhead;

            decimal? GrossMarginPct() => revenue == 0m ? null : grossProfit / revenue;
            decimal? DirectLaborMultiplier() => directLabor == 0m ? null : revenue / directLabor;
            decimal? OverheadRatio() => directLabor == 0m ? null : overhead / directLabor;

            // Flat list for now; window renders in order.
            var rows = new List<ProfitLossReportRow>
            {
                // Income
                new() { Section = "Income", LineItem = "Fee Billed (Revenue)", ValueKind = ProfitLossValueKind.Currency, Amount = revenue },

                // Direct Expenses (proxy)
                new() { Section = "Direct Expenses", LineItem = "Engineering Labor", ValueKind = ProfitLossValueKind.Currency, Amount = engLaborCost },
                new() { Section = "Direct Expenses", LineItem = "Drafting Labor", ValueKind = ProfitLossValueKind.Currency, Amount = draftLaborCost },
                new() { Section = "Direct Expenses", LineItem = "Other Direct Labor (Inspection + DocPrep)", ValueKind = ProfitLossValueKind.Currency, Amount = otherDirectCost },
                new() { Section = "Direct Expenses", LineItem = "Total Direct Labor (proxy)", ValueKind = ProfitLossValueKind.Currency, Amount = directLabor },

                // Gross Profit
                new() { Section = "Gross Profit", LineItem = "Gross Profit", ValueKind = ProfitLossValueKind.Currency, Amount = grossProfit },

                // Overhead (proxy)
                new() { Section = "Overhead", LineItem = "General/Admin/Non-billable Labor", ValueKind = ProfitLossValueKind.Currency, Amount = overhead },

                // Net Income
                new() { Section = "Net Income", LineItem = "Net Income (derived)", ValueKind = ProfitLossValueKind.Currency, Amount = netIncome },

                // Ratios
                new() { Section = "Ratios", LineItem = "Gross Profit Margin", ValueKind = ProfitLossValueKind.Percent, Amount = GrossMarginPct() },
                new() { Section = "Ratios", LineItem = "Direct Labor Multiplier", ValueKind = ProfitLossValueKind.Number, Amount = DirectLaborMultiplier() },
                new() { Section = "Ratios", LineItem = "Overhead Ratio", ValueKind = ProfitLossValueKind.Percent, Amount = OverheadRatio() },
            };

            // Stash note rows at bottom so users understand what they are seeing.
            rows.Add(new ProfitLossReportRow
            {
                Section = "Notes",
                LineItem =
                    "Direct Labor cost is sourced from Deltek tkDetail (RegAmt + OvtAmt + SpecialOvtAmt) — the Deltek-computed " +
                    "per-line labor cost using each employee's ProvCostRate. This is not a GL-based income statement; " +
                    "non-labor direct expenses (subconsultants, equipment, software) and non-labor overhead are not included.",
                ValueKind = ProfitLossValueKind.Text,
                Text = ""
            });

            return rows;
        }
    }
}
