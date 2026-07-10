// One-shot probe: discover the DOM shape of MERX's public DCC solicitations
// listing so the real MerxDccScraper is written against observed selectors,
// not guesses (validate-by-running doctrine). Uses the installed Edge channel
// so no Playwright browser download is needed.
using Microsoft.Playwright;

var outDir = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
Directory.CreateDirectory(outDir);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,
    Channel = "msedge",
    Args = new[] { "--disable-blink-features=AutomationControlled" },
});
await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
    Locale = "en-CA",
    TimezoneId = "America/Vancouver",
});
var page = await context.NewPageAsync();

foreach (var url in new[] { "https://www.merx.com/dcc", "https://www.merx.com/public/solicitations/open?agency=dcc" })
{
    try
    {
        Console.WriteLine($"== GET {url}");
        var resp = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        Console.WriteLine($"   status={resp?.Status} finalUrl={page.Url}");
        await page.WaitForTimeoutAsync(3000);

        var title = await page.TitleAsync();
        Console.WriteLine($"   title={title}");

        var html = await page.ContentAsync();
        var name = new Uri(page.Url).Host + "-" + Guid.NewGuid().ToString("N")[..6] + ".html";
        var path = Path.Combine(outDir, name);
        File.WriteAllText(path, html);
        Console.WriteLine($"   saved {html.Length:N0} chars -> {path}");

        // Row-shape reconnaissance: count common listing containers.
        foreach (var sel in new[] { "table tr", "[class*='solicitation']", "[class*='tender']", "[class*='opportunit']",
                                    "[class*='result']", "article", "li[class*='row']", "div[class*='row']" })
        {
            var n = await page.Locator(sel).CountAsync();
            if (n > 0) Console.WriteLine($"   {sel} -> {n}");
        }

        // Dump the first plausible row's outerHTML for selector design.
        foreach (var sel in new[] { "table tbody tr", "[class*='solicitation']", "[class*='result-item']", "article" })
        {
            var loc = page.Locator(sel);
            if (await loc.CountAsync() > 1)
            {
                var sample = await loc.Nth(1).EvaluateAsync<string>("el => el.outerHTML");
                File.WriteAllText(Path.Combine(outDir, "sample-row.html"), sample);
                Console.WriteLine($"   sample row ({sel}) -> sample-row.html ({sample.Length} chars)");
                break;
            }
        }
        break; // first successful URL wins
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   FAILED: {ex.Message}");
    }
}
