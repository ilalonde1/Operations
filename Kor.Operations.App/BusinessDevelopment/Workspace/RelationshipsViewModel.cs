#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class RelationshipsViewModel : INotifyPropertyChanged
{
    private readonly ICanonicalOrgStore _store;
    private string _searchText = string.Empty;
    private string? _kindFilter;
    private CanonicalOrgRow? _selectedOrg;

    public RelationshipsViewModel(ICanonicalOrgStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value ?? string.Empty);
    }

    public string? KindFilter
    {
        get => _kindFilter;
        set => SetField(ref _kindFilter, value);
    }

    public ObservableCollection<CanonicalOrgRow> Orgs { get; } = new();

    public CanonicalOrgRow? SelectedOrg
    {
        get => _selectedOrg;
        set => SetField(ref _selectedOrg, value);
    }

    public async Task SearchAsync(CancellationToken ct)
    {
        var rows = await _store.SearchCanonicalOrgsAsync(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            string.IsNullOrWhiteSpace(KindFilter) ? null : KindFilter,
            200,
            ct).ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        Orgs.Clear();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            Orgs.Add(row);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
