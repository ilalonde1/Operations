#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Awards;

public static class BdPersonResearchProviders
{
    public const string BatchProviderName = "PersonBrief";
}

public sealed class BdPersonResearchTrigger
{
    public Guid Id { get; init; }
    public long IntelPersonId { get; init; }
    public string ProviderName { get; init; } = "";
    public BdResearchTriggerStatus Status { get; init; }
    public string RequestedBy { get; init; } = "";
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? ClaimedAtUtc { get; init; }
    public string? ClaimedBy { get; init; }
    public Guid? ClaimToken { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? ErrorSummary { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
}

public interface IBdPersonResearchTriggerStore
{
    Task<Guid> EnqueueAsync(long intelPersonId, string providerName, string requestedBy, CancellationToken ct);

    Task<BdPersonResearchTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct);

    Task CompleteAsync(
        Guid triggerId,
        Guid claimToken,
        BdResearchTriggerStatus terminalStatus,
        long? inputTokens,
        long? outputTokens,
        string? errorSummary,
        CancellationToken ct);

    Task<bool> HasPendingForPersonAsync(long intelPersonId, string providerName, CancellationToken ct);
}
