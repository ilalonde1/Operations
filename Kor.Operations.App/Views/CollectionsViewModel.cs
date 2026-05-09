#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.App;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.Views;

internal sealed class CollectionsViewModel : ObservableObject
{
    private readonly CollectionsClient _client;
    private string _header = "Collections  All Cases";
    private string _lastUpdated = "";
    private bool _isBusy;
    private bool _activeOnly;
    private string? _errorMessage;

    public CollectionsViewModel(CollectionsClient client)
    {
        _client = client;
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
