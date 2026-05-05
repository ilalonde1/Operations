#nullable enable
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class BillingsRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string FeeBilledText { get; }
    public string EstimatedFeeBilledText { get; }
    public string FeeText { get; }
    public string PercentBilledText { get; }
    public string EstimatedPercentBilledText { get; }
    public string ContributionText { get; }
    public bool   HasUnpostedBilling { get; }

    public BillingsRowVm(
        string wbs1,
        string projectName,
        string pm,
        double feeBilled,
        double unpostedFeeBilled,
        double fee,
        double percentBilled,
        double estimatedPercentBilled,
        double contributionPercent,
        bool hasUnpostedBilling)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        FeeBilledText = feeBilled.ToString("C0", CultureInfo.CurrentCulture);
        EstimatedFeeBilledText = (feeBilled + unpostedFeeBilled).ToString("C0", CultureInfo.CurrentCulture);
        FeeText = fee.ToString("C0", CultureInfo.CurrentCulture);
        PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
        EstimatedPercentBilledText = estimatedPercentBilled.ToString("P1", CultureInfo.CurrentCulture);
        ContributionText = contributionPercent.ToString("P1", CultureInfo.CurrentCulture);
        HasUnpostedBilling = hasUnpostedBilling;
    }
}
