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

public sealed class RelationshipsViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // T5.001 audit fix (2026-05-30): expose Relationships state to AI.
    public string ProviderName => "BD Relationships";
    public bool HasData => Orgs.Count > 0;
    public string BuildContext()
        => $"BD Relationships — {CountDisplay}. Search='{SearchText}'; KindFilter='{KindFilter ?? "(all)"}'.";
    public string BuildLocalContext()
        => SelectedOrg is null
            ? BuildContext()
            : $"{BuildContext()} Selected: {SelectedOrg.DisplayName} (kind={SelectedOrg.Kind}, id={SelectedOrg.Id}).";

    private readonly ICanonicalOrgStore _store;
    private string _searchText = string.Empty;
    private string? _kindFilter;
    private CanonicalOrgRow? _selectedOrg;
    private bool _showAll;
    private string _countDisplay = string.Empty;

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

    /// <summary>
    /// When false (default) the list is filtered to canonical orgs with at
    /// least one KOR relationship signal — the alphabetical A-B cutoff Ian saw
    /// was the old 200-row TOP + ~7k unfiltered orgs. When true, the full
    /// canonical universe is shown.
    /// </summary>
    public bool ShowAll
    {
        get => _showAll;
        set => SetField(ref _showAll, value);
    }

    public string CountDisplay
    {
        get => _countDisplay;
        private set => SetField(ref _countDisplay, value);
    }

    public ObservableCollection<CanonicalOrgRow> Orgs { get; } = new();

    public CanonicalOrgRow? SelectedOrg
    {
        get => _selectedOrg;
        set => SetField(ref _selectedOrg, value);
    }

    public async Task SearchAsync(CancellationToken ct)
    {
        var q = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var kind = string.IsNullOrWhiteSpace(KindFilter) ? null : KindFilter;
        const int take = 500;

        // Round 50 (ss2 fix): autocomplete-style behaviour. With 1,400+ Buyer
        // canonicals (and 56K+ total), the old "load TOP 500 alphabetical when
        // empty search" UI capped at letter "C" and offered no way to find
        // anything past it. Instead: empty search shows nothing and prompts the
        // user to start typing. ShowAll keeps the browse-everything escape
        // hatch (zero-relationship canonicals included).
        if (q is null && !ShowAll)
        {
            Orgs.Clear();
            CountDisplay = "Type to search…";
            return;
        }

        var rows = ShowAll
            ? await _store.SearchCanonicalOrgsAsync(q, kind, take, ct).ConfigureAwait(true)
            : await _store.SearchCanonicalOrgsWithRelationshipsAsync(q, kind, take, ct).ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        Orgs.Clear();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            Orgs.Add(row);
        }

        var suffix = rows.Count >= take ? "+" : string.Empty;
        var scope = ShowAll ? "all canonical" : "with relationships";
        CountDisplay = $"{rows.Count:N0}{suffix} {scope}";
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
