#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One activity log entry on a <see cref="CrmEngagement"/>. Append-only — once
/// inserted these rows are never edited; corrections happen by inserting a
/// new "correction" row. Mirrors <c>opportunities.CrmActivities</c>.
/// </summary>
public sealed record CrmActivity
{
    public long Id { get; init; }

    public long EngagementId { get; init; }

    public CrmActivityType ActivityType { get; init; } = CrmActivityType.Note;

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Subject { get; init; } = "";

    public string? Body { get; init; }

    /// <summary>Optional FK to a <see cref="CrmContact"/> on the same engagement.</summary>
    public long? ContactId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; init; } = "";
}

/// <summary>
/// Type of CRM activity. Stable on disk; values map to
/// opportunities.CrmActivities.ActivityType.
/// </summary>
public enum CrmActivityType
{
    Note = 1,
    Call = 2,
    Meeting = 3,
    EmailOut = 4,
    EmailIn = 5,
    Deliverable = 6,
    Other = 99,
}
