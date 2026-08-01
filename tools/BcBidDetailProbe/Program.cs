// Live verification of BcBidLiveDetailExtractor.ParseDetail against a REAL
// authenticated BC Bid detail page. Logs in as KOR (env creds), navigates to the
// tender, evaluates {text, links}, and runs the production parser — printing what
// the Phase-2 enricher would persist. Read-only; writes nothing.
//
//   dotnet run --project tools/BcBidDetailProbe -- <processId>   (default 230528 = RIH)

using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Playwright;

var processId = args.Length > 0 ? args[0] : "230528";
var url = $"https://bcbid.gov.bc.ca/page.aspx/en/bpm/process_manage_extranet/{processId}";
var user = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BCBIDUSERNAME");
var pass = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BCBIDPASSWORD");
if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
{
    Console.Error.WriteLine("Set KOR_OPPORTUNITIES_BCBIDUSERNAME and KOR_OPPORTUNITIES_BCBIDPASSWORD.");
    return 2;
}

const int T = 30_000;
using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new()
{
    ViewportSize = new() { Width = 1920, Height = 1080 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    Locale = "en-CA",
    TimezoneId = "America/Vancouver",
});
var page = await ctx.NewPageAsync();

Console.Error.WriteLine("[login] ...");
await page.GotoAsync("https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = T });
await page.GetByRole(AriaRole.Link, new() { Name = "Login", Exact = true }).First.ClickAsync(new() { Timeout = T });
await page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"Business\s+or\s+Basic\s+BCeID", RegexOptions.IgnoreCase) }).First.ClickAsync(new() { Timeout = T });
await page.WaitForURLAsync(u => u.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase) || u.Contains("bceid", StringComparison.OrdinalIgnoreCase), new() { Timeout = T });
await page.Locator("input[type='text']:visible").First.FillAsync(user, new() { Timeout = T });
await page.Locator("input[type='password']:visible").First.FillAsync(pass, new() { Timeout = T });
await page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).First.ClickAsync(new() { Timeout = T });
await page.WaitForURLAsync(u => u.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase) && !u.Contains("logon", StringComparison.OrdinalIgnoreCase), new() { Timeout = T });
try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10_000 }); } catch { }

Console.Error.WriteLine($"[nav] {url}");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 45_000 });
await page.WaitForTimeoutAsync(2500);

var json = await page.EvaluateAsync<string>(@"() => JSON.stringify({
    text: document.body ? document.body.innerText : '',
    links: Array.from(document.querySelectorAll('a[href]')).map(a => ({ text: (a.innerText||'').trim().substring(0,200), href: a.href||'' })).filter(l => l.href)
})");
using var doc = JsonDocument.Parse(json);
var text = doc.RootElement.GetProperty("text").GetString() ?? "";
var links = doc.RootElement.GetProperty("links").EnumerateArray()
    .Select(l => new DetailLink(l.GetProperty("text").GetString() ?? "", l.GetProperty("href").GetString() ?? ""))
    .ToList();

var r = BcBidLiveDetailExtractor.ParseDetail(text, links);
var disc = DisciplineClassifier.Classify(r.CommodityCodes, "RFP RIH ERCP", null);

Console.WriteLine($"\n===== LIVE PARSE of process {processId} (innerText {text.Length} chars, {links.Count} links) =====");
Console.WriteLine($"Discipline (from commodities) : {disc}");
Console.WriteLine($"Commodity codes ({r.CommodityCodes.Count}):");
foreach (var c in r.CommodityCodes) Console.WriteLine($"    {c}");
Console.WriteLine($"Contact name  : {r.ContactName ?? "(none)"}");
Console.WriteLine($"Contact email : {r.ContactEmail ?? "(none)"}");
Console.WriteLine($"Contact phone : {r.ContactPhone ?? "(none)"}");
Console.WriteLine($"Documents ({r.Documents.Count}):");
foreach (var d in r.Documents.Take(15)) Console.WriteLine($"    {d.Name}  ->  {d.Url}");
return 0;
