#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncLogViewerWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly FileSyncLogViewerViewModel _vm;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;

    public FileSyncLogViewerWindow(
        FileSyncControlPlaneReader reader,
        string? initialHost = null,
        string? initialJobFilter = null,
        DateTimeOffset? initialFrom = null,
        DateTimeOffset? initialTo = null)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        _vm = new FileSyncLogViewerViewModel(reader, initialHost, initialJobFilter, initialFrom, initialTo);
        InitializeComponent();
        DataContext = _vm;

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollAsync().ConfigureAwait(true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var token = ResetToken();
        await _vm.InitializeAsync(token).ConfigureAwait(true);
        ScrollToEndIfRequested();
        _timer.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task PollAsync()
    {
        if (!_vm.AutoRefresh) return;
        var token = ResetToken();
        var beforeCount = _vm.DisplayedLines.Count;
        await _vm.PollOnceAsync(token).ConfigureAwait(true);
        if (_vm.DisplayedLines.Count != beforeCount)
            ScrollToEndIfRequested();
    }

    private async void RefreshNowBtn_Click(object sender, RoutedEventArgs e)
    {
        var token = ResetToken();
        await _vm.PollOnceAsync(token).ConfigureAwait(true);
        ScrollToEndIfRequested();
    }

    private void OpenInExplorerBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_vm.CurrentLogPath);
        if (string.IsNullOrEmpty(folder))
        {
            MessageBox.Show(this, "Could not resolve a folder for the current log path.", "Open log folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Don't auto-create remote (UNC) folders -- only the local one,
            // which is the case where the operator is sitting on the host.
            if (!folder.StartsWith(@"\\", StringComparison.Ordinal))
                Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open '{folder}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyLine_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLine is null) return;
        TryCopy(
            $"{_vm.SelectedLine.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{_vm.SelectedLine.Level}] {_vm.SelectedLine.SourceContext} {_vm.SelectedLine.Message}");
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLine is null) return;
        TryCopy(_vm.SelectedLine.ToCopyText());
    }

    private void FilterBySource_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLine is null) return;
        // Use the last segment so e.g. "RenameReportsUploadsRunner" filters
        // both that runner's logs and the corresponding cron-shim entries.
        _vm.JobFilter = _vm.SelectedLine.ShortSource;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _vm.JobFilter = FileSyncLogViewerViewModel.AllJobsLabel;
        _vm.SearchText = string.Empty;
        _vm.MinLevel = "INF";
        _vm.ClearTimeWindow();
    }

    private void ClearJobPill_Click(object sender, RoutedEventArgs e)
    {
        _vm.JobFilter = FileSyncLogViewerViewModel.AllJobsLabel;
    }

    private void ClearSearchPill_Click(object sender, RoutedEventArgs e)
    {
        _vm.SearchText = string.Empty;
    }

    private void ClearTimePill_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearTimeWindow();
    }

    private void ScrollToEndIfRequested()
    {
        if (!_vm.AutoScroll || _vm.DisplayedLines.Count == 0) return;
        var last = _vm.DisplayedLines[_vm.DisplayedLines.Count - 1];
        LinesGrid.ScrollIntoView(last);
    }

    private void TryCopy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Copy failed: {ex.Message}", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private CancellationToken ResetToken()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }
}
