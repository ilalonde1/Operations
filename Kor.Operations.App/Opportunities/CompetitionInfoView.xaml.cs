#nullable enable
using System;
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
    private bool _aiRegistered;

    public CompetitionInfoView(CompetitionInfoViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the inline Competition
        // Info view while it is hosted in BdWorkspaceWindow. Same Loaded/Unloaded
        // pattern as OrgDossierView; separate flag from _initialized because the
        // workspace can unload/reload this view without re-running InitializeAsync.
        if (!_aiRegistered)
        {
            var builder = AppServices.Get<AppAiContextBuilder>();
            builder.Register(_vm);
            builder.Register(_vm.Rfps);
            builder.Register(_vm.Awards);
            _aiRegistered = true;
        }

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        // Round 37a (T2.001): async void without try/catch crashes the app on
        // header-load / DB-init failure. The popup CompetitionInfoWindow was
        // fixed in Round 35b; the inline view (hosted in BdWorkspaceWindow) was
        // missed. Mirror the inline-view try/catch pattern from BdTrackingView.
        try
        {
            await _vm.InitializeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this) ?? Application.Current?.MainWindow,
                ex.Message,
                "Competition Info — Load Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_aiRegistered)
        {
            var builder = AppServices.Get<AppAiContextBuilder>();
            builder.Unregister(_vm);
            builder.Unregister(_vm.Rfps);
            builder.Unregister(_vm.Awards);
            _aiRegistered = false;
        }
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
