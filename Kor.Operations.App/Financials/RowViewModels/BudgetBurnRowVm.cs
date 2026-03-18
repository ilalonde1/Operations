#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class BudgetBurnRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string EngHoursText { get; }
    public string EngBudgetText { get; }
    public string PercentUsedText { get; }
    public string RemainingHoursText { get; }

    public BudgetBurnRowVm(
        string wbs1,
        string projectName,
        string pm,
        double engHours,
        double engBudget,
        double percentUsed,
        double remainingHours)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        EngHoursText = engHours.ToString("N1", CultureInfo.CurrentCulture);
        EngBudgetText = engBudget.ToString("N1", CultureInfo.CurrentCulture);
        PercentUsedText = percentUsed.ToString("P1", CultureInfo.CurrentCulture);
        RemainingHoursText = remainingHours.ToString("N1", CultureInfo.CurrentCulture);
    }
}
