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

    private string _statusMessage = "Ready.";
    private bool _isLoading;

    // Immutable snapshot of the loaded pool, swapped on the UI thread after each
    // load. BuildContext()/HasData run on AppAiContextBuilder's worker thread and
    // read this reference instead of enumerating the live ObservableCollection.
    private OpportunityRowView[] _contextSnapshot = Array.Empty<OpportunityRowView>();

    public BazaarViewModel(IOpportunityStore opportunityStore)
    {
        _opportunityStore = opportunityStore ?? throw new ArgumentNullException(nameof(opportunityStore));
    }

    /// <summary>The un-claimed opportunity pool, ranked best-first.</summary>
    public ObservableCollection<OpportunityRowView> Grabbable { get; } = new();

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
        StatusMessage = "Loading the Bazaar…";
        try
        {
            // Reuse the tested list path (excludes closed Won/Lost), then keep
            // only the grabbable pool: still New and not yet owned. Ranked by
            // relevance (scored first, highest score first), deadline as tiebreak.
            var all = await _opportunityStore.ListAsync(ct, includeClosed: false).ConfigureAwait(true);
            var grabbable = all
                .Where(o => o.Status == OpportunityStatus.New && string.IsNullOrWhiteSpace(o.OwnerStaffId))
                .OrderByDescending(o => o.RelevanceScore.HasValue)
                .ThenByDescending(o => o.RelevanceScore)
                .ThenByDescending(o => o.SubmissionDeadlineUtc ?? DateTimeOffset.MinValue);

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
