#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Authenticated-session APC award scraper. Browser authentication is supplied
/// by captured Playwright storage state; this scraper never enters credentials.
/// </summary>
public sealed class AlbertaPurchasingAwardsScraper : PlaywrightScraperBase<AwardCandidate>, IAwardProvider
{
    private const int DefaultMaxPages = 5;
    private const int PageWaitTimeoutMs = 30_000;
    private const int PostingWaitTimeoutMs = 15_000;

    private static readonly Regex PostingReferenceRegex = new(
        @"/posting/(AB-\d{4}-\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AwardedVendorRegex = new(
        @"^(?:Awarded\s+to|Vendor|Winner)\s*:?\s*(?<vendor>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContractValueRegex = new(
        @"\$[\d,]+(?:\.\d{2})?",
        RegexOptions.Compiled);

    private static readonly Regex DateLikeRegex = new(
        @"\b(?:\d{4}-\d{1,2}-\d{1,2}|[A-Z][a-z]{2,8}\s+\d{1,2},?\s+\d{4}|\d{1,2}/\d{1,2}/\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<AlbertaPurchasingAwardsScraper> _logger;

    public AlbertaPurchasingAwardsScraper(
        PlaywrightBrowserPool pool,
        ILogger<AlbertaPurchasingAwardsScraper> logger)
        : base(pool, logger)
    {
        _logger = logger;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.AlbertaPurchasingConnectionAwards;

    protected override async Task<IReadOnlyList<AwardCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        if (SessionExpired(page.Url))
        {
            _logger.LogWarning(
                "APC awards source {SourceName} redirected to a sign-in page. Re-capture Playwright storage state.",
                source.Name);
            await TryWriteDiagnosticAsync(page, "APCAwards-sessionexpired", ct).ConfigureAwait(false);
            return Array.Empty<AwardCandidate>();
        }

        if (!await WaitForPostingsAsync(page).ConfigureAwait(false))
        {
            await TryWriteDiagnosticAsync(page, "APCAwards-norows", ct).ConfigureAwait(false);
            return Array.Empty<AwardCandidate>();
        }

        // APC paginates by 10 per page by default with internal AJAX state
        // (not URL-driven). Bump the page-size <select> to its max so we get
        // the full set in a single render. The dropdown lives at
        // <select class="page-size-options"> and offers values 5/10/25/50/100.
        // Validated 2026-05-21: full Alberta open set was 100 postings, all
        // ingested in one scrape (was 10/run before the page-size bump).
        await TrySelectMaxPageSizeAsync(page).ConfigureAwait(false);

        var buyer = ResolveBuyer(source, sourceConfig);
        var candidates = new List<AwardCandidate>();
        var baseUri = new Uri(source.BaseUrl);
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();

            var pageCandidates = await ExtractPageAsync(page, baseUri, buyer, ct).ConfigureAwait(false);
            candidates.AddRange(pageCandidates);

            if (!await TryAdvanceToNextPageAsync(page, ct).ConfigureAwait(false))
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            await TryWriteDiagnosticAsync(page, "APCAwards-nocandidates", ct).ConfigureAwait(false);
        }

        return candidates;
    }

    private static bool SessionExpired(string url)
    {
        return url.Contains("/supplier-login", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/signin", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBuyer(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        return sourceConfig.TryGetValue("apc.buyer", out var configuredBuyer)
            && !string.IsNullOrWhiteSpace(configuredBuyer)
            ? configuredBuyer.Trim()
            : source.Name;
    }

    private static async Task<bool> WaitForPostingsAsync(IPage page)
    {
        var selectors = new[]
        {
            "a[href*='/posting/AB-']",
            "table tbody tr td",
        };

        foreach (var selector in selectors)
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                {
                    Timeout = PostingWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                // APC may render postings as links or table rows depending on the view.
            }
        }

        return false;
    }

