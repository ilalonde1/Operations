#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// ViewModel behind <c>OverwatchView</c> — the manager board of every owned,
/// active pursuit with its staleness signal, coldest-first. This read-only
/// slice surfaces who-owns-what and what is going cold; one-click reassign
/// lands as a follow-up.
/// </summary>
public sealed class OverwatchViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Pursuit Overwatch (BD)";

    private const int ColdDays = 21;
    private const int WatchDays = 7;

    private readonly IPursuitOverwatchStore _store;

    private OverwatchRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _isReassigning;

    private OverwatchRowView[] _contextSnapshot = Array.Empty<OverwatchRowView>();

    public OverwatchViewModel(IPursuitOverwatchStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Owned active pursuits, coldest-first.</summary>
    public ObservableCollection<OverwatchRowView> Pursuits { get; } = new();

    public OverwatchRowView? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanReassign));
            }
        }
    }

    public bool CanReassign => _selected is not null && !_isReassigning;

    /// <summary>Distinct owners currently holding pursuits — the reassign target
    /// candidates (the editable picker also accepts a new owner).</summary>
    public IReadOnlyList<string> KnownOwners =>
        Pursuits.Select(p => p.OwnerDisplay)
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
                .ToList();

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

    public bool HasPursuits => Pursuits.Count > 0;

    public string Headline
    {
        get
        {
            if (Pursuits.Count == 0) return string.Empty;
            var cold = Pursuits.Count(p => p.DaysSinceTouch >= ColdDays);
            var watch = Pursuits.Count(p => p.DaysSinceTouch >= WatchDays && p.DaysSinceTouch < ColdDays);
            return $"{Pursuits.Count} active pursuit{(Pursuits.Count == 1 ? "" : "s")}"
                 + $"  •  {cold} going cold ({ColdDays}d+)  •  {watch} to watch";
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading pursuits…";
        try
        {
            var rows = await _store.ListAsync(ct).ConfigureAwait(true);

            var preservedId = Selected?.EngagementId;
            Pursuits.Clear();
            foreach (var r in rows)
            {
                Pursuits.Add(new OverwatchRowView(r));
            }
            _contextSnapshot = Pursuits.ToArray();

            Selected = preservedId.HasValue
                ? Pursuits.FirstOrDefault(p => p.EngagementId == preservedId.Value)
                : null;

            OnPropertyChanged(nameof(HasPursuits));
            OnPropertyChanged(nameof(Headline));
            StatusMessage = Pursuits.Count == 0
                ? "No owned active pursuits."
                : $"Loaded {Pursuits.Count} owned active pursuits.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Reassign <paramref name="row"/> to <paramref name="toOwner"/> (acting as
    /// <paramref name="byStaff"/>). On success the board reloads so the new owner
    /// + sort order are reflected. Re-entrancy-guarded.
    /// </summary>
    public async Task<ReassignOutcome> ReassignAsync(
        OverwatchRowView row, string toOwner, string byStaff, string? reason, CancellationToken ct)
    {
        if (_isReassigning)
        {
            return ReassignOutcome.OwnerChanged;
        }

        _isReassigning = true;
        OnPropertyChanged(nameof(CanReassign));
        try
        {
            var outcome = await _store.ReassignAsync(
                row.EngagementId, row.OpportunityId, row.OwnerDisplay, toOwner, byStaff, reason, ct)
                .ConfigureAwait(true);

            if (outcome == ReassignOutcome.Reassigned)
            {
                // Owner changed → reload so the board re-sorts and the AI snapshot
                // refreshes. Set the status AFTER, so LoadAsync's "Loaded N…"
                // doesn't clobber the reassign confirmation.
                await LoadAsync(ct).ConfigureAwait(true);
            }

            StatusMessage = outcome switch
            {
                ReassignOutcome.Reassigned => $"Reassigned “{row.ProjectName}” from {row.OwnerDisplay} to {toOwner}.",
                ReassignOutcome.OwnerChanged => $"“{row.ProjectName}” was already moved by someone else — refresh to see who owns it.",
                ReassignOutcome.DuplicateForTarget => $"{toOwner} already holds a pursuit for this buyer/region — not moved.",
                _ => StatusMessage,
            };
            return outcome;
        }
        finally
        {
            _isReassigning = false;
            OnPropertyChanged(nameof(CanReassign));
        }
    }

    // ===== IAiContextProvider =====

    public string ProviderName => Provider;

    public bool HasData => _contextSnapshot.Length > 0;

    public string BuildContext()
    {
        var pool = _contextSnapshot;
        if (pool.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Owned active pursuits (manager overwatch): {pool.Length}.");

        var byOwner = pool.GroupBy(p => p.OwnerDisplay).OrderByDescending(g => g.Count());
        sb.AppendLine("By owner:");
        foreach (var g in byOwner)
        {
            var cold = g.Count(p => p.DaysSinceTouch >= ColdDays);
            sb.AppendLine($"  {g.Key}: {g.Count()} ({cold} cold)");
        }

        var coldest = pool.Where(p => p.DaysSinceTouch >= ColdDays)
            .OrderByDescending(p => p.DaysSinceTouch)
            .Take(15)
            .ToList();
        if (coldest.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Going cold ({ColdDays}d+ since last activity):");
            foreach (var p in coldest)
            {
                sb.AppendLine($"  {p.ProjectName} — owner {p.OwnerDisplay}, {p.DaysSinceTouch}d since last touch");
            }
        }

        return sb.ToString();
    }

    public string BuildLocalContext() => string.Empty;
}
