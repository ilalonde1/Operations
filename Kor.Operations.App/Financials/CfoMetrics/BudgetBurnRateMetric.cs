#nullable enable
using System;

namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Burn-rate index comparing hours consumption to billing progress.
    /// Values above 1.0 indicate hours are being consumed faster than fee is being billed.
    /// </summary>
    public sealed class BudgetBurnRateMetric : ICfoMetric
    {
        public string Name => "Budget Burn Rate";

        public string Description =>
            "WHAT:\n" +
            "A burn-rate index comparing delivery effort consumption to billing progress.\n\n" +
            "WHY IT MATTERS:\n" +
            "When burn rate is above 1.0, execution is outrunning billing which often compresses margin and signals overrun risk.\n\n" +
            "HOW IT IS CALCULATED:\n" +
            "Percent Hours Spent / Percent Billed (with safeguards when Percent Billed is 0).";

        public string Formula => "BurnRate = PercentHoursSpent / PercentBilled";

        public decimal ComputeValue(ProjectData data)
        {
            data ??= ProjectData.Create();

            var billed = data.PercentBilled;
            if (billed <= 0m)
                return 0m;

            return data.PercentHoursSpent / Math.Abs(billed);
        }
    }
}
