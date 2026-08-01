#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface INewsStore
{
    Task<IReadOnlyList<NewsFeedRow>> ListActiveFeedsAsync(CancellationToken ct);

    /// <summary>Insert article if (FeedId, ExternalId) is new. Returns true if inserted.</summary>
    Task<bool> InsertArticleIfNewAsync(NewsArticleInsert article, CancellationToken ct);

    Task UpdateFeedHeartbeatAsync(long feedId, string? errorMessage, CancellationToken ct);

    Task<int> CountArticlesAsync(CancellationToken ct);
    Task<int> CountArticlesByFeedAsync(long feedId, CancellationToken ct);

    Task<IReadOnlyList<NewsArticleForClassification>> ListPendingClassificationAsync(
        int batchSize,
        CancellationToken ct);

    Task RecordMentionAsync(NewsMentionInsert mention, CancellationToken ct);

    Task MarkArticleClassifiedAsync(long articleId, string status, CancellationToken ct);

    Task<int> CountClassifiedAsync(CancellationToken ct);
}
