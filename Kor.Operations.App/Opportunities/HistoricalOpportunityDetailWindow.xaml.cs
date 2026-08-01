#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class HistoricalOpportunityDetailWindow : Window
{
    private readonly HistoricalOpportunityDetailViewModel _vm;
    private readonly long _id;
    private bool _aiRegistered;

    public HistoricalOpportunityDetailWindow(HistoricalOpportunityDetailViewModel vm, long historicalOpportunityId)
    {
        InitializeComponent();
        _vm = vm;
        _id = historicalOpportunityId;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open RFP detail.
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        await HeaderLoader.ApplyAsync(HeaderBar);
        await _vm.LoadAsync(_id).ConfigureAwait(true);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
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
