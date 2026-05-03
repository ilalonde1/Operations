#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Manual "Run Now" request from the WPF admin pane. Mirrors
/// <c>opportunities.IngestionTriggers</c>. The Worker's
/// <c>IngestionTriggerPoller</c> claims rows in <see cref="IngestionTriggerStatus.Pending"/>
/// and runs them through <c>IIngestionService</c>.
/// </summary>
public sealed record IngestionTrigger
{
    public Guid Id { get; init; }

    public Guid OpportunitySourceId { get; init; }

    public IngestionTriggerStatus Status { get; init; } = IngestionTriggerStatus.Pending;

    public string RequestedBy { get; init; } = "";

    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClaimedAtUtc { get; init; }

    public string? ClaimedBy { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public Guid? IngestionRunId { get; init; }

    public string? ErrorSummary { get; init; }
}

/// <summary>
/// Lifecycle of a manual ingestion trigger. Stored on disk as a string
/// (matches the CHECK constraint in the schema migration).
/// </summary>
public enum IngestionTriggerStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
}
