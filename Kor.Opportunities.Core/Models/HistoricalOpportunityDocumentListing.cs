#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public sealed record HistoricalOpportunityDocumentListing
{
    public long Id { get; init; }
    public long HistoricalOpportunityId { get; init; }
    public string FileName { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string? LocalPath { get; init; }
    public long? SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? DownloadedAtUtc { get; init; }
}
