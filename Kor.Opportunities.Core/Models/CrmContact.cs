#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Buyer-side person tied to a <see cref="CrmEngagement"/>. Mirrors
/// <c>opportunities.CrmContacts</c>. RowVersion drives optimistic concurrency.
/// </summary>
public sealed record CrmContact
{
    public long Id { get; init; }

    public long EngagementId { get; init; }

    public string DisplayName { get; init; } = "";

    public string? Role { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public bool IsPrimary { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string UpdatedBy { get; init; } = "";

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}
