#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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

    private Task RefreshAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _vm.RefreshAsync(_cts.Token);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
