#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Kor.Operations.App.NetworkOps;

public partial class NetworkOpsCommandCenterWindow : Window, INetworkTabHost
{
    // Same cadence as the FileSync Command Center: fresh enough to watch a check land, light on the service.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly NetworkOpsCommandCenterViewModel _vm;
    private readonly NetworkOpsNavigator _nav;   // every window this one opens, and they open each other, through this
    private readonly DispatcherTimer _autoRefreshTimer;
    // A manual refresh and the timer each have their own token, so a tick never cancels a click.
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _autoRefreshCts;

    public NetworkOpsCommandCenterWindow(NetworkOpsCommandCenterViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
        _nav = new NetworkOpsNavigator(_vm.Client, () => _vm.Snapshot, () => _vm.RackSnapshot, this);

        _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += async (_, _) => await AutoTickAsync().ConfigureAwait(true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Land on the "To clear" worklist, not the read-only Fleet: it is the consolidated, actionable view (every open
        // issue grouped, with fix-on-all), so the fix level is in front of you on open. Set here, not in XAML, because the
        // tab's Checked handler creates the view into ToClearHost, which does not exist until the window's tree is built.
        ToClearTab.IsChecked = true;
        await RefreshAsync(ResetToken()).ConfigureAwait(true);
        _autoRefreshTimer.Start();
    }

    /// <summary>The Fleet headline's "N issues to clear →" nudge: switch to the worklist tab.</summary>
    private void GoToClear_Click(object sender, RoutedEventArgs e) => ToClearTab.IsChecked = true;

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshAsync(ResetToken()).ConfigureAwait(true);

    // Re-check the WHOLE fleet now (every PC runs its health probe) instead of waiting for the scheduled sweep, and
    // refresh the view as each PC answers so new findings appear while it runs.
    private async void RecheckAll_Click(object sender, RoutedEventArgs e)
    {
        RecheckBtn.IsEnabled = false;
        var label = RecheckBtn.Content;
        try
        {
            var ct = ResetToken();
            var trigger = await _vm.Client.QueueFleetSweepAsync(ct).ConfigureAwait(true);
            RecheckBtn.Content = trigger is null ? "Already running…" : "Re-checking…";
            for (var i = 0; i < 180; i++)   // every PC is probed; cap ~15 min
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(true);
                await RefreshAsync(ct).ConfigureAwait(true);
                if (trigger is null) break;   // one was already running; a single refresh is enough
                if (await _vm.Client.GetTriggerAsync(trigger.Value, ct).ConfigureAwait(true) is { CompletedUtc: not null }) break;
            }
        }
        catch (OperationCanceledException) { /* superseded, or the window is closing */ }
        catch (Exception ex) { MessageBox.Show(this, $"Could not start a fleet re-check: {ex.Message}", "Re-check all", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { RecheckBtn.Content = label; RecheckBtn.IsEnabled = true; }
    }

    private async Task AutoTickAsync()
    {
        if (!_vm.AutoRefresh || WindowState == WindowState.Minimized) return;
        _autoRefreshCts?.Cancel();
        _autoRefreshCts = new CancellationTokenSource();
        await RefreshAsync(_autoRefreshCts.Token).ConfigureAwait(true);
    }

    // The machine chosen in whichever view is showing (cards or table).
    private Selector PcList => _vm.CardView ? FleetCards : FleetGrid;
    private Selector RackList => _vm.CardView ? RackCards : RackGrid;

    private async Task RefreshAsync(CancellationToken ct)
    {
        // The lists are rebuilt on every read; keep what Ian had selected selected.
        var selected = (PcList.SelectedItem as FleetRow)?.Name;
        var selectedRack = (RackList.SelectedItem as FleetRow)?.Name;
        try
        {
            await _vm.RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* superseded */ }
        if (selectedRack is not null)
            foreach (var row in _vm.Rack)
                if (row.Name == selectedRack) { RackList.SelectedItem = row; break; }
        if (selected is null) return;
        foreach (var row in _vm.Fleet)
            if (row.Name == selected) { PcList.SelectedItem = row; break; }
    }

    // ---- cards: double-click or Enter opens the machine; choosing one in a section clears the other section's choice,
    //      so Ask Claude and Open PC always mean the one card that is outlined ----
    private void FleetCards_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FleetCards, d) is not ListBoxItem) return;
        OpenSelected();
    }

