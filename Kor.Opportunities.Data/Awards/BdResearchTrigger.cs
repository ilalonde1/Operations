#nullable enable
using System;

namespace Kor.Opportunities.Data.Awards;

public enum BdResearchTriggerStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
}

public sealed class BdResearchTrigger
{
    public Guid Id { get; init; }
    public long CanonicalOrgId { get; init; }
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
