#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// Persistence for <c>opportunities.IngestionTriggers</c>. The WPF "Run Now"
/// button writes via <see cref="EnqueueAsync"/>; the Worker's
/// IngestionTriggerPoller drains via <see cref="ClaimNextPendingAsync"/>.
/// </summary>
public interface IIngestionTriggerStore
{
    /// <summary>Inserts a new Pending trigger and returns its Id.</summary>
    Task<Guid> EnqueueAsync(Guid opportunitySourceId, string requestedBy, CancellationToken ct);

    /// <summary>
    /// Atomically claims the oldest Pending trigger by setting its Status to
    /// <c>InProgress</c> and stamping <c>ClaimedAtUtc</c>/<c>ClaimedBy</c>.
    /// Returns the claimed row, or <c>null</c> if no Pending row was waiting.
    /// Two pollers running concurrently will not double-claim — the UPDATE
    /// uses a single statement with OUTPUT.
    /// </summary>
    Task<IngestionTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct);

    /// <summary>Marks a previously-claimed trigger as Completed/Failed.</summary>
    Task CompleteAsync(
        Guid triggerId,
        IngestionTriggerStatus terminalStatus,
        Guid? ingestionRunId,
        string? errorSummary,
        CancellationToken ct);

    /// <summary>Returns the most recent triggers, newest first. Used by the
    /// WPF admin viewer.</summary>
    Task<IReadOnlyList<IngestionTrigger>> ListRecentAsync(int max, CancellationToken ct);
}
