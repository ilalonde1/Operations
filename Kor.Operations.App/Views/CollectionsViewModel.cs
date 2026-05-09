#nullable enable
using System;
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

internal sealed class CollectionsViewModel : ObservableObject
{
    private readonly CollectionsClient _client;
    private readonly IDeltekLookupService _lookup;
    private CancellationTokenSource? _searchCts;
    private string _header = "Collections — All Cases";
    private string _lastUpdated = "";
    private bool _isBusy;
    private bool _activeOnly;
    private string? _errorMessage;
    private string _clientSearchText = "";
    private DeltekClientCandidate? _selectedClientCandidate;
    private bool _isClientSearchPopupOpen;

    public CollectionsViewModel(CollectionsClient client, IDeltekLookupService lookup)
    {
        _client = client;
        _lookup = lookup;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<CollectionsCaseDto> Cases { get; } = new();

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetField(ref _activeOnly, value))
            {
                _ = RefreshAsync();
            }
        }
    }

    public string Header
    {
        get => _header;
        private set => SetField(ref _header, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(BusyVisibility));
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
            }
        }
    }

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public ICommand RefreshCommand { get; }

    public ObservableCollection<DeltekClientCandidate> ClientSearchResults { get; } = new();

    public string ClientSearchText
    {
        get => _clientSearchText;
        set
        {
            if (SetField(ref _clientSearchText, value))
            {
                _ = SearchClientsAsync(value);
            }
        }
    }

    public DeltekClientCandidate? SelectedClientCandidate
    {
        get => _selectedClientCandidate;
        set
        {
            if (SetField(ref _selectedClientCandidate, value))
            {
                OnPropertyChanged(nameof(CanCreateCase));
                if (value is not null)
                {
                    // Show the picked name in the textbox without re-triggering a search
                    // (silent backing-field assign + manual notify).
                    _searchCts?.Cancel();
                    _clientSearchText = value.Name;
                    OnPropertyChanged(nameof(ClientSearchText));
                    IsClientSearchPopupOpen = false;
                }
            }
        }
    }

    public bool CanCreateCase => _selectedClientCandidate is not null;

    public bool IsClientSearchPopupOpen
    {
        get => _isClientSearchPopupOpen;
        set => SetField(ref _isClientSearchPopupOpen, value);
    }

    internal void ClearClientSearch()
    {
        _searchCts?.Cancel();
        ClientSearchResults.Clear();
        _selectedClientCandidate = null;
        OnPropertyChanged(nameof(SelectedClientCandidate));
        OnPropertyChanged(nameof(CanCreateCase));
        _clientSearchText = "";
        OnPropertyChanged(nameof(ClientSearchText));
        IsClientSearchPopupOpen = false;
    }

    private async Task SearchClientsAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            ClientSearchResults.Clear();
            IsClientSearchPopupOpen = false;
            return;
        }

        try
        {
            // Light debounce so we don't query Deltek on every keystroke.
            await Task.Delay(180, ct).ConfigureAwait(true);
            // Substring search filtered to clients with actual open AR — no point
            // listing clients who owe nothing.
            var results = await _lookup.SearchClientsAsync(query, 20, ct, requireOpenAr: true).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            ClientSearchResults.Clear();
            foreach (var r in results)
            {
                ClientSearchResults.Add(r);
            }
            IsClientSearchPopupOpen = ClientSearchResults.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — ignore.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Client search failed: {ex.Message}";
        }
    }

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!_client.IsConfigured)
            {
                ErrorMessage = "MCP server is not configured. Set McpServer.ServiceUrl/Username/Password in App.config.";
                return;
            }

            var rows = ActiveOnly
                ? await _client.GetActiveAsync(ct).ConfigureAwait(true)
                : await _client.GetAllAsync(ct).ConfigureAwait(true);

            Cases.Clear();
            foreach (var row in rows.OrderByDescending(r => r.openedAt))
            {
                Cases.Add(row);
            }

            Header = ActiveOnly ? $"Collections  Active ({Cases.Count})" : $"Collections  All ({Cases.Count})";
            LastUpdated = $"Loaded {DateTime.Now:yyyy-MM-dd HH:mm}";
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
}
