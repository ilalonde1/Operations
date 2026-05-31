#nullable enable
using System;
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
        try { await HeaderLoader.ApplyAsync(HeaderBar); }
        catch (Exception ex)
        {
            // Best-effort header decoration (logo + version line). Failure here
            // must not block the buyer-profile load, but it does indicate a
            // KOR-internal asset gap, so leave a breadcrumb.
            Serilog.Log.Debug(ex, "BuyerProfileWindow header decoration failed; continuing without it.");
        }
        await _vm.LoadAsync(_buyerName).ConfigureAwait(true);
    }
}
