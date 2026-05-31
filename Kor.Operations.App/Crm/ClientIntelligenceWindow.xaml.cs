#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kor.Operations.Services;

namespace Kor.Operations.App.Crm;

public partial class ClientIntelligenceWindow : Window
{
    private readonly ClientIntelligenceViewModel _vm;
    private readonly IDeltekLookupService _lookup;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _searchCts;
    private DateTime _lastTypedAtUtc = DateTime.MinValue;

    public ClientIntelligenceWindow(ClientIntelligenceViewModel vm, IDeltekLookupService lookup)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        InitializeComponent();
        DataContext = _vm;

        AppServices.Get<AppAiContextBuilder>().Register(_vm);
    }

    private async void ClientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = ClientSearchBox?.Text?.Trim() ?? "";
        if (query.Length < 2)
        {
            ClientSearchPopup.IsOpen = false;
            return;
        }

        // Debounce: only fire if 200ms elapses with no further typing.
        var typedAt = DateTime.UtcNow;
        _lastTypedAtUtc = typedAt;
        await Task.Delay(200).ConfigureAwait(true);
        if (_lastTypedAtUtc != typedAt) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        try
        {
            var results = await _lookup.SearchClientsAsync(query, 20, _searchCts.Token).ConfigureAwait(true);
            ClientSearchResults.ItemsSource = results;
            ClientSearchPopup.IsOpen = results.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch { /* lookup failures are silent — just don't show suggestions */ }
    }

    private void ClientSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && ClientSearchPopup.IsOpen)
        {
            ClientSearchResults.Focus();
            if (ClientSearchResults.Items.Count > 0 && ClientSearchResults.SelectedIndex < 0)
                ClientSearchResults.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClientSearchPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // If popup is open and there's a top match, load it; otherwise interpret as raw ClientID.
            if (ClientSearchPopup.IsOpen && ClientSearchResults.Items.Count > 0)
            {
                if (ClientSearchResults.SelectedIndex < 0) ClientSearchResults.SelectedIndex = 0;
                LoadSelectedSearchResult();
            }
            else
            {
                LoadByRawText();
            }
            e.Handled = true;
        }
    }

    private void ClientSearchResults_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => LoadSelectedSearchResult();

    private void ClientSearchResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoadSelectedSearchResult();
            e.Handled = true;
        }
    }

    // Fire-and-forget from the search-popup UI events (MouseLeftButtonUp /
    // Enter key). LoadClientAsync swallows OCE and the VM surfaces other
    // failures via status text.
    // async-void OK: see above
    private async void LoadSelectedSearchResult()
    {
        if (ClientSearchResults.SelectedItem is not DeltekClientCandidate c) return;
        ClientSearchPopup.IsOpen = false;
        ClientSearchBox.Text = c.Name;
        await LoadClientAsync(c.ClientId);
    }

    // async-void OK: fire-and-forget from the search-box Enter handler.
    // Same justification as LoadSelectedSearchResult above.
    private async void LoadByRawText()
    {
        var id = ClientSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(id)) return;
        await LoadClientAsync(id);
    }

    private async Task LoadClientAsync(string clientId)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try { await _vm.LoadAsync(clientId, _cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Caller sets the client id before Show()/ShowDialog(). Loads on
    /// Window_Loaded so the spinner is visible, not before the window paints.
    /// </summary>
    public string? PendingClientId { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PendingClientId)) return;
        _cts = new CancellationTokenSource();
        try
        {
            await _vm.LoadAsync(PendingClientId.Trim(), _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
        AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
    }
    private void NewsMentionHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch
        {
            // User can copy the URL manually.
        }
    }
}
