#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Project-based "P&amp;L" derived from the same Deltek project KPI sources used by the Financials dashboard.
    /// This is not a true GL-based income statement; it is intended as an operational view.
    /// </summary>
public sealed class ProfitLossReportService
    {
        // Defaults chosen to match current FinancialsService constants; override via App.config if needed.
        private readonly decimal _engRate;
        private readonly decimal _draftRate;
        private readonly decimal _otherDirectRate;
        private readonly decimal _overheadRate;
        private readonly FinancialsService _financialsService;

        public ProfitLossReportService(FinancialsService financialsService, FinancialsOptions financialsOptions)
        {
            _financialsService = financialsService ?? throw new ArgumentNullException(nameof(financialsService));
            _engRate = ReadDecimal(financialsOptions.PnLEngRate, 550m);
            _draftRate = ReadDecimal(financialsOptions.PnLDraftRate, 550m);
            _otherDirectRate = ReadDecimal(financialsOptions.PnLOtherDirectRate, 550m);
            _overheadRate = ReadDecimal(financialsOptions.PnLOverheadRate, 550m);
        }

        public async Task<IReadOnlyList<ProfitLossReportRow>> LoadAsync(bool forceRefresh, CancellationToken cancelToken)
        {
            var snap = await _financialsService.GetSnapshotAsync(forceRefresh, cancelToken).ConfigureAwait(false);

            var revenue = (decimal)snap.Rows.Sum(r => r.FeeBilled);

            // Direct labor: delivery work (exclude Gen/Admin/NonBill as overhead-ish).
            var engHours = (decimal)snap.Rows.Sum(r => r.EngHrs);
            var draftHours = (decimal)snap.Rows.Sum(r => r.DraftHrs);
            var otherDirectHours = (decimal)snap.Rows.Sum(r => r.ChkHrs + r.InspHrs + r.DocPrepHrs);

            var directLabor =
                (engHours * _engRate) +
                (draftHours * _draftRate) +
                (otherDirectHours * _otherDirectRate);

            // Overhead proxy: general/admin/nonbill hours at overhead rate.
            var overheadHours = (decimal)snap.Rows.Sum(r => r.GenHrs + r.AdminHrs + r.NonBillHrs);
            var overhead = overheadHours * _overheadRate;

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
                new() { Section = "Direct Expenses", LineItem = "Engineering Labor (hours x rate)", ValueKind = ProfitLossValueKind.Currency, Amount = engHours * _engRate },
                new() { Section = "Direct Expenses", LineItem = "Drafting Labor (hours x rate)", ValueKind = ProfitLossValueKind.Currency, Amount = draftHours * _draftRate },
                new() { Section = "Direct Expenses", LineItem = "Other Direct Labor (hours x rate)", ValueKind = ProfitLossValueKind.Currency, Amount = otherDirectHours * _otherDirectRate },
                new() { Section = "Direct Expenses", LineItem = "Total Direct Labor (proxy)", ValueKind = ProfitLossValueKind.Currency, Amount = directLabor },

                // Gross Profit
                new() { Section = "Gross Profit", LineItem = "Gross Profit", ValueKind = ProfitLossValueKind.Currency, Amount = grossProfit },

                // Overhead (proxy)
                new() { Section = "Overhead", LineItem = "General/Admin/Non-billable (hours x rate)", ValueKind = ProfitLossValueKind.Currency, Amount = overhead },

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
                    "This statement is derived from Deltek project KPIs (PRSummaryMain revenue + tkDetail hours). " +
                    "It is not a GL-based income statement; non-labor direct expenses and true overhead are not included unless modeled via rates.",
                ValueKind = ProfitLossValueKind.Text,
                Text = ""
            });

            return rows;
        }

        private static decimal ReadDecimal(string raw, decimal @default)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return @default;

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return d;

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out d))
                return d;

            return @default;
        }
    }
}
