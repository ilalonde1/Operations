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
    private Button? _activeButton;

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
            await HeaderLoader.ApplyAsync(HeaderBar);
        }
        catch
        {
            // Header identity is cosmetic and should not block the workspace.
        }

        ShowDashboard();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
        => ShowDashboard();

    private void Rfps_Click(object sender, RoutedEventArgs e)
    {
        SetActive(RfpsButton);
        var win = _services.GetRequiredService<App.Opportunities.OpportunitiesWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Upcoming_Click(object sender, RoutedEventArgs e)
    {
        SetActive(UpcomingButton);
        var win = _services.GetRequiredService<App.Opportunities.MajorProjectsInventoryWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Relationships_Click(object sender, RoutedEventArgs e)
    {
        SetActive(RelationshipsButton);
        ContentHost.Content = "Relationships folding into the workspace next.";
    }

    private void People_Click(object sender, RoutedEventArgs e)
    {
        SetActive(PeopleButton);
        ContentHost.Content = "Relationships folding into the workspace next.";
    }

    private void Competition_Click(object sender, RoutedEventArgs e)
    {
        SetActive(CompetitionButton);
        var win = _services.GetRequiredService<App.Opportunities.CompetitionInfoWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Pursuits_Click(object sender, RoutedEventArgs e)
    {
        SetActive(PursuitsButton);
        var win = _services.GetRequiredService<App.Crm.CrmWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Proposals_Click(object sender, RoutedEventArgs e)
    {
        SetActive(ProposalsButton);
        var win = AppServices.Get<App.FeeProposal.FeeProposalBuilderWindow>();
        win.Owner = this;
        win.Show();
    }

    private void Brochures_Click(object sender, RoutedEventArgs e)
    {
        SetActive(BrochuresButton);
        var win = _brochureFactory();
        win.Owner = this;
        win.Show();
    }

    private void ShowDashboard()
    {
        SetActive(DashboardButton);
        ContentHost.Content = _services.GetRequiredService<DashboardViewModel>();
    }

    private void SetActive(Button button)
    {
        if (_activeButton is not null)
        {
            _activeButton.Style = (Style)FindResource("NavButton");
        }

        button.Style = (Style)FindResource("ActiveNavButton");
        _activeButton = button;
    }
}
