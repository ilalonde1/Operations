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
        await HeaderLoader.ApplyAsync(HeaderBar);
        await _vm.LoadAsync(_id).ConfigureAwait(true);
    }

    private void AddToPursuits_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Detail is null) return;

        var dlgVm = AppServices.Get<KorPursuitDialogViewModel>();
        dlgVm.HistoricalOpportunityId = _vm.Detail.Id;
        dlgVm.BuyerName = _vm.Detail.BuyerName;
        dlgVm.Title = _vm.Detail.Name;
        dlgVm.SourceExternalRef = _vm.Detail.OpportunityKey;

        var dlg = new KorPursuitDialog(dlgVm) { Owner = this };
        dlg.ShowDialog();
    }
}
