#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.MajorProjects;

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
    private readonly IBdDashboardStore _dashboard;
    private string _searchText = string.Empty;
    private string? _kindFilter;
    private CanonicalOrgRow? _selectedOrg;
    private bool _showAll;
    private string _countDisplay = string.Empty;
    private string _openSeatsNote = "Loading…";

    public RelationshipsViewModel(ICanonicalOrgStore store, IBdDashboardStore dashboard)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    /// <summary>Architects whose structural partner is not settled, from research on the
    /// architect (an organisation-level pattern, not a project appointment). Moved here
    /// from the BD dashboard on 2026-10-13; this is the default content of the dossier pane.</summary>
    public ObservableCollection<BdOpenStructuralSeatRow> OpenSeats { get; } = new();

    public string OpenSeatsNote
    {
        get => _openSeatsNote;
        private set => SetField(ref _openSeatsNote, value);
    }

    public async Task LoadOpenSeatsAsync(CancellationToken ct)
    {
        try
        {
            // Every architect with an open seat: 12 by name showed A to D of 30 and called it "12 architects" (2026-10-22).
            var rows = await _dashboard.GetOpenStructuralSeatsAsync(200, ct).ConfigureAwait(true);
            OpenSeats.Clear();
            foreach (var row in rows)
            {
                OpenSeats.Add(row);
            }

            OpenSeatsNote = rows.Count == 0
                ? "No architect currently reads as having an unsettled structural partner."
                : $"{rows.Count} architects, from research on each firm. Not a project appointment; see the project brief for that.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            OpenSeatsNote = "Could not retrieve the list: " + ex.Message;
        }
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
