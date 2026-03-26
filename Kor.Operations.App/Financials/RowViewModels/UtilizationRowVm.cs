#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class UtilizationRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string BillableHoursText { get; }
    public string NonBillableHoursText { get; }
    public string TotalHoursText { get; }
    public string UtilizationPctText { get; }

    public UtilizationRowVm(
        string wbs1,
        string projectName,
        string pm,
        double billableHours,
        double nonBillableHours,
        double totalHours,
        double utilizationPct)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        BillableHoursText = billableHours.ToString("N1", CultureInfo.CurrentCulture);
        NonBillableHoursText = nonBillableHours.ToString("N1", CultureInfo.CurrentCulture);
        TotalHoursText = totalHours.ToString("N1", CultureInfo.CurrentCulture);
        UtilizationPctText = utilizationPct.ToString("P1", CultureInfo.CurrentCulture);
    }
}
