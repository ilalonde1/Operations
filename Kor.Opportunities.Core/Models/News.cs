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

public sealed record NewsArticleForClassification(
    long Id,
    long FeedId,
    string Title,
    string Url,
    string? Summary,
    string? Content);

public sealed record NewsMentionInsert(
    long NewsArticleId,
    long CanonicalOrgId,
    string? MentionType,
    int Confidence,
    string? Excerpt);

public static class NewsMentionTypes
{
    public const string ProjectWin  = "project_win";
    public const string MAndA       = "m_and_a";
    public const string Hiring      = "hiring";
    public const string Leadership  = "leadership";
    public const string Award       = "award";
    public const string Expansion   = "expansion";
    public const string Regulatory  = "regulatory";
    public const string Partnership = "partnership";
    public const string Other       = "other";
}
