// One-shot DOM probe of an APC posting detail page. Goal: figure out the
// selectors for the "Expressed Interest" / "View Suppliers" panel before we
// write the production scraper extension. Outputs JSON to stdout with the
// page's interesting elements, headers, and any modal/panel content found
// when we click the expected button.
//
// Usage:
//   dotnet run --project tools/ApcInterestProbe -- <posting-url>
//   dotnet run --project tools/ApcInterestProbe -- https://purchasing.alberta.ca/posting/AB-2026-04147

using System.Text.Json;
using Microsoft.Playwright;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: ApcInterestProbe <posting-url>");
    return 2;
}

var url = args[0];
var screenshotPath = Path.Combine(Path.GetTempPath(), $"apc-probe-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,
});
await using var ctx = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1366, Height = 800 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    Locale = "en-CA",
});
var page = await ctx.NewPageAsync();

await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 45_000 });
await page.WaitForTimeoutAsync(3000);

var title = await page.TitleAsync();
var bodyText = await page.Locator("body").InnerTextAsync();

// Find any element whose text mentions interest / supplier / bidder / plan-taker.
// Return as a JSON-serialized string so we can re-parse and emit cleanly.
var matchesJson = await page.EvaluateAsync<string>(@"() => {
    const isRelevant = t => /interest|supplier|bidder|plan.taker|view document|attach|partner|list|view bus/i.test(t);
    const out = [];
    document.querySelectorAll('a, button, [role=button]').forEach(el => {
        const t = (el.innerText || el.textContent || '').trim();
        const href = el.getAttribute('href') || '';
        // Match either by text OR by URL pattern
        if ((t && isRelevant(t) && t.length < 300) || /interest|partner|list-of|businesses/i.test(href)) {
            out.push({
                tag: el.tagName,
                text: t.substring(0, 200),
                href: el.getAttribute('href') || null,
                cls: el.className || null,
                id: el.id || null,
            });
        }
    });
    return JSON.stringify(out);
}");
using var matchesDoc = JsonDocument.Parse(matchesJson);
var matches = matchesDoc.RootElement.Clone();

// PROBE-2 (post-discovery): the "Interested suppliers" section lives at
// anchor #interested-suppliers further down the page. Scroll to it and dump
// the structured list rows.
string? interestedSectionHtml = null;
string? interestedSectionText = null;
string? interestedListJson = null;
try
{
    await page.EvaluateAsync("() => document.querySelector('#interested-suppliers')?.scrollIntoView({behavior:'instant', block:'start'})");
    await page.WaitForTimeoutAsync(2000);

    var section = page.Locator("#interested-suppliers").First;
    if (await section.CountAsync() > 0)
    {
        // Walk to the nearest containing section (parent looking like a panel)
        var sectionContainer = page.Locator("section:has(#interested-suppliers), div:has(#interested-suppliers), [class*='interested-suppliers' i]").First;
        if (await sectionContainer.CountAsync() == 0) sectionContainer = section;
        interestedSectionText = await sectionContainer.InnerTextAsync();
        if (interestedSectionText.Length > 3000) interestedSectionText = interestedSectionText.Substring(0, 3000);
        var html = await sectionContainer.InnerHTMLAsync();
        interestedSectionHtml = html.Length > 6000 ? html.Substring(0, 6000) : html;
    }

    // Extract supplier rows. Strategy: find the H2 "Interested suppliers",
    // then walk its NEXT siblings (or its parent's next siblings) for the
    // actual list content rather than walking UP (which grabs the whole page).
    interestedListJson = await page.EvaluateAsync<string>(@"() => {
        // 1) Locate the H2 (case-insensitive match)
        const h2 = Array.from(document.querySelectorAll('h2')).find(h => /interested\s*suppliers/i.test((h.innerText || '').trim()));
        if (!h2) return JSON.stringify({ ok: false, reason: 'h2 not found' });

        // 2) Identify the section/container that immediately follows or contains the list.
        // The Angular markup typically wraps the H2 inside an apc-* element; the list
        // sits as a sibling div under the same parent.
        let scope = h2.parentElement;
        while (scope && scope.parentElement && scope.children.length < 3) {
            scope = scope.parentElement;
        }
        if (!scope) return JSON.stringify({ ok: false, reason: 'scope not found' });

        // 3) Collect everything inside this scope that follows the H2.
        const after = [];
        let walker = h2.nextElementSibling;
        while (walker) {
            after.push(walker);
            walker = walker.nextElementSibling;
        }

        // 4) Inside those follow-siblings (and h2's parent if relevant), grab any structured rows.
        const candidates = [];
        const visit = (el) => {
            if (!el || el.nodeType !== 1) return;
            // Stop at the next H2 / H1 (next section)
            if (/^H[12]$/.test(el.tagName) && el !== h2) return;
            candidates.push(el);
            for (const child of el.children) visit(child);
        };
        for (const a of after) visit(a);

        const rows = [];
        const seenText = new Set();
        for (const el of candidates) {
            const text = (el.innerText || '').trim();
            if (!text) continue;
            // Skip the section heading itself + repeats
            if (/interested\s*suppliers\s*list?$/i.test(text) || seenText.has(text)) continue;
            // Heuristics: candidate ROW is either a table tr, a list li, or a styled flex row
            const tag = el.tagName;
            const cls = el.className || '';
            if (tag === 'TR') {
                const cells = Array.from(el.querySelectorAll('td')).map(td => (td.innerText || '').trim()).filter(c => c);
                if (cells.length) {
                    rows.push({ type: 'tableRow', cells });
                    seenText.add(text);
                }
            } else if (tag === 'LI') {
                rows.push({ type: 'listItem', text: text.substring(0, 400) });
                seenText.add(text);
            } else if (/flex|grid|row|supplier|interest/i.test(cls) && text.length < 500 && text.length > 2) {
                // Heuristic flex/grid rows
                rows.push({ type: 'rowLike', cls, text: text.substring(0, 400) });
                seenText.add(text);
            }
        }
        return JSON.stringify({ ok: true, rowsFound: rows.length, rows: rows.slice(0, 50) });
    }");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Interested-suppliers extraction failed: {ex.GetType().Name}: {ex.Message}");
}

