#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.App;
using Kor.Operations.App.Crm;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.Views;

internal sealed class CollectionsCaseViewModel : ObservableObject
{
    private static readonly string[] StatusValues = { "Open", "Lien", "Foreclosure", "WriteOff", "Resolved" };

    private readonly CollectionsClient _client;
    private readonly IDeltekLookupService _lookup;
    private string _clientId = "";
    private string _clientName = "";
    private long? _caseId;
    private string _windowTitle = "Collections Case";
    private string _status = "Open";
    private decimal? _legalAmount;
    private string? _notes;
    private bool _isBusy;
    private string? _errorMessage;

    public CollectionsCaseViewModel(CollectionsClient client, IDeltekLookupService lookup)
    {
        _client = client;
        _lookup = lookup;
        SelectAllCommand = new AsyncRelayCommand(_ =>
        {
            foreach (var row in Invoices)
            {
                if (row.IsEnabled)
                {
                    row.IsChecked = true;
                }
            }

            return Task.CompletedTask;
        });
        SelectNoneCommand = new AsyncRelayCommand(_ =>
        {
            foreach (var row in Invoices)
            {
                row.IsChecked = false;
            }

            return Task.CompletedTask;
        });
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsBusy);
        ResolveCommand = new AsyncRelayCommand(_ => ResolveAsync(), _ => !IsBusy && IsEditMode && !string.Equals(_status, "Resolved", StringComparison.OrdinalIgnoreCase));
        CancelCommand = new AsyncRelayCommand(_ =>
        {
            RaiseClose(false);
            return Task.CompletedTask;
        });
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<InvoicePickerRow> Invoices { get; } = new();

    public IReadOnlyList<string> AvailableStatuses { get; } = StatusValues;

    public string ClientID
    {
        get => _clientId;
        private set => SetField(ref _clientId, value);
    }

    public string ClientName
    {
        get => _clientName;
        private set
        {
            if (SetField(ref _clientName, value))
            {
                OnPropertyChanged(nameof(ClientDisplay));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    /// <summary>Friendly display: client name. Falls back to "(loading…)" while the lookup is in flight.</summary>
    public string ClientDisplay => string.IsNullOrWhiteSpace(_clientName) ? "(loading…)" : _clientName;

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetField(ref _windowTitle, value);
    }

    public bool IsEditMode => _caseId.HasValue;

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(ResolveButtonVisibility));
            }
        }
    }

    public decimal? LegalAmount
    {
        get => _legalAmount;
        set => SetField(ref _legalAmount, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetField(ref _notes, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(BusyVisibility));
                OnPropertyChanged(nameof(EmptyPickerVisibility));
            }
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(ErrorVisibility));
                OnPropertyChanged(nameof(EmptyPickerVisibility));
            }
        }
    }

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EmptyPickerVisibility =>
        !IsBusy && Invoices.Count == 0 && string.IsNullOrWhiteSpace(ErrorMessage)
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResolveButtonVisibility =>
        IsEditMode && !string.Equals(_status, "Resolved", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ResolveCommand { get; }

    public ICommand CancelCommand { get; }

    internal void Initialize(string clientId, string? clientName, long? caseId)
    {
        _clientId = clientId;
        _caseId = caseId;
        ClientID = clientId;
        ClientName = clientName ?? "";
        UpdateWindowTitle();
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(ResolveButtonVisibility));
    }

    private void UpdateWindowTitle()
    {
        var who = string.IsNullOrWhiteSpace(_clientName) ? "(loading…)" : _clientName;
        WindowTitle = _caseId.HasValue
            ? $"Collections Case — {who} (#{_caseId})"
            : $"Collections Case — {who} (new)";
    }

    internal async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // If we don't already have a friendly name (e.g. the case window was opened
            // by double-clicking a row in the read-only list — which only carries ClientID),
            // resolve it via the Deltek client master so the title and field show the name.
            if (string.IsNullOrWhiteSpace(_clientName) && !string.IsNullOrWhiteSpace(_clientId))
            {
                try
                {
                    var resolved = await _lookup.FindClientByIdAsync(_clientId, ct).ConfigureAwait(true);
                    if (resolved is not null)
                    {
                        ClientName = resolved.Name;
                        UpdateWindowTitle();
                    }
                }
                catch
                {
                    // Name resolution is cosmetic — fall through to ID-only display.
                }
            }

            var alreadyOnThisCase = new HashSet<(string Wbs1, string Inv)>();
            if (_caseId.HasValue)
            {
                var detail = await _client.GetDetailAsync(_caseId.Value, ct).ConfigureAwait(true);
                if (detail is null)
                {
                    ErrorMessage = $"Case #{_caseId} not found.";
                    return;
                }

                _status = detail.header.status;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(ResolveButtonVisibility));
                LegalAmount = detail.header.legalAmount;
                Notes = detail.header.notes;
                foreach (var line in detail.invoices)
                {
                    alreadyOnThisCase.Add((line.wbS1, line.invoiceNumber));
                }
            }

            var ar = await _client.GetArByClientAsync(_clientId, ct).ConfigureAwait(true);

            Invoices.Clear();
            foreach (var inv in ar)
            {
                var key = (inv.wbS1, inv.invoiceNumber);
                var lockedToOther = inv.activeCaseId.HasValue && (!_caseId.HasValue || inv.activeCaseId.Value != _caseId.Value);
                var row = new InvoicePickerRow(inv)
                {
                    IsEnabled = !lockedToOther,
                    OtherCaseNote = lockedToOther ? $"on case #{inv.activeCaseId}" : null,
                    IsChecked = alreadyOnThisCase.Contains(key),
                };
                Invoices.Add(row);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var picked = Invoices.Where(r => r.IsChecked)
                .Select(r => new InvoiceRefDto(r.WBS1, r.InvoiceNumber))
                .ToList();

            if (_caseId.HasValue)
            {
                var req = new UpdateCollectionsCaseRequestDto(_status, _legalAmount, _notes, picked);
                await _client.UpdateCaseAsync(_caseId.Value, req, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                var req = new OpenCollectionsCaseRequestDto(_clientId, _status, _legalAmount, _notes, picked);
                _caseId = await _client.OpenCaseAsync(req, CancellationToken.None).ConfigureAwait(true);
            }

            RaiseClose(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResolveAsync()
    {
        Status = "Resolved";
        await SaveAsync().ConfigureAwait(true);
    }

    private void RaiseClose(bool success) => RequestClose?.Invoke(this, success);
}
