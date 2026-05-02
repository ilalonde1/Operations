#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncActivityWindow : Window
{
    private readonly FileSyncActivityViewModel _vm;
    private readonly FileSyncControlPlaneReader _reader;
    private CancellationTokenSource? _cts;

    public FileSyncActivityWindow(FileSyncControlPlaneReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        _reader = reader;
        _vm = new FileSyncActivityViewModel(reader);
        InitializeComponent();
        DataContext = _vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private Task RefreshAsync()
    {
        var token = ResetToken();
        return _vm.RefreshAsync(token);
    }

    private CancellationToken ResetToken()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private void RunsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.SelectedRun is null) return;
        OpenLogsForRun(_vm.SelectedRun);
    }

    private void ViewLogsForRun_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRun is null) return;
        OpenLogsForRun(_vm.SelectedRun);
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRun is null) return;
        TryCopy(_vm.SelectedRun.Summary ?? string.Empty);
    }

    private void CopyErrorStack_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRun is null) return;
        TryCopy(_vm.SelectedRun.ErrorStack ?? _vm.SelectedRun.ErrorMessage ?? string.Empty);
    }

    private void OpenLogsForRun(JobRunRow run)
    {
        // Window: 1 min before StartedAt -> 1 min after CompletedAt (or now if
        // still Running). Generous on both sides so the trigger and the
        // post-run heartbeat update are visible alongside the dispatch lines.
        var pad = TimeSpan.FromMinutes(1);
        DateTimeOffset from = run.StartedAt - pad;
        DateTimeOffset to = (run.CompletedAt ?? DateTimeOffset.Now) + pad;

        var w = new FileSyncLogViewerWindow(
            _reader,
            initialHost: run.HostName,
            initialJobFilter: run.JobName,
            initialFrom: from,
            initialTo: to)
        {
            Owner = this,
        };
        w.Show();
    }

    private void TryCopy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Copy failed: {ex.Message}", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
