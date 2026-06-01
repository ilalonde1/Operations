#nullable enable
using System;
using System.Collections.Generic;
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
        => await GenerateOrgBriefAsync(asPdf: true).ConfigureAwait(true);

    private async void GenerateOrgBriefAsWord_Click(object sender, RoutedEventArgs e)
        => await GenerateOrgBriefAsync(asPdf: false).ConfigureAwait(true);

    private async Task GenerateOrgBriefAsync(bool asPdf)
    {
        var briefStore = AppServices.Get<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var ext = asPdf ? "pdf" : "docx";
        var path = System.IO.Path.Combine(
            desktop,
            $"KOR-Org-Brief-{_canonicalOrgId}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");

        GenerateOrgBriefButton.IsEnabled = false;
        try
        {
            var data = await briefStore.GetOrgBriefAsync(_canonicalOrgId, CancellationToken.None).ConfigureAwait(true);
            if (data is null)
            {
                MessageBox.Show("Organization not found.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (data is { DeltekClientId: { Length: > 0 } cid })
            {
                try
                {
                    var deltekSvc = AppServices.Get<Kor.Operations.App.Crm.IDeltekClientContextService>();
                    var intel = await deltekSvc.LoadAsync(cid, CancellationToken.None).ConfigureAwait(true);
                    if (intel is not null)
                    {
                        data = data with { Deltek = BuildDeltekSection(intel) };
                    }
                }
                catch
                {
                    // Deltek enrichment is best-effort; the BD-canonical brief still renders.
                }
            }

            if (asPdf)
            {
                var pdf = AppServices.Get<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefPdfGenerator>();
                await Task.Run(() => pdf.WriteOrgBrief(data, path)).ConfigureAwait(true);
            }
            else
            {
                var docx = AppServices.Get<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator>();
                await Task.Run(() => docx.WriteOrgBrief(data, path)).ConfigureAwait(true);
            }

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

    private static Kor.Opportunities.Data.Briefs.OrgBriefDeltekSection BuildDeltekSection(
        Kor.Operations.App.Crm.DeltekClientIntelligence intel)
    {
        var topProjects = new List<Kor.Opportunities.Data.Briefs.OrgBriefDeltekProject>();
        foreach (var p in intel.Projects)
        {
            if (topProjects.Count >= 5)
            {
                break;
            }

            topProjects.Add(new Kor.Opportunities.Data.Briefs.OrgBriefDeltekProject(
                p.Wbs1, p.Name, p.OpenDate, p.Status, p.Fee, p.FeeBilled));
        }

        return new Kor.Opportunities.Data.Briefs.OrgBriefDeltekSection(
            DeltekClientId: intel.ClientId,
            ClientName: intel.ClientName,
            ProjectCount: intel.ProjectCount,
            LifetimeFee: intel.LifetimeFee,
            LatestProjectStart: intel.LatestProjectStart,
            LatestProjectName: intel.LatestProjectName,
            RecentProjects: topProjects,
            ContactCount: intel.Contacts.Count,
            ArOutstanding: intel.Ar?.TotalOutstanding ?? 0m,
            Ar90Plus: intel.Ar?.Outstanding90Plus ?? 0m,
            DegradedSections: intel.HasDegradedSections);
    }
}
