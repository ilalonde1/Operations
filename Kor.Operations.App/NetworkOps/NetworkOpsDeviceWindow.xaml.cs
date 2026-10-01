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

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await Run(_vm.CheckNowAsync).ConfigureAwait(true);

    private async void Acknowledge_Click(object sender, RoutedEventArgs e) => await Run(_vm.AcknowledgeAsync).ConfigureAwait(true);

    /// <summary>Choose a fix for the selected finding, run it, follow it to the re-check. A restart on a PC someone is
    /// actively using is refused by the service until it is confirmed here.</summary>
    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFinding is not { } row) return;
        await Run(async ct =>
        {
            var fixes = await _vm.FixesForSelectedAsync(ct).ConfigureAwait(true);
            var dlg = new NetworkOpsFixWindow(row.Title, _vm.DeviceName, _vm.Device.Presence ?? "", _vm.SomeoneActive, fixes) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Chosen is not { } fix) return;
            var (refused, needsConfirmation) = await _vm.RunFixAsync(fix, dlg.Param, confirmed: false, ct).ConfigureAwait(true);
            if (needsConfirmation && MessageBox.Show(this, $"{refused}\n\nRestart it anyway? They get a 5-minute warning on screen.", "Someone is using this PC",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                await _vm.RunFixAsync(fix, dlg.Param, confirmed: true, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

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
