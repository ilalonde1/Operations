#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

public abstract class PlaywrightScraperBase<TCandidate>
{
    private readonly PlaywrightBrowserPool _pool;
    private readonly ILogger _logger;

    protected PlaywrightScraperBase(PlaywrightBrowserPool pool, ILogger logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public abstract OpportunitySourceType SourceType { get; }

    protected abstract Task<IReadOnlyList<TCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct);

    public async Task<IReadOnlyList<TCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var storageStatePath = sourceConfig.TryGetValue("playwright.storageStateFile", out var configuredStorageStatePath)
            ? configuredStorageStatePath
            : null;
        await using var context = await _pool.AcquireContextAsync(storageStatePath, ct).ConfigureAwait(false);
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
        catch { }
    }
}
