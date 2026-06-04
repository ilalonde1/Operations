#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class RelationshipsView : UserControl
{
    private readonly RelationshipsViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _dossierCts;
    private bool _loaded;

    public RelationshipsView(RelationshipsViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        DataContext = vm;
    }

    private async void RelationshipsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Register(_vm);
        await SearchAsync().ConfigureAwait(true);
    }

    private void RelationshipsView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        CancelAndDisposeSearchCts();
        CancelAndDisposeDossierCts();
        if (_loaded)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(_vm);
        }
        _loaded = false;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        await SearchAsync().ConfigureAwait(true);
    }

    private async void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindCombo.SelectedItem is ComboBoxItem item && item.Content is string value)
        {
            _vm.KindFilter = value switch
            {
                "All kinds" => null,
                _ => value,
            };
        }

        if (_loaded)
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    private async void ShowAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    private async void OrgList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SelectedOrg is not { } row)
        {
            return;
        }

        var cts = ReplaceDossierCts();
        try
        {
            var dvm = _services.GetRequiredService<OrgDossierViewModel>();
            var dview = new OrgDossierView(dvm);
            DossierHost.Content = dview;
            await dview.ShowOrgAsync(row.Id, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Relationships — Load Dossier Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void GenerateOrgBriefButton_Click(object sender, RoutedEventArgs e)
        => await GenerateOrgBriefAsync(asPdf: true).ConfigureAwait(true);

    private async void GenerateOrgBriefAsWord_Click(object sender, RoutedEventArgs e)
        => await GenerateOrgBriefAsync(asPdf: false).ConfigureAwait(true);

    private async Task GenerateOrgBriefAsync(bool asPdf)
    {
        if (_vm.SelectedOrg is not { } row)
        {
            MessageBox.Show(OwnerWindow(), "Select an organization first.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var briefStore = _services.GetRequiredService<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var ext = asPdf ? "pdf" : "docx";
        var path = System.IO.Path.Combine(
            desktop,
            $"KOR-Org-Brief-{row.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");

        GenerateOrgBriefButton.IsEnabled = false;
        try
        {
            var data = await briefStore.GetOrgBriefAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            if (data is null)
            {
                MessageBox.Show(OwnerWindow(), "Organization not found.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    else
                    {
                        // Linked but Deltek's Clendor table returned no row — show a
                        // brief-side note instead of a workflow-blocking popup. Tech
                        // detail goes to Serilog (under Generate Brief category).
                        Serilog.Log.Information(
                            "Generate Brief: Deltek lookup returned null for ClientId {DeltekClientId} on canonical org {CanonicalOrgId} ({DisplayName}).",
                            cid, data.Id, data.DisplayName);
                        data = data with { DeltekNote = "No Deltek engagement history on file for this organization." };
                    }
                }
                catch (Exception deltekEx)
                {
                    Serilog.Log.Warning(
                        deltekEx,
                        "Generate Brief: Deltek lookup failed for ClientId {DeltekClientId} on canonical org {CanonicalOrgId} ({DisplayName}).",
                        cid, data.Id, data.DisplayName);
                    data = data with { DeltekNote = "Deltek engagement history temporarily unavailable." };
                }
            }
            else
            {
                Serilog.Log.Information(
                    "Generate Brief: canonical org {CanonicalOrgId} ({DisplayName}) has no ClendorClientId — Deltek section skipped.",
                    data.Id, data.DisplayName);
                data = data with { DeltekNote = "No Deltek connection on file for this organization." };
            }

            if (asPdf)
            {
                var pdf = _services.GetRequiredService<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefPdfGenerator>();
                await Task.Run(() => pdf.WriteOrgBrief(data, path)).ConfigureAwait(true);
            }
            else
            {
                var docx = _services.GetRequiredService<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator>();
                await Task.Run(() => docx.WriteOrgBrief(data, path)).ConfigureAwait(true);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), "Brief generation failed: " + ex.Message, "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private async Task SearchAsync()
    {
        var cts = ReplaceSearchCts();
        try
        {
            await _vm.SearchAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Relationships — Search Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private CancellationTokenSource ReplaceSearchCts()
    {
        var old = _searchCts;
        _searchCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
        return _searchCts;
    }

    private CancellationTokenSource ReplaceDossierCts()
    {
        var old = _dossierCts;
        _dossierCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
        return _dossierCts;
    }

    private void CancelAndDisposeSearchCts()
    {
        var old = _searchCts;
        _searchCts = null;
        old?.Cancel();
        old?.Dispose();
    }

    private void CancelAndDisposeDossierCts()
    {
        var old = _dossierCts;
        _dossierCts = null;
        old?.Cancel();
        old?.Dispose();
    }

    private Window? OwnerWindow()
    {
        return Window.GetWindow(this) ?? Application.Current?.MainWindow;
    }
}
