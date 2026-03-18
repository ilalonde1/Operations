#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class ArOutstandingRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string TotalText { get; }
    public string CurrentText { get; }
    public string Aged31To60Text { get; }
    public string Aged61To90Text { get; }
    public string Aged90PlusText { get; }
    public string OldestDateText { get; }

    public ArOutstandingRowVm(
        string wbs1,
        string projectName,
        string pm,
        double total,
        double current,
        double aged31To60,
        double aged61To90,
        double aged90Plus,
        DateTime? oldestInvoiceDate)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        TotalText = total.ToString("C0", CultureInfo.CurrentCulture);
        CurrentText = current.ToString("C0", CultureInfo.CurrentCulture);
        Aged31To60Text = aged31To60.ToString("C0", CultureInfo.CurrentCulture);
        Aged61To90Text = aged61To90.ToString("C0", CultureInfo.CurrentCulture);
        Aged90PlusText = aged90Plus.ToString("C0", CultureInfo.CurrentCulture);
        OldestDateText = oldestInvoiceDate.HasValue
            ? oldestInvoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
            : string.Empty;
    }
}

