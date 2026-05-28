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
