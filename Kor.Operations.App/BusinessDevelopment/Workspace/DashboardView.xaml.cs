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

    private bool _aiRegistered;

    private async void DashboardView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            if (!_aiRegistered)
            {
                AppServices.Get<AppAiContextBuilder>().Register(vm);
                _aiRegistered = true;
            }
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
        if (_aiRegistered && DataContext is DashboardViewModel vm)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(vm);
            _aiRegistered = false;
        }
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

    /// <summary>
    /// Per-bar click. Inner Button's Click stops bubbling so the outer chart
    /// card's *_Chart_Click doesn't also fire. We discriminate which chart
    /// the bar belongs to by walking up to the parent ItemsControl and
    /// comparing its ItemsSource against the VM's chart collections — that
    /// way one handler covers all four filter-aware charts.
    /// </summary>
    private void Bar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string label || string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (DataContext is not DashboardViewModel vm)
        {
            return;
        }

        var items = FindAncestorItemsControl(fe);
        if (items is null)
        {
            return;
        }

        if (Window.GetWindow(this) is not BdWorkspaceWindow workspace)
        {
            return;
        }

        // Route by which chart's collection this bar's ItemsControl is bound to.
        if (ReferenceEquals(items.ItemsSource, vm.SectorBars))
        {
            workspace.NavigateToForwardPipelineWithFilter(province: null, stage: null, sector: label);
        }
        else if (ReferenceEquals(items.ItemsSource, vm.MarketBars)
            || ReferenceEquals(items.ItemsSource, vm.MarketValueBars))
        {
            workspace.NavigateToForwardPipelineWithFilter(province: label, stage: null, sector: null);
        }
        else if (ReferenceEquals(items.ItemsSource, vm.StageBars))
        {
            workspace.NavigateToForwardPipelineWithFilter(province: null, stage: label, sector: null);
        }
        else if (ReferenceEquals(items.ItemsSource, vm.DeadlineMonthBars))
        {
            // No per-month filter on the RFPs screen yet; fall back to the
            // card-level navigation (open RFPs unfiltered). Better than the
            // alternative of stuffing the month into FilterText, which would
            // narrow by substring match — fragile.
            workspace.NavigateToRfps();
        }
    }

    private static ItemsControl? FindAncestorItemsControl(DependencyObject child)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is ItemsControl ic)
            {
                return ic;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

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
