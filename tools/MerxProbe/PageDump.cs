// Second probe mode: dump readable text of arbitrary pages through a real
// browser (beats bot-walls that 403 plain HTTP: MERX detail pages, canada.ca
// CSP/CGP program pages). Usage:
//   dotnet run --project tools/MerxProbe -- pages <outDir> <url1> <url2> ...
using Microsoft.Playwright;

internal static class PageDump
{
    public static async Task<int> RunAsync(string outDir, string[] urls)
    {
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
        });
        var page = await context.NewPageAsync();
        var n = 0;
        foreach (var url in urls)
        {
            n++;
            try
            {
                Console.WriteLine($"== [{n}] {url}");
                var resp = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
                Console.WriteLine($"   status={resp?.Status}");
                await page.WaitForTimeoutAsync(2000);
                var text = await page.InnerTextAsync("body");
                var name = $"page-{n:00}-" + new Uri(page.Url).Host.Replace('.', '_') + ".txt";
                File.WriteAllText(Path.Combine(outDir, name), $"URL: {page.Url}\nSTATUS: {resp?.Status}\nTITLE: {await page.TitleAsync()}\n\n{text}");
                Console.WriteLine($"   saved {text.Length:N0} chars -> {name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   FAILED: {ex.Message}");
            }
        }
        return 0;
    }
}
