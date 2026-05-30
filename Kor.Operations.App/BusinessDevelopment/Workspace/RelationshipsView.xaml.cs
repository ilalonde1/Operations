#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
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
        await SearchAsync().ConfigureAwait(true);
    }

    private void RelationshipsView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        CancelAndDisposeSearchCts();
        CancelAndDisposeDossierCts();
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
                "All (curated)" => null,
                _ => value,
            };
        }

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
    {
        if (_vm.SelectedOrg is not { } row)
        {
            MessageBox.Show(OwnerWindow(), "Select an organization first.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var briefStore = _services.GetRequiredService<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
        var generator = _services.GetRequiredService<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var path = System.IO.Path.Combine(
            desktop,
            $"KOR-Org-Brief-{row.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.docx");

        GenerateOrgBriefButton.IsEnabled = false;
        try
        {
            var data = await briefStore.GetOrgBriefAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            if (data is null)
            {
                MessageBox.Show(OwnerWindow(), "Organization not found.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await Task.Run(() => generator.WriteOrgBrief(data, path)).ConfigureAwait(true);
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
