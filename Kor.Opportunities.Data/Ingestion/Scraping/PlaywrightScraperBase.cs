#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Base class for Playwright-driven portal scrapers. Handles browser context
/// acquisition, page lifecycle, top-level try/catch with diagnostic screenshot
/// on failure, and logging. Subclasses implement <see cref="ScrapeAsync"/>
/// against an already-opened IPage.
/// </summary>
public abstract class PlaywrightScraperBase : IOpportunityProvider
{
    private readonly PlaywrightBrowserPool _pool;
    private readonly ILogger _logger;

    protected PlaywrightScraperBase(PlaywrightBrowserPool pool, ILogger logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public abstract OpportunitySourceType SourceType { get; }

    /// <summary>Subclass-supplied scrape logic. The page is already attached
    /// to a fresh, isolated context. Subclass owns navigation, waits, and DOM
    /// extraction; everything around it (context, page disposal, error
    /// handling, screenshot) is handled by the base class.</summary>
    protected abstract Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct);

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await using var context = await _pool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        try
        {
            var candidates = await ScrapeAsync(page, source, sourceConfig, ct).ConfigureAwait(false);
            sw.Stop();

            _logger.LogInformation(
                "Playwright scrape {Source} ({Type}): {Count} candidate(s) in {Elapsed}ms.",
                source.Name, SourceType, candidates.Count, sw.ElapsedMilliseconds);

            return candidates;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await TryDiagnosticScreenshotAsync(page, source).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Playwright scrape {Source} ({Type}) failed after {Elapsed}ms: {Message}",
                source.Name, SourceType, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private async Task TryDiagnosticScreenshotAsync(IPage page, OpportunitySource source)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{source.Name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true })
                .ConfigureAwait(false);
            _logger.LogInformation("Diagnostic screenshot saved: {Path}", path);
        }
        catch
        {
            // Best-effort - don't let diagnostic failures mask the original error.
        }
    }
}
