#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class BuyerProfileWindow : Window
{
    private readonly BuyerProfileViewModel _vm;
    private readonly string _buyerName;
    private bool _aiRegistered;

    public BuyerProfileWindow(BuyerProfileViewModel vm, string buyerName)
    {
        InitializeComponent();
        _vm = vm;
        _buyerName = buyerName;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open buyer profile.
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

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

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
    }
}
