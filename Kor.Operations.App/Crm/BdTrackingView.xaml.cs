#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Inline UserControl version of the BD Tracking spreadsheet replica. Hosted in
/// <c>BdWorkspaceWindow</c>'s ContentHost via the "BD Tracking" nav button.
/// Region tabs + per-initiator filter + rollup card + grid of engagements +
/// drill-detail panel with Activities / Contacts / Linked MPI Projects.
/// </summary>
public partial class BdTrackingView : UserControl
{
    private readonly BdTrackingViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public BdTrackingView(BdTrackingViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
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
            MessageBox.Show(OwnerWindow(), ex.Message, "BD Tracking — Refresh Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenOrgDossier_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.Selected is { BuyerCanonicalOrgId: long orgId })
        {
            OpenOrgDossierWindow(orgId);
            e.Handled = true;
        }
    }

    private void OpenLinkedMpiProject_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BdTrackingLinkedProjectRow link)
        {
            // Open the MPI inventory view scoped to this project: select the row,
            // load the detail. For now, open the OrgDossier of the project's
            // proponent (if any) — direct MPI deep-link would need a new entry
            // window. Defer that; the linked-project chip already informs.
            // Future: scoped MPI window.
            MessageBox.Show(OwnerWindow(),
                $"{link.ProjectName}\n\nProvince: {link.Province}\nStage: {link.Stage}\nConfidence: {link.ConfidenceDisplay}\nMatched on: {link.MatchedText}",
                "Linked MPI Project",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Handled = true;
        }
    }

    private void OpenOrgDossierWindow(long canonicalOrgId)
    {
        try
        {
            var vm = _services.GetRequiredService<OrgDossierViewModel>();
            var win = new OrgDossierWindow(vm, canonicalOrgId) { Owner = OwnerWindow() };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "BD Tracking — Open Dossier Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
