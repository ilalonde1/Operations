#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Brochures;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class BdWorkspaceWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly Func<BrochureBuilderWindow> _brochureFactory;

    public BdWorkspaceWindow(IServiceProvider services, Func<BrochureBuilderWindow> brochureFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _brochureFactory = brochureFactory ?? throw new ArgumentNullException(nameof(brochureFactory));
        InitializeComponent();
        GlobalSearch.Store = _services.GetRequiredService<ICanonicalOrgStore>();
    }

    private bool _loadedOnce;

    private async void BdWorkspaceWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded can re-fire (re-templating/theme reload); wiring the search
        // subscription twice would open N dossiers per click (audit 2026-07-01 2.1).
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;

        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block the workspace.
        }

        try
        {
            SetActiveNav(DashboardButton);
            ContentHost.Content = _services.GetRequiredService<DashboardView>();
        }
        catch (Exception ex)
        {
            // async void Loaded: an unguarded DI/ctor failure here crashed the
            // whole app on workspace open (audit 2026-07-01 3.7).
            Serilog.Log.Error(ex, "BD workspace dashboard failed to load.");
            MessageBox.Show(this, $"The dashboard could not be loaded:\n{ex.Message}",
                "Business Development", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        GlobalSearch.OrgSelected += (_, orgId) => OpenOrgDossier(orgId);
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => NavigateToDashboard();
    private void Rfps_Click(object sender, RoutedEventArgs e) => NavigateToRfps();
    private void Upcoming_Click(object sender, RoutedEventArgs e) => NavigateToForwardPipeline();
    private void Relationships_Click(object sender, RoutedEventArgs e) => NavigateToRelationships();
    private void Events_Click(object sender, RoutedEventArgs e) => NavigateToEvents();
    private void Competition_Click(object sender, RoutedEventArgs e) => NavigateToCompetition();

    // ===== Public navigation API =====
    //
    // The dashboard cards drill into these screens on click. Exposing as
    // public methods avoids each child view having to know about the
    // workspace's nav buttons.

    public void NavigateToDashboard()
    {
        SetActiveNav(DashboardButton);
        ContentHost.Content = _services.GetRequiredService<DashboardView>();
    }

    public void NavigateToRfps()
    {
        SetActiveNav(RfpsButton);
        ContentHost.Content = _services.GetRequiredService<App.Opportunities.OpportunitiesView>();
    }

    public void NavigateToForwardPipeline()
    {
        NavigateToForwardPipelineWithFilter(null, null, null);
    }

    /// <summary>
    /// Like <see cref="NavigateToForwardPipeline"/> but pre-stamps one or more
    /// filter values on the Forward Pipeline VM before its first load. Used by
    /// the dashboard bar charts — clicking a "BC" bar in "By market" drills
    /// straight into the Forward Pipeline filtered to BC.
    /// </summary>
    public void NavigateToForwardPipelineWithFilter(string? province, string? stage, string? sector)
    {
        SetActiveNav(UpcomingButton);
        var view = _services.GetRequiredService<App.Opportunities.MajorProjectsInventoryView>();
        if (view.DataContext is App.Opportunities.MajorProjectsInventoryViewModel vm)
        {
            vm.PendingProvinceFilter = province;
            vm.PendingStageFilter = stage;
            vm.PendingSectorFilter = sector;
        }

        ContentHost.Content = view;
    }

    /// <summary>
    /// Like <see cref="NavigateToForwardPipeline"/> but pre-stamps a
    /// funnel category (Open Seats / In Bid Window / Radar) on the
    /// Forward Pipeline VM. Used by the three dashboard funnel badges
    /// so clicking "Open Seats" drills into the same likely-open subset
    /// the count badge represents.
    /// </summary>
    public void NavigateToForwardPipelineWithFunnel(App.Opportunities.PipelineFunnel funnel)
    {
        SetActiveNav(UpcomingButton);
        var view = _services.GetRequiredService<App.Opportunities.MajorProjectsInventoryView>();
        if (view.DataContext is App.Opportunities.MajorProjectsInventoryViewModel vm)
        {
            vm.PendingFunnelFilter = funnel;
        }

        ContentHost.Content = view;
    }

    public void NavigateToRelationships()
    {
        SetActiveNav(RelationshipsButton);
        ContentHost.Content = _services.GetRequiredService<RelationshipsView>();
    }

    private async void OpenOrgDossier(long canonicalOrgId)
    {
        NavigateToRelationships();
        if (ContentHost.Content is not RelationshipsView relView)
        {
            return;
        }

        try
        {
            await relView.SelectOrgAsync(canonicalOrgId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Previously fire-and-forget: a failing global-search click did
            // nothing, silently (audit 2026-07-01 3.8).
            Serilog.Log.Warning(ex, "Open org dossier failed for org {OrgId}.", canonicalOrgId);
            MessageBox.Show(this, $"Could not open that organization:\n{ex.Message}",
                "Business Development", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void NavigateToEvents()
    {
        SetActiveNav(EventsButton);
        ContentHost.Content = _services.GetRequiredService<EventsView>();
    }

    public void NavigateToCompetition()
    {
        SetActiveNav(CompetitionButton);
        ContentHost.Content = _services.GetRequiredService<App.Opportunities.CompetitionInfoView>();
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(AdminButton);
        ContentHost.Content = _services.GetRequiredService<AdminView>();
    }

    private void Bazaar_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(BazaarButton);
        ContentHost.Content = _services.GetRequiredService<BazaarView>();
    }

    private void Pursuits_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(PursuitsButton);
        ContentHost.Content = _services.GetRequiredService<App.Crm.CrmView>();
    }

    /// <summary>
    /// Open the Pursuits screen scoped to one engagement — the Overwatch board's
    /// double-click "manage this pursuit" path. Same pending-state pattern as
    /// <see cref="NavigateToForwardPipelineWithFilter"/>: stamp the id on the VM
    /// before its first load so the row arrives selected.
    /// </summary>
    public void NavigateToPursuit(long engagementId)
    {
        SetActiveNav(PursuitsButton);
        var view = _services.GetRequiredService<App.Crm.CrmView>();
        if (view.DataContext is App.Crm.CrmViewModel vm)
        {
            vm.PendingSelectEngagementId = engagementId;
        }

        ContentHost.Content = view;
    }

    private void Overwatch_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(OverwatchButton);
        ContentHost.Content = _services.GetRequiredService<OverwatchView>();
    }

    private void Attribution_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(AttributionButton);
        ContentHost.Content = _services.GetRequiredService<AttributionView>();
    }

    private void BdTracking_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(BdTrackingButton);
        ContentHost.Content = _services.GetRequiredService<App.Crm.BdTrackingView>();
    }

    private void Proposals_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(ProposalsButton);
        var win = AppServices.Get<App.FeeProposal.FeeProposalBuilderWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Brochures_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(BrochuresButton);
        var win = _brochureFactory();
        win.Owner = this;
        win.Show();
    }

    private void BriefsMaker_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<Kor.Operations.App.BusinessDevelopment.Briefs.BriefsMakerWindow>();
        win.Owner = this;
        win.ShowDialog();
    }

    private void SetActiveNav(Button active)
    {
        foreach (var button in new[]
        {
            DashboardButton,
            RfpsButton,
            UpcomingButton,
            RelationshipsButton,
            EventsButton,
            CompetitionButton,
            AdminButton,
            BazaarButton,
            BdTrackingButton,
            PursuitsButton,
            OverwatchButton,
            AttributionButton,
            ProposalsButton,
            BrochuresButton,
        })
        {
            button.Style = (Style)FindResource(ReferenceEquals(button, active) ? "ActiveNavButton" : "NavButton");
        }
    }
}
