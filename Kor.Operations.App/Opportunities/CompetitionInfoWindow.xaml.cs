#nullable enable
using System.Windows;
using Kor.Operations.Services;  // HeaderLoader

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
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar);
        }
        catch
        {
            // Header avatar is cosmetic — failure shouldn't block the window.
        }
        await _vm.InitializeAsync().ConfigureAwait(true);
    }

    private void OpenAboutSources_Click(object sender, RoutedEventArgs e)
    {
        var win = new CompetitionInfoSourcesWindow { Owner = this };
        win.ShowDialog();
    }
    private void WinnerCell_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;
        var vm = Kor.Operations.Services.AppServices.Get<CompetitorProfileViewModel>();
        new CompetitorProfileWindow(vm, name) { Owner = this }.Show();
    }

    private void BuyerCell_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBlock tb) return;
        var name = tb.Text;
        if (string.IsNullOrWhiteSpace(name)) return;
        var vm = Kor.Operations.Services.AppServices.Get<BuyerProfileViewModel>();
        new BuyerProfileWindow(vm, name) { Owner = this }.Show();
    }
}
