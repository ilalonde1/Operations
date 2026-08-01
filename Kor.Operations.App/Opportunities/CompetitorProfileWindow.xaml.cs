#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitorProfileWindow : Window
{
    private readonly CompetitorProfileViewModel _vm;
    private readonly string _vendorName;
    private bool _aiRegistered;

    public CompetitorProfileWindow(CompetitorProfileViewModel vm, string vendorName)
    {
        InitializeComponent();
        _vm = vm;
        _vendorName = vendorName;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open competitor profile.
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        await HeaderLoader.ApplyAsync(HeaderBar);
        await _vm.LoadAsync(_vendorName).ConfigureAwait(true);
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

    private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            // Process.Start failures are commonly absent shell handler, malformed
            // URI, or user-cancelled UAC. None of these warrant a popup, but the
            // breadcrumb lets us spot a pattern in the logs.
            Serilog.Log.Debug(ex, "CompetitorProfileWindow hyperlink navigation failed; URI={Uri}.", e.Uri);
        }
    }
}
