#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class TrendPayerRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string PayerName { get; }
    public string AmountText { get; }
    public string RevenueText { get; }
    public string BilledText { get; }
    public string UnbilledText { get; }
    public string ArOutstandingText { get; }
    public string BilledVsRevenueText { get; }
    public string ArVsBilledText { get; }

    public TrendPayerRowVm(
        string wbs1,
        string projectName,
        string pm,
        string payerName,
        double amount,
        double revenueAmount,
        double billedAmount,
        double arOutstandingAmount)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        PayerName = payerName ?? string.Empty;
        AmountText = amount.ToString("C0", CultureInfo.CurrentCulture);
        RevenueText = revenueAmount.ToString("C0", CultureInfo.CurrentCulture);
        BilledText = billedAmount.ToString("C0", CultureInfo.CurrentCulture);
        UnbilledText = (revenueAmount - billedAmount).ToString("C0", CultureInfo.CurrentCulture);
        ArOutstandingText = arOutstandingAmount.ToString("C0", CultureInfo.CurrentCulture);
        var billedVsRevenue = Math.Abs(revenueAmount) <= 0.004 ? 0.0 : (billedAmount / revenueAmount);
        BilledVsRevenueText = billedVsRevenue.ToString("P1", CultureInfo.CurrentCulture);
        var arVsBilled = Math.Abs(billedAmount) <= 0.004 ? 0.0 : (arOutstandingAmount / billedAmount);
        ArVsBilledText = arVsBilled.ToString("P1", CultureInfo.CurrentCulture);
    }
}

