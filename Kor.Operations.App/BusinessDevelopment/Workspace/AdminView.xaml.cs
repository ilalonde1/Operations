#nullable enable
using System;
using System.Threading;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class AdminView : UserControl
{
    private CancellationTokenSource? _cts;

    public AdminView(AdminViewModel vm)
    {
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private async void AdminView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not AdminViewModel vm)
        {
            return;
        }

        var cts = ReplaceCts();
        try
        {
            await vm.LoadAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AdminView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        CancelAndDisposeCts();
    }

    private CancellationTokenSource ReplaceCts()
    {
        var old = _cts;
        _cts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
        return _cts;
    }

    private void CancelAndDisposeCts()
    {
        var old = _cts;
        _cts = null;
        old?.Cancel();
        old?.Dispose();
    }
}
