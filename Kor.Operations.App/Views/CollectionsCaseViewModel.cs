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
    private readonly CollectionsClient _client;
    private readonly IDeltekLookupService _lookup;
    private string _clientId = "";
    private string _clientName = "";
    private long? _caseId;
    private string _windowTitle = "Collections Case";
    private string _status = "Open";
    private decimal? _legalAmount;
    private string? _notes;
    private DateTime? _lienExpiryDate;
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
        // SaveCommand takes an optional target-status string as CommandParameter.
        // null  = save with current status (e.g. the plain "Save" button keeps Open on a new case).
        // "Lien" / "Foreclosure" / "WriteOff" / "Resolved" = set status to that value, then save.
        SaveCommand = new AsyncRelayCommand(p => SaveAsync(p as string), _ => !IsBusy && HasSelection);
        ResolveCommand = new AsyncRelayCommand(_ => SaveAsync("Resolved"), _ => !IsBusy && HasSelection && IsEditMode && !string.Equals(_status, "Resolved", StringComparison.OrdinalIgnoreCase));
        Invoices.CollectionChanged += OnInvoicesCollectionChanged;
        CancelCommand = new AsyncRelayCommand(_ =>
        {
            RaiseClose(false);
            return Task.CompletedTask;
        });
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<InvoicePickerRow> Invoices { get; } = new();


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
                OnPropertyChanged(nameof(LienExpiryVisibility));
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

    public DateTime? LienExpiryDate
    {
        get => _lienExpiryDate;
        set => SetField(ref _lienExpiryDate, value);
    }

    /// <summary>Lien expiry date is only relevant when status is Lien — UI shows the date picker conditionally.</summary>
    public Visibility LienExpiryVisibility =>
        string.Equals(_status, "Lien", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

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

    public int SelectedCount => Invoices.Count(r => r.IsChecked);
    public decimal SelectedTotal => Invoices.Where(r => r.IsChecked).Sum(r => r.OutstandingBalance);
    public bool HasSelection => SelectedCount > 0;
    public string SelectionSummary
    {
        get
        {
            if (Invoices.Count == 0) return "";
            var enabled = Invoices.Count(r => r.IsEnabled);
            return $"{SelectedCount} of {enabled} invoices selected — {SelectedTotal:C0} in collections";
        }
    }

    private void OnInvoicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (InvoicePickerRow r in e.OldItems) r.PropertyChanged -= OnRowChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (InvoicePickerRow r in e.NewItems) r.PropertyChanged += OnRowChanged;
        }
        NotifySelectionChanged();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InvoicePickerRow.IsChecked))
        {
            NotifySelectionChanged();
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

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
                OnPropertyChanged(nameof(LienExpiryVisibility));
                LegalAmount = detail.header.legalAmount;
                Notes = detail.header.notes;
                LienExpiryDate = detail.header.lienExpiryDate;
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

    private async Task SaveAsync(string? targetStatus = null)
    {
        if (IsBusy)
        {
            return;
        }

        // Confirm destructive / operationally-significant transitions before
        // persisting. Plain "Save" with no targetStatus skips this guard
        // because it doesn't change status — only the targeted-status buttons
        // (Mark Lien / Mark Foreclosure / Write Off / Resolve) trigger it.
        if (!string.IsNullOrWhiteSpace(targetStatus) &&
            !ConfirmStatusTransition(targetStatus))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetStatus))
        {
            Status = targetStatus;
        }

        // Validation: when persisting under Status=Lien, an expiry date is
        // required. BC builders' liens lapse one year after filing unless
        // extended; saving a Lien with no expiry date defeats the
        // LienExpiryRule alert that's supposed to flag the deadline.
        if (string.Equals(_status, "Lien", StringComparison.OrdinalIgnoreCase) &&
            !_lienExpiryDate.HasValue)
        {
            MessageBox.Show(
                "Set the lien expiry date before saving — it's how the alert system warns you before the lien lapses.",
                "Collections — Lien expiry date required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var picked = Invoices.Where(r => r.IsChecked)
                .Select(r => new InvoiceRefDto(r.WBS1, r.InvoiceNumber))
                .ToList();

            // Only persist LienExpiryDate when status is actually Lien — clears stale dates if you escalate to Foreclosure or back.
            var lienExpiry = string.Equals(_status, "Lien", StringComparison.OrdinalIgnoreCase) ? _lienExpiryDate : null;

            if (_caseId.HasValue)
            {
                var req = new UpdateCollectionsCaseRequestDto(_status, _legalAmount, _notes, picked, lienExpiry);
                await _client.UpdateCaseAsync(_caseId.Value, req, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                var req = new OpenCollectionsCaseRequestDto(_clientId, _status, _legalAmount, _notes, picked, lienExpiry);
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

    private void RaiseClose(bool success) => RequestClose?.Invoke(this, success);

    /// <summary>
    /// Operationally-significant status transitions get a confirmation prompt.
    /// Plain Save (no target status) is unguarded — it doesn't change state.
    /// Returns true to proceed, false to cancel the save.
    /// </summary>
    private bool ConfirmStatusTransition(string targetStatus)
    {
        var label = string.IsNullOrWhiteSpace(_clientName) ? _clientId : _clientName;
        string message;
        MessageBoxImage icon;
        switch (targetStatus.ToUpperInvariant())
        {
            case "LIEN":
                message =
                    $"Mark this case as Lien for {label}?\n\n" +
                    "Status will move to Lien and the case will be excluded from regular AR aging. " +
                    "Make sure the lien has actually been filed before confirming.";
                icon = MessageBoxImage.Warning;
                break;

            case "FORECLOSURE":
                message =
                    $"Mark this case as Foreclosure for {label}?\n\n" +
                    "Foreclosure proceedings should be underway with legal counsel. " +
                    "The case will stay excluded from regular AR aging until Resolved.";
                icon = MessageBoxImage.Warning;
                break;

            case "WRITEOFF":
                message =
                    $"Write off this case for {label}?\n\n" +
                    "WriteOff marks the AR as uncollectable and locks the invoices to this case so they don't reappear in AR aging. " +
                    "This is hard to undo cleanly — only confirm if accounting has signed off on writing the balance off.";
                icon = MessageBoxImage.Stop;
                break;

            case "RESOLVED":
                message =
                    $"Mark this case as Resolved for {label}?\n\n" +
                    "Use this when the case is paid, settled, or otherwise closed. " +
                    "Any remaining unpaid invoices return to the regular AR aging stream.";
                icon = MessageBoxImage.Question;
                break;

            default:
                return true;
        }

        var result = MessageBox.Show(
            message,
            $"Collections — Confirm {targetStatus}",
            MessageBoxButton.OKCancel,
            icon,
            MessageBoxResult.Cancel);
        return result == MessageBoxResult.OK;
    }
}