    private void FleetCards_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenSelected();
    }

    private void RackCards_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(RackCards, d) is not ListBoxItem) return;
        OpenRackSelected();
    }

    private void RackCards_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenRackSelected();
    }

    private void FleetCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FleetCards.SelectedItem is not null) RackCards.SelectedItem = null;
    }

    private void RackCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RackCards.SelectedItem is not null) FleetCards.SelectedItem = null;
    }

    private void FleetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Headers and scrollbars raise this too; only act on a real row.
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FleetGrid, d) is not DataGridRow) return;
        OpenSelected();
    }

    private void FleetGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenSelected();
    }

    private void OpenPc_Click(object sender, RoutedEventArgs e) => OpenSelected();

    // The Updates view is a console tab now (folded from a standalone window, 2026-10-07), created the first time it is
    // shown; it reads on its own Loaded, like the Network tab.
    private NetworkOpsUpdatesView EnsureUpdates()
        => (NetworkOpsUpdatesView)(UpdatesHost.Content ??= new NetworkOpsUpdatesView(_vm.Client));

    private void UpdatesTab_Checked(object sender, RoutedEventArgs e) => EnsureUpdates();

    private void Deploy_Click(object sender, RoutedEventArgs e) => new NetworkOpsDeployWindow(_vm.Client) { Owner = this }.Show();

    // The To-clear worklist is a tab, created the first time it is opened (it reads the fleet on its own Loaded).
    private void ToClearTab_Checked(object sender, RoutedEventArgs e) => ToClearHost.Content ??= new NetworkOpsToClearView(_vm.Client);

    // The Network port map is a tab too (folded from a standalone window 2026-10-04). It is created the first time it is
    // shown; it reads the map on its own Loaded. Created with the navigator so a port tile still opens the one device page.
    private NetworkOpsNetworkView EnsureNetwork()
        => (NetworkOpsNetworkView)(NetworkHost.Content ??= new NetworkOpsNetworkView(_vm.Client, _nav));

    private void NetworkTab_Checked(object sender, RoutedEventArgs e) => EnsureNetwork();

    /// <summary>INetworkTabHost: the navigator drives the console here -- a device page's "Open full view", a part's
    /// "network:..." link. Bring the console forward, switch to the Network tab, and focus the switch/port when given.</summary>
    public void ShowNetwork(string? focusMac, int? port)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var view = EnsureNetwork();
        NetworkTab.IsChecked = true;
        if (focusMac is { Length: > 0 }) view.FocusOn(focusMac, port);   // before the map loads this is kept and applied on load
    }

    /// <summary>A tile filters both lists to its rows; the same tile again shows everything.</summary>
    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        var tile = ((FrameworkElement)sender).Name switch
        {
            nameof(CriticalTile) => NetworkOpsCommandCenterViewModel.TileCritical,
            nameof(AttentionTile) => NetworkOpsCommandCenterViewModel.TileAttention,
            nameof(HealthyTile) => NetworkOpsCommandCenterViewModel.TileHealthy,
            _ => NetworkOpsCommandCenterViewModel.TileStale,
        };
        _vm.ToggleTile(tile);
    }

    private void RackGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(RackGrid, d) is not DataGridRow) return;
        OpenRackSelected();
    }

    private void RackGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenRackSelected();
    }

    /// <summary>A rack device opens in the same window as a PC: its findings, what they mean, history, notes.</summary>
    private void OpenRackSelected()
    {
        if (RackList.SelectedItem is not FleetRow row || _vm.RackSnapshot is not { } rack) return;
        _nav.Open(rack, row.Device);
    }

    private async void RehearseBtn_Click(object sender, RoutedEventArgs e)
    {
        RehearseBtn.IsEnabled = false;
        try { await _vm.RehearseAsync(ResetToken()).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* superseded */ }
        finally { RehearseBtn.IsEnabled = true; }
    }

    private void OpenSelected()
    {
        if (PcList.SelectedItem is not FleetRow row || _vm.Snapshot is not { } snapshot) return;
        _nav.Open(snapshot, row.Device);
    }

    private CancellationToken ResetToken()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoRefreshTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        base.OnClosed(e);
    }
}
