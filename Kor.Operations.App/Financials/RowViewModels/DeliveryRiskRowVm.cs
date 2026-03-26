#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class DeliveryRiskRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string DeliveryRiskText { get; }
    public string BudgetStatusText { get; }
    public string PercentUsedText { get; }
    public string RemainingHoursText { get; }

    public DeliveryRiskRowVm(
        string wbs1,
        string projectName,
        string pm,
        string deliveryRisk,
        string budgetStatus,
        double percentUsed,
        double remainingHours)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        DeliveryRiskText = deliveryRisk ?? string.Empty;
        BudgetStatusText = budgetStatus ?? string.Empty;
        PercentUsedText = percentUsed.ToString("P1", CultureInfo.CurrentCulture);
        RemainingHoursText = remainingHours.ToString("N1", CultureInfo.CurrentCulture);
    }
}
