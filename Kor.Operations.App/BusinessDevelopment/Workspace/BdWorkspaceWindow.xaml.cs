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

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(DashboardButton);
        ContentHost.Content = _services.GetRequiredService<DashboardView>();
    }

    private void Rfps_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(RfpsButton);
        ContentHost.Content = _services.GetRequiredService<App.Opportunities.OpportunitiesView>();
    }

    private void Upcoming_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(UpcomingButton);
        ContentHost.Content = _services.GetRequiredService<App.Opportunities.MajorProjectsInventoryView>();
    }

    private void Relationships_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(RelationshipsButton);
        ContentHost.Content = _services.GetRequiredService<RelationshipsView>();
    }

    private void Events_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(EventsButton);
        ContentHost.Content = _services.GetRequiredService<EventsView>();
    }

    private void Competition_Click(object sender, RoutedEventArgs e)
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
        var win = _services.GetRequiredService<App.Crm.CrmWindow>();
        win.Owner = this;
        win.Show();
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
