#nullable enable
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class HistoricalOpportunityDetailWindow : Window
{
    private readonly HistoricalOpportunityDetailViewModel _vm;
    private readonly long _id;

    public HistoricalOpportunityDetailWindow(HistoricalOpportunityDetailViewModel vm, long historicalOpportunityId)
    {
        InitializeComponent();
        _vm = vm;
        _id = historicalOpportunityId;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { }
        await _vm.LoadAsync(_id).ConfigureAwait(true);
    }
}
