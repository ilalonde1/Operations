#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class DashboardView : UserControl
{
    private CancellationTokenSource? _cts;

    public DashboardView()
    {
        InitializeComponent();
    }

    public DashboardView(DashboardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private async void DashboardView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            var cts = ReplaceCts();
            try
            {
                await vm.LoadAsync(cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(OwnerWindow(), ex.Message, "BD Dashboard — Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DashboardView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
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

    private Window? OwnerWindow()
    {
        return Window.GetWindow(this) ?? Application.Current?.MainWindow;
    }
}
