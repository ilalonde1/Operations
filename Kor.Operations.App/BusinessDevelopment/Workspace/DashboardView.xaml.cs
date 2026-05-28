#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;

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

    private async void ForwardPipelineGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not DashboardViewModel.ForwardPipelinePanelRow row)
        {
            return;
        }

        try
        {
            var vm = AppServices.Get<PursuitBriefViewModel>();
            await vm.LoadAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            var win = new PursuitBriefWindow(vm)
            {
                Owner = OwnerWindow(),
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Pursuit Brief — Generate Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
