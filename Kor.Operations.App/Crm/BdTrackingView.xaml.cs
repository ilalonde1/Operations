#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Inline UserControl version of the BD Tracking spreadsheet replica. Hosted in
/// <c>BdWorkspaceWindow</c>'s ContentHost via the "BD Tracking" nav button.
/// Region tabs + per-initiator filter + rollup card + grid of engagements.
/// </summary>
public partial class BdTrackingView : UserControl
{
    private readonly BdTrackingViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public BdTrackingView(BdTrackingViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        AppServices.Get<AppAiContextBuilder>().Register(_vm);

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
            MessageBox.Show(OwnerWindow(), ex.Message, "BD Tracking — Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
        }
        _initialized = false;
        CancelAndDisposeCts();
    }

    private void RegionTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string region)
        {
            _vm.SelectedRegion = region;
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

    private Window? OwnerWindow() => Window.GetWindow(this) ?? Application.Current?.MainWindow;
}
