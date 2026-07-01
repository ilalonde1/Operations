#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Observations;

/// <summary>
/// Read/write access to <c>opportunities.OpportunityObservations</c>.
/// The unique index on <see cref="OpportunityObservation.HashSha256"/>
/// is the dedup mechanism — <see cref="TryInsertAsync"/> swallows the
/// resulting collision and returns <c>false</c>.
/// </summary>
public interface IOpportunityObservationStore
{
    /// <summary>
    /// Inserts a new observation. Returns the persisted row when the hash
    /// is new; returns <c>null</c> when the hash collides (duplicate from
    /// a previous run). Caller updates ingestion-run counters accordingly.
    /// </summary>
    Task<OpportunityObservation?> TryInsertAsync(OpportunityObservation observation, CancellationToken ct);

    /// <summary>Sets <see cref="OpportunityObservation.OpportunityId"/> for
    /// an already-persisted observation. Used when the ingestion service
    /// links a new observation to a freshly-created canonical opportunity.</summary>
    Task LinkAsync(long observationId, long opportunityId, CancellationToken ct);

    /// <summary>Fetches the observation carrying the given content hash, or null.
    /// Used by ingestion's repair path: a duplicate-hash hit whose opportunity is
    /// missing (a prior run failed mid-persist) re-creates the opportunity and
    /// re-links the stranded observation.</summary>
    Task<OpportunityObservation?> TryGetByHashAsync(byte[] hashSha256, CancellationToken ct);

    Task<IReadOnlyList<OpportunityObservation>> ListByOpportunityAsync(long opportunityId, CancellationToken ct);
}
