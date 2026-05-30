#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Inline UserControl version of the Competition Info screen. Hosted in
/// <c>BdWorkspaceWindow</c>'s ContentHost via the "Competition" nav button.
/// Mirrors <c>CompetitionInfoWindow</c>'s logic; opens drill-downs (vendor
/// profile, buyer profile, RFP detail) as their own Windows like before.
/// </summary>
public partial class CompetitionInfoView : UserControl
{
    private readonly CompetitionInfoViewModel _vm;
    private bool _initialized;

    public CompetitionInfoView(CompetitionInfoViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _vm.InitializeAsync().ConfigureAwait(true);
    }

    private void OpenAboutSources_Click(object sender, RoutedEventArgs e)
    {
        var win = new CompetitionInfoSourcesWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    private void WinnerCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var vm = AppServices.Get<CompetitorProfileViewModel>();
        new CompetitorProfileWindow(vm, name) { Owner = Window.GetWindow(this) }.Show();
    }

    private void BuyerCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var vm = AppServices.Get<BuyerProfileViewModel>();
        new BuyerProfileWindow(vm, name) { Owner = Window.GetWindow(this) }.Show();
    }

    private void RfpRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not HistoricalOpportunityListing row) return;

        var vm = AppServices.Get<HistoricalOpportunityDetailViewModel>();
        new HistoricalOpportunityDetailWindow(vm, row.Id) { Owner = Window.GetWindow(this) }.Show();
    }
}
