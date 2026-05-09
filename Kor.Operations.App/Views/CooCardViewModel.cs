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

internal sealed class CooCardViewModel : ObservableObject
{
    private readonly CooCardClient _client;
    private string _header = "";
    private string _lastUpdated = "";
    private bool _isBusy;
    private string? _errorMessage;

    public CooCardViewModel(CooCardClient client)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        RegenerateCommand = new AsyncRelayCommand(_ => RegenerateAsync(), _ => !IsBusy);
        AcknowledgeCommand = new AsyncRelayCommand(p => AcknowledgeAsync((CooCardItemDto)p!), p => p is CooCardItemDto && !IsBusy);
    }

    public ObservableCollection<CooCardItemDto> Items { get; } = new();

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

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public ICommand RefreshCommand { get; }

    public ICommand RegenerateCommand { get; }

    public ICommand AcknowledgeCommand { get; }

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!_client.IsConfigured)
            {
                ErrorMessage = "MCP server is not configured. Set McpServer.ServiceUrl/Username/Password in App.config.";
                Header = "COO Card — Not configured";
                return;
            }

            var rows = await _client.GetLatestAsync(ct).ConfigureAwait(true);
            Replace(Items, rows.OrderBy(r => r.rank));

            var weekOf = Items.FirstOrDefault()?.weekOf;
            Header = weekOf is null
                ? "COO Card — No card yet (run regenerate to build one)"
                : $"COO Card — Week of {weekOf:yyyy-MM-dd}";
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

    private async Task RegenerateAsync()
    {
        if (IsBusy) return;

        var confirm = MessageBox.Show(
            "Regenerate the COO Card now? This calls the AI synthesis pipeline server-side and replaces the current week's card. Takes ~30-60 seconds.",
            "COO Card — Regenerate",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.OK);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RunNowAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show($"Regenerate failed:\n{ex.Message}", "COO Card — Regenerate Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            IsBusy = false;
            return;
        }

        IsBusy = false;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task AcknowledgeAsync(CooCardItemDto item)
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.AcknowledgeAsync(item.id, CancellationToken.None).ConfigureAwait(true);
            Items.Remove(item);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show($"Acknowledge failed:\n{ex.Message}", "COO Card — Acknowledge Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }
}
