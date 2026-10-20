#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail reader for Alberta Purchasing Connection postings (purchasing.alberta.ca/posting/AB-…).
///
/// The posting page is an Angular app that loads everything it shows from ONE public JSON call,
/// <c>/api/opportunity/public/{year}/{number}</c>: the full description, the procurement contact, the questions deadline,
/// the submission method and the document list (each with an id, file name, size and type). The site sits behind
/// Cloudflare, so the feed is read the way a browser reads it: open the posting and take the response the page itself
/// requests. No login is needed for any of this.
///
/// What it does NOT do: download the files. APC serves a document only to a signed-in supplier, and downloading adds the
/// account to the posting's public interested-suppliers list. Each document is recorded with a URL of the form
/// <c>{posting url}#document-{id}</c>, which is unique per file and carries the id a signed-in download needs.
/// Interested suppliers are left to <c>ApcInterestEnrichmentJob</c>, which owns that list for APC.
///
/// Feed-to-fields is a pure function (<see cref="ParseFeed"/>), tested against a captured feed.
/// </summary>
public sealed class ApcLiveDetailExtractor : ILiveOppDetailExtractor
{
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    private readonly ILogger<ApcLiveDetailExtractor> _logger;

    public ApcLiveDetailExtractor(ILogger<ApcLiveDetailExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "APC";

    public string UrlHostLike => "%purchasing.alberta.ca/posting/%";

    public bool RequiresLogin => false;

    public bool IsAvailable => true;

    public Task LoginAsync(IPage page, CancellationToken ct) => Task.CompletedTask;

    public async Task<LiveDetailResult?> ExtractAsync(IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IResponse feed;
        try
        {
            feed = await page.RunAndWaitForResponseAsync(
                () => page.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 }),
                r => r.Url.Contains("/api/opportunity/public/", StringComparison.OrdinalIgnoreCase),
                new PageRunAndWaitForResponseOptions { Timeout = 45_000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("APC: the posting feed never loaded for {Url}.", detailUrl);
            return null;
        }

        if (!feed.Ok)
        {
            _logger.LogWarning("APC: posting feed returned {Status} for {Url}.", feed.Status, detailUrl);
            return null;
        }

        var result = ParseFeed(await feed.TextAsync().ConfigureAwait(false), detailUrl);
        if (result is null)
        {
            _logger.LogWarning("APC: posting feed for {Url} had no opportunity block.", detailUrl);
        }

        return result;
    }

    /// <summary>Pure feed-to-fields. Returns null when the JSON carries no opportunity.</summary>
    internal static LiveDetailResult? ParseFeed(string json, string detailUrl)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("opportunity", out var o) || o.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? contactName = null, contactEmail = null, contactPhone = null;
        if (o.TryGetProperty("contactInformation", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            contactName = Clean(string.Join(' ', new[] { Text(c, "firstName"), Text(c, "lastName") }.Where(s => !string.IsNullOrWhiteSpace(s))));
            var title = Text(c, "title");
            if (contactName is not null && title is not null)
            {
                contactName = $"{contactName}, {title}";
            }

            contactEmail = Text(c, "emailAddress");
            var phone = Text(c, "phoneNumber");
            var ext = Text(c, "phoneNumberExtension");
            contactPhone = phone is null ? null : ext is null ? phone : $"{phone} ext. {ext}";
        }

        return new LiveDetailResult(
            CommodityCodes: Array.Empty<string>(),
            Description: Compose(o),
            ContactName: contactName,
            ContactEmail: contactEmail,
            ContactPhone: contactPhone,
            Documents: Documents(o, detailUrl));
    }

    /// <summary>The posting's description, then the two dates and the method a bid team needs before anything else.</summary>
    private static string? Compose(JsonElement o)
    {
        var parts = new List<string>();
        if (Text(o, "projectDescription") is { } description)
        {
            parts.Add(description);
        }

        if (Text(o, "solicitationNumber") is { } number)
        {
            parts.Add($"Buyer reference: {number}");
        }

        if (LocalTime(Text(o, "questionSubmissionEndDate")) is { } questions)
        {
            parts.Add($"Questions by {questions} (MT)");
        }

        var methods = new List<string>();
        if (Bool(o, "useESubmission"))
        {
            methods.Add("APC e-submission");
        }

        if (Bool(o, "useEmailSubmission"))
        {
            methods.Add(Text(o, "emailSubmissionValue") is { } to ? $"email to {to}" : "email");
        }

        if (Bool(o, "useHardCopySubmission"))
        {
            methods.Add(Text(o, "hardCopySubmissionValue") is { } at ? $"hard copy to {at}" : "hard copy");
        }

        if (Bool(o, "useOtherSubmissionMethod") && Text(o, "otherSubmissionMethodValue") is { } other)
        {
            methods.Add(other);
        }

        if (methods.Count > 0)
        {
            parts.Add("Submission: " + string.Join("; ", methods));
        }

        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
    }

    /// <summary>Procurement documents, then addenda, in the posting's own order.</summary>
    private static IReadOnlyList<DetailDocument> Documents(JsonElement o, string detailUrl)
    {
        var posting = detailUrl.Split('#')[0];
        var docs = new List<DetailDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var listName in new[] { "documents", "addendaDocuments" })
        {
            if (!o.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var d in list.EnumerateArray().OrderBy(d => d.TryGetProperty("order", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0))
            {
                if (Text(d, "deletedOnUtc") is not null || Text(d, "id") is not { } id || !seen.Add(id))
                {
                    continue;
                }

                var title = Text(d, "title");
                var file = Text(d, "filename");
                var name = title is not null && file is not null && !file.StartsWith(title, StringComparison.OrdinalIgnoreCase)
                    ? $"{title} ({file})"
                    : file ?? title ?? id;
                docs.Add(new DetailDocument(name, $"{posting}#document-{id}"));
            }
        }

        return docs;
    }

    private static string? LocalTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)
            : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString()) : null;

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var flat = Spaces.Replace(value.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
        return flat.Length == 0 ? null : flat;
    }
}
