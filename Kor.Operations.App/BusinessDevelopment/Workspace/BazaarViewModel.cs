#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// ViewModel behind <c>BazaarView</c> — the opportunity "clearing house".
/// Shows the pool of un-claimed opportunities (<see cref="OpportunityStatus.New"/>
/// with no owner) ranked by relevance, ready to be grabbed into an owned
/// pursuit. This read-only slice surfaces the pool; the guarded Grab
/// transaction lands in the next step.
/// </summary>
public sealed class BazaarViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Opportunity Bazaar (BD)";

    private readonly IOpportunityStore _opportunityStore;
    private readonly IPursuitGrabStore _grabStore;
    private readonly Kor.Opportunities.Data.MajorProjects.IPursuitLifecycleStore _lifecycleStore;

    private OpportunityRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _isGrabbing;

    // Immutable snapshot of the loaded pool, swapped on the UI thread after each
    // load. BuildContext()/HasData run on AppAiContextBuilder's worker thread and
    // read this reference instead of enumerating the live ObservableCollection.
    private OpportunityRowView[] _contextSnapshot = Array.Empty<OpportunityRowView>();

    public BazaarViewModel(
        IOpportunityStore opportunityStore,
        IPursuitGrabStore grabStore,
        Kor.Opportunities.Data.MajorProjects.IPursuitLifecycleStore lifecycleStore)
    {
        _opportunityStore = opportunityStore ?? throw new ArgumentNullException(nameof(opportunityStore));
        _grabStore = grabStore ?? throw new ArgumentNullException(nameof(grabStore));
        _lifecycleStore = lifecycleStore ?? throw new ArgumentNullException(nameof(lifecycleStore));
    }

    /// <summary>The un-claimed opportunity pool, ranked best-first.</summary>
    public ObservableCollection<OpportunityRowView> Grabbable { get; } = new();

    /// <summary>The row selected in the grid; the Grab action targets it.</summary>
    public OpportunityRowView? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanGrab));
            }
        }
    }

    public bool CanGrab => _selected is not null && !_isGrabbing;

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

    public bool HasGrabbable => Grabbable.Count > 0;

    /// <summary>One-line headline above the list: total + relevance breakdown.</summary>
    public string Headline
    {
        get
        {
            if (Grabbable.Count == 0) return string.Empty;
            var high = Grabbable.Count(r => r.Model.RelevanceTier == RelevanceTier.High);
            var medium = Grabbable.Count(r => r.Model.RelevanceTier == RelevanceTier.Medium);
            return $"{Grabbable.Count} opportunit{(Grabbable.Count == 1 ? "y" : "ies")} available to grab"
                 + $"  •  {high} High  •  {medium} Medium relevance";
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading opportunities to grab…";
        try
        {
            // Reuse the tested list path (excludes closed Won/Lost), then keep
            // only the grabbable pool: still New, not yet owned, not dismissed
            // ("not for us", migration 284 — client mirror of
            // vw_ActionableOpportunities). The default view also drops expired
            // tenders, notification digests and same-tender duplicates before
            // ranking by relevance and earliest live deadline.
            var all = await _opportunityStore.ListAsync(ct, includeClosed: false).ConfigureAwait(true);
            var grabbable = BazaarOpportunitySelector.SelectDefaultView(all, DateTimeOffset.UtcNow);

            Grabbable.Clear();
            foreach (var o in grabbable)
            {
                Grabbable.Add(new OpportunityRowView(o));
            }
            // Publish the immutable snapshot on the UI thread for the AI builder.
            _contextSnapshot = Grabbable.ToArray();

            OnPropertyChanged(nameof(HasGrabbable));
            OnPropertyChanged(nameof(Headline));
            StatusMessage = Grabbable.Count == 0
                ? "No un-claimed opportunities right now."
                : $"Loaded {Grabbable.Count} un-claimed opportunities.";
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
    /// On-demand buyer-org lookup for the dossier affordance (plan 1.4): the
    /// pool rows are domain Opportunities, which deliberately don't carry
    /// BuyerCanonicalOrgId — one lean read resolves it when the user asks.
    /// </summary>
    public Task<long?> GetBuyerCanonicalOrgIdAsync(OpportunityRowView row, CancellationToken ct)
        => _opportunityStore.GetBuyerCanonicalOrgIdAsync(row.Id, ct);

    /// <summary>
    /// Claim <paramref name="row"/> for <paramref name="staffUpn"/> via the
    /// guarded grab transaction. On success the row leaves the Bazaar (it is no
    /// longer New/un-owned) and becomes a Drafting pursuit in the CRM (the
    /// result carries the new engagement id so the view can deep-link straight
    /// to it — plan 1.4). If it was taken by someone else first, it is removed
    /// too (it has left the pool).
    /// </summary>
    public async Task<GrabResult> GrabAsync(OpportunityRowView row, string staffUpn, CancellationToken ct)
    {
        if (_isGrabbing)
        {
            // A grab is already in flight; drop the re-entrant click. Ignored —
            // not AlreadyTaken — so the view doesn't tell the user someone else
            // claimed it (audit 2026-07-01 n6: the old sentinel message lied).
            return new GrabResult(GrabOutcome.Ignored, 0);
        }

        _isGrabbing = true;
        OnPropertyChanged(nameof(CanGrab));
        try
        {
            var result = await _grabStore.GrabAsync(row.Id, staffUpn, ct).ConfigureAwait(true);

            // Either way the opportunity has left the New/un-owned pool — drop it.
            Grabbable.Remove(row);
            if (ReferenceEquals(_selected, row))
            {
                Selected = null;
            }
            _contextSnapshot = Grabbable.ToArray();

            StatusMessage = result.Outcome == GrabOutcome.Grabbed
                ? $"Grabbed “{row.Name}” — now a Drafting pursuit in your Pursuits list."
                : $"“{row.Name}” was already taken — removed from the Bazaar.";

            OnPropertyChanged(nameof(HasGrabbable));
            OnPropertyChanged(nameof(Headline));
            return result;
        }
        finally
        {
            _isGrabbing = false;
            OnPropertyChanged(nameof(CanGrab));
        }
    }

    /// <summary>
    /// "Not for us" (migration 284): audited dismissal with a reason. The row
    /// leaves the pool everywhere (Bazaar, weekly sheet, digests) but is never
    /// deleted — admins see it under Removed and can restore it.
    /// </summary>
    public async Task<bool> DismissAsync(OpportunityRowView row, string staffUpn, string reason, CancellationToken ct)
    {
        var outcome = await _lifecycleStore
            .DismissOpportunityAsync(row.Id, staffUpn, reason, ct).ConfigureAwait(true);

        if (outcome == Kor.Opportunities.Data.MajorProjects.LifecycleOutcome.Applied)
        {
            Grabbable.Remove(row);
            if (ReferenceEquals(_selected, row))
            {
                Selected = null;
            }
            _contextSnapshot = Grabbable.ToArray();
            StatusMessage = $"Removed “{row.Name}” — not for us. Admins can restore it from the Opportunities board.";
            OnPropertyChanged(nameof(HasGrabbable));
            OnPropertyChanged(nameof(Headline));
            return true;
        }

        StatusMessage = $"“{row.Name}” changed under you (grabbed or already removed) — refresh.";
        return false;
    }

    // ===== IAiContextProvider =====

    public string ProviderName => Provider;

    public bool HasData => _contextSnapshot.Length > 0;

    public string BuildContext()
    {
        // Read the immutable snapshot published by LoadAsync on the UI thread;
        // BuildContext runs on the AI builder's worker thread.
        var pool = _contextSnapshot;
        if (pool.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Opportunity Bazaar (un-claimed pool, available to grab): {pool.Length}.");

        var high = pool.Count(r => r.Model.RelevanceTier == RelevanceTier.High);
        var medium = pool.Count(r => r.Model.RelevanceTier == RelevanceTier.Medium);
        sb.AppendLine($"By relevance: {high} High, {medium} Medium.");

        var top = pool.Take(15).ToList();
        sb.AppendLine("Top un-claimed opportunities (best-first):");
        foreach (var r in top)
        {
            var loc = string.IsNullOrWhiteSpace(r.LocationDisplay) ? "" : $" — {r.LocationDisplay}";
            var deadline = string.IsNullOrWhiteSpace(r.DeadlineDisplay) ? "" : $"; due {r.DeadlineDisplay}";
            sb.AppendLine($"  [{r.TierDisplay}] {r.Name} ({r.BuyerName}){loc}{deadline}");
        }

        return sb.ToString();
    }

    /// <summary>No per-row drill-down in the read-only Bazaar — the whole pool
    /// is already in <see cref="BuildContext"/>. Returns empty.</summary>
    public string BuildLocalContext() => string.Empty;
}
