// Generic detail-page inspector: dumps innerText + link inventory for a URL so a
// parser can be written against the REAL DOM, not a guess. Read-only.
//   dotnet run --project tools/DetailPageProbe -- <url> [<url> ...]

using Microsoft.Playwright;

if (args.Length == 0) { Console.Error.WriteLine("usage: DetailPageProbe <url> [...]"); return 2; }

using var pw = await Playwright.CreateAsync();
var headed = Environment.GetEnvironmentVariable("PROBE_HEADED") == "1";
var channel = Environment.GetEnvironmentVariable("PROBE_CHANNEL");   // "" => Chromium (pool default); "msedge" => Edge
var launch = new BrowserTypeLaunchOptions
{
    Headless = !headed,
    Args = new[] { "--disable-blink-features=AutomationControlled", "--disable-dev-shm-usage" },
};
if (!string.IsNullOrWhiteSpace(channel)) launch.Channel = channel;
await using var browser = await pw.Chromium.LaunchAsync(launch);
Console.Error.WriteLine($"[browser] channel={(string.IsNullOrWhiteSpace(channel) ? "chromium" : channel)} headed={headed}");
await using var ctx = await browser.NewContextAsync(new()
{
    ViewportSize = new() { Width = 1600, Height = 1200 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
});

foreach (var url in args)
{
    var page = await ctx.NewPageAsync();
    try
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        // Cloudflare "Verify you are human" (Turnstile) — try to click the checkbox,
        // then wait for the real page. Turnstile lives in a cross-origin iframe.
        for (var i = 0; i < 30; i++)
        {
            var t = await page.TitleAsync();
            var len = await page.EvaluateAsync<int>("() => (document.body?document.body.innerText:'').length");
            if (!t.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) && len > 500) { Console.Error.WriteLine($"[cf] cleared after {i}s"); break; }
            if (i is 3 or 8 or 14)
            {
                try
                {
                    var fl = page.FrameLocator("iframe[src*='challenges.cloudflare.com'], iframe[title*='Cloudflare'], iframe[title*='challenge']");
                    await fl.Locator("input[type='checkbox'], label").First.ClickAsync(new() { Timeout = 4000 });
                    Console.Error.WriteLine("[cf] clicked turnstile checkbox");
                }
                catch (Exception cx) { Console.Error.WriteLine($"[cf] checkbox click failed: {cx.Message.Split('\n')[0]}"); }
            }
            await page.WaitForTimeoutAsync(1000);
        }
        await page.WaitForTimeoutAsync(1500);
        var json = await page.EvaluateAsync<string>(@"() => JSON.stringify({
            title: document.title,
            len: (document.body?document.body.innerText:'').length,
            text: (document.body?document.body.innerText:'').substring(0,2600),
            links: Array.from(document.querySelectorAll('a[href]')).map(a=>({t:(a.innerText||'').trim().substring(0,80),h:a.href})).filter(l=>l.h).slice(0,400)
        })");
        Console.WriteLine($"\n=================== {url} ===================");
        Console.WriteLine($"landed: {page.Url}");
        Console.WriteLine(json.Length > 6000 ? json[..6000] : json);

        // Optional: click tabs (semicolon-separated in PROBE_CLICK), dump each.
        var clicks = Environment.GetEnvironmentVariable("PROBE_CLICK");
        if (!string.IsNullOrWhiteSpace(clicks))
        {
            foreach (var label in clicks.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var sel = label.Trim();
                    var loc = sel.Contains('[') ? page.Locator(sel) : page.GetByText(sel, new() { Exact = false });
                    await loc.First.ClickAsync(new() { Timeout = 8000 });
                    await page.WaitForTimeoutAsync(4000);
                    var tj = await page.EvaluateAsync<string>(@"() => JSON.stringify({
                        text: (document.body?document.body.innerText:'').substring(0,2200),
                        allLinks: Array.from(document.querySelectorAll('a[href]')).map(a=>({t:(a.innerText||'').trim().substring(0,60),h:a.href})).filter(l=>l.h && !/merx\.com\/(public|dcc\/solicitations\/open-bids)/.test(l.h)).slice(0,40)
                    })");
                    Console.WriteLine($"\n----- after click '{sel}' -----");
                    Console.WriteLine(tj.Length > 3500 ? tj[..3500] : tj);
                }
                catch (Exception cx) { Console.WriteLine($"click '{label}' failed: {cx.Message.Split('\n')[0]}"); }
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"ERR {url}: {ex.Message}"); }
    finally { await page.CloseAsync(); }
}
return 0;
