#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncJobDetailWindow : Window
{
    private readonly FileSyncJobDetailViewModel _vm;
    private readonly FileSyncControlPlaneReader _reader;
    private readonly string _currentUserUpn;
    private CancellationTokenSource? _cts;

    public FileSyncJobDetailWindow(JobRow job, FileSyncControlPlaneReader reader, string currentUserUpn)
    {
        if (job is null) throw new ArgumentNullException(nameof(job));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _currentUserUpn = currentUserUpn ?? throw new ArgumentNullException(nameof(currentUserUpn));
        _vm = new FileSyncJobDetailViewModel(_reader, job);
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

    private void OpenLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(_vm.LogsFolder);
    }

    private void OpenShadowBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(_vm.ShadowFolderForJob);
    }

    private async void ToggleModeBtn_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Job;
        if (row.Mode == "Shadow")
        {
            var ok = MessageBox.Show(
                this,
                $"Flip job '{row.JobName}' from Shadow to LIVE?\n\nIn Live mode this job will perform real Graph writes / file moves the next time it runs.",
                "Confirm: switch to Live",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        var newMode = row.Mode == "Shadow" ? "Live" : "Shadow";
        var token = ResetToken();
        try
        {
            await _reader.SetJobModeAsync(row.JobName, newMode, _currentUserUpn, token).ConfigureAwait(true);
            MessageBox.Show(this, $"'{row.JobName}' set to {newMode}. Close and reopen the job detail to see the updated row.", "Mode changed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Mode change failed: {ex.GetType().Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ManualFireBtn_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Job;
        var token = ResetToken();
        try
        {
            var triggerId = await _reader.QueueManualFireAsync(row.JobName, _currentUserUpn, args: null, token).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(6), token).ConfigureAwait(true);
            await _vm.RefreshAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Manual fire failed: {ex.GetType().Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private void OpenInExplorer(string path)
    {
        try
        {
            // CreateDirectory is a no-op if it already exists. Pre-creating
            // means "Open shadow folder" works even before the runner has
            // ever produced a Shadow run -- the folder just appears empty.
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open '{path}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
