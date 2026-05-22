#nullable enable
using System;
using System.Windows;
using Kor.Operations.Brochures;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment;

/// <summary>
/// BD hub window. Replaces the standalone Opportunities / FeeProposal / Brochure
/// tiles on the Home grid with a single Business Development entry point so the
/// pursuit lifecycle (find → propose → track → win/lose) lives in one place.
///
/// Each sub-tile resolves the actual feature window from DI and shows it as a
/// child of the hub, the same pattern HomeWindow uses for top-level cards.
/// </summary>
public partial class BusinessDevelopmentWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly Func<BrochureBuilderWindow> _brochureBuilderWindowFactory;

    public BusinessDevelopmentWindow(
        IServiceProvider services,
        Func<BrochureBuilderWindow> brochureBuilderWindowFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _brochureBuilderWindowFactory = brochureBuilderWindowFactory ?? throw new ArgumentNullException(nameof(brochureBuilderWindowFactory));
        InitializeComponent();
        Loaded += BusinessDevelopmentWindow_Loaded;
    }

    private async void BusinessDevelopmentWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar);
        }
        catch
        {
            // Header is cosmetic — failure shouldn't block opening the hub.
        }
    }

    private void OpenOpportunities_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<App.Opportunities.OpportunitiesWindow>();
        win.Owner = this;
        win.Show();
    }

    private void OpenCrm_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<App.Crm.CrmWindow>();
        win.Owner = this;
        win.Show();
    }

    private void OpenFeeProposal_Click(object sender, RoutedEventArgs e)
    {
        var win = AppServices.Get<App.FeeProposal.FeeProposalBuilderWindow>();
        win.Owner = this;
        win.Show();
    }

    private void OpenBrochure_Click(object sender, RoutedEventArgs e)
    {
        var win = _brochureBuilderWindowFactory();
        win.Owner = this;
        win.Show();
    }
    private void OpenCompetitionInfo_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<App.Opportunities.CompetitionInfoWindow>();
        win.Owner = this;
        win.Show();
    }
}
