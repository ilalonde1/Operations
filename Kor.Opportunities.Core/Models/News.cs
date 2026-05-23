#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public static class NewsClassificationStatuses
{
    public const string Pending = "pending";
    public const string Ok      = "ok";
    public const string Failed  = "failed";
    public const string Skipped = "skipped";
}

public sealed record NewsFeedRow(
    long Id,
    string Name,
    string FeedUrl,
    string? SiteUrl,
    string? Region,
    string? Discipline,
    bool IsActive,
    DateTimeOffset? LastPolledAtUtc);

public sealed record NewsArticleInsert(
    long FeedId,
    string ExternalId,
    string Title,
    string Url,
    string? Author,
    DateTimeOffset? PublishedAtUtc,
    string? Summary,
    string? Content,
    IReadOnlyList<string> Categories);
