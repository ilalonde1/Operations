#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncCommandCenterWindow : Window
{
    // Refresh cadence chosen so the "imminent" 5-min window catches a fire
    // within ~15s of crossing the threshold without hammering SQL.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    private readonly FileSyncCommandCenterViewModel _vm;
    private readonly DispatcherTimer _autoRefreshTimer;
    // Last-seen run snapshot per job, used to detect Running -> terminal
    // transitions across auto-refresh ticks. Seeded on first refresh so we
    // don't fire toasts for runs that completed before the window opened.
    private readonly Dictionary<string, (long? RunId, string? Status)> _lastRunSnapshot = new(StringComparer.Ordinal);
    private bool _snapshotSeeded;
    private CancellationTokenSource? _cts;

    public FileSyncCommandCenterWindow(FileSyncCommandCenterViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
        HeartbeatsGrid.ItemsSource = _vm.Heartbeats;
        JobsGrid.ItemsSource = _vm.Jobs;

        _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += async (_, _) => await AutoTickAsync().ConfigureAwait(true);
    }

    private async Task AutoTickAsync()
    {
        if (!_vm.AutoRefresh) return;
        // Skip when minimized -- no point reloading a hidden grid.
        if (WindowState == WindowState.Minimized) return;
        await RefreshAsync().ConfigureAwait(false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
        _autoRefreshTimer.Start();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private void ActivityBtn_Click(object sender, RoutedEventArgs e)
    {
        var w = new FileSyncActivityWindow(_vm.Reader)
        {
            Owner = this,
        };
        w.Show();
    }

    private void LogsBtn_Click(object sender, RoutedEventArgs e)
    {
        // No initial host/job filter -- the viewer picks the first heartbeat
        // host on initialize and starts at "all jobs / INF and above".
        var w = new FileSyncLogViewerWindow(_vm.Reader)
        {
            Owner = this,
        };
        w.Show();
    }

    private async void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not JobRow row)
            return;

        // Confirm Shadow -> Live transitions; Live -> Shadow is always safe.
        if (row.Mode == "Shadow")
        {
            var ok = MessageBox.Show(
                this,
                $"Flip job '{row.JobName}' from Shadow to LIVE?\n\nIn Live mode this job will perform real Graph writes / file moves the next time it runs.",
                "Confirm: switch to Live",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK)
                return;
        }

        var token = ResetToken();
        await _vm.ToggleModeAsync(row, token).ConfigureAwait(true);
        await _vm.RefreshAsync(token).ConfigureAwait(false);
    }

    private async void CancelPending_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PendingTriggerRow row)
            return;

        var token = ResetToken();
        await _vm.CancelPendingTriggerAsync(row, token).ConfigureAwait(true);
        await _vm.RefreshAsync(token).ConfigureAwait(false);
    }

    private async void ManualFire_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not JobRow row)
            return;

        var token = ResetToken();
        var triggerId = await _vm.QueueManualFireAsync(row, token).ConfigureAwait(true);
        if (triggerId.HasValue)
        {
            await Task.Delay(TimeSpan.FromSeconds(6), token).ConfigureAwait(true);
            await _vm.RefreshAsync(token).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync()
    {
        var token = ResetToken();
        await _vm.RefreshAsync(token).ConfigureAwait(true);
        DetectAndToastCompletions();
    }

    private void DetectAndToastCompletions()
    {
        // First refresh of the session: seed without toasting. Otherwise a
        // run that completed an hour ago would toast every time the window
        // is opened.
        if (!_snapshotSeeded)
        {
            foreach (var j in _vm.Jobs)
                _lastRunSnapshot[j.JobName] = (j.LastRunId, j.LastRunStatus);
            _snapshotSeeded = true;
            return;
        }

        foreach (var j in _vm.Jobs)
        {
            var prev = _lastRunSnapshot.TryGetValue(j.JobName, out var p) ? p : (RunId: (long?)null, Status: (string?)null);
            var nowState = (j.LastRunId, j.LastRunStatus);
            if (prev == nowState)
                continue;

            _lastRunSnapshot[j.JobName] = nowState;

            // Only toast when we cross into a terminal state. Running starts
            // get the row glow already; we don't need a second signal.
            if (j.LastRunStatus is "Success" or "Failed" or "TimedOut" or "Cancelled")
            {
                var crossedFromRunning = prev.Status == "Running" && prev.RunId == j.LastRunId;
                var newRunArrivedTerminal = prev.RunId != j.LastRunId;
                if (crossedFromRunning || newRunArrivedTerminal)
                    ShowCompletionToast(j);
            }
        }
    }

    private void ShowCompletionToast(JobRow row)
    {
        var (icon, brush) = row.LastRunStatus switch
        {
            "Success"   => ("✓", new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22))),
            "Failed"    => ("✗", new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E))),
            "TimedOut"  => ("⏱", new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E))),
            "Cancelled" => ("⦸", new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80))),
            _           => ("•", new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0))),
        };

        var duration = row.LastRunCompletedAt.HasValue && row.LastRunStartedAt.HasValue
            ? row.LastRunCompletedAt.Value - row.LastRunStartedAt.Value
            : (TimeSpan?)null;
        var durText = duration.HasValue
            ? (duration.Value.TotalSeconds < 60 ? $"{duration.Value.TotalSeconds:0.0}s" : $"{(int)duration.Value.TotalMinutes}m {duration.Value.Seconds}s")
            : "?";

        var headline = new TextBlock
        {
            Text = $"{icon}  {row.JobName} [{row.Mode}]",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
        };
        var subline = new TextBlock
        {
            Text = $"{row.LastRunStatus} · {durText}{(string.IsNullOrEmpty(row.LastRunSummary) ? string.Empty : " · " + row.LastRunSummary)}",
            Foreground = Brushes.White,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(headline);
        stack.Children.Add(subline);

        var toast = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 6, 0, 0),
            Child = stack,
            Opacity = 0,
        };

        ToastStack.Children.Add(toast);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        toast.BeginAnimation(OpacityProperty, fadeIn);

        var dismiss = new DispatcherTimer { Interval = ToastLifetime };
        dismiss.Tick += (_, _) =>
        {
            dismiss.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) => ToastStack.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fadeOut);
        };
        dismiss.Start();
    }

    private CancellationToken ResetToken()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private void JobsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Headers/scrollbars also raise MouseDoubleClick; only act on a
        // real cell hit via the DataGrid's selected item.
        if (JobsGrid.SelectedItem is not JobRow row) return;
        var detail = new FileSyncJobDetailWindow(row, _vm.Reader, _vm.CurrentUserUpn)
        {
            Owner = this,
        };
        detail.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoRefreshTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
