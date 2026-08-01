#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Persistence for <c>opportunities.BdResearchTriggers</c>. The WPF "Refresh now"
/// button writes via <see cref="EnqueueAsync"/>; the Worker's
/// BdResearchTriggerPoller drains via <see cref="ClaimNextPendingAsync"/>.
/// </summary>
public interface IBdResearchTriggerStore
{
    /// <summary>Inserts a new Pending trigger and returns its Id.</summary>
    Task<Guid> EnqueueAsync(long canonicalOrgId, string providerName, string requestedBy, CancellationToken ct);

    /// <summary>
    /// Atomically claims the oldest Pending trigger by setting its Status to
    /// <c>InProgress</c> and stamping <c>ClaimedAtUtc</c>/<c>ClaimedBy</c>
    /// plus a fresh claim token.
    /// Returns the claimed row, or <c>null</c> if no Pending row was waiting.
    /// Two pollers running concurrently will not double-claim — the UPDATE
    /// uses a single statement with OUTPUT.
    /// </summary>
    Task<BdResearchTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct);

    /// <summary>Marks a previously-claimed trigger as Completed/Failed.</summary>
    Task CompleteAsync(
        Guid triggerId,
        Guid claimToken,
        BdResearchTriggerStatus terminalStatus,
        long? inputTokens,
        long? outputTokens,
        string? errorSummary,
        CancellationToken ct);

    /// <summary>Returns the most recent triggers, newest first. Used by the
    /// WPF admin viewer.</summary>
    Task<IReadOnlyList<BdResearchTrigger>> ListRecentAsync(int max, CancellationToken ct);

    /// <summary>
    /// Returns true when the same org/provider already has a Pending or
    /// InProgress request.
    /// </summary>
    Task<bool> HasPendingForOrgAsync(long canonicalOrgId, string providerName, CancellationToken ct);
}
