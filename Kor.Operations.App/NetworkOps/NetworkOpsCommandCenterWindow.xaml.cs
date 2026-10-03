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

public partial class NetworkOpsCommandCenterWindow : Window
{
    // Same cadence as the FileSync Command Center: fresh enough to watch a check land, light on the service.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly NetworkOpsCommandCenterViewModel _vm;
    private readonly DispatcherTimer _autoRefreshTimer;
    // A manual refresh and the timer each have their own token, so a tick never cancels a click.
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _autoRefreshCts;

    public NetworkOpsCommandCenterWindow(NetworkOpsCommandCenterViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;

        _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += async (_, _) => await AutoTickAsync().ConfigureAwait(true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(ResetToken()).ConfigureAwait(true);
        _autoRefreshTimer.Start();
        FilterBox.Focus();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshAsync(ResetToken()).ConfigureAwait(true);

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

    private void Updates_Click(object sender, RoutedEventArgs e) => new NetworkOpsUpdatesWindow(_vm.Client) { Owner = this }.Show();

    private void Network_Click(object sender, RoutedEventArgs e) => new NetworkOpsNetworkWindow(_vm.Client) { Owner = this }.Show();

    /// <summary>Ask Claude, on the selected PC or rack device when there is one (the ask box then says it is about that machine).</summary>
    private void AskClaude_Click(object sender, RoutedEventArgs e)
    {
        var selected = (PcList.SelectedItem ?? RackList.SelectedItem) as FleetRow;
        var request = selected is null ? null : new Kor.Operations.NetworkOps.Core.Learning.PromptRequest("device", null, selected.DeviceId, null);
        new PromptLibraryWindow(_vm.Client, request) { Owner = this }.Show();
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
        var vm = new NetworkOpsDeviceViewModel(_vm.Client, rack, row.Device);
        new NetworkOpsDeviceWindow(vm) { Owner = this }.Show();
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
        var vm = new NetworkOpsDeviceViewModel(_vm.Client, snapshot, row.Device);
        new NetworkOpsDeviceWindow(vm) { Owner = this }.Show();
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
