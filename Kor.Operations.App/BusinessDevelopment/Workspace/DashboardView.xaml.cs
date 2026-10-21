#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class DashboardView : UserControl
{
    private readonly IServiceProvider? _services;
    private CancellationTokenSource? _cts;
    private readonly System.Collections.Generic.HashSet<long> _closingAsks = new();

    // No parameterless ctor: MS.DI picks the greediest resolvable constructor,
    // but if selection ever regressed the bare ctor produced a view with no
    // DataContext where every drill-down silently no-ops (audit 2026-07-01 D5).
    public DashboardView(DashboardViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private bool _aiRegistered;

    // 2026-06-12 UX pass: Priority Actions starts collapsed (the list got
    // long with live data); the user's expand choice survives dashboard
    // re-navigation for the rest of the app session — static on purpose,
    // no settings plumbing.
    private static bool s_priorityActionsExpanded;

    private void PriorityActionsExpander_Toggled(object sender, RoutedEventArgs e)
        => s_priorityActionsExpanded = PriorityActionsExpander.IsExpanded;

    private async void DashboardView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        PriorityActionsExpander.IsExpanded = s_priorityActionsExpanded;
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
                await vm.LoadAsksAsync(ResolveActor(), cts.Token).ConfigureAwait(true);
                await vm.LoadPriorityActionsAsync(cts.Token).ConfigureAwait(true);
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

    // R82: Priority Actions surface handlers.

    private async void PriorityFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;
        if (PriorityProvinceCombo is null) return; // not yet bound during template apply

        vm.PriorityProvinceFilter = PriorityProvinceCombo.SelectedItem as string;
        vm.PriorityActionTypeFilter = PriorityTypeCombo.SelectedItem as string;
        vm.PriorityMinConfidence = (PriorityConfidenceCombo.SelectedItem as string) switch
        {
            "High" => Kor.Opportunities.Data.Intel.IntelConfidence.High,
            "Medium" => Kor.Opportunities.Data.Intel.IntelConfidence.Medium,
            "Low" => Kor.Opportunities.Data.Intel.IntelConfidence.Low,
            _ => null,
        };

        var cts = ReplaceCts();
        try { await vm.LoadPriorityActionsAsync(cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(OwnerWindow(), ex.Message, "Priority Actions — Filter Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void PriorityAction_Done_Click(object sender, RoutedEventArgs e)
        => await UpdateActionStatusAsync(sender, "Done").ConfigureAwait(true);

    private async void PriorityAction_Dismiss_Click(object sender, RoutedEventArgs e)
        => await UpdateActionStatusAsync(sender, "Dismissed").ConfigureAwait(true);

    private async Task UpdateActionStatusAsync(object sender, string status)
    {
        if (DataContext is not DashboardViewModel vm) return;
        if (sender is not Button btn || btn.Tag is not Kor.Opportunities.Data.Intel.PriorityActionRow row) return;

        // Disable during the write (double-click double-fired the update), and
        // surface a failed write instead of silently no-op'ing (audit 4.3/8.1).
        btn.IsEnabled = false;
        try
        {
            var ok = await vm.SetActionStatusAsync(row.ActionId, status, CancellationToken.None).ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(OwnerWindow(),
                    $"The action could not be marked {status} — it may have been retired or removed. Refresh and try again.",
                    "Priority Actions", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Priority Actions — Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private void PriorityAction_OpenOrg_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Kor.Opportunities.Data.Intel.PriorityActionRow row) return;
        if (_services is null) return;
        try
        {
            var vm = _services.GetRequiredService<Kor.Operations.App.Opportunities.OrgDossierViewModel>();
            var win = new Kor.Operations.App.Opportunities.OrgDossierWindow(vm, row.CanonicalOrgId)
            {
                Owner = OwnerWindow(),
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Priority Actions — Open Dossier Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PriorityAction_OpenCrm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Kor.Opportunities.Data.Intel.PriorityActionRow row) return;
        // R82 MVP: open the org Dossier — the dedicated CRM panel hookup is
        // covered by the separate CRM rebuild task. For now the Dossier is
        // the org-rooted surface and the user can navigate to CRM from there.
        if (_services is null) return;
        try
        {
            var vm = _services.GetRequiredService<Kor.Operations.App.Opportunities.OrgDossierViewModel>();
            var win = new Kor.Operations.App.Opportunities.OrgDossierWindow(vm, row.CanonicalOrgId)
            {
                Owner = OwnerWindow(),
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Priority Actions — Open CRM Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
    // Each row carries the Opportunity.Id. A double-click opens the RFP read view;
    // editing is a second step from there (2026-10-13).

    private async void DeadlinesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not DashboardViewModel.DeadlineRow row)
        {
            return;
        }

        // One click opens a tender, but only a click on a row: the scrollbar is part of the list too.
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != list; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ScrollBar) return;
            if (d is ListBoxItem) break;
        }

        await OpenOpportunityDialogAsync(row.OpportunityId).ConfigureAwait(true);
    }

    private async Task OpenOpportunityDialogAsync(long opportunityId)
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

        // Read first: the RFP, its source postings and the link. Editing the record
        // is a deliberate second step from that view. Until 2026-10-13 this
        // double-click landed in the eleven-field editor with no posting, no link
        // and no description, and a Save button whose purpose nobody could name.
        var detail = new OpportunityDetailWindow(new OpportunityDetailViewModel(opp,
            _services!.GetRequiredService<IOpportunityObservationStore>(),
            _services!.GetRequiredService<IOpportunitySourceStore>())) { Owner = OwnerWindow() };
        detail.ShowDialog();
        if (!detail.EditRequested)
        {
            return;
        }

        // This is the editable dialog. As in OpportunitiesView, its caller must
        // persist Result through the scored, row-version-checked update path.
        var dlg = new OpportunityEntryDialog(opp) { Owner = OwnerWindow() };
        if (dlg.ShowDialog() != true || dlg.Result is null)
        {
            return;
        }

        try
        {
            var editor = _services!.GetRequiredService<OpportunitiesViewModel>();
            await editor.UpdateAsync(dlg.Result, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OpportunityConcurrencyException)
        {
            MessageBox.Show(OwnerWindow(),
                "This opportunity was modified by another user since you opened it. Refresh and try again.",
                "BD Dashboard - Concurrent Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "BD Dashboard - Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // LoadAsync reports refresh failures in dashboard status. The write has
        // already succeeded; do not relabel a failed refresh as a failed save.
        await vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await vm.LoadAsksAsync(ResolveActor(), CancellationToken.None).ConfigureAwait(true);
    }

    private async void AskDone_Click(object sender, RoutedEventArgs e)
        => await CloseDashboardAskAsync(sender, "Done").ConfigureAwait(true);

    private async void AskCant_Click(object sender, RoutedEventArgs e)
        => await CloseDashboardAskAsync(sender, "Declined").ConfigureAwait(true);

    private async Task CloseDashboardAskAsync(object sender, string status)
    {
        if (DataContext is not DashboardViewModel vm || sender is not FrameworkElement { DataContext: DashboardViewModel.BdAskRow row }
            || !_closingAsks.Add(row.Id)) return;
        try
        {
            var dialog = new NoteDialog(status == "Done" ? "Done" : "Can't",
                status == "Done" ? "What was done? (optional)" : "What prevents this? (optional)", 500) { Owner = OwnerWindow() };
            if (dialog.ShowDialog() == true)
                await vm.CloseAskAsync(row.Id, status, dialog.Note, ResolveActor()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Dashboard could not close ask.");
            MessageBox.Show(OwnerWindow(), "Could not save your response. Please try again.", "Asked of you", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _closingAsks.Remove(row.Id); }
    }

    private string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn))
        {
            return global::Kor.Operations.OperationsApp.SignedInUserUpn.Trim();
        }

        var overrideUpn = _services!.GetRequiredService<Kor.Operations.App.Options.UserOptions>().UserUpnOverride;
        return string.IsNullOrWhiteSpace(overrideUpn) ? Environment.UserName : overrideUpn.Trim();
    }

    // The funnel badges drill into the Pipeline window pre-filtered to the stage
    // their count came from.
    private void OpenSeatsTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipelineWithFunnel(Kor.Operations.App.Opportunities.PipelineFunnel.OpenSeats);
    private void InBidWindowTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipelineWithFunnel(Kor.Operations.App.Opportunities.PipelineFunnel.BidWindow);
    private void RadarTile_Click(object sender, RoutedEventArgs e) => GoToForwardPipelineWithFunnel(Kor.Operations.App.Opportunities.PipelineFunnel.Radar);

    private void GoToForwardPipelineWithFunnel(Kor.Operations.App.Opportunities.PipelineFunnel funnel)
    {
        if (Window.GetWindow(this) is BdWorkspaceWindow workspace)
        {
            workspace.NavigateToForwardPipelineWithFunnel(funnel);
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
