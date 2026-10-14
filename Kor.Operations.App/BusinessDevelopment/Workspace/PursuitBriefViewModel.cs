#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class PursuitBriefViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // T5.001 audit fix (2026-05-30): expose Pursuit Brief state to AI.
    public string ProviderName => "BD Pursuit Brief";
    public bool HasData => _brief is not null;
    public string BuildContext()
        => _brief is null
            ? "BD Pursuit Brief — no brief loaded."
            : $"BD Pursuit Brief — project: {_brief.Project.ProjectName}; owner: {_brief.Project.Owner ?? "(unnamed)"}; architect: {_brief.Architect.ArchitectName ?? "(unnamed)"}; market: {_brief.Project.Market ?? "?"}.";
    public string BuildLocalContext() => BuildContext();

    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");
    private readonly IPursuitBriefStore _store;
    private readonly IDeltekClientContextService _deltek;
    private readonly IKorClientBdIntelligenceStore _korBd;
    private PursuitBrief? _brief;
    private string? _korEdgeDisplay;
    private string? _ownerContactsDisplay;
    private string? _ownerBdRecordDisplay;
    private string? _architectWarmthDisplay;
    private string _statusMessage = "Ready.";
    private enum ReadState { NotRetrieved, Unlinked, Available, Partial, Unavailable }
    private ReadState _ownerRead, _ownerBdRead, _architectRead;

    private static string EvidenceDisplay(string? text, ReadState state, string empty) => state switch
    {
        ReadState.Available => string.IsNullOrWhiteSpace(text) ? empty : text,
        ReadState.Partial => (string.IsNullOrWhiteSpace(text) ? "Unknown." : text) + " Partial retrieval; some relationship data is unavailable.",
        ReadState.Unlinked => "Unknown — organization not linked to a Deltek client; relationship data not retrieved.",
        ReadState.Unavailable => "Unavailable — could not retrieve relationship data.",
        _ => "Relationship data not retrieved.",
    };

    private void SetReadState(ref ReadState field, ReadState state)
    {
        field = state;
        OnPropertyChanged(nameof(KorEdgeDisplay));
        OnPropertyChanged(nameof(OwnerContactsDisplay));
        OnPropertyChanged(nameof(OwnerBdRecordDisplay));
        OnPropertyChanged(nameof(ArchitectWarmthDisplay));
    }

    public PursuitBriefViewModel(
        IPursuitBriefStore store,
        IDeltekClientContextService deltek,
        IKorClientBdIntelligenceStore korBd)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _deltek = deltek ?? throw new ArgumentNullException(nameof(deltek));
        _korBd = korBd ?? throw new ArgumentNullException(nameof(korBd));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PursuitBrief? Brief
    {
        get => _brief;
        private set
        {
            if (Equals(_brief, value))
            {
                return;
            }

            _brief = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(HasProcurementProfile));
            OnPropertyChanged(nameof(NoProcurementProfile));
            OnPropertyChanged(nameof(KorEdgeDisplay));
            OnPropertyChanged(nameof(KorEdgeIsPlaceholder));
            OnPropertyChanged(nameof(OwnerContactsDisplay));
            OnPropertyChanged(nameof(OwnerContactsIsPlaceholder));
            OnPropertyChanged(nameof(OwnerBdRecordDisplay));
            OnPropertyChanged(nameof(OwnerBdRecordIsPlaceholder));
            OnPropertyChanged(nameof(ArchitectWarmthDisplay));
            OnPropertyChanged(nameof(ArchitectWarmthIsPlaceholder));
            OnPropertyChanged(nameof(ThePlayDisplay));
            OnPropertyChanged(nameof(ThePlayIsPlaceholder));
            OnPropertyChanged(nameof(FitScoreDisplay));
            OnPropertyChanged(nameof(FitScoreIsPlaceholder));
            OnPropertyChanged(nameof(SeatStatusDisplay));
            OnPropertyChanged(nameof(HasSeatPriority));
            OnPropertyChanged(nameof(SeatPriorityDisplay));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string ProjectName => Brief?.Project.ProjectName ?? "";

    public bool HasProcurementProfile
        => !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.ProcurementMethod)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.RosterProgram)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.EvaluationCriteria)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.BudgetCadence);

    public bool NoProcurementProfile => !HasProcurementProfile;

    public string KorEdgeDisplay => EvidenceDisplay(_korEdgeDisplay, _ownerRead,
        "No prior KOR work found in the retrieved Deltek records for this owner.");

    public bool KorEdgeIsPlaceholder => string.IsNullOrWhiteSpace(_korEdgeDisplay);

    public string OwnerContactsDisplay => EvidenceDisplay(_ownerContactsDisplay, _ownerRead,
        "No KOR contacts found in the retrieved Deltek records for this owner.");

    public bool OwnerContactsIsPlaceholder => string.IsNullOrWhiteSpace(_ownerContactsDisplay);

    public string OwnerBdRecordDisplay => EvidenceDisplay(_ownerBdRecordDisplay, _ownerBdRead,
        "No KOR pursuit history found in the retrieved records for this owner.");

    public bool OwnerBdRecordIsPlaceholder => string.IsNullOrWhiteSpace(_ownerBdRecordDisplay);

    public string ArchitectWarmthDisplay => EvidenceDisplay(_architectWarmthDisplay, _architectRead,
        "No KOR contacts found in the retrieved Deltek records for this firm.");

    public bool ArchitectWarmthIsPlaceholder => string.IsNullOrWhiteSpace(_architectWarmthDisplay);

    public string ThePlayDisplay => string.IsNullOrWhiteSpace(Brief?.ThePlay) ? "—" : Brief!.ThePlay!;

    public bool ThePlayIsPlaceholder => string.IsNullOrWhiteSpace(Brief?.ThePlay);

    public string FitScoreDisplay => Brief?.FitScore is { } score ? score.ToString("0.###") : "—";

    public bool FitScoreIsPlaceholder => Brief?.FitScore.HasValue != true;

    public string SeatStatusDisplay => string.IsNullOrWhiteSpace(Brief?.Architect.SeatStatus)
        ? "SEAT UNKNOWN"
        : Brief!.Architect.SeatStatus!;

    public bool HasSeatPriority => !string.IsNullOrWhiteSpace(Brief?.Architect.SeatPriority);

    public string SeatPriorityDisplay => Brief?.Architect.SeatPriority ?? "";

    /// <summary>The MajorProjectsInventory id this brief was loaded for —
    /// the lifecycle buttons (Own it / Not for us) act on it.</summary>
    public long LoadedMpiId { get; private set; }

    public async Task LoadAsync(long mpiId, CancellationToken ct)
    {
        try
        {
            LoadedMpiId = mpiId;
            StatusMessage = "Loading pursuit brief...";
            ClearDynamicDisplays();
            Brief = null;
            Brief = await _store.GetBriefForProjectAsync(mpiId, ct).ConfigureAwait(true);
            await LoadOwnerDeltekAsync(Brief, ct).ConfigureAwait(true);
            await LoadOwnerBdRecordAsync(Brief, ct).ConfigureAwait(true);
            await LoadArchitectWarmthAsync(Brief, ct).ConfigureAwait(true);
            StatusMessage = Brief is null
                ? $"Major Projects Inventory row {mpiId} was not found."
                : $"Loaded pursuit brief for {Brief.Project.ProjectName}." +
                  (_ownerRead != ReadState.Available || _ownerBdRead != ReadState.Available || _architectRead != ReadState.Available
                      ? " Some relationship context is unavailable or incomplete; see section notes." : "");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void ClearDynamicDisplays()
    {
        SetReadState(ref _ownerRead, ReadState.NotRetrieved);
        SetReadState(ref _ownerBdRead, ReadState.NotRetrieved);
        SetReadState(ref _architectRead, ReadState.NotRetrieved);
        KorEdgeRaw = null;
        OwnerContactsRaw = null;
        OwnerBdRecordRaw = null;
        ArchitectWarmthRaw = null;
    }

    private string? KorEdgeRaw
    {
        get => _korEdgeDisplay;
        set
        {
            if (SetField(ref _korEdgeDisplay, value, nameof(KorEdgeDisplay)))
            {
                OnPropertyChanged(nameof(KorEdgeIsPlaceholder));
            }
        }
    }

    private string? OwnerContactsRaw
    {
        get => _ownerContactsDisplay;
        set
        {
            if (SetField(ref _ownerContactsDisplay, value, nameof(OwnerContactsDisplay)))
            {
                OnPropertyChanged(nameof(OwnerContactsIsPlaceholder));
            }
        }
    }

    private string? OwnerBdRecordRaw
    {
        get => _ownerBdRecordDisplay;
        set
        {
            if (SetField(ref _ownerBdRecordDisplay, value, nameof(OwnerBdRecordDisplay)))
            {
                OnPropertyChanged(nameof(OwnerBdRecordIsPlaceholder));
            }
        }
    }

    private string? ArchitectWarmthRaw
    {
        get => _architectWarmthDisplay;
        set
        {
            if (SetField(ref _architectWarmthDisplay, value, nameof(ArchitectWarmthDisplay)))
            {
                OnPropertyChanged(nameof(ArchitectWarmthIsPlaceholder));
            }
        }
    }

    private async Task LoadOwnerDeltekAsync(PursuitBrief? brief, CancellationToken ct)
    {
        var ownerClendorClientId = brief?.OwnerClendorClientId?.Trim();
        if (string.IsNullOrWhiteSpace(ownerClendorClientId))
        {
            SetReadState(ref _ownerRead, ReadState.Unlinked);
            return;
        }

        try
        {
            var intelligence = await _deltek.LoadAsync(ownerClendorClientId, ct).ConfigureAwait(true);
            SetReadState(ref _ownerRead, intelligence is null ? ReadState.Unavailable :
                intelligence.HasDegradedSections ? ReadState.Partial : ReadState.Available);
            KorEdgeRaw = BuildKorEdgeDisplay(intelligence);
            OwnerContactsRaw = BuildOwnerContactsDisplay(intelligence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetReadState(ref _ownerRead, ReadState.Unavailable);
            KorEdgeRaw = null;
            OwnerContactsRaw = null;
        }
    }

    private async Task LoadOwnerBdRecordAsync(PursuitBrief? brief, CancellationToken ct)
    {
        var ownerClendorClientId = brief?.OwnerClendorClientId?.Trim();
        if (string.IsNullOrWhiteSpace(ownerClendorClientId))
        {
            SetReadState(ref _ownerBdRead, ReadState.Unlinked);
            return;
        }

        try
        {
            var bd = await _korBd.LoadByClendorIdAsync(ownerClendorClientId, ct).ConfigureAwait(true);
            SetReadState(ref _ownerBdRead, bd is null ? ReadState.Unavailable : ReadState.Available);
            OwnerBdRecordRaw = BuildOwnerBdRecordDisplay(bd);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetReadState(ref _ownerBdRead, ReadState.Unavailable);
            OwnerBdRecordRaw = null;
        }
    }

    private async Task LoadArchitectWarmthAsync(PursuitBrief? brief, CancellationToken ct)
    {
        var architectClendorClientId = brief?.ArchitectClendorClientId?.Trim();
        if (string.IsNullOrWhiteSpace(architectClendorClientId))
        {
            SetReadState(ref _architectRead, ReadState.Unlinked);
            return;
        }

        try
        {
            var intelligence = await _deltek.LoadAsync(architectClendorClientId, ct).ConfigureAwait(true);
            SetReadState(ref _architectRead, intelligence is null ? ReadState.Unavailable :
                intelligence.HasDegradedSections ? ReadState.Partial : ReadState.Available);
            ArchitectWarmthRaw = BuildArchitectWarmthDisplay(intelligence, brief?.Architect.ArchitectName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetReadState(ref _architectRead, ReadState.Unavailable);
            ArchitectWarmthRaw = null;
        }
    }

    private static string? BuildKorEdgeDisplay(DeltekClientIntelligence? intelligence)
    {
        if (intelligence is null)
        {
            return null;
        }

        var flags = new List<string>();
        if (intelligence.Company?.PriorWork == true)
        {
            flags.Add("Prior work");
        }

        if (intelligence.Company?.Recommend == true)
        {
            flags.Add("Recommended");
        }

        if (intelligence.ProjectCount <= 0
            && intelligence.LifetimeFee <= 0m
            && !intelligence.LatestProjectStart.HasValue
            && flags.Count == 0)
        {
            return null;
        }

        var parts = new List<string>
        {
            $"KOR client: {intelligence.ProjectCount:N0} projects",
            $"{intelligence.LifetimeFee.ToString("C0", CanadianCulture)} lifetime",
        };

        if (intelligence.LatestProjectStart.HasValue)
        {
            parts.Add($"last active {intelligence.LatestProjectStart.Value:yyyy}");
        }

        parts.AddRange(flags);
        return string.Join(" - ", parts);
    }

    private static string? BuildOwnerContactsDisplay(DeltekClientIntelligence? intelligence)
    {
        var contacts = OrderedContacts(intelligence?.Contacts)
            .Take(5)
            .Select(FormatOwnerContact)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return contacts.Length == 0 ? null : string.Join(Environment.NewLine, contacts);
    }

    private static string? BuildOwnerBdRecordDisplay(ClientBdIntelligence? bd)
    {
        if (bd is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (bd.Pursuits.TotalCount > 0)
        {
            var pursuitSummary = "KOR pursuits with this owner: "
                + $"{bd.Pursuits.WonCount:N0} won - "
                + $"{bd.Pursuits.LostCount:N0} lost - "
                + $"{bd.Pursuits.TotalCount:N0} total";
            parts.Add(
                pursuitSummary);
        }

        if (bd.ExternalActivity.AwardCount > 0)
        {
            var awardsSummary = "this owner has awarded "
                + $"{bd.ExternalActivity.AwardCount:N0} contracts to "
                + $"{bd.ExternalActivity.DistinctVendorsAwarded:N0} vendors";
            parts.Add(
                awardsSummary);
        }

        return parts.Count == 0 ? null : string.Join(" - ", parts);
    }

    private static string? BuildArchitectWarmthDisplay(DeltekClientIntelligence? intelligence, string? architectName)
    {
        if (intelligence is null || intelligence.Contacts.Count == 0)
        {
            return null;
        }

        var firm = string.IsNullOrWhiteSpace(architectName) ? intelligence.ClientName : architectName.Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"KOR knows {intelligence.Contacts.Count:N0} people at {firm}");
        foreach (var contact in OrderedContacts(intelligence.Contacts).Take(12))
        {
            var name = $"{contact.FirstName} {contact.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(contact.Title) ? "" : $"  {contact.Title}";
            sb.AppendLine($"- {name}{title}");
        }

        return sb.ToString().Trim();
    }

    private static IEnumerable<DeltekContactSummary> OrderedContacts(IReadOnlyList<DeltekContactSummary>? contacts)
        => (contacts ?? Array.Empty<DeltekContactSummary>())
            .OrderBy(c => string.Equals(c.SourceFlag, "Direct", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(c => c.MostRecentProjectYear ?? 0)
            .ThenBy(c => c.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase);

    private static string FormatOwnerContact(DeltekContactSummary contact)
    {
        var name = $"{contact.FirstName} {contact.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var title = string.IsNullOrWhiteSpace(contact.Title) ? "" : $"  {contact.Title}";
        return $"{name}{title} ({OwnerContactTag(contact)})";
    }

    private static string OwnerContactTag(DeltekContactSummary contact)
    {
        if (string.Equals(contact.SourceFlag, "Direct", StringComparison.OrdinalIgnoreCase))
        {
            return "Direct";
        }

        return contact.MostRecentProjectYear is int year && year > 0
            ? $"via projects {year}"
            : "via projects";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
