#nullable enable
using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class AdminView : UserControl
{
    private CancellationTokenSource? _cts;
    private bool _registered;

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

        if (!_registered)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Register(vm);
            _registered = true;
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
        if (_registered && DataContext is AdminViewModel vm)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(vm);
            _registered = false;
        }

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
