#nullable enable
using System;
using System.Threading;
using System.Windows.Controls;
using Kor.Operations.App.Opportunities;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class RelationshipsView : UserControl
{
    private readonly RelationshipsViewModel _vm;
    private readonly IServiceProvider _services;
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
        await _vm.SearchAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        await _vm.SearchAsync(CancellationToken.None).ConfigureAwait(true);
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
            await _vm.SearchAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    private async void OrgList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SelectedOrg is not { } row)
        {
            return;
        }

        var dvm = _services.GetRequiredService<OrgDossierViewModel>();
        var dview = new OrgDossierView(dvm);
        DossierHost.Content = dview;
        await dview.ShowOrgAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
    }
}
