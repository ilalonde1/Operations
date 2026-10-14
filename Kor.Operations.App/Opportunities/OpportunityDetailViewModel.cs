#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Sources;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Read view of one RFP: the record, and every source posting behind it. Until
/// 2026-10-13 a double-click on an RFP anywhere in the BD workspace opened the
/// record EDITOR (eleven metadata fields, a Save button, and no posting, no link,
/// no description). This is what a double-click opens now; editing is a deliberate
/// second step from here.
///
/// The picking rules (which posting is primary, which description to show) live in
/// <see cref="Build"/> so they are testable without WPF.
/// </summary>
public sealed class OpportunityDetailViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    public sealed record SourceRow(string Source, string Title, string Posted, string Url);

    public sealed record Built(IReadOnlyList<SourceRow> Sources, string Description, string? PrimaryUrl);

    private static readonly CultureInfo Canadian = CultureInfo.GetCultureInfo("en-CA");

    private readonly IOpportunityObservationStore _observations;
    private readonly IOpportunitySourceStore _sources;
    private string _description = "Loading the source posting…";
    private string? _primaryUrl;
    private string _statusText = "";

    public OpportunityDetailViewModel(
        Opportunity opportunity,
        IOpportunityObservationStore observations,
        IOpportunitySourceStore sources)
    {
        Opportunity = opportunity ?? throw new ArgumentNullException(nameof(opportunity));
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public Opportunity Opportunity { get; }

    public string Name => Opportunity.Name;
    public string BuyerName => Opportunity.BuyerName;
    public string Key => Opportunity.OpportunityKey;

    public string StatusDisplay => Opportunity.DismissedAtUtc is null
        ? Opportunity.Status.ToString()
        : $"{Opportunity.Status} — dismissed";

    public string OwnerDisplay => string.IsNullOrWhiteSpace(Opportunity.OwnerStaffId) ? "unowned" : Opportunity.OwnerStaffId!;

    public string ClosesDisplay => OpportunityDeadlineDisplay.Format(Opportunity.SubmissionDeadlineUtc, DateTimeOffset.UtcNow);

    public string PostedDisplay => Opportunity.RfpReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not recorded";

    public string ValueDisplay => Opportunity.EstimatedValue is { } v
        ? string.Format(Canadian, "{0:N0} {1}", v, Opportunity.EstimatedValueCurrency)
        : "not stated";

    public string LocationDisplay
    {
        get
        {
            var parts = new[] { Opportunity.ProjectCity, Opportunity.ProjectProvince }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            return parts.Length == 0 ? "not recorded" : string.Join(", ", parts);
        }
    }

    public string FitDisplay => Opportunity.RelevanceTier is { } tier
        ? (Opportunity.RelevanceScore is { } score ? $"{tier} ({score.ToString("0", CultureInfo.InvariantCulture)})" : tier.ToString())
        : "not scored";

    public string SectorDisplay => string.IsNullOrWhiteSpace(Opportunity.PrimeProjectSector) ? "not classified" : Opportunity.PrimeProjectSector!;

    public List<SourceRow> Sources { get; } = new();

    public string Description
    {
        get => _description;
        private set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    public string? PrimaryUrl
    {
        get => _primaryUrl;
        private set { if (_primaryUrl != value) { _primaryUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPrimaryUrl)); } }
    }

    public bool HasPrimaryUrl => !string.IsNullOrWhiteSpace(PrimaryUrl);

    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var observations = await _observations.ListByOpportunityAsync(Opportunity.Id, ct).ConfigureAwait(true);
            var sources = await _sources.ListEnabledAsync(ct).ConfigureAwait(true);
            var names = sources.ToDictionary(s => s.Id, s => s.Name);
            var built = Build(observations, names);

            Sources.Clear();
            Sources.AddRange(built.Sources);
            OnPropertyChanged(nameof(Sources));
            Description = built.Description;
            PrimaryUrl = built.PrimaryUrl;
            StatusText = observations.Count == 0
                ? "No source posting is linked to this record."
                : $"{observations.Count} source posting(s), newest first.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // "Could not retrieve" is a different statement from "none" (BD audit, brief 3).
            Description = "Could not retrieve the source postings: " + ex.Message;
            StatusText = "Source postings unavailable.";
        }
    }

    /// <summary>
    /// Newest posting first (by posted date, else ingestion time). The description shown
    /// is the LONGEST any source carries, because sources truncate differently. The
    /// primary link is the newest posting's URL. A source whose name is unknown (a
    /// disabled source) is shown as "source", never dropped.
    /// </summary>
    public static Built Build(IReadOnlyList<OpportunityObservation> observations, IReadOnlyDictionary<Guid, string> sourceNames)
    {
        var ordered = observations
            .OrderByDescending(o => o.PostedDateUtc ?? o.IngestedAtUtc)
            .ThenByDescending(o => o.Id)
            .ToList();

        var rows = ordered.Select(o => new SourceRow(
                sourceNames.TryGetValue(o.OpportunitySourceId, out var name) ? name : "source",
                o.Title,
                (o.PostedDateUtc ?? o.IngestedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + (o.PostedDateUtc is null ? " (ingested)" : ""),
                o.Url))
            .ToList();

        var description = ordered
            .Select(o => o.Description?.Trim())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .OrderByDescending(d => d!.Length)
            .FirstOrDefault();

        description ??= ordered.Count == 0
            ? "No source posting is linked to this record; there is nothing beyond the fields above."
            : "The source posting carries no description. Open the posting for the documents.";

        var url = ordered.Select(o => o.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

        return new Built(rows, description, url);
    }

    // IAiContextProvider: the assistant sees the RFP the user is looking at.
    public string ProviderName => "RFP detail";
    public bool HasData => true;
    public string BuildContext()
        => $"RFP {Key}: {Name} — {BuyerName}; status {StatusDisplay}; owner {OwnerDisplay}; closes {ClosesDisplay}; "
         + $"value {ValueDisplay}; location {LocationDisplay}; fit {FitDisplay}; sector {SectorDisplay}. "
         + $"Source posting: {PrimaryUrl ?? "none linked"}. {Description}";
    public string BuildLocalContext() => BuildContext();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