    private static async Task TrySelectMaxPageSizeAsync(IPage page)
    {
        try
        {
            var select = page.Locator("select.page-size-options").First;
            if (await select.CountAsync().ConfigureAwait(false) == 0)
            {
                return;
            }

            // Available values per current markup: 5 / 10 / 25 / 50 / 100.
            // Pick the largest available so the full result set lands in one render.
            await select.SelectOptionAsync(new[] { "100" }, new LocatorSelectOptionOptions
            {
                Timeout = PostingWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Angular re-renders the list after the change. Wait briefly for
            // the AJAX to settle.
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PostingWaitTimeoutMs,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best-effort; falls back to the default page size if anything fails.
        }
        catch (PlaywrightException)
        {
            // Selector or option not present in some tenant variants. Same fallback.
        }
    }

    private static async Task<List<AwardCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        string buyer,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var links = await page.QuerySelectorAllAsync("a[href*='/posting/AB-']").ConfigureAwait(false);
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AwardCandidate>(links.Count);

        foreach (var link in links)
        {
            ct.ThrowIfCancellationRequested();

            var href = (await link.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
            var externalReference = ParseExternalReference(href);
            var sourceUrl = TryResolveUrl(baseUri, href);
            if (string.IsNullOrWhiteSpace(externalReference)
                || string.IsNullOrWhiteSpace(sourceUrl)
                || !seenReferences.Add(externalReference))
            {
                continue;
            }

            var title = NormalizeText(await link.InnerTextAsync().ConfigureAwait(false));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var containerText = await ReadContainerTextAsync(link).ConfigureAwait(false);
            result.Add(new AwardCandidate
            {
                ExternalReference = externalReference,
                Title = title,
                SolicitationType = null,
                AwardingOrganization = buyer,
                AwardedToOrganization = FindAwardedToOrganization(containerText) ?? "Unknown",
                ContractValue = FindContractValue(containerText),
                ContractCurrency = "CAD",
                AwardedAtUtc = FindDate(containerText),
                IssuingLocation = null,
                SupplierAddress = null,
                ContactEmail = null,
                ContractNumber = null,
                SourceUrl = sourceUrl,
                RawJson = $"{title}|{href}",
            });
        }

        return result;
    }

    private static async Task<string?> ReadContainerTextAsync(IElementHandle link)
    {
        var container = await link
            .QuerySelectorAsync("xpath=ancestor::*[self::tr or self::li or @role='row'][1]")
            .ConfigureAwait(false);
        return container is null
            ? null
            : await container.InnerTextAsync().ConfigureAwait(false);
    }

    private static string? FindAwardedToOrganization(string? containerText)
    {
        if (string.IsNullOrWhiteSpace(containerText))
        {
            return null;
        }

        var lines = containerText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeText)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var match = AwardedVendorRegex.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var inlineVendor = match.Groups["vendor"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(inlineVendor))
            {
                return inlineVendor;
            }

            if (i + 1 < lines.Count)
            {
                return lines[i + 1];
            }
        }

        var normalized = NormalizeText(containerText);
        var inlineMatch = Regex.Match(
            normalized,
            @"(?:Awarded\s+to|Vendor|Winner)\s*:?\s*(?<vendor>.+?)(?=\s+\$|\s+\d{4}-\d{1,2}-\d{1,2}|\s+[A-Z][a-z]{2,8}\s+\d{1,2},?\s+\d{4}|$)",
            RegexOptions.IgnoreCase);
        return inlineMatch.Success ? inlineMatch.Groups["vendor"].Value.Trim() : null;
    }

    private static decimal? FindContractValue(string? containerText)
    {
        if (string.IsNullOrWhiteSpace(containerText))
        {
            return null;
        }

        var match = ContractValueRegex.Match(containerText);
        if (!match.Success)
        {
            return null;
        }

        var cleaned = match.Value.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? FindDate(string? containerText)
    {
        if (string.IsNullOrWhiteSpace(containerText))
        {
            return null;
        }

        foreach (Match match in DateLikeRegex.Matches(containerText))
        {
            if (DateTimeOffset.TryParse(
                match.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ParseExternalReference(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = PostingReferenceRegex.Match(href);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryResolveUrl(Uri baseUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return Uri.TryCreate(baseUri, href, out var uri) ? uri.AbsoluteUri : null;
    }

    private static async Task<bool> TryAdvanceToNextPageAsync(IPage page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var nextLocators = new[]
        {
            page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.Locator("a.next-page, button.next, li.next a, [aria-label*='Next' i]"),
        };

        foreach (var locator in nextLocators)
        {
            try
            {
                if (await locator.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                var next = locator.First;
                if (await IsDisabledAsync(next).ConfigureAwait(false))
                {
                    continue;
                }

                await next.ClickAsync(new LocatorClickOptions
                {
                    Timeout = PostingWaitTimeoutMs,
                }).ConfigureAwait(false);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                continue;
            }
        }

        return false;
    }

    private static async Task<bool> IsDisabledAsync(ILocator locator)
    {
        if (await locator.IsDisabledAsync().ConfigureAwait(false))
        {
            return true;
        }

        var ariaDisabled = await locator.GetAttributeAsync("aria-disabled").ConfigureAwait(false);
        if (string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var className = await locator.GetAttributeAsync("class").ConfigureAwait(false);
        return className?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizeText(string text) =>
        Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    private static int ResolveInt(IReadOnlyDictionary<string, string> config, string key, int defaultValue)
        => config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static async Task TryWriteDiagnosticAsync(IPage page, string stem, CancellationToken ct)
    {
        try
        {
            var diagnosticsDir = Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(diagnosticsDir, $"{stem}-{stamp}.png"),
                FullPage = true,
            }).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"{stem}-{stamp}.url.txt"),
                page.Url,
                ct).ConfigureAwait(false);
            var html = await page.ContentAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"{stem}-{stamp}.html"),
                html,
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostic only.
        }
    }
}
