#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IOpportunityAwardStore
{
    /// <summary>Insert or refresh by (SourceId, ExternalReference). Returns the
    /// new row Id for an insert, or 0 when an existing row was updated.</summary>
    Task<long> UpsertAsync(OpportunityAward award, CancellationToken ct);

    Task<IReadOnlyList<OpportunityAward>> ListRecentAsync(int max, CancellationToken ct);

    Task<IReadOnlyList<PendingAgentEnrichmentRow>> ListPendingAgentEnrichmentAsync(
        int batchSize,
        int maxAttempts,
        CancellationToken ct);

    Task<IReadOnlyList<PendingAgentEnrichmentRow>> ListPendingAgentEnrichmentAsync(
        int batchSize,
        int maxAttempts,
        IReadOnlyCollection<Guid>? excludeSourceIds,
        decimal? minContractValue,
        CancellationToken ct);

    Task RecordAgentEnrichmentAsync(
        long id,
        AwardAgentEnrichmentPayload payload,
        CancellationToken ct);

    Task RecordAgentFailureAsync(long id, string error, CancellationToken ct);

    /// <summary>Count of rows where AgentEnrichedAtUtc IS NOT NULL. Used by the
    /// job to enforce a total-cap ceiling without scanning row-by-row.</summary>
    Task<int> CountAgentEnrichedAsync(CancellationToken ct);
}
