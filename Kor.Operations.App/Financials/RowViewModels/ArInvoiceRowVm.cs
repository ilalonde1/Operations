#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class ArInvoiceRowVm
{
    public string Wbs1 { get; }
    public string ProjectName { get; }
    public string Pm { get; }
    public string Invoice { get; }
    public string ClientId { get; }
    public string ClientName { get; }
    public string ClientDisplay { get; }
    public string InvoiceDateText { get; }
    public string DueDateText { get; }
    public string DaysPastDueText { get; }
    public string BalanceText { get; }

    public ArInvoiceRowVm(
        string wbs1,
        string projectName,
        string pm,
        string invoice,
        string clientId,
        string clientName,
        DateTime? invoiceDate,
        DateTime? dueDate,
        int daysPastDue,
        double balance)
    {
        Wbs1 = wbs1 ?? string.Empty;
        ProjectName = projectName ?? string.Empty;
        Pm = pm ?? string.Empty;
        Invoice = invoice ?? string.Empty;
        ClientId = clientId ?? string.Empty;
        ClientName = clientName ?? string.Empty;
        // Prefer the human-readable client name; fall back to the ClientID
        // when Clendor lookup misses (e.g., archived clients) so the column
        // always says *something*.
        ClientDisplay = !string.IsNullOrWhiteSpace(ClientName)
            ? ClientName
            : ClientId;
        InvoiceDateText = invoiceDate.HasValue ? invoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
        DueDateText = dueDate.HasValue ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
        DaysPastDueText = daysPastDue.ToString("N0", CultureInfo.CurrentCulture);
        BalanceText = balance.ToString("C0", CultureInfo.CurrentCulture);
    }
}
