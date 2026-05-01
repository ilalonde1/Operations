#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.App.FileSync;

public partial class FileSyncActivityWindow : Window
{
    private readonly FileSyncActivityViewModel _vm;
    private CancellationTokenSource? _cts;

    public FileSyncActivityWindow(FileSyncControlPlaneReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
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

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
