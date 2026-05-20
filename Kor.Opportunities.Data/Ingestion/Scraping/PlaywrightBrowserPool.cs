#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Singleton wrapper around a Playwright instance + a shared headless Chromium
/// browser. Vends an isolated <see cref="IBrowserContext"/> per scrape so cookies
/// from one portal don't leak to the next. Lazy-initialised on first acquire.
///
/// Dispose order: contexts  browser  playwright. The pool itself implements
/// IAsyncDisposable; the host calls it on app shutdown.
/// </summary>
public sealed class PlaywrightBrowserPool : IAsyncDisposable
{
    private readonly ILogger<PlaywrightBrowserPool> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightBrowserPool(ILogger<PlaywrightBrowserPool> logger)
    {
        _logger = logger;
    }

    /// <summary>Get a fresh, isolated browser context. The caller MUST dispose it.
    /// Each context has its own cookie jar and storage - safe for cross-portal use.</summary>
    public async Task<IBrowserContext> AcquireContextAsync(CancellationToken ct)
    {
        await EnsureBrowserAsync(ct).ConfigureAwait(false);

        // Realistic browser fingerprint; some portals reject obvious headless UAs.
        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-CA",
            TimezoneId = "America/Vancouver",
            JavaScriptEnabled = true,
            IgnoreHTTPSErrors = false,
        }).ConfigureAwait(false);

        context.SetDefaultTimeout(45_000); // 45s per action; portals can be slow

        return context;
    }

    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null && _browser.IsConnected) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_browser is not null && _browser.IsConnected) return;

            _logger.LogInformation("PlaywrightBrowserPool: launching Chromium...");
            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // Reasonable defaults for headless scraping on Windows Server.
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-dev-shm-usage",
                },
            }).ConfigureAwait(false);
            _logger.LogInformation(
                "PlaywrightBrowserPool: Chromium launched, version {Version}.",
                _browser.Version);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            try { await _browser.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Browser dispose failed."); }
        }

        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
