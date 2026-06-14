#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page extractor for BC Bid "Plan Takers" / plan holder lists.
/// Selectors are intentionally diagnostic-first until the live DOM is
/// confirmed from production probe dumps.
/// </summary>
public sealed class BcBidPlanTakerExtractor
{
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly BcBidCredentials _credentials;
    private readonly ILogger<BcBidPlanTakerExtractor> _logger;
    private int _diagnosticWritten;

    public BcBidPlanTakerExtractor(
        BcBidCredentials credentials,
        ILogger<BcBidPlanTakerExtractor> logger)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LoginAsync(IPage page, CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            return;
        }

        try
        {
            await page.GotoAsync(LoginEntryUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                Name = "Login",
                Exact = true,
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    @"Business\s+or\s+Basic\s+BCeID",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            await page.WaitForURLAsync(
                url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            await page.Locator("input[type='text']:visible")
                .First.FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
            await page.Locator("input[type='password']:visible")
                .First.FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
            {
                Name = "Continue",
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            await page.WaitForURLAsync(
                url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                    && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 10_000 }).ConfigureAwait(false);
            }
            catch { }
        }
        catch (Exception ex)
        {
            await TryWriteDiagnosticAsync(page, "login-fail", ct).ConfigureAwait(false);
            throw new InvalidOperationException("BC Bid login failed: " + ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<ScrapedSupplier>> ExtractAsync(
        IPage page,
        string detailUrl,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await page.GotoAsync(detailUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45_000,
        }).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(2500).ConfigureAwait(false);

        string json;
        try
        {
            json = await page.EvaluateAsync<string>(@"() => {
                const planWords = /plan\s*(taker|holder)|document\s*taker|bidder|interested/i;
                const rows = [];
                const seen = new Set();

                function add(name, descriptor, rawText) {
                    name = (name || '').trim();
                    rawText = (rawText || '').trim();
                    if (!name || name.length < 2 || seen.has(name.toLowerCase())) return;
                    seen.add(name.toLowerCase());
                    rows.push({ name, descriptor: (descriptor || '').trim(), rawText });
                }

                for (const row of Array.from(document.querySelectorAll('table tr'))) {
                    const raw = (row.innerText || '').trim();
                    if (!raw || raw.length < 3) continue;
                    const tableText = (row.closest('table')?.innerText || '');
                    if (!planWords.test(tableText) && !planWords.test(raw)) continue;
                    const cells = Array.from(row.querySelectorAll('td,th'))
                        .map(td => (td.innerText || '').trim())
                        .filter(Boolean);
                    if (cells.length === 0) continue;
                    if (/company|supplier|vendor|plan/i.test(cells[0]) && cells.length < 3) continue;
                    add(cells[0], cells.slice(1).join(' | '), raw);
                }

                for (const el of Array.from(document.querySelectorAll(
                    '.plan-taker, .plan-takers li, .plan-holders li, .plan-holder, .bidder, .bidders li, [class*=""plan""][class*=""taker""], [class*=""plan""][class*=""holder""]'))) {
                    const raw = (el.innerText || '').trim();
                    if (!raw) continue;
                    const lines = raw.split(/\r?\n/).map(s => s.trim()).filter(Boolean);
                    if (lines.length === 0) continue;
                    add(lines[0], lines.slice(1).join(' | '), raw);
                }

                return JSON.stringify(rows);
            }").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BC Bid plan taker selector probe failed for {Url}", detailUrl);
            await TryWriteDiagnosticAsync(page, "probe", ct).ConfigureAwait(false);
            return Array.Empty<ScrapedSupplier>();
        }

        IReadOnlyList<ScrapedSupplier> suppliers = Array.Empty<ScrapedSupplier>();
        if (!string.IsNullOrWhiteSpace(json) && json != "[]")
        {
            try
            {
                suppliers = JsonSerializer.Deserialize<ScrapedSupplier[]>(json, JsonOpts)
                    ?? Array.Empty<ScrapedSupplier>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse BC Bid plan taker JSON for {Url}", detailUrl);
            }
        }

        if (Interlocked.Exchange(ref _diagnosticWritten, 1) == 0 || suppliers.Count == 0)
        {
            await TryWriteDiagnosticAsync(page, "probe", ct).ConfigureAwait(false);
        }

        return suppliers;
    }

    private async Task TryWriteDiagnosticAsync(IPage page, string suffix, CancellationToken ct)
    {
        try
        {
            var diagDir = Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            Directory.CreateDirectory(diagDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var prefix = Path.Combine(diagDir, $"BcBidPlanTakers-{suffix}-{stamp}");

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = prefix + ".png",
                FullPage = true,
            }).ConfigureAwait(false);

            await File.WriteAllTextAsync(prefix + ".html", await page.ContentAsync().ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(prefix + ".txt", await page.EvaluateAsync<string>(
                "() => document.body ? document.body.innerText : ''").ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(prefix + ".url.txt", page.Url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BC Bid plan taker diagnostic dump failed");
        }
    }
}