// Try clicking the most-likely interest button + capture the panel that appears
string? panelText = null;
string? panelHtml = null;
string? clickedSelector = null;
foreach (var selector in new[] {
    "button:has-text('Expressed Interest')",
    "a:has-text('Expressed Interest')",
    "button:has-text('View Expressed Interests')",
    "button:has-text('Suppliers')",
    "button:has-text('Interested')",
    "button:has-text('Plan Takers')",
})
{
    try
    {
        var loc = page.Locator(selector).First;
        if (await loc.CountAsync() > 0)
        {
            await loc.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
            await page.WaitForTimeoutAsync(2500);
            // Grab anything that looks like a modal or expanded panel
            foreach (var panelSel in new[] {
                "[role=dialog]",
                ".modal",
                ".panel",
                "[class*='interest' i]",
                "[class*='supplier' i]",
                "[class*='bidder' i]",
            })
            {
                var panelLoc = page.Locator(panelSel).First;
                if (await panelLoc.CountAsync() > 0)
                {
                    panelText = (await panelLoc.InnerTextAsync()).Substring(0, Math.Min(2000, (await panelLoc.InnerTextAsync()).Length));
                    panelHtml = (await panelLoc.InnerHTMLAsync()).Substring(0, Math.Min(4000, (await panelLoc.InnerHTMLAsync()).Length));
                    clickedSelector = $"{selector} → {panelSel}";
                    break;
                }
            }
            if (panelText != null) break;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Click attempt for {selector} failed: {ex.GetType().Name}: {ex.Message}");
    }
}

var headers = await page.EvaluateAsync<string[]>(@"() =>
    Array.from(document.querySelectorAll('h1, h2, h3')).slice(0, 30).map(h => (h.innerText || '').trim()).filter(t => t)");

// CanadaBuys-specific: hunt for partner-list section text + structure
var canadaBuysPartnerJson = await page.EvaluateAsync<string>(@"() => {
    // Find section by heading text 'Partner with another business' or similar
    const headings = Array.from(document.querySelectorAll('h2, h3, h4'));
    const partnerHeading = headings.find(h => /partner with|interested in partnering|businesses interested/i.test((h.innerText || '').trim()));
    if (!partnerHeading) return JSON.stringify({ ok: false, reason: 'no partner heading' });

    // Walk to the section's container
    let container = partnerHeading.parentElement || partnerHeading;
    for (let i=0;i<4 && container.parentElement;i++) {
        if (container.children.length >= 3) break;
        container = container.parentElement;
    }
    const sectionHtml = (container.innerHTML || '').substring(0, 6000);
    const sectionText = (container.innerText || '').substring(0, 4000);

    // Look for actual firm-name rows
    const candidates = [];
    container.querySelectorAll('table tr, ul li, div[class*=business], div[class*=company]').forEach(el => {
        const t = (el.innerText || '').trim();
        if (t && t.length > 2 && t.length < 400 && !/partner with|are you interested|add your company|this list does not/i.test(t)) {
            candidates.push({ tag: el.tagName, cls: el.className || '', text: t.substring(0, 300) });
        }
    });

    return JSON.stringify({
        ok: true,
        headingText: (partnerHeading.innerText || '').trim(),
        sectionTextSample: sectionText,
        sectionHtmlSample: sectionHtml,
        candidateRows: candidates.slice(0, 40),
    });
}");

await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });

var result = new
{
    url,
    title,
    pageHeaders = headers,
    relevantElementMatches = matches,
    interestedSuppliersSectionText = interestedSectionText,
    interestedSuppliersSectionHtmlSample = interestedSectionHtml,
    interestedSuppliersListExtracted = interestedListJson,
    canadaBuysPartnerSection = canadaBuysPartnerJson,
    clickedSelector,
    panelTextSample = panelText,
    panelHtmlSample = panelHtml,
    bodyTextSnippet = bodyText.Length > 12000 ? bodyText.Substring(0, 12000) : bodyText,
    screenshotPath,
};

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;
