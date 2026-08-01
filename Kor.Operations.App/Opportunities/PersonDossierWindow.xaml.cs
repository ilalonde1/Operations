#nullable enable
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.Opportunities;

public partial class PersonDossierWindow : Window
{
    private readonly PersonDossierViewModel _vm;
    private bool _aiRegistered;

    public PersonDossierWindow(PersonDossierViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block loading the dossier.
        }
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

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null || !e.Uri.IsAbsoluteUri)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Open link failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void OnOrgClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: PersonAffiliationRow row })
        {
            return;
        }

        var dossierVm = AppServices.Get<OrgDossierViewModel>();
        new OrgDossierWindow(dossierVm, row.CanonicalOrgId) { Owner = this }.Show();
        e.Handled = true;
    }
}
