// Probes a BidsAndTenders tender detail page anonymously and authenticated,
// then dumps the delta. Goal: find out if there's a plan-takers /
// interested-suppliers / document-takers list visible to KOR's free
// supplier account that isn't visible anonymously.
//
// Reads credentials from env vars:
//   KOR_OPPORTUNITIES_BIDSANDTENDERSEMAIL
//   KOR_OPPORTUNITIES_BIDSANDTENDERSPASSWORD
//
// Usage:
//   dotnet run --project tools/BidsAndTendersInterestProbe -- <tender-detail-url>
//   (example) https://metrovancouver.bidsandtenders.ca/Module/Tenders/en/Tender/Detail/9fc4128a-b8cb-4d14-a41d-8c3de8f2fa1d
//
// Output: JSON with sections, headers, tabs, tables and supplier-like
// rows found in both anonymous + authenticated views, plus screenshot
// paths for visual inspection.

using System.Text.Json;
using Microsoft.Playwright;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BidsAndTendersInterestProbe <tender-detail-url>");
    return 2;
}

var tenderUrl = args[0];
var email = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BIDSANDTENDERSEMAIL");
var password = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_BIDSANDTENDERSPASSWORD");
if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("KOR_OPPORTUNITIES_BIDSANDTENDERSEMAIL and KOR_OPPORTUNITIES_BIDSANDTENDERSPASSWORD must both be set in env.");
    return 2;
}

const int PageWaitTimeoutMs = 30_000;
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var diagDir = Path.GetTempPath();
var anonScreenshot = Path.Combine(diagDir, $"bidsandtenders-anon-{stamp}.png");
var authScreenshot = Path.Combine(diagDir, $"bidsandtenders-auth-{stamp}.png");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
await using var ctx = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    Locale = "en-CA",
    TimezoneId = "America/Vancouver",
});
var page = await ctx.NewPageAsync();

// --- BASELINE: anonymous load ---
Console.Error.WriteLine("[ANON] loading tender...");
await page.GotoAsync(tenderUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = PageWaitTimeoutMs });
await page.WaitForTimeoutAsync(2000);
var anonSnapshot = await SnapshotAsync(page);
await page.ScreenshotAsync(new PageScreenshotOptions { Path = anonScreenshot, FullPage = true });

// --- FIND login link ---
Console.Error.WriteLine("[AUTH] looking for login link...");
var loginUrl = await page.EvaluateAsync<string?>(@"() => {
    const candidates = Array.from(document.querySelectorAll('a'));
    // Common BidsAndTenders patterns: 'Login' link in top-right nav
    const a = candidates.find(x => /^\s*login\s*$/i.test((x.innerText || '').trim()))
        ?? candidates.find(x => /^\s*sign\s*in\s*$/i.test((x.innerText || '').trim()))
        ?? candidates.find(x => /Account\/Login/i.test(x.getAttribute('href') || ''));
    return a ? new URL(a.getAttribute('href'), location.href).toString() : null;
}");

if (string.IsNullOrWhiteSpace(loginUrl))
{
    // Common BidsAndTenders fallback: tenant's /Account/Login
    var tenantOrigin = new Uri(tenderUrl).GetLeftPart(UriPartial.Authority);
    loginUrl = $"{tenantOrigin}/Account/Login";
    Console.Error.WriteLine($"[AUTH] no in-page login link found; falling back to {loginUrl}");
}

Console.Error.WriteLine($"[AUTH] going to login page: {loginUrl}");
await page.GotoAsync(loginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = PageWaitTimeoutMs });
await page.WaitForTimeoutAsync(1500);

// --- FILL credentials ---
Console.Error.WriteLine("[AUTH] filling email + password...");
// BidsAndTenders forms typically use name='Email' and name='Password' OR id-based selectors
try
{
    await page.Locator("input[name='Email'], input[type='email']:visible, input[id*='Email' i]").First
        .FillAsync(email, new LocatorFillOptions { Timeout = PageWaitTimeoutMs });
}
catch
{
    // No last-resort "first visible text input" fill — typing credentials into
    // an unrecognized form on an arbitrary page is never acceptable.
    Console.Error.WriteLine($"[AUTH] no recognized login form found at {page.Url}; aborting.");
    return 2;
}
await page.Locator("input[type='password']:visible, input[name='Password'], input[id*='Password' i]").First
    .FillAsync(password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs });

