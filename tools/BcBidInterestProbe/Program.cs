// One-shot DOM probe of a BC Bid tender detail page, authenticated as KOR.
// Mirrors BcBidScraper.LoginAsync's login flow exactly:
//   1. GET https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage
//   2. Click "Login" link in top nav
//   3. Click "Business or Basic BCeID" option
//   4. Fill username + password on BCeID gateway
//   5. Click "Continue"
//   6. Wait for redirect back to bcbid.gov.bc.ca authenticated
//   7. Navigate to the tender URL passed on the command line
//   8. Dump page structure (headers, tabs, supplier-like sections, tables)
//
// Reads credentials from env vars (same names as production):
//   KOR_OPPORTUNITIES_BCBIDUSERNAME
//   KOR_OPPORTUNITIES_BCBIDPASSWORD
// Refuses to run if either is empty.
//
// Usage:
//   dotnet run --project tools/BcBidInterestProbe -- <tender-detail-url>
//   dotnet run --project tools/BcBidInterestProbe -- https://bcbid.gov.bc.ca/page.aspx/en/bpm/process_manage_extranet/230247

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BcBidInterestProbe <tender-detail-url>");
    return 2;
}

var tenderUrl = args[0];
var username = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BCBIDUSERNAME");
var password = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BCBIDPASSWORD");
if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("KOR_OPPORTUNITIES_BCBIDUSERNAME and KOR_OPPORTUNITIES_BCBIDPASSWORD must both be set in env.");
    return 2;
}

const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";
const int PageWaitTimeoutMs = 30_000;
var screenshotPath = Path.Combine(Path.GetTempPath(), $"bcbid-probe-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
var diagDir = Path.GetTempPath();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,
});
await using var ctx = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    Locale = "en-CA",
    TimezoneId = "America/Vancouver",
});
var page = await ctx.NewPageAsync();

// === STEP 1-6: replicate BcBidScraper.LoginAsync ===
Console.Error.WriteLine("[LOGIN] navigating to homepage...");
await page.GotoAsync(LoginEntryUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] clicking 'Login' link...");
await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Login", Exact = true })
    .First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] clicking 'Business or Basic BCeID' option...");
await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
{
    NameRegex = new Regex(@"Business\s+or\s+Basic\s+BCeID", RegexOptions.IgnoreCase),
}).First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] waiting for BCeID gateway redirect...");
await page.WaitForURLAsync(
    url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
        || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
    new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] filling credentials...");
await page.Locator("input[type='text']:visible")
    .First.FillAsync(username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs });
await page.Locator("input[type='password']:visible")
    .First.FillAsync(password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] clicking Continue...");
await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" })
    .First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs });

Console.Error.WriteLine("[LOGIN] waiting for redirect back to bcbid.gov.bc.ca...");
await page.WaitForURLAsync(
    url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
        && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
    new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs });
try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 }); } catch { }

Console.Error.WriteLine($"[LOGIN] authenticated. URL: {page.Url}");

// Diagnostic screenshot of post-login landing
await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(diagDir, $"bcbid-postlogin-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png"), FullPage = true });

// === STEP 7: navigate to the tender URL ===
Console.Error.WriteLine($"[NAV] navigating to tender: {tenderUrl}");
await page.GotoAsync(tenderUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = PageWaitTimeoutMs });
await page.WaitForTimeoutAsync(2500);

var landedUrl = page.Url;
var title = await page.TitleAsync();
var bodyText = await page.Locator("body").InnerTextAsync();

// === STEP 8: dump page structure ===
var headersJson = await page.EvaluateAsync<string>(@"() =>
    JSON.stringify(Array.from(document.querySelectorAll('h1, h2, h3, h4')).slice(0, 40).map(h => ({ tag: h.tagName, text: (h.innerText || '').trim().substring(0, 200) })).filter(h => h.text))");
using var headersDoc = JsonDocument.Parse(headersJson);

var tabsJson = await page.EvaluateAsync<string>(@"() =>
    JSON.stringify(Array.from(document.querySelectorAll('[role=tab], .tab, .tabs li, .nav-tabs li')).map(el => ({
        tag: el.tagName,
        text: (el.innerText || '').trim().substring(0, 200),
        href: el.getAttribute('href') || null,
        ariaControls: el.getAttribute('aria-controls') || null,
        ariaSelected: el.getAttribute('aria-selected') || null,
        cls: el.className || null,
    })).filter(t => t.text))");
using var tabsDoc = JsonDocument.Parse(tabsJson);

var sectionsJson = await page.EvaluateAsync<string>(@"() => {
    const interesting = /supplier|interest|bidder|plan|holder|taker|company|firm|partic|q\s*&\s*a|question|invit/i;
    const out = [];
    document.querySelectorAll('h1, h2, h3, h4, h5, [role=heading], legend, fieldset > label, .section-title, .panel-title').forEach(el => {
        const t = (el.innerText || '').trim();
        if (t && interesting.test(t) && t.length < 200) {
            out.push({ tag: el.tagName, text: t, cls: el.className || '', id: el.id || '' });
        }
    });
    return JSON.stringify(out);
}");
using var sectionsDoc = JsonDocument.Parse(sectionsJson);

var tablesJson = await page.EvaluateAsync<string>(@"() => {
    const out = [];
    document.querySelectorAll('table').forEach(t => {
        const headers = Array.from(t.querySelectorAll('thead th, thead td, tr:first-child th')).map(h => (h.innerText || '').trim());
        const sample = Array.from(t.querySelectorAll('tbody tr')).slice(0, 3).map(tr =>
            Array.from(tr.querySelectorAll('td')).map(td => (td.innerText || '').trim()));
        out.push({ id: t.id || '', cls: t.className || '', headers, sampleRows: sample });
    });
    return JSON.stringify(out);
}");
using var tablesDoc = JsonDocument.Parse(tablesJson);

await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });

var result = new
{
    landedUrl,
    title,
    bodyTextLength = bodyText.Length,
    bodyTextSnippet = bodyText.Length > 3000 ? bodyText.Substring(0, 3000) : bodyText,
    pageHeaders = headersDoc.RootElement.Clone(),
    tabs = tabsDoc.RootElement.Clone(),
    interestingSectionHeadings = sectionsDoc.RootElement.Clone(),
    tablesFound = tablesDoc.RootElement.Clone(),
    screenshotPath,
};

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;
