#nullable enable
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitorProfileWindow : Window
{
    private readonly CompetitorProfileViewModel _vm;
    private readonly string _vendorName;

    public CompetitorProfileWindow(CompetitorProfileViewModel vm, string vendorName)
    {
        InitializeComponent();
        _vm = vm;
        _vendorName = vendorName;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { }
        await _vm.LoadAsync(_vendorName).ConfigureAwait(true);
    }
}
