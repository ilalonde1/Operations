#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    private async Task RefreshAsync(CancellationToken ct)
    {
        // The grid is rebuilt on every read; keep the PC Ian had selected selected.
        var selected = (FleetGrid.SelectedItem as FleetRow)?.Name;
        try
        {
            await _vm.RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* superseded */ }
        if (selected is null) return;
        foreach (var row in _vm.Fleet)
            if (row.Name == selected) { FleetGrid.SelectedItem = row; break; }
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

    private async void RehearseBtn_Click(object sender, RoutedEventArgs e)
    {
        RehearseBtn.IsEnabled = false;
        try { await _vm.RehearseAsync(ResetToken()).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* superseded */ }
        finally { RehearseBtn.IsEnabled = true; }
    }

    private void OpenSelected()
    {
        if (FleetGrid.SelectedItem is not FleetRow row || _vm.Snapshot is not { } snapshot) return;
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
