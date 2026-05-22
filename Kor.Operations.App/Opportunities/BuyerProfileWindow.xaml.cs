#nullable enable
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class BuyerProfileWindow : Window
{
    private readonly BuyerProfileViewModel _vm;
    private readonly string _buyerName;

    public BuyerProfileWindow(BuyerProfileViewModel vm, string buyerName)
    {
        InitializeComponent();
        _vm = vm;
        _buyerName = buyerName;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { }
        await _vm.LoadAsync(_buyerName).ConfigureAwait(true);
    }
}
