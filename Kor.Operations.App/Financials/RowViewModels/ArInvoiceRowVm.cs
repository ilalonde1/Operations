#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class ArInvoiceRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string InvoiceDateText { get; }
    public string DueDateText { get; }
    public string DaysPastDueText { get; }
    public string BalanceText { get; }

    public ArInvoiceRowVm(
        string wbs1,
        string projectName,
        string pm,
        DateTime? invoiceDate,
        DateTime? dueDate,
        int daysPastDue,
        double balance)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        InvoiceDateText = invoiceDate.HasValue ? invoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
        DueDateText = dueDate.HasValue ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
        DaysPastDueText = daysPastDue.ToString("N0", CultureInfo.CurrentCulture);
        BalanceText = balance.ToString("C0", CultureInfo.CurrentCulture);
    }
}

