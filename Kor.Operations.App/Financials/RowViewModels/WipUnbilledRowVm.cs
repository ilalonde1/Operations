#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class WipUnbilledRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string EarnedText { get; }
    public string OverbilledText { get; }
    public string NetText { get; }
    public string NetPctFeeText { get; }
    public string PeriodText { get; }

    public WipUnbilledRowVm(
        string wbs1,
        string projectName,
        string pm,
        double earned,
        double overbilled,
        double net,
        double netAsPercentOfFee,
        string period)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        EarnedText = earned.ToString("C0", CultureInfo.CurrentCulture);
        OverbilledText = overbilled.ToString("C0", CultureInfo.CurrentCulture);
        NetText = net.ToString("C0", CultureInfo.CurrentCulture);
        NetPctFeeText = netAsPercentOfFee.ToString("P1", CultureInfo.CurrentCulture);
        PeriodText = string.IsNullOrWhiteSpace(period) ? string.Empty : period.Trim();
    }
}

