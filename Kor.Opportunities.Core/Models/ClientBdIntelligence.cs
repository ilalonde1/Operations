#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record ClientBdIntelligence(
    long? CanonicalOrgId,
    string? ClendorClientId,
    KorPursuitSummary Pursuits,
    IReadOnlyList<KorPursuitRecent> RecentPursuits,
    ExternalAwardActivity ExternalActivity,
    IReadOnlyList<ExternalAwardRecent> RecentExternalAwards,
    CompetitorActivity CompetitorActivity,
    IReadOnlyList<NewsMentionRecent> RecentNewsMentions);

public sealed record KorPursuitSummary(
    int TotalCount,
    int WonCount,
    int SubmittedCount,
    int PursuingCount,
    int LostCount,
    decimal? TotalBidFee,
    DateTimeOffset? LastSubmittedAtUtc);

public sealed record KorPursuitRecent(
    long Id,
    string Title,
    string Stage,
    decimal? BidFee,
    DateTime? SubmittedDate,
    DateTime? AwardDate);

public sealed record ExternalAwardActivity(
    int AwardCount,
    int ActiveOpportunityCount,
    decimal? TotalAwardedValue,
    int DistinctVendorsAwarded);

public sealed record ExternalAwardRecent(
    string Title,
    string? AwardedTo,
    decimal? Value,
    DateTimeOffset? AwardedAtUtc,
    string? SourceName);

public sealed record CompetitorActivity(
    int AwardsAsVendorCount,
    decimal? TotalContractsWonValue,
    int DistinctBuyersWonFrom,
    DateTimeOffset? LatestVendorWinAtUtc);

public sealed record NewsMentionRecent(
    long NewsArticleId,
    string ArticleTitle,
    string ArticleUrl,
    DateTimeOffset? PublishedAtUtc,
    string? FeedName,
    string? MentionType,
    int Confidence,
    string? Excerpt);
