#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlNewsStore : INewsStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlNewsStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<NewsFeedRow>> ListActiveFeedsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, Name, FeedUrl, SiteUrl, Region, Discipline, IsActive, LastPolledAtUtc
FROM   opportunities.NewsFeed
WHERE  IsActive = 1
ORDER  BY Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        var list = new List<NewsFeedRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NewsFeedRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetBoolean(6),
                r.IsDBNull(7) ? (DateTimeOffset?)null : r.GetDateTimeOffset(7)));
        }

        return list;
    }

    public async Task<bool> InsertArticleIfNewAsync(NewsArticleInsert a, CancellationToken ct)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.NewsArticle WHERE FeedId = @feed AND ExternalId = @ext)
BEGIN
    INSERT INTO opportunities.NewsArticle
        (FeedId, ExternalId, Title, Url, Author, PublishedAtUtc, Summary, Content, Categories)
    VALUES
        (@feed, @ext, @title, @url, @author, @pub, @summary, @content, @cats);
    SELECT 1;
END
ELSE
    SELECT 0;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@feed", SqlDbType.BigInt).Value = a.FeedId;
        cmd.Parameters.Add("@ext", SqlDbType.NVarChar, 500).Value = a.ExternalId;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = a.Title;
        cmd.Parameters.Add("@url", SqlDbType.NVarChar, 800).Value = a.Url;
        cmd.Parameters.Add("@author", SqlDbType.NVarChar, 200).Value = (object?)a.Author ?? DBNull.Value;
        cmd.Parameters.Add("@pub", SqlDbType.DateTimeOffset).Value = (object?)a.PublishedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@summary", SqlDbType.NVarChar, -1).Value = (object?)a.Summary ?? DBNull.Value;
        cmd.Parameters.Add("@content", SqlDbType.NVarChar, -1).Value = (object?)a.Content ?? DBNull.Value;

        var catsJson = a.Categories.Count == 0 ? null : JsonSerializer.Serialize(a.Categories);
        cmd.Parameters.Add("@cats", SqlDbType.NVarChar, -1).Value = (object?)catsJson ?? DBNull.Value;

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(v ?? 0) == 1;
    }

    public async Task UpdateFeedHeartbeatAsync(long feedId, string? errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.NewsFeed
SET    LastPolledAtUtc = sysdatetimeoffset(),
       LastErrorMessage = @err
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = feedId;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 1000).Value = (object?)errorMessage ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountArticlesAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.NewsArticle;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    public async Task<int> CountArticlesByFeedAsync(long feedId, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.NewsArticle WHERE FeedId = @id;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = feedId;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    public async Task<IReadOnlyList<NewsArticleForClassification>> ListPendingClassificationAsync(
        int batchSize,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@n) Id, FeedId, Title, Url, Summary, Content
FROM   opportunities.NewsArticle
WHERE  ClassificationStatus = 'pending'
ORDER  BY PublishedAtUtc DESC, Id DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;

        var list = new List<NewsArticleForClassification>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NewsArticleForClassification(
                r.GetInt64(0),
                r.GetInt64(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        }

        return list;
    }

    public async Task RecordMentionAsync(NewsMentionInsert m, CancellationToken ct)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention
               WHERE NewsArticleId = @a AND CanonicalOrgId = @o)
BEGIN
    INSERT INTO opportunities.NewsArticleOrgMention
        (NewsArticleId, CanonicalOrgId, MentionType, Confidence, Excerpt)
    VALUES (@a, @o, @type, @conf, @ex);
END
ELSE
BEGIN
    UPDATE opportunities.NewsArticleOrgMention
    SET    MentionType = COALESCE(@type, MentionType),
           Confidence  = CASE WHEN @conf > Confidence THEN @conf ELSE Confidence END,
           Excerpt     = COALESCE(@ex, Excerpt)
    WHERE  NewsArticleId = @a AND CanonicalOrgId = @o;
END";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = m.NewsArticleId;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = m.CanonicalOrgId;
        cmd.Parameters.Add("@type", SqlDbType.NVarChar, 40).Value = (object?)m.MentionType ?? DBNull.Value;
        cmd.Parameters.Add("@conf", SqlDbType.Int).Value = m.Confidence;
        cmd.Parameters.Add("@ex", SqlDbType.NVarChar, 2000).Value = (object?)m.Excerpt ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkArticleClassifiedAsync(long articleId, string status, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.NewsArticle
SET    ClassifiedAtUtc = sysdatetimeoffset(),
       ClassificationStatus = @s
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = articleId;
        cmd.Parameters.Add("@s", SqlDbType.NVarChar, 20).Value = status;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountClassifiedAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.NewsArticle WHERE ClassificationStatus = 'ok';";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }
}
