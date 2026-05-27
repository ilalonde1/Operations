#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Navigation;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class OrgDossierView : UserControl
{
    private readonly OrgDossierViewModel _vm;
    private bool _initialized;

    public OrgDossierView(OrgDossierViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = vm;
    }

    public async Task ShowOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        try
        {
            await _vm.LoadAsync(canonicalOrgId, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void OrgDossierView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppServices.Get<AppAiContextBuilder>().Register(_vm);
    }

    private void OrgDossierView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
        _initialized = false;
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is null || !e.Uri.IsAbsoluteUri)
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch
        {
        }
    }
}
