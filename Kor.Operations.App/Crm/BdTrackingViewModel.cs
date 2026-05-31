#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Crm;

/// <summary>
/// ViewModel behind <c>BdTrackingView</c>. Surfaces the BD-tracking spreadsheet
/// engagements (CrmEngagements where OpportunityId IS NULL and Region IS NOT NULL)
/// in the same shape as the original spreadsheet: region tabs / per-initiator
/// filter / rollup card with Activities / $Submitted / $Accepted / Capture
/// Rate. Read-only; the importer is the source of truth.
/// </summary>
public sealed class BdTrackingViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    public const string AllRegions = "All Regions";
    public const string AllInitiators = "All";

    public static readonly IReadOnlyList<string> Regions = new[]
    {
        AllRegions,
        "Vancouver/LowerMainland",
        "VancouverIsland",
        "Alberta",
        "Okanagan-BcInterior",
        "USA",
        "EasternCanada",
    };

    public static readonly IReadOnlyList<string> InitiatorOptions = new[]
    {
        AllInitiators,
        "Omar",
        "Conor",
        "Islam",
        "Jim",
        "Rory",
        "John",
        "Ian",
    };

    private readonly string _connectionString;
    private readonly Microsoft.Extensions.Logging.ILogger<BdTrackingViewModel>? _logger;
    private string _selectedRegion = AllRegions;
    private string _selectedInitiator = AllInitiators;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private BdTrackingEngagementRow? _selected;
    private CancellationTokenSource? _detailCts;

    public BdTrackingViewModel(string connectionString, Microsoft.Extensions.Logging.ILogger<BdTrackingViewModel>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bindable copies of the static option lists so XAML DataContext=VM works through.</summary>
    public IReadOnlyList<string> RegionOptions => Regions;
    public IReadOnlyList<string> InitiatorOptionsList => InitiatorOptions;

    /// <summary>All loaded rows. Filtered for display via FilteredEngagements.</summary>
    public ObservableCollection<BdTrackingEngagementRow> Engagements { get; } = new();

    // Round 37c (T3.002): materialize the filtered set into a stable
    // ObservableCollection so XAML bindings don't recompute the LINQ query on
    // every read and the grid's selection / scroll position survive filter
    // toggles. RefreshFiltered() is the single mutator.
    private readonly ObservableCollection<BdTrackingEngagementRow> _filteredEngagements = new();
    public ObservableCollection<BdTrackingEngagementRow> FilteredEngagements => _filteredEngagements;

    public string SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (SetField(ref _selectedRegion, value ?? AllRegions))
            {
                RefreshFiltered();
                OnPropertyChanged(nameof(Rollup));
                UpdateFilteredStatus();
            }
        }
    }

    public string SelectedInitiator
    {
        get => _selectedInitiator;
        set
        {
            if (SetField(ref _selectedInitiator, value ?? AllInitiators))
            {
                RefreshFiltered();
                OnPropertyChanged(nameof(Rollup));
                UpdateFilteredStatus();
            }
        }
    }

    private void RefreshFiltered()
    {
        _filteredEngagements.Clear();
        foreach (var r in Engagements.Where(r =>
            (SelectedRegion == AllRegions || string.Equals(r.Region, SelectedRegion, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedInitiator == AllInitiators || string.Equals(r.Initiator, SelectedInitiator, StringComparison.OrdinalIgnoreCase))))
        {
            _filteredEngagements.Add(r);
        }
    }

    // Round 37c (T6.001): the status line used to say "Loaded N..." forever,
    // even after the user changed region/initiator. Switch it to "Showing
    // X of N..." once a filter is active so the count matches what the grid
    // is actually displaying.
    private void UpdateFilteredStatus()
    {
        var total = Engagements.Count;
        var shown = _filteredEngagements.Count;
        StatusMessage = (SelectedRegion == AllRegions && SelectedInitiator == AllInitiators)
            ? $"Showing all {total:N0} BD-tracking engagements."
            : $"Showing {shown:N0} of {total:N0} BD-tracking engagements (region={SelectedRegion}, initiator={SelectedInitiator}).";
    }

    public BdTrackingEngagementRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                // Per-selection CTS so a slow detail load doesn't stomp a newer
                // selection's results (mirrors the T2.001 audit fix on CrmViewModel).
                var prev = _detailCts;
                _detailCts = new CancellationTokenSource();
                prev?.Cancel();
                prev?.Dispose();
                _ = LoadDetailAsync(_detailCts.Token);
                OnPropertyChanged(nameof(HasSelected));
            }
        }
    }

    public bool HasSelected => _selected is not null;

    /// <summary>Activities for the currently selected engagement, most-recent-first.</summary>
    public ObservableCollection<BdTrackingActivityRow> SelectedActivities { get; } = new();

    /// <summary>Contacts for the currently selected engagement.</summary>
    public ObservableCollection<BdTrackingContactRow> SelectedContacts { get; } = new();

    /// <summary>Cross-linked MPI projects for the currently selected engagement.</summary>
    public ObservableCollection<BdTrackingLinkedProjectRow> SelectedLinkedProjects { get; } = new();

    public BdTrackingRollup Rollup
    {
        get
        {
            var filtered = FilteredEngagements.ToList();
            var count = filtered.Count;
            var submitted = filtered.Sum(r => r.ProposalsSubmittedCad ?? 0m);
            var accepted = filtered.Sum(r => r.ProposalsAcceptedCad ?? 0m);
            var captureRate = submitted == 0 ? 0 : (decimal)accepted / submitted;
            return new BdTrackingRollup(count, submitted, accepted, captureRate);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading BD tracking…";
        try
        {
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync(ct).ConfigureAwait(true);

            // Load engagement rows joined to canonical buyer + activity counts.
            // Round 37c (T3.001): collapsed 4 correlated subqueries to OUTER
            // APPLYs. SQL Server's optimizer fuses these into seek-based joins
            // and walks CrmActivities once per engagement instead of four
            // times. The ordinal contract (Id, OwnerStaffId, Region, ...,
            // LatestActivityType) is preserved so the reader code below
            // doesn't shift.
            const string sql = @"
SELECT  e.Id, e.OwnerStaffId, e.Region,
        e.BuyerCanonicalOrgId,
        ISNULL(co.DisplayName, N'(unknown)') AS BuyerDisplayName,
        e.ProposalsSubmittedCad, e.ProposalsAcceptedCad,
        e.PotentialProjects,
        e.OpenedAtUtc, e.UpdatedAtUtc, e.Stage,
        ISNULL(ag.ActivityCount, 0) AS ActivityCount,
        latest.OccurredAtUtc        AS LatestActivityUtc,
        cx.PrimaryContact           AS PrimaryContact,
        latest.ActivityType         AS LatestActivityType
FROM    opportunities.CrmEngagements e
LEFT    JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId
OUTER APPLY (
    SELECT  COUNT(*) AS ActivityCount
    FROM    opportunities.CrmActivities a
    WHERE   a.EngagementId = e.Id
) ag
OUTER APPLY (
    SELECT TOP 1 a.OccurredAtUtc, a.ActivityType
    FROM   opportunities.CrmActivities a
    WHERE  a.EngagementId = e.Id
    ORDER  BY a.OccurredAtUtc DESC
) latest
OUTER APPLY (
    SELECT TOP 1 c.DisplayName AS PrimaryContact
    FROM   opportunities.CrmContacts c
    WHERE  c.EngagementId = e.Id
    ORDER  BY c.Id
) cx
WHERE   e.OpportunityId IS NULL
  AND   e.Region IS NOT NULL
ORDER   BY e.Region, e.OwnerStaffId, BuyerDisplayName;";

            var rows = new List<BdTrackingEngagementRow>();
            await using var cmd = new SqlCommand(sql, con);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(true);
            while (await r.ReadAsync(ct).ConfigureAwait(true))
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(new BdTrackingEngagementRow
                {
                    Id = r.GetInt64(0),
                    Initiator = r.IsDBNull(1) ? "(unknown)" : r.GetString(1),
                    Region = r.GetString(2),
                    BuyerCanonicalOrgId = r.IsDBNull(3) ? null : r.GetInt64(3),
                    BuyerDisplayName = r.GetString(4),
                    ProposalsSubmittedCad = r.IsDBNull(5) ? null : r.GetDecimal(5),
                    ProposalsAcceptedCad = r.IsDBNull(6) ? null : r.GetDecimal(6),
                    PotentialProjects = r.IsDBNull(7) ? null : r.GetString(7),
                    OpenedAtUtc = r.GetDateTimeOffset(8),
                    UpdatedAtUtc = r.GetDateTimeOffset(9),
                    Stage = r.GetInt32(10),
                    ActivityCount = r.GetInt32(11),
                    LatestActivityUtc = r.IsDBNull(12) ? null : r.GetDateTimeOffset(12),
                    PrimaryContact = r.IsDBNull(13) ? null : r.GetString(13),
                    LatestActivityType = r.IsDBNull(14) ? null : r.GetInt32(14),
                });
            }

            Engagements.Clear();
            foreach (var row in rows) Engagements.Add(row);

            // Round 37c: materialize the filtered view exactly once per load;
            // setters then mutate it directly via RefreshFiltered.
            RefreshFiltered();
            OnPropertyChanged(nameof(Rollup));
            UpdateFilteredStatus();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Round 37b (T4.002): the main load is the most diagnostic-worthy
            // event in this VM (gate to everything BdTracking surfaces). The
            // status line was the only signal before — now logger gets the
            // stack too so we can triage from logs without UI repro.
            _logger?.LogError(ex, "BdTracking LoadAsync failed.");
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads activities + contacts + linked MPI projects for the currently
    /// selected engagement. Cancellable + per-selection-scoped so stale loads
    /// don't overwrite fresher state.
    /// </summary>
    private async Task LoadDetailAsync(CancellationToken ct)
    {
        SelectedActivities.Clear();
        SelectedContacts.Clear();
        SelectedLinkedProjects.Clear();
        if (Selected is not { } row) return;

        var capturedId = row.Id;
        try
        {
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;

            // Activities, most-recent-first.
            const string actSql = @"
SELECT a.Id, a.ActivityType, a.OccurredAtUtc, a.Subject, a.Body, a.CreatedBy,
       c.DisplayName AS ContactName
FROM   opportunities.CrmActivities a
LEFT   JOIN opportunities.CrmContacts c ON c.Id = a.ContactId
WHERE  a.EngagementId = @eid
ORDER  BY a.OccurredAtUtc DESC, a.Id DESC;";
            await using (var cmd = new SqlCommand(actSql, con))
            {
                cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = capturedId;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(true);
                while (await r.ReadAsync(ct).ConfigureAwait(true))
                {
                    if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;
                    SelectedActivities.Add(new BdTrackingActivityRow
                    {
                        Id = r.GetInt64(0),
                        ActivityType = r.GetInt32(1),
                        OccurredAtUtc = r.GetDateTimeOffset(2),
                        Subject = r.GetString(3),
                        Body = r.IsDBNull(4) ? null : r.GetString(4),
                        CreatedBy = r.GetString(5),
                        ContactName = r.IsDBNull(6) ? null : r.GetString(6),
                    });
                }
            }

            if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;

            // Contacts.
            const string ctcSql = @"
SELECT Id, DisplayName, Role, Email, Phone, IsPrimary
FROM   opportunities.CrmContacts
WHERE  EngagementId = @eid
ORDER  BY IsPrimary DESC, DisplayName;";
            await using (var cmd = new SqlCommand(ctcSql, con))
            {
                cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = capturedId;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(true);
                while (await r.ReadAsync(ct).ConfigureAwait(true))
                {
                    if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;
                    SelectedContacts.Add(new BdTrackingContactRow
                    {
                        Id = r.GetInt64(0),
                        DisplayName = r.GetString(1),
                        Role = r.IsDBNull(2) ? null : r.GetString(2),
                        Email = r.IsDBNull(3) ? null : r.GetString(3),
                        Phone = r.IsDBNull(4) ? null : r.GetString(4),
                        IsPrimary = r.GetBoolean(5),
                    });
                }
            }

            if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;

            // Cross-linked MPI projects from Round 36a.
            // Round 37b (T1.005): exclude rows the nightly DataRetirementJob
            // has retired — those are archive, not live BD targets.
            const string linkSql = @"
SELECT l.MajorProjectsInventoryId, m.ProjectName, m.Province, m.Stage, l.Confidence, l.MatchedText
FROM   opportunities.CrmEngagementProjectLink l
JOIN   opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
WHERE  l.EngagementId = @eid
  AND  m.RetiredAtUtc IS NULL
ORDER  BY l.Confidence DESC;";
            await using (var cmd = new SqlCommand(linkSql, con))
            {
                cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = capturedId;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(true);
                while (await r.ReadAsync(ct).ConfigureAwait(true))
                {
                    if (ct.IsCancellationRequested || Selected?.Id != capturedId) return;
                    SelectedLinkedProjects.Add(new BdTrackingLinkedProjectRow
                    {
                        MpiId = r.GetInt64(0),
                        ProjectName = r.GetString(1),
                        Province = r.IsDBNull(2) ? null : r.GetString(2),
                        Stage = r.IsDBNull(3) ? null : r.GetString(3),
                        Confidence = r.GetDecimal(4),
                        MatchedText = r.IsDBNull(5) ? null : r.GetString(5),
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BdTracking detail load failed for engagement {EngagementId}.", capturedId);
        }
    }

    // ===== IAiContextProvider =====
    public string ProviderName => "BD Tracking";
    public bool HasData => Engagements.Count > 0;
    public string BuildContext()
    {
        var roll = Rollup;
        return $"BD Tracking — {Engagements.Count:N0} engagements across {Engagements.Select(r => r.Region).Distinct().Count()} regions; currently filtered to {SelectedRegion} / initiator={SelectedInitiator}; rollup: {roll.ActivityCount} engagements, ${roll.ProposalsSubmittedCad:N0} submitted, ${roll.ProposalsAcceptedCad:N0} accepted, capture rate {roll.CaptureRate:P0}.";
    }
    public string BuildLocalContext()
    {
        if (Selected is null) return BuildContext();
        return $"{BuildContext()} Selected: {Selected.BuyerDisplayName} (initiator={Selected.Initiator}, region={Selected.Region}, activities={Selected.ActivityCount}); potential projects: {Selected.PotentialProjects ?? "(none)"}.";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class BdTrackingEngagementRow
{
    public long Id { get; init; }
    public string Initiator { get; init; } = "";
    public string Region { get; init; } = "";
    public long? BuyerCanonicalOrgId { get; init; }
    public string BuyerDisplayName { get; init; } = "";
    public decimal? ProposalsSubmittedCad { get; init; }
    public decimal? ProposalsAcceptedCad { get; init; }
    public string? PotentialProjects { get; init; }
    public DateTimeOffset OpenedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public int Stage { get; init; }
    public int ActivityCount { get; init; }
    public DateTimeOffset? LatestActivityUtc { get; init; }
    public string? PrimaryContact { get; init; }
    public int? LatestActivityType { get; init; }

    public string LatestActivityDisplay =>
        LatestActivityUtc.HasValue ? LatestActivityUtc.Value.LocalDateTime.ToString("yyyy-MM-dd") : "—";
    public string SubmittedDisplay =>
        ProposalsSubmittedCad.HasValue ? ProposalsSubmittedCad.Value.ToString("C0", CultureInfo.GetCultureInfo("en-CA")) : "—";
    public string AcceptedDisplay =>
        ProposalsAcceptedCad.HasValue ? ProposalsAcceptedCad.Value.ToString("C0", CultureInfo.GetCultureInfo("en-CA")) : "—";
    public string LatestActivityTypeDisplay => LatestActivityType switch
    {
        2 => "Phone",
        3 => "Meeting",
        4 => "Email",
        6 => "RFP",
        99 => "Other",
        1 => "Note",
        _ => "—",
    };
}

public sealed record BdTrackingRollup(int ActivityCount, decimal ProposalsSubmittedCad, decimal ProposalsAcceptedCad, decimal CaptureRate)
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");
    public string SubmittedDisplay => ProposalsSubmittedCad.ToString("C0", CanadianCulture);
    public string AcceptedDisplay => ProposalsAcceptedCad.ToString("C0", CanadianCulture);
    public string CaptureRateDisplay => CaptureRate.ToString("P1", CanadianCulture);
}

/// <summary>One CrmActivity row, projected for the BdTrackingView detail panel.</summary>
public sealed class BdTrackingActivityRow
{
    public long Id { get; init; }
    public int ActivityType { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string Subject { get; init; } = "";
    public string? Body { get; init; }
    public string CreatedBy { get; init; } = "";
    public string? ContactName { get; init; }

    public string OccurredDisplay => OccurredAtUtc.LocalDateTime.ToString("yyyy-MM-dd");
    public string TypeDisplay => ActivityType switch
    {
        1 => "Note",
        2 => "Phone",
        3 => "Meeting",
        4 => "Email",
        6 => "RFP",
        99 => "Other",
        _ => "—",
    };
}

/// <summary>One CrmContact row, projected for the BdTrackingView detail panel.</summary>
public sealed class BdTrackingContactRow
{
    public long Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Role { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>One cross-linked MajorProjectsInventory row, from CrmEngagementProjectLink (Round 36a).</summary>
public sealed class BdTrackingLinkedProjectRow
{
    public long MpiId { get; init; }
    public string ProjectName { get; init; } = "";
    public string? Province { get; init; }
    public string? Stage { get; init; }
    public decimal Confidence { get; init; }
    public string? MatchedText { get; init; }

    public string ConfidenceDisplay => $"{Confidence:F0}%";
}
