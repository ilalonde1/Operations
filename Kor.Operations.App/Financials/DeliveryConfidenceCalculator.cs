using System;
using System.Globalization;

namespace Kor.Operations.Financials
{
    internal static class DeliveryConfidenceCalculator
    {
        internal sealed class Result
        {
            public string Status { get; init; } = "High Confidence";
            public string ColorName { get; init; } = "Green";
            public string Summary { get; init; } = "";
            public string Tooltip { get; init; } = "";
        }

        internal static Result Compute(FinancialsProjectRow? p)
        {
            p ??= new FinancialsProjectRow();

            var fee = p.Fee;
            var feeBilled = p.FeeBilled;

            var hoursSpent =
                p.EngHrs +
                p.DraftHrs +
                p.ChkHrs +
                p.InspHrs +
                p.DocPrepHrs +
                p.GenHrs +
                p.AdminHrs +
                p.NonBillHrs;

            var hoursBudgeted = p.DraftBudget + p.EngBudget;
            var remainingEng = p.EngBudget - p.EngHrs;

            var pctFeeBilled = fee <= 0.0 ? 0.0 : SafeDiv(feeBilled, fee);
            var pctHoursSpent = hoursBudgeted <= 0.0 ? 0.0 : SafeDiv(hoursSpent, hoursBudgeted);

            var gap = pctHoursSpent - pctFeeBilled;
            var watchThreshold = 0.15 * hoursBudgeted;

            if (feeBilled > fee || hoursSpent > hoursBudgeted)
            {
                var summary = feeBilled > fee
                    ? "Fee billed exceeds fee."
                    : "Hours spent exceeds budgeted hours.";

                return new Result
                {
                    Status = "Critical",
                    ColorName = "Red",
                    Summary = summary,
                    Tooltip = BuildTooltip(fee, feeBilled, pctFeeBilled, hoursSpent, hoursBudgeted, pctHoursSpent, remainingEng, watchThreshold, gap)
                };
            }

            if (pctHoursSpent > (pctFeeBilled + 0.15))
            {
                var summary = $"Hours spent {pctHoursSpent:P0} vs billed {pctFeeBilled:P0} (gap {gap:P0}).";
                return new Result
                {
                    Status = "At Risk",
                    ColorName = "Orange",
                    Summary = summary,
                    Tooltip = BuildTooltip(fee, feeBilled, pctFeeBilled, hoursSpent, hoursBudgeted, pctHoursSpent, remainingEng, watchThreshold, gap)
                };
            }

            if (remainingEng < watchThreshold)
            {
                var summary = $"Remaining engineering hours {remainingEng:N0} below {watchThreshold:N0} (15% of budget).";
                return new Result
                {
                    Status = "Watch",
                    ColorName = "Yellow",
                    Summary = summary,
                    Tooltip = BuildTooltip(fee, feeBilled, pctFeeBilled, hoursSpent, hoursBudgeted, pctHoursSpent, remainingEng, watchThreshold, gap)
                };
            }

            return new Result
            {
                Status = "High Confidence",
                ColorName = "Green",
                Summary = "On track based on current hours and billing.",
                Tooltip = BuildTooltip(fee, feeBilled, pctFeeBilled, hoursSpent, hoursBudgeted, pctHoursSpent, remainingEng, watchThreshold, gap)
            };
        }

        private static string BuildTooltip(
            double fee,
            double feeBilled,
            double pctFeeBilled,
            double hoursSpent,
            double hoursBudgeted,
            double pctHoursSpent,
            double remainingEng,
            double watchThreshold,
            double gap)
        {
            var feeBilledText = feeBilled.ToString("C0", CultureInfo.CurrentCulture);
            var feeText = fee.ToString("C0", CultureInfo.CurrentCulture);
            var hoursSpentText = hoursSpent.ToString("N1", CultureInfo.CurrentCulture);
            var hoursBudgetedText = hoursBudgeted.ToString("N1", CultureInfo.CurrentCulture);
            var remainingEngText = remainingEng.ToString("N1", CultureInfo.CurrentCulture);
            var watchThresholdText = watchThreshold.ToString("N1", CultureInfo.CurrentCulture);

            return $@"Fee billed: {feeBilledText} of {feeText} ({pctFeeBilled:P0})
Hours spent: {hoursSpentText} of {hoursBudgetedText} ({pctHoursSpent:P0})
Burn vs billed gap: {gap:P0} (burn - billed)
Remaining engineering hours: {remainingEngText} (watch threshold {watchThresholdText})";
        }

        private static double SafeDiv(double num, double den)
            => den == 0.0 ? 0.0 : (num / den);
    }
}
