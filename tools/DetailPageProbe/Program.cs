// Generic detail-page inspector: dumps innerText + link inventory for a URL so a
// parser can be written against the REAL DOM, not a guess. Read-only.
//   dotnet run --project tools/DetailPageProbe -- <url> [<url> ...]

using Microsoft.Playwright;

if (args.Length == 0) { Console.Error.WriteLine("usage: DetailPageProbe <url> [...]"); return 2; }

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true, Channel = "msedge" });
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
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 45_000 });
        await page.WaitForTimeoutAsync(2500);
        var json = await page.EvaluateAsync<string>(@"() => JSON.stringify({
            title: document.title,
            len: (document.body?document.body.innerText:'').length,
            text: (document.body?document.body.innerText:'').substring(0,2600),
            links: Array.from(document.querySelectorAll('a[href]')).map(a=>({t:(a.innerText||'').trim().substring(0,80),h:a.href})).filter(l=>l.h).slice(0,400)
        })");
        Console.WriteLine($"\n=================== {url} ===================");
        Console.WriteLine($"landed: {page.Url}");
        Console.WriteLine(json.Length > 6000 ? json[..6000] : json);
    }
    catch (Exception ex) { Console.WriteLine($"ERR {url}: {ex.Message}"); }
    finally { await page.CloseAsync(); }
}
return 0;
