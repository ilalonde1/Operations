#nullable enable
using System;
using System.Threading;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class OrgDossierWindow : Window
{
    private readonly long _canonicalOrgId;
    private readonly OrgDossierView _view;

    public OrgDossierWindow(OrgDossierViewModel vm, long canonicalOrgId)
    {
        _canonicalOrgId = canonicalOrgId;
        InitializeComponent();
        _view = new OrgDossierView(vm ?? throw new ArgumentNullException(nameof(vm)));
        ViewHost.Content = _view;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block loading the dossier.
        }

        await _view.ShowOrgAsync(_canonicalOrgId, CancellationToken.None).ConfigureAwait(true);
    }
}
