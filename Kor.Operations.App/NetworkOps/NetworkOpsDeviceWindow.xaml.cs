#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.App.NetworkOps;

public partial class NetworkOpsDeviceWindow : Window
{
    private readonly NetworkOpsDeviceViewModel _vm;
    // Closing the window cancels whatever it was waiting on, including a check in progress
    // (the check itself carries on in the service; only the waiting stops).
    private readonly CancellationTokenSource _cts = new();

    public NetworkOpsDeviceWindow(NetworkOpsDeviceViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await Run(_vm.LoadHistoryAsync).ConfigureAwait(true);

    /// <summary>The one way windows open each other (set by whoever opens this one); null in a test.</summary>
    public NetworkOpsNavigator? Navigator { get; init; }

    /// <summary>A tile: its finding if it has one; else, a part with a page of its own (a UniFi switch or access point: the
    /// Network window) goes there; else (or clicked again) "About this part".</summary>
    private void ComponentTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ComponentTile tile) return;
        if (tile.Part.RuleKeys.Count == 0 && Navigator?.Follow(tile.Part.Opens) == true) return;
        _vm.ClickTile(tile);
    }

    private void ClosePart_Click(object sender, RoutedEventArgs e) => _vm.SelectedPart = null;

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await Run(_vm.CheckNowAsync).ConfigureAwait(true);

    private async void Acknowledge_Click(object sender, RoutedEventArgs e) => await Run(_vm.AcknowledgeAsync).ConfigureAwait(true);

    /// <summary>Choose a fix for the selected finding, run it, follow it to the re-check. A restart on a PC someone is
    /// actively using is refused by the service until it is confirmed here.</summary>
    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFinding is not { } row) return;
        await FixAsync(row, this).ConfigureAwait(true);
    }

    /// <summary>The one fix flow, from this window's Fix button or the KOR Remote viewer's toolbar (<paramref name="from"/>
    /// owns the dialogs): pick the finding, choose a fix, run it through the service, confirm a restart on a PC in use.</summary>
    internal Task FixAsync(FindingRow row, Window from)
    {
        _vm.SelectedFinding = row;
        return Run(async ct =>
        {
            var fixes = await _vm.FixesForSelectedAsync(ct).ConfigureAwait(true);
            var dlg = new NetworkOpsFixWindow(row.Title, _vm.DeviceName, _vm.Device.Presence ?? "", _vm.SomeoneActive, fixes) { Owner = from };
            if (dlg.ShowDialog() != true || dlg.Chosen is not { } fix) return;
            var (refused, needsConfirmation) = await _vm.RunFixAsync(fix, dlg.Param, confirmed: false, ct).ConfigureAwait(true);
            if (needsConfirmation && MessageBox.Show(from, $"{refused}\n\nRestart it anyway? They get a 5-minute warning on screen.", "Someone is using this PC",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                await _vm.RunFixAsync(fix, dlg.Param, confirmed: true, ct).ConfigureAwait(true);
        });
    }

    internal NetworkOpsDeviceViewModel ViewModel => _vm;

    /// <summary>Opens this machine in the app's KOR Remote viewer (one per machine: a second Connect brings it forward).</summary>
    private void Connect_Click(object sender, RoutedEventArgs e) => KorRemoteViewerWindow.Open(this);

    /// <summary>The fallback: MeshCentral's full page in an Edge app window, for whatever the viewer's toolbar does not do.</summary>
    internal void OpenInBrowser(Window from)
    {
        if (_vm.ConnectUrl is not { } url) return;
        try { KorRemoteWindow.Open(url); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show(from, $"Could not open the browser: {ex.Message}\n\n{url}", "Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The Prompt Library, on the selected finding (or this device): the prompt is written when it is opened there.</summary>
    private void SolveWithClaude_Click(object sender, RoutedEventArgs e)
        => new PromptLibraryWindow(_vm.Client, _vm.ClaudeRequest) { Owner = this }.Show();

    /// <summary>The rarer actions, behind one button so the header shows only Connect and Check.</summary>
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreBtn.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MoreBtn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = _vm;
        menu.IsOpen = true;
    }

    private async void Wake_Click(object sender, RoutedEventArgs e) => await Run(_vm.WakeAsync).ConfigureAwait(true);

    /// <summary>Web-RDP (a full-size session for a PC with no monitor, which shows 1024x768 otherwise) in the app's own clean
    /// viewer -- the same window as Connect, never the browser.</summary>
    private void WebRdp_Click(object sender, RoutedEventArgs e) => KorRemoteViewerWindow.OpenRdp(this);

    private async void InstallRemote_Click(object sender, RoutedEventArgs e) => await Run(_vm.InstallRemoteAsync).ConfigureAwait(true);

    private async void InstallAgent_Click(object sender, RoutedEventArgs e) => await Run(ct => _vm.ChangeAgentAsync("install", ct)).ConfigureAwait(true);

    private async void RemoveAgent_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Remove the NetworkOps agent from {_vm.DeviceName}? Checks and fixes go back to reaching it over the network.",
                "Remove agent", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Run(ct => _vm.ChangeAgentAsync("remove", ct)).ConfigureAwait(true);
    }

    private async void SnoozeDay_Click(object sender, RoutedEventArgs e) => await Run(ct => _vm.SnoozeAsync(TimeSpan.FromDays(1), ct)).ConfigureAwait(true);

    private async void SnoozeWeek_Click(object sender, RoutedEventArgs e) => await Run(ct => _vm.SnoozeAsync(TimeSpan.FromDays(7), ct)).ConfigureAwait(true);

    private async void Reopen_Click(object sender, RoutedEventArgs e) => await Run(_vm.ReopenAsync).ConfigureAwait(true);

    private async void AddNote_Click(object sender, RoutedEventArgs e) => await Run(_vm.AddNoteAsync).ConfigureAwait(true);

    // Every handler is async void, so nothing may escape it: the view model reports its own failures
    // in the status line, and a cancel only ever means the window closed.
    private async Task Run(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Cancelled, not disposed: a handler still unwinding may read the token after close.
        _cts.Cancel();
        base.OnClosed(e);
    }
}
