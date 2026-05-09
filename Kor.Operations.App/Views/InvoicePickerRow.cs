#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.Views;

internal sealed class InvoicePickerRow : ObservableObject
{
    private bool _isChecked;
    private bool _isEnabled = true;

    public InvoicePickerRow(ClientArInvoiceDto src)
    {
        Source = src;
    }

    public ClientArInvoiceDto Source { get; }

    public string WBS1 => Source.wbS1;

    public string InvoiceNumber => Source.invoiceNumber;

    public string? ProjectName => Source.projectName;

    public string Currency => Source.currency;

    public decimal OutstandingBalance => Source.outstandingBalance;

    public int DaysOutstanding => Source.daysOutstanding;

    public string? OtherCaseNote { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }
}
