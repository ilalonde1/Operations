#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class NewsFeedPollService
{
    private readonly HttpClient _http;
    private readonly INewsStore _store;
    private readonly ILogger<NewsFeedPollService> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxItemsPerFeed;

    public NewsFeedPollService(
        HttpClient http,
        INewsStore store,
        ILogger<NewsFeedPollService> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxItemsPerFeed = 200)
    {
        _http = http;
        _store = store;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxItemsPerFeed = maxItemsPerFeed > 0 ? maxItemsPerFeed : int.MaxValue;
    }

    public sealed record PollResult(int FeedsPolled, int ArticlesPulled, int Inserted, int Failed);

    public async Task<PollResult> PollAllAsync(CancellationToken ct)
    {
        var feeds = await _store.ListActiveFeedsAsync(ct).ConfigureAwait(false);
        var polled = 0;
        var pulled = 0;
        var inserted = 0;
        var failed = 0;

        foreach (var feed in feeds)
        {
            ct.ThrowIfCancellationRequested();
            polled++;

            try
            {
                using var resp = await _http.GetAsync(feed.FeedUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await _store.UpdateFeedHeartbeatAsync(feed.Id, $"HTTP {(int)resp.StatusCode}", ct)
                        .ConfigureAwait(false);
                    failed++;
                    continue;
                }

                var xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (xml.Length > _maxBytesPerResponse)
                {
                    throw new InvalidOperationException(
                        $"News feed {feed.Name} response exceeded configured limit ({xml.Length} > {_maxBytesPerResponse}).");
                }

                var items = ParseFeed(xml, _maxItemsPerFeed);
                if (items.Count >= _maxItemsPerFeed)
                {
                    _logger.LogWarning(
                        "News feed {Name}: item cap {MaxItems} reached; remaining items were skipped.",
                        feed.Name,
                        _maxItemsPerFeed);
                }
                pulled += items.Count;

                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();

                    var externalId = string.IsNullOrWhiteSpace(item.Guid) ? item.Link : item.Guid;
                    var insert = new NewsArticleInsert(
                        FeedId: feed.Id,
                        ExternalId: externalId,
                        Title: item.Title ?? "(no title)",
                        Url: item.Link,
                        Author: item.Author,
                        PublishedAtUtc: item.Published,
                        Summary: item.Summary,
                        Content: item.Content,
                        Categories: item.Categories);

                    if (await _store.InsertArticleIfNewAsync(insert, ct).ConfigureAwait(false))
                    {
                        inserted++;
                    }
                }

                await _store.UpdateFeedHeartbeatAsync(feed.Id, null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Feed {Name} ({Url}) failed.", feed.Name, feed.FeedUrl);
                try
                {
                    await _store.UpdateFeedHeartbeatAsync(feed.Id, ex.Message, ct).ConfigureAwait(false);
                }
                catch
                {
                }

                failed++;
            }
        }

        return new PollResult(polled, pulled, inserted, failed);
    }

    private sealed record ParsedItem(
        string? Guid,
        string Link,
        string? Title,
        string? Author,
        DateTimeOffset? Published,
        string? Summary,
        string? Content,
        IReadOnlyList<string> Categories);

    private static List<ParsedItem> ParseFeed(string xml, int maxItems)
    {
        var items = new List<ParsedItem>();

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreWhitespace = true,
                IgnoreComments = true,
            });

            string? guid = null;
            string? link = null;
            string? title = null;
            string? author = null;
            string? summary = null;
            string? content = null;
            DateTimeOffset? published = null;
            var categories = new List<string>();
            var inItem = false;
            var itemElement = "item";

            while (reader.Read())
            {
                if (items.Count >= maxItems)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element)
                {
                    var name = reader.LocalName.ToLowerInvariant();
                    if (name is "item" or "entry")
                    {
                        inItem = true;
                        itemElement = name;
                        guid = link = title = author = summary = content = null;
                        published = null;
                        categories = new List<string>();
                        continue;
                    }

                    if (!inItem)
                    {
                        continue;
                    }

                    switch (name)
                    {
                        case "guid":
                        case "id":
                            guid = ReadElementOrEmpty(reader);
                            break;
                        case "link":
                            var href = reader.GetAttribute("href");
                            if (!string.IsNullOrWhiteSpace(href))
                            {
                                link = href;
                            }
                            else
                            {
                                link = ReadElementOrEmpty(reader);
                            }
                            break;
                        case "title":
                            title = ReadElementOrEmpty(reader);
                            break;
                        case "creator":
                        case "author":
                            author = ReadElementOrEmpty(reader);
                            break;
                        case "pubdate":
                        case "published":
                        case "updated":
                            var dateText = ReadElementOrEmpty(reader);
                            if (DateTimeOffset.TryParse(dateText, out var parsedDate))
                            {
                                published = parsedDate;
                            }
                            break;
                        case "description":
                        case "summary":
                            summary = ReadElementOrEmpty(reader);
                            break;
                        case "encoded":
                        case "content":
                            content = ReadElementOrEmpty(reader);
                            break;
                        case "category":
                            var category = ReadElementOrEmpty(reader);
                            if (!string.IsNullOrWhiteSpace(category))
                            {
                                categories.Add(category.Trim());
                            }
                            break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement &&
                         string.Equals(reader.LocalName, itemElement, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(link))
                    {
                        items.Add(new ParsedItem(guid, link, title, author, published, summary, content, categories));
                    }

                    inItem = false;
                }
            }
        }
        catch
        {
            // Return whatever was parsed before a malformed feed segment.
        }

        return items;

        static string ReadElementOrEmpty(XmlReader r)
        {
            try
            {
                return r.ReadElementContentAsString().Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
