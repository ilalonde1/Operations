#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// Persistence for <c>opportunities.IngestionRuns</c>. The Worker writes a
/// row at the start of every ingestion attempt and updates it on completion.
/// </summary>
public interface IIngestionRunStore
{
    /// <summary>Inserts a new "in progress" row. Returns the persisted Id.</summary>
    Task<Guid> StartAsync(string providerName, string? hostInstance, string? correlationId, CancellationToken ct);

    /// <summary>Updates an in-progress row with the final stats.</summary>
    Task CompleteAsync(
        Guid runId,
        bool success,
        int insertedCount,
        int duplicateCount,
        int skippedCount,
        int failedCount,
        string? errorSummary,
        CancellationToken ct);

    /// <summary>Returns the most recent runs, newest first. Used by the WPF
    /// admin viewer.</summary>
    Task<IReadOnlyList<IngestionRun>> ListRecentAsync(int max, CancellationToken ct);
}
