#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One observability row per (provider, run-attempt). Mirrors
/// <c>opportunities.IngestionRuns</c>. The Worker writes one of these for
/// every scheduled or manually-triggered ingestion pass.
/// </summary>
public sealed record IngestionRun
{
    public Guid Id { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string ProviderName { get; init; } = "";

    public bool Success { get; init; }

    public int InsertedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int SkippedCount { get; init; }

    public int FailedCount { get; init; }

    public string? ErrorSummary { get; init; }

    /// <summary>Hostname of the Worker that produced this row. Useful for
    /// disambiguating runs in a future multi-Worker deployment.</summary>
    public string? HostInstance { get; init; }

    /// <summary>Free-form correlation id propagated through Serilog
    /// scopes for cross-log tracing.</summary>
    public string? CorrelationId { get; init; }
}
