#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// One-stop entry for "run ingestion against this source". Looks up the
/// configured <see cref="OpportunitySource"/>, picks the matching provider
/// for the source's <see cref="OpportunitySourceType"/>, and hands both to
/// <see cref="IIngestionService"/>.
///
/// Lets the Quartz job and the WPF "Run Now" path share the same
/// dispatch logic.
/// </summary>
public interface IIngestionDispatcher
{
    /// <summary>Runs ingestion for the source with the given <c>Name</c>
    /// (e.g. <c>"CanadaBuys"</c>). Throws if no enabled source matches or
    /// no provider is registered for its source type.</summary>
    Task<DispatchResult> RunByNameAsync(string sourceName, string? correlationId, CancellationToken ct);

    /// <summary>Runs ingestion for the source with the given <c>Id</c>.
    /// Throws if the source is missing/disabled or no provider is registered.</summary>
    Task<DispatchResult> RunByIdAsync(Guid opportunitySourceId, string? correlationId, CancellationToken ct);
}

/// <summary>Outcome of one dispatch — exposes the underlying ingestion stats.</summary>
public sealed record DispatchResult(OpportunitySource Source, IngestionResult Result);