// Submit — multiple candidate selectors
Console.Error.WriteLine("[AUTH] submitting...");
try
{
    await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex(@"login|sign\s*in", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })
        .First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs });
}
catch
{
    await page.Locator("button[type='submit'], input[type='submit']").First.ClickAsync();
}

await page.WaitForTimeoutAsync(2500);
try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 }); } catch { }
Console.Error.WriteLine($"[AUTH] post-login URL: {page.Url}");

// Diagnostic screenshot of where login landed
await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(diagDir, $"bidsandtenders-postlogin-{stamp}.png"), FullPage = true });

// --- AUTH: re-load the tender ---
Console.Error.WriteLine("[AUTH] re-loading tender authenticated...");
await page.GotoAsync(tenderUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = PageWaitTimeoutMs });
await page.WaitForTimeoutAsync(2500);
var authSnapshot = await SnapshotAsync(page);
await page.ScreenshotAsync(new PageScreenshotOptions { Path = authScreenshot, FullPage = true });

var result = new
{
    tenderUrl,
    loginUrl,
    anonymous = anonSnapshot,
    authenticated = authSnapshot,
    bodyLengthDelta = authSnapshot.BodyTextLength - anonSnapshot.BodyTextLength,
    anonScreenshot,
    authScreenshot,
};

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;

// ----------------------------------------------------------------------

static async Task<PageSnapshot> SnapshotAsync(IPage page)
{
    var title = await page.TitleAsync();
    var url = page.Url;
    var bodyText = await page.Locator("body").InnerTextAsync();

    var headersJson = await page.EvaluateAsync<string>(@"() =>
        JSON.stringify(Array.from(document.querySelectorAll('h1, h2, h3, h4'))
            .slice(0, 40)
            .map(h => ({ tag: h.tagName, text: (h.innerText || '').trim().substring(0, 200) }))
            .filter(h => h.text))");
    using var headersDoc = JsonDocument.Parse(headersJson);

    var supplierLikeJson = await page.EvaluateAsync<string>(@"() => {
        const interesting = /supplier|interest|bidder|plan|holder|taker|company|firm|partic|q\s*&\s*a|question|invit|document.taker/i;
        const out = [];
        document.querySelectorAll('h1, h2, h3, h4, h5, [role=heading], legend, label, .panel-title, .section-title, .card-header').forEach(el => {
            const t = (el.innerText || '').trim();
            if (t && interesting.test(t) && t.length < 200) {
                out.push({ tag: el.tagName, text: t, cls: el.className || '', id: el.id || '' });
            }
        });
        return JSON.stringify(out);
    }");
    using var supplierDoc = JsonDocument.Parse(supplierLikeJson);

    var tablesJson = await page.EvaluateAsync<string>(@"() => {
        const out = [];
        document.querySelectorAll('table').forEach(t => {
            const headers = Array.from(t.querySelectorAll('thead th, thead td, tr:first-child th')).map(h => (h.innerText || '').trim());
            const sample = Array.from(t.querySelectorAll('tbody tr')).slice(0, 4).map(tr =>
                Array.from(tr.querySelectorAll('td')).map(td => (td.innerText || '').trim().substring(0, 200)));
            out.push({ id: t.id || '', cls: t.className || '', headers, sampleRows: sample });
        });
        return JSON.stringify(out);
    }");
    using var tablesDoc = JsonDocument.Parse(tablesJson);

    var tabsJson = await page.EvaluateAsync<string>(@"() =>
        JSON.stringify(Array.from(document.querySelectorAll('[role=tab], .nav-tabs li a, .nav-pills li a, .tab-header'))
            .map(el => ({ text: (el.innerText || '').trim().substring(0, 100), href: el.getAttribute('href') || null }))
            .filter(t => t.text))");
    using var tabsDoc = JsonDocument.Parse(tabsJson);

    return new PageSnapshot(
        Url: url,
        Title: title,
        BodyTextLength: bodyText.Length,
        BodyTextSnippet: bodyText.Length > 2500 ? bodyText.Substring(0, 2500) : bodyText,
        Headers: headersDoc.RootElement.Clone(),
        Tabs: tabsDoc.RootElement.Clone(),
        SupplierLikeHeadings: supplierDoc.RootElement.Clone(),
        Tables: tablesDoc.RootElement.Clone());
}

record PageSnapshot(
    string Url,
    string Title,
    int BodyTextLength,
    string BodyTextSnippet,
    JsonElement Headers,
    JsonElement Tabs,
    JsonElement SupplierLikeHeadings,
    JsonElement Tables);
