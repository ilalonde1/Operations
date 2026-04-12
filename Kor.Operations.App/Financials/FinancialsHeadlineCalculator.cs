#nullable enable
using System.Collections.Generic;
using static Kor.Operations.Core.MathHelpers;

namespace Kor.Operations.Financials;

internal static class FinancialsHeadlineCalculator
{
    internal static FinancialsHeadlineKpis Compute(List<FinancialsProjectRow> rows)
    {
        var totalFees = 0.0;
        var totalFeeBilled = 0.0;
        var totalGfa = 0.0;
        var hoursSpent = 0.0;
        var hoursBudgeted = 0.0;
        var feeWhereGfa = 0.0;
        var gfaWhereGfa = 0.0;

        foreach (var r in rows)
        {
            totalFees += r.Fee;
            totalFeeBilled += r.FeeBilled;
            totalGfa += r.Gfa;
            hoursSpent += r.EngHrs + r.DraftHrs;
            hoursBudgeted += r.DraftBudget + r.EngBudget;
            if (r.Gfa > 0)
            {
                feeWhereGfa += r.Fee;
                gfaWhereGfa += r.Gfa;
            }
        }

        var totalUnbilled = totalFees - totalFeeBilled;
        var percentFeeUnbilled = SafeDiv(totalUnbilled, totalFees);
        var avgFeePerFt2 = gfaWhereGfa > 0 ? (feeWhereGfa / gfaWhereGfa) : 0.0;
        var hoursRemaining = hoursBudgeted - hoursSpent;
        var percentHoursSpent = SafeDiv(hoursSpent, hoursBudgeted);
        var teamDaysRemaining = hoursRemaining / 7.5 / 35.0;

        return new FinancialsHeadlineKpis
        {
            Projects = rows.Count,
            TotalFees = totalFees,
            TotalFeeBilled = totalFeeBilled,
            TotalGfa = totalGfa,
            HoursSpent = hoursSpent,
            HoursBudgeted = hoursBudgeted,
            TotalUnbilled = totalUnbilled,
            PercentFeeUnbilled = percentFeeUnbilled,
            AvgFeePerFt2 = avgFeePerFt2,
            HoursRemaining = hoursRemaining,
            PercentHoursSpent = percentHoursSpent,
            TeamDaysRemaining = teamDaysRemaining
        };
    }
}

public sealed class FinancialsHeadlineKpis
{
    public int Projects { get; set; }
    public double TotalFees { get; set; }
    public double TotalFeeBilled { get; set; }
    public double TotalGfa { get; set; }
    public double HoursSpent { get; set; }
    public double HoursBudgeted { get; set; }
    public double TotalUnbilled { get; set; }
    public double PercentFeeUnbilled { get; set; }
    public double AvgFeePerFt2 { get; set; }
    public double HoursRemaining { get; set; }
    public double PercentHoursSpent { get; set; }
    public double TeamDaysRemaining { get; set; }
}
