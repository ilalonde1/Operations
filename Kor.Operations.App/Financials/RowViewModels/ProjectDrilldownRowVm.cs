#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class ProjectDrilldownRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string OverByHoursText { get; }
    public string PercentEngUsedText { get; }
    public string PercentBilledText { get; }

    public ProjectDrilldownRowVm(
        string wbs1,
        string projectName,
        string pm,
        double overByHours,
        double percentEngUsed,
        double percentBilled)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        OverByHoursText = string.Format(CultureInfo.CurrentCulture, "{0:N1} hrs", overByHours);
        PercentEngUsedText = percentEngUsed.ToString("P1", CultureInfo.CurrentCulture);
        PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
    }
}

