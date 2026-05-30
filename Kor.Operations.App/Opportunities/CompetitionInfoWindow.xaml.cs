#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Operations.Services;
using Serilog;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitionInfoWindow : Window
{
    private readonly CompetitionInfoViewModel _vm;

    public CompetitionInfoWindow(CompetitionInfoViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // T2.002 audit fix (2026-05-30): async void Loaded with no try/catch
        // surfaces any header-load or initialize failure through the WPF
        // dispatcher exception path, crashing the app. Bound the failure to
        // a logged warning + a non-fatal status message on the VM.
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
            await _vm.InitializeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CompetitionInfoWindow load failed.");
            MessageBox.Show(this,
                $"Competition Info failed to load: {ex.GetType().Name}: {ex.Message}",
                "Competition Info",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenAboutSources_Click(object sender, RoutedEventArgs e)
    {
        var win = new CompetitionInfoSourcesWindow { Owner = this };
        win.ShowDialog();
    }

    private void WinnerCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var vm = AppServices.Get<CompetitorProfileViewModel>();
        new CompetitorProfileWindow(vm, name) { Owner = this }.Show();
    }

    private void BuyerCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var vm = AppServices.Get<BuyerProfileViewModel>();
        new BuyerProfileWindow(vm, name) { Owner = this }.Show();
    }

    private void RfpRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not HistoricalOpportunityListing row) return;

        var vm = AppServices.Get<HistoricalOpportunityDetailViewModel>();
        new HistoricalOpportunityDetailWindow(vm, row.Id) { Owner = this }.Show();
    }
}
