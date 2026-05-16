#nullable enable
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// Orchestrates one ingestion pass: opens an <see cref="IngestionRun"/>, asks
/// a provider for candidates, dedupes via SHA-256 on the observation table,
/// and on each new observation creates a canonical <see cref="Opportunity"/>
/// (scored by the rules engine) and links the observation to it.
///
/// Hash key: <c>UPPER(Title) + "|" + UPPER(Buyer) + "|" + UPPER(Location) + "|" + UPPER(Url)</c>.
/// The unique index <c>UX_Opp_Obs_HashSha256</c> enforces uniqueness server-side;
/// <see cref="IOpportunityObservationStore.TryInsertAsync"/> swallows the collision
/// so duplicates increment a counter rather than raising.
///
/// Auto-merge of multiple observations onto one canonical opportunity is a Phase 7
/// concern. v1 inserts one Opportunity per non-duplicate observation.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Runs one ingestion pass against <paramref name="source"/> using
    /// <paramref name="provider"/>. The run is recorded in <c>opportunities.IngestionRuns</c>
    /// regardless of outcome.
    /// </summary>
    Task<IngestionResult> IngestAsync(
        IOpportunityProvider provider,
        OpportunitySource source,
        string? correlationId,
        CancellationToken ct);
}

public sealed class IngestionService : IIngestionService
{
    /// <summary>
    /// Actor string written to <c>Opportunity.CreatedBy</c> / <c>UpdatedBy</c> for
    /// rows that came from automated ingestion. Distinguishes them from manual
    /// WPF entries (which use the Windows user) when auditing later.
    /// </summary>
    private const string IngestionActor = "ingestion";

