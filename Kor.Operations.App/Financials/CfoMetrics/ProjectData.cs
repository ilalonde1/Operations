#nullable enable
using System;
using Kor.Operations.Financials;
using static Kor.Operations.Core.MathHelpers;
namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Normalized project-level inputs for CFO metrics.
    /// This intentionally mirrors what the Financials dashboard already computes, without changing any upstream logic.
    /// </summary>
    public sealed class ProjectData
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }

        public decimal Fee { get; }
        public decimal FeeBilled { get; }
        public decimal PercentBilled { get; }

        public decimal HoursSpent { get; }
        public decimal HoursBudgeted { get; }
        public decimal PercentHoursSpent { get; }

        public DeliveryConfidenceLevel DeliveryConfidenceLevel { get; }

        public PortfolioHealthCounts? PortfolioCounts { get; }

        internal ProjectData(
            string wbs1,
            string projectName,
            decimal fee,
            decimal feeBilled,
            decimal percentBilled,
            decimal hoursSpent,
            decimal hoursBudgeted,
            decimal percentHoursSpent,
            DeliveryConfidenceLevel deliveryConfidenceLevel,
            PortfolioHealthCounts? portfolioCounts)
        {
            Wbs1 = wbs1;
            ProjectName = projectName;
            Fee = fee;
            FeeBilled = feeBilled;
            PercentBilled = percentBilled;
            HoursSpent = hoursSpent;
            HoursBudgeted = hoursBudgeted;
            PercentHoursSpent = percentHoursSpent;
            DeliveryConfidenceLevel = deliveryConfidenceLevel;
            PortfolioCounts = portfolioCounts;
        }

        /// <summary>
        /// Create a ProjectData instance from explicit values.
        /// Intended for unit tests and deterministic scenarios; production code should prefer FromProject().
        /// </summary>
        public static ProjectData Create(
            string wbs1 = "",
            string projectName = "",
            decimal fee = 0m,
            decimal feeBilled = 0m,
            decimal percentBilled = 0m,
            decimal hoursSpent = 0m,
            decimal hoursBudgeted = 0m,
            DeliveryConfidenceLevel deliveryConfidenceLevel = DeliveryConfidenceLevel.HighConfidence,
            PortfolioHealthCounts? portfolioCounts = null)
        {
            var percentHoursSpent = SafeDiv(hoursSpent, hoursBudgeted);
            return new ProjectData(
                wbs1: wbs1,
                projectName: projectName,
                fee: fee,
                feeBilled: feeBilled,
                percentBilled: percentBilled,
                hoursSpent: hoursSpent,
                hoursBudgeted: hoursBudgeted,
                percentHoursSpent: percentHoursSpent,
                deliveryConfidenceLevel: deliveryConfidenceLevel,
                portfolioCounts: portfolioCounts);
        }

        public static ProjectData FromProject(FinancialsProjectRow p, PortfolioHealthCounts? portfolioCounts = null)
        {
            p ??= new FinancialsProjectRow();

            var fee = (decimal)(p.Fee);
            var feeBilled = (decimal)(p.FeeBilled);
            var percentBilled = (decimal)(p.PercentBilled);
            if (percentBilled == 0m && fee != 0m)
                percentBilled = SafeDiv(feeBilled, fee);

            var hoursSpent =
                (decimal)(p.EngHrs + p.DraftHrs + p.InspHrs + p.DocPrepHrs + p.GenHrs + p.AdminHrs + p.NonBillHrs);
            var hoursBudgeted = (decimal)(p.DraftBudget + p.EngBudget);
            var percentHoursSpent = SafeDiv(hoursSpent, hoursBudgeted);

            var dc = DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                DeliveryConfidenceLevel.HighConfidence;

            return new ProjectData(
                wbs1: (p.Wbs1 ?? string.Empty).Trim(),
                projectName: (p.Name ?? string.Empty).Trim(),
                fee: fee,
                feeBilled: feeBilled,
                percentBilled: percentBilled,
                hoursSpent: hoursSpent,
                hoursBudgeted: hoursBudgeted,
                percentHoursSpent: percentHoursSpent,
                deliveryConfidenceLevel: level,
                portfolioCounts: portfolioCounts);
        }

    }

    public sealed record PortfolioHealthCounts(int Healthy, int Watch, int Critical);
}
