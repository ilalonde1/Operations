using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

// Read-only live probe using the production scrape, including its existing page limits.
// Covers returned candidates and deadline diagnostics, not DB persistence, historical
// rows or portal completeness. A pagination fault can still omit unseen postings.
internal static class ApcDeadlineProbe
{
    public static async Task<int> RunAsync(string outputDirectory, string? snapshotPath = null)
    {
        Directory.CreateDirectory(outputDirectory);
        await using var pool = new PlaywrightBrowserPool(NullLogger<PlaywrightBrowserPool>.Instance);
        await using var context = await pool.AcquireContextAsync(CancellationToken.None);
        var page = await context.NewPageAsync();
        if (snapshotPath is not null)
        {
            var html = await File.ReadAllTextAsync(snapshotPath);
            await page.RouteAsync("**/*", route => route.Request.ResourceType == "document"
                ? route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html })
                : route.AbortAsync());
        }
        var source = new OpportunitySource
        {
            Name = "APC_AllBuyers",
            SourceType = OpportunitySourceType.AlbertaPurchasingConnection,
            BaseUrl = "https://purchasing.alberta.ca/search",
            RequestTimeoutSeconds = 60,
        };
        var config = new Dictionary<string, string>
        {
            ["apc.buyer"] = "Government of Alberta (APC)",
            ["playwright.maxPages"] = "20",
        };
        var scraper = new AlbertaPurchasingScraper(pool, NullLogger<AlbertaPurchasingScraper>.Instance);
        // Keep production visibility unchanged; use the same entry point as FetchAsync
        // while retaining the page for DOM evidence and the old-selector measurement.
        var method = typeof(AlbertaPurchasingScraper).GetMethod("ScrapeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IngestionRunDiagnostics.BeginRun();
        var watch = Stopwatch.StartNew();
        var candidates = await (Task<IReadOnlyList<OpportunityCandidate>>)method.Invoke(
            scraper, new object[] { page, source, config, CancellationToken.None })!;
        watch.Stop();
        var warnings = IngestionRunDiagnostics.Drain();
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "listing.html"), await page.ContentAsync());
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "listing.png") });
        var links = await page.QuerySelectorAllAsync("a[href*='/posting/AB-']");
        var oldAncestors = 0;
        foreach (var link in links)
        {
            if (await link.QuerySelectorAsync("xpath=ancestor::*[self::tr or self::li or @role='row'][1]") is not null)
                oldAncestors++;
        }
        var report = new
        {
            capturedAtUtc = DateTimeOffset.UtcNow,
            source.BaseUrl,
            snapshotPath,
            elapsedSeconds = watch.Elapsed.TotalSeconds,
            total = candidates.Count,
            withDeadline = candidates.Count(c => c.SubmissionDeadlineUtc.HasValue),
            finalPageLinks = links.Count,
            finalPageOldAncestors = oldAncestors,
            warnings,
            candidates,
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "candidates.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"APC: {report.withDeadline}/{report.total} deadlines in {report.elapsedSeconds:F2}s; " +
            $"final page old ancestors {oldAncestors}/{links.Count}; warnings: {string.Join(" | ", warnings)}");
        return candidates.Count == 0 ? 1 : 0;
    }
}
