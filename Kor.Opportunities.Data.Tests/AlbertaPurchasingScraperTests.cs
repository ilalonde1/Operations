using System.Reflection;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Exercises actual DOM selectors and scraper diagnostics on locally captured APC cards.
// Does not cover live navigation/pagination or SQL candidate persistence; a portal
// pagination change that omits a posting will not be caught by these fixtures.
public sealed class AlbertaPurchasingScraperTests : IAsyncLifetime
{
    private readonly PlaywrightBrowserPool _pool = new(NullLogger<PlaywrightBrowserPool>.Instance);
    private IBrowserContext _context = null!;
    private static readonly OpportunitySource Source = new()
    {
        Name = "APC_Test",
        BaseUrl = "https://apc.test/search",
        SourceType = OpportunitySourceType.AlbertaPurchasingConnection,
    };

    public async Task InitializeAsync() => _context = await _pool.AcquireContextAsync(CancellationToken.None);
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _pool.DisposeAsync();
    }

    private static string Fixture()
    {
        using var stream = typeof(AlbertaPurchasingScraperTests).Assembly.GetManifestResourceStream(
            "Kor.Opportunities.Data.Tests.Fixtures.apc-listing-20261008.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(string html)
    {
        var page = await _context.NewPageAsync();
        await page.RouteAsync("**/*", route => route.FulfillAsync(new()
        {
            ContentType = "text/html; charset=utf-8", Body = html,
        }));
        var scraper = new AlbertaPurchasingScraper(_pool, NullLogger<AlbertaPurchasingScraper>.Instance);
        var method = typeof(AlbertaPurchasingScraper).GetMethod("ScrapeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<IReadOnlyList<OpportunityCandidate>>)method.Invoke(scraper,
            new object[] { page, Source, new Dictionary<string, string>(), CancellationToken.None })!;
    }

    [Fact]
    public async Task LiveCards_UseClosingFieldAndMountainDaylightOrStandardTime()
    {
        IngestionRunDiagnostics.BeginRun();
        var rows = await ScrapeAsync(Fixture());
        Assert.Equal(2, rows.Count);
        Assert.Equal("AB-2026-06774", rows[0].ExternalReference);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T22:00:00Z"), rows[0].SubmissionDeadlineUtc);
        Assert.Equal("AB-2026-06767", rows[1].ExternalReference);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T00:00:00Z"), rows[1].SubmissionDeadlineUtc);
        Assert.Empty(IngestionRunDiagnostics.Drain());
    }

    [Fact]
    public async Task LiveCards_TakeTheBuyerAndSummaryFromTheCard_NotTheSourceName()
    {
        var rows = await ScrapeAsync(Fixture());
        Assert.Equal("Infrastructure", rows[0].Buyer);
        Assert.Equal("Alberta Electric System Operator", rows[1].Buyer);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));

        // A card without the organisation line falls back to the source-level buyer, never to nothing.
        var bare = await ScrapeAsync(Fixture().Replace("class=\"fg-text\"", "class=\"gone\""));
        Assert.All(bare, r => Assert.Equal("APC_Test", r.Buyer));
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("bad-date")]
    [InlineData("missing-card")]
    public async Task MissingDeadline_RetainsBothRowsAndReportsReference(string failure)
    {
        var html = BrokenFixture(failure);
        IngestionRunDiagnostics.BeginRun();
        var rows = await ScrapeAsync(html);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].SubmissionDeadlineUtc);
        var warning = Assert.Single(IngestionRunDiagnostics.Drain());
        Assert.Contains("APC closing date missing for", warning);
        Assert.Contains("AB-2026-06774", warning);
    }

    private static string BrokenFixture(string failure) => failure switch
    {
        "missing-field" => Fixture().Replace("Closing Date", "Removed Date"),
        "bad-date" => Fixture().Replace("Oct 23, 2026 • 4:00:00 pm (MT)", "TBD"),
        "missing-card" => Fixture().Replace("apc-opportunity-search-result", "unknown-card"),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    [Fact]
    public async Task MissingDeadline_ReachesRunErrorSummaryWithoutFailingRun()
    {
        var runs = new RecordingRunStore();
        var service = new IngestionService(new MappingStore(), null!, null!, null!, null!, null!,
            runs, null!, NullLogger<IngestionService>.Instance);
        var provider = new ScrapedProvider(async () => await ScrapeAsync(BrokenFixture("bad-date")));
        var result = await service.IngestAsync(provider, Source, null, CancellationToken.None);
        Assert.True(result.Success);
        Assert.True(runs.Success);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(result.ErrorSummary, runs.ErrorSummary);
        Assert.Contains("DEGRADED: APC closing date missing for 1/2 posting(s)", runs.ErrorSummary);
        Assert.Contains("AB-2026-06774", runs.ErrorSummary);
    }

    private sealed class ScrapedProvider(Func<Task<IReadOnlyList<OpportunityCandidate>>> scrape) : IOpportunityProvider
    {
        public OpportunitySourceType SourceType => OpportunitySourceType.AlbertaPurchasingConnection;
        public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(OpportunitySource source,
            IReadOnlyDictionary<string, string> config, CancellationToken ct)
        {
            // Preserve the actual scraper's diagnostics, then skip candidate persistence
            // to isolate the real service's ErrorSummary/run-success handling without SQL.
            return (await scrape()).Select(c => c with { Url = "" }).ToArray();
        }
    }

    private sealed class MappingStore : IOpportunitySourceStore
    {
        public Task<IReadOnlyDictionary<string, string>> GetMappingsAsync(Guid id, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<IReadOnlyList<OpportunitySource>> ListEnabledAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<OpportunitySource?> GetByNameAsync(string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<OpportunitySource> EnsureAsync(OpportunitySource desired, CancellationToken ct) => throw new NotSupportedException();
        public Task EnsureMappingsAsync(Guid id, IReadOnlyDictionary<string, string> mappings, CancellationToken ct) => throw new NotSupportedException();
        public Task SetMappingAsync(Guid id, string key, string value, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunStore : IIngestionRunStore
    {
        public bool? Success { get; private set; }
        public string? ErrorSummary { get; private set; }
        public Task<Guid> StartAsync(string name, string? host, string? correlation, CancellationToken ct)
            => Task.FromResult(Guid.NewGuid());
        public Task CompleteAsync(Guid id, bool success, int insertedCount, int duplicateCount,
            int skippedCount, int failedCount, string? errorSummary, CancellationToken ct)
        {
            Success = success;
            ErrorSummary = errorSummary;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<IngestionRun>> ListRecentAsync(int max, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CloseAbandonedAsync(string hostInstance, string reason, CancellationToken ct) => throw new NotSupportedException();
    }
}
