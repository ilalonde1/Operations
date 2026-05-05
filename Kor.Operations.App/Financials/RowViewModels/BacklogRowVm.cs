#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class BacklogRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string FeeText { get; }
    public string BilledText { get; }
    public string EstimatedBilledText { get; }
    public string BacklogText { get; }
    public string PercentBilledText { get; }
    public string EstimatedPercentBilledText { get; }
    public bool   HasUnpostedBilling { get; }

    public BacklogRowVm(
        string wbs1,
        string projectName,
        string pm,
        double fee,
        double billed,
        double unpostedFeeBilled,
        double backlog,
        double percentBilled,
        double estimatedPercentBilled,
        bool hasUnpostedBilling)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        FeeText = fee.ToString("C0", CultureInfo.CurrentCulture);
        BilledText = billed.ToString("C0", CultureInfo.CurrentCulture);
        EstimatedBilledText = (billed + unpostedFeeBilled).ToString("C0", CultureInfo.CurrentCulture);
        BacklogText = backlog.ToString("C0", CultureInfo.CurrentCulture);
        PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
        EstimatedPercentBilledText = estimatedPercentBilled.ToString("P1", CultureInfo.CurrentCulture);
        HasUnpostedBilling = hasUnpostedBilling;
    }
}
