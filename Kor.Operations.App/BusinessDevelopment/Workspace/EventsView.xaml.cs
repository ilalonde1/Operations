#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class EventsView : UserControl
{
    private readonly EventsViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _loaded;

    public EventsView(EventsViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = vm;
    }

    private async void EventsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Register(_vm);
        var cts = ReplaceCts();
        try
        {
            await _vm.LoadAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Events - Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EventsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(_vm);
        }
        _loaded = false;
        CancelAndDisposeCts();
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is { IsAbsoluteUri: true })
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Open Link Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private Window? OwnerWindow()
    {
        return Window.GetWindow(this) ?? Application.Current?.MainWindow;
    }
}
