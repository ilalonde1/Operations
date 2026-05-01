#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncCommandCenterWindow : Window
{
    private readonly FileSyncCommandCenterViewModel _vm;
    private CancellationTokenSource? _cts;

    public FileSyncCommandCenterWindow(FileSyncCommandCenterViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
        HeartbeatsGrid.ItemsSource = _vm.Heartbeats;
        JobsGrid.ItemsSource = _vm.Jobs;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
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
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