    private readonly IOpportunitySourceStore _sourceStore;
    private readonly IOpportunityObservationStore _observationStore;
    private readonly IOpportunityStore _opportunityStore;
    private readonly IOpportunityScoringService _scoringService;
    private readonly IIngestionRunStore _runStore;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IOpportunitySourceStore sourceStore,
        IOpportunityObservationStore observationStore,
        IOpportunityStore opportunityStore,
        IOpportunityScoringService scoringService,
        IIngestionRunStore runStore,
        ILogger<IngestionService> logger)
    {
        _sourceStore = sourceStore;
        _observationStore = observationStore;
        _opportunityStore = opportunityStore;
        _scoringService = scoringService;
        _runStore = runStore;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        IOpportunityProvider provider,
        OpportunitySource source,
        string? correlationId,
        CancellationToken ct)
    {
        var providerName = $"{source.Name} ({provider.SourceType})";
        var hostInstance = SafeHostName();
        var runId = await _runStore.StartAsync(providerName, hostInstance, correlationId, ct).ConfigureAwait(false);

        var inserted = 0;
        var duplicate = 0;
        var skipped = 0;
        var failed = 0;
        string? errorSummary = null;

        try
        {
            var mappings = await _sourceStore.GetMappingsAsync(source.Id, ct).ConfigureAwait(false);
            var candidates = await provider.FetchAsync(source, mappings, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Ingestion {Run} for {Source}: provider returned {Count} candidate(s).",
                runId, source.Name, candidates.Count);

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.Url))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var outcome = await ProcessCandidateAsync(source, candidate, ct).ConfigureAwait(false);
                    switch (outcome)
                    {
                        case CandidateOutcome.Inserted:
                            inserted++;
                            break;
                        case CandidateOutcome.Duplicate:
                            duplicate++;
                            break;
                        case CandidateOutcome.Skipped:
                            skipped++;
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Ingestion {Run}: failed to process candidate '{Title}' from {Source}.",
                        runId, candidate.Title, source.Name);
                }
            }

            var success = failed == 0;
            await _runStore.CompleteAsync(runId, success, inserted, duplicate, skipped, failed, errorSummary, CancellationToken.None)
                .ConfigureAwait(false);

            return new IngestionResult
            {
                Inserted = inserted,
                Duplicate = duplicate,
                Skipped = skipped,
                Failed = failed,
                Success = success,
                ErrorSummary = errorSummary,
            };
        }
        catch (OperationCanceledException)
        {
            errorSummary = "cancelled";
            await _runStore.CompleteAsync(runId, success: false, inserted, duplicate, skipped, failed, errorSummary, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            errorSummary = Truncate(ex.Message, 2000);
            _logger.LogError(ex, "Ingestion {Run} for {Source} failed.", runId, source.Name);
            await _runStore.CompleteAsync(runId, success: false, inserted, duplicate, skipped, failed, errorSummary, CancellationToken.None)
                .ConfigureAwait(false);

            return new IngestionResult
            {
                Inserted = inserted,
                Duplicate = duplicate,
                Skipped = skipped,
                Failed = failed,
                Success = false,
                ErrorSummary = errorSummary,
            };
        }
    }

    private async Task<CandidateOutcome> ProcessCandidateAsync(
        OpportunitySource source,
        OpportunityCandidate candidate,
        CancellationToken ct)
    {
        var hash = ComputeHash(candidate);

        var observation = new OpportunityObservation
        {
            OpportunitySourceId = source.Id,
            Title = Truncate(candidate.Title.Trim(), 400),
            Buyer = Truncate(string.IsNullOrWhiteSpace(candidate.Buyer) ? "Unknown" : candidate.Buyer.Trim(), 300),
            Location = string.IsNullOrWhiteSpace(candidate.Location) ? null : Truncate(candidate.Location.Trim(), 300),
            Url = Truncate(candidate.Url.Trim(), 2000),
            Description = candidate.Description,
            RawJson = candidate.RawJson,
            PostedDateUtc = candidate.PostedDateUtc,
            HashSha256 = hash,
            IsActive = true,
        };

        var persistedObservation = await _observationStore.TryInsertAsync(observation, ct).ConfigureAwait(false);
        if (persistedObservation is null)
        {
            return CandidateOutcome.Duplicate;
        }

        var key = ComposeOpportunityKey(source, candidate, hash);

        var existing = await _opportunityStore.GetByKeyAsync(key, ct).ConfigureAwait(false);
        long opportunityId;
        if (existing is null)
        {
            var opportunity = BuildOpportunity(source, candidate, key);
            var scored = _scoringService.Score(opportunity);
            opportunity = opportunity with
            {
                RelevanceScore = scored.Score,
                RelevanceTier = scored.Tier,
            };

            var persisted = await _opportunityStore.InsertAsync(opportunity, IngestionActor, ct).ConfigureAwait(false);
            opportunityId = persisted.Id;
        }
        else
        {
            // Same external reference re-observed. Refresh score against current rules
            // and bump UpdatedAt; preserve user-supplied edits (status, owner, etc.).
            var scored = _scoringService.Score(existing);
            var refreshed = existing with
            {
                RelevanceScore = scored.Score,
                RelevanceTier = scored.Tier,
                SubmissionDeadlineUtc = candidate.SubmissionDeadlineUtc ?? existing.SubmissionDeadlineUtc,
            };

            try
            {
                var persisted = await _opportunityStore.UpdateAsync(refreshed, IngestionActor, ct).ConfigureAwait(false);
                opportunityId = persisted.Id;
            }
            catch (OpportunityConcurrencyException)
            {
                // A user is editing concurrently — link the observation but skip the
                // refresh. Their edit wins; next run will pick up the new RowVersion.
                opportunityId = existing.Id;
                _logger.LogInformation(
                    "Opportunity {Key} updated concurrently; skipping ingestion-side refresh.", key);
            }
        }

        await _observationStore.LinkAsync(persistedObservation.Id, opportunityId, ct).ConfigureAwait(false);
        return CandidateOutcome.Inserted;
    }

    /// <summary>
    /// Composes a stable business key. When the provider supplies a non-empty
    /// <see cref="OpportunityCandidate.ExternalReference"/> we use it (with a source
    /// prefix so different sources never collide); otherwise we fall back to a short
    /// hash-derived suffix so the key remains deterministic and idempotent across runs.
    /// </summary>
    private static string ComposeOpportunityKey(OpportunitySource source, OpportunityCandidate candidate, byte[] hash)
    {
        var prefix = SourcePrefix(source);

        if (!string.IsNullOrWhiteSpace(candidate.ExternalReference))
        {
            var sanitized = Sanitize(candidate.ExternalReference!.Trim());
            var budget = 64 - prefix.Length - 1;  // -1 for the '-' separator
            if (sanitized.Length <= budget)
            {
                return $"{prefix}-{sanitized}";
            }

            // External reference too long to fit. Hash it for uniqueness.
            // First 12 hex chars of SHA-256(externalReference) gives 48 bits -
            // collision-resistant enough that two different references colliding
            // is astronomically unlikely.
            var refHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.ExternalReference!.Trim()));
            var refHashHex = Convert.ToHexString(refHash, 0, 6);
            return $"{prefix}-{refHashHex}";
        }

        // Use the first 6 bytes of the hash (12 hex chars) - collisions across distinct
        // candidates are astronomically unlikely.
        var shortHash = Convert.ToHexString(hash, 0, 6);
        return $"{prefix}-{shortHash}";
    }

    private static string SourcePrefix(OpportunitySource source)
    {
        // Short, alphanumeric prefix so keys read cleanly. Falls back to source type
        // when the configured Name has no usable letters (defensive only).
        var raw = source.Name ?? "";
        var sb = new StringBuilder(8);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }

            if (sb.Length >= 8)
            {
                break;
            }
        }

        if (sb.Length == 0)
        {
            sb.Append(source.SourceType.ToString().ToUpperInvariant());
        }

        return sb.ToString();
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '/')
            {
                sb.Append('-');
            }
        }

        return sb.Length == 0 ? "x" : sb.ToString();
    }

    private static Opportunity BuildOpportunity(OpportunitySource source, OpportunityCandidate candidate, string opportunityKey)
    {
        var name = Truncate(candidate.Title.Trim(), 400);
        var buyer = Truncate(string.IsNullOrWhiteSpace(candidate.Buyer) ? "Unknown" : candidate.Buyer.Trim(), 300);

        return new Opportunity
        {
            OpportunityKey = opportunityKey,
            Name = name,
            BuyerName = buyer,
            BuyerType = GuessBuyerType(buyer, source),
            ProjectCity = string.IsNullOrWhiteSpace(candidate.ProjectCity) ? null : Truncate(candidate.ProjectCity.Trim(), 150),
            ProjectProvince = string.IsNullOrWhiteSpace(candidate.ProjectProvince) ? null : Truncate(candidate.ProjectProvince.Trim(), 20),
            EstimatedValue = candidate.EstimatedValueCad,
            EstimatedValueCurrency = "CAD",
            SubmissionDeadlineUtc = candidate.SubmissionDeadlineUtc,
            Status = OpportunityStatus.New,
            IdentifiedAtUtc = candidate.PostedDateUtc ?? DateTimeOffset.UtcNow,
            Discipline = OpportunityDiscipline.Unknown,
        };
    }

    private static BuyerType GuessBuyerType(string buyer, OpportunitySource source)
    {
        // CanadaBuys is overwhelmingly federal-issued. Cheap default for v1;
        // users can refine to Provincial/Municipal via the WPF entry dialog.
        if (string.Equals(source.Name, "CanadaBuys", StringComparison.OrdinalIgnoreCase))
        {
            return BuyerType.Federal;
        }

        if (string.Equals(source.Name, "SamGov", StringComparison.OrdinalIgnoreCase))
        {
            return BuyerType.Federal;
        }

        return BuyerType.Unknown;
    }

    /// <summary>
    /// Builds the dedup key — uppercased, pipe-separated <c>Title|Buyer|Location|Url</c>
    /// — and returns its SHA-256 digest. Matches the doc-comment on
    /// <see cref="OpportunityObservation.HashSha256"/>; do not change without
    /// rebuilding the observation table.
    /// </summary>
    private static byte[] ComputeHash(OpportunityCandidate candidate)
    {
        var key = string.Join("|",
            (candidate.Title ?? "").Trim().ToUpperInvariant(),
            (candidate.Buyer ?? "").Trim().ToUpperInvariant(),
            (candidate.Location ?? "").Trim().ToUpperInvariant(),
            (candidate.Url ?? "").Trim().ToUpperInvariant());

        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return value.Length <= max ? value : value.Substring(0, max);
    }

    private static string SafeHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private enum CandidateOutcome
    {
        Inserted,
        Duplicate,
        Skipped,
    }
}
