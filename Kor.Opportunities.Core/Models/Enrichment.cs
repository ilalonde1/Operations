#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public static class EnrichmentStatuses
{
    public const string Pending = "pending";
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string NoData = "no_data";
    public const string Blocked = "blocked";
}

public sealed record EnrichmentTrackingRow(
    long Id,
    long CanonicalOrgId,
    string ProviderName,
    string Status,
    DateTimeOffset? LastRefreshAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? NextRefreshAtUtc,
    int Attempts,
    string? ErrorMessage,
    string? ResultJson);

public sealed record EnrichmentResult(
    string Status,
    string? ErrorMessage,
    string? ResultJson,
    string? Notes);
