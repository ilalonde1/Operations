#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class DashboardView : UserControl
{
    private readonly IServiceProvider? _services;
    private CancellationTokenSource? _cts;

    public DashboardView()
    {
        InitializeComponent();
    }

    public DashboardView(DashboardViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _services = services ?? throw new ArgumentNullException(nameof(services));
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

    // ===== Drill-down handlers =====
    //
    // Each row carries the canonical id (Opportunity.Id for LatestRfps/Deadlines,
    // BdCanonicalOrg.Id for OpenStructuralSeats/CompetitorWatch). Open the same
    // detail surface the rest of the BD module uses so dashboard double-click
    // is consistent with RFPs grid / Org Dossier elsewhere.

    private void LatestRfpsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not DashboardViewModel.FeedRow row)
        {
            return;
        }

        OpenOpportunityDialog(row.OpportunityId);
    }

    private void DeadlinesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not DashboardViewModel.DeadlineRow row)
        {
            return;
        }

        OpenOpportunityDialog(row.OpportunityId);
    }

    private void OpenStructuralSeatsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not DashboardViewModel.OpenStructuralSeatRow row)
        {
            return;
        }

        OpenOrgDossier(row.OrgId);
    }

    private void CompetitorWatchGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not DashboardViewModel.CompetitorWatchPanelRow row)
        {
            return;
        }

        OpenOrgDossier(row.OrgId);
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

    private void OpenOpportunityDialog(long opportunityId)
    {
        if (DataContext is not DashboardViewModel vm)
        {
            return;
        }

        var opp = vm.GetOpportunity(opportunityId);
        if (opp is null)
        {
            MessageBox.Show(OwnerWindow(),
                "This row is no longer in the live opportunities cache. Refresh the dashboard and try again.",
                "BD Dashboard — Open Opportunity",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Read-only view of the opportunity; user can Cancel to bail without
        // saving. This matches the OpportunitiesView grid's double-click flow.
        var dlg = new OpportunityEntryDialog(opp) { Owner = OwnerWindow() };
        dlg.ShowDialog();
    }

    private void OpenOrgDossier(long canonicalOrgId)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var dvm = _services.GetRequiredService<OrgDossierViewModel>();
            var win = new OrgDossierWindow(dvm, canonicalOrgId)
            {
                Owner = OwnerWindow(),
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "BD Dashboard — Open Dossier Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== Stat-tile + chart click handlers =====
    //
    // Each stat tile + chart card on the dashboard drills into the BD nav
    // screen most relevant to that number/chart. No per-bar filter is pushed
    // yet — clicking the card navigates; clicking a specific bar would need
    // a filter-broker on the destination view (deferred).

    private void TotalPipelineTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void OpenRfpsTile_Click(object sender, RoutedEventArgs e) => GoToRfps();
    private void UpcomingProjectsTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void ClosingSoonTile_Click(object sender, RoutedEventArgs e) => GoToRfps();
    private void PipelineValueTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();

    private void SectorChart_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void MarketChart_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void StageChart_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void MarketValueChart_Click(object sender, RoutedEventArgs e) => GoToForwardPipeline();
    private void DeadlineMonthChart_Click(object sender, RoutedEventArgs e) => GoToRfps();

    private void GoToRfps()
    {
        if (Window.GetWindow(this) is BdWorkspaceWindow workspace)
        {
            workspace.NavigateToRfps();
        }
    }

    private void GoToForwardPipeline()
    {
        if (Window.GetWindow(this) is BdWorkspaceWindow workspace)
        {
            workspace.NavigateToForwardPipeline();
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
