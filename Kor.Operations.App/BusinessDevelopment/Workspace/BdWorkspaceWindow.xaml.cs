#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Brochures;
using Kor.Operations.Services;
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
    }

    private async void BdWorkspaceWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block the workspace.
        }

        SetActiveNav(DashboardButton);
        ContentHost.Content = _services.GetRequiredService<DashboardView>();
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

    public void NavigateToRelationships()
    {
        SetActiveNav(RelationshipsButton);
        ContentHost.Content = _services.GetRequiredService<RelationshipsView>();
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

    private void Pursuits_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(PursuitsButton);
        ContentHost.Content = _services.GetRequiredService<App.Crm.CrmView>();
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

    private void RegionBrief_Click(object sender, RoutedEventArgs e)
    {
        // Modal action — does not switch screens, so we don't call SetActiveNav.
        var dlg = _services.GetRequiredService<Kor.Operations.App.BusinessDevelopment.Briefs.RegionBriefDialog>();
        dlg.Owner = this;
        dlg.ShowDialog();
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
            RegionBriefButton,
            PursuitsButton,
            ProposalsButton,
            BrochuresButton,
        })
        {
            button.Style = (Style)FindResource(ReferenceEquals(button, active) ? "ActiveNavButton" : "NavButton");
        }
    }
}
