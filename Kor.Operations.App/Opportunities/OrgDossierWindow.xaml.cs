#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
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

    private async void GenerateOrgBriefButton_Click(object sender, RoutedEventArgs e)
    {
        var briefStore = AppServices.Get<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
        var generator = AppServices.Get<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var path = System.IO.Path.Combine(
            desktop,
            $"KOR-Org-Brief-{_canonicalOrgId}-{DateTime.Now:yyyyMMdd-HHmmss}.docx");

        GenerateOrgBriefButton.IsEnabled = false;
        try
        {
            var data = await briefStore.GetOrgBriefAsync(_canonicalOrgId, CancellationToken.None).ConfigureAwait(true);
            if (data is null)
            {
                MessageBox.Show("Organization not found.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await Task.Run(() => generator.WriteOrgBrief(data, path)).ConfigureAwait(true);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Brief generation failed: " + ex.Message, "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            GenerateOrgBriefButton.IsEnabled = true;
        }
    }
}
