using System.CommandLine;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Playwright;

var htmlOpt = new Option<FileInfo>("--html", "Path to an HTML file containing one or more BC Bid <tr> rows")
{
    IsRequired = true,
};
var baseUrlOpt = new Option<string>(
    "--base-url",
    () => "https://bcbid.gov.bc.ca/page.aspx/en/rfp/request_browse_public",
    "Base URL used to resolve relative hrefs.");
var buyerOpt = new Option<string>("--buyer", () => "TestBuyer", "Buyer string passed to the row mapper.");

var root = new RootCommand("Scraper probe  runs row extraction against a saved HTML file.");
root.AddOption(htmlOpt);
root.AddOption(baseUrlOpt);
root.AddOption(buyerOpt);

root.SetHandler(async (FileInfo htmlFile, string baseUrl, string buyer) =>
{
    if (!htmlFile.Exists)
    {
        Console.Error.WriteLine($"File not found: {htmlFile.FullName}");
        Environment.Exit(1);
        return;
    }

    var raw = await File.ReadAllTextAsync(htmlFile.FullName);
    var wrapped = $"<!doctype html><html><body><table><tbody>{raw}</tbody></table></body></html>";

    using var pw = await Playwright.CreateAsync();
    await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    var page = await browser.NewPageAsync();
    await page.SetContentAsync(wrapped);

    var rows = await page.QuerySelectorAllAsync("tr[id*='_grd_tr_']");
    Console.WriteLine($"Rows matched: {rows.Count}");
    var baseUri = new Uri(baseUrl);
    var i = 0;
    foreach (var row in rows)
    {
        var candidate = await BcBidHistoricalScraper.TryMapRowForProbeAsync(row, baseUri, buyer);
        if (candidate is null)
        {
            Console.WriteLine($"[{++i}] (skipped  TryMapRow returned null)");
            continue;
        }

        Console.WriteLine($"[{++i}]");
        Console.WriteLine($"  ExternalRef:  {candidate.ExternalReference}");
        Console.WriteLine($"  Title:        {candidate.Title}");
        Console.WriteLine($"  Buyer:        {candidate.Buyer}");
        Console.WriteLine($"  Url:          {candidate.Url}");
        Console.WriteLine($"  Description:  {candidate.Description}");
        Console.WriteLine($"  Posted:       {candidate.PostedDateUtc:yyyy-MM-dd HH:mm zzz}");
        Console.WriteLine($"  Deadline:     {candidate.SubmissionDeadlineUtc:yyyy-MM-dd HH:mm zzz}");
        Console.WriteLine($"  ProjectProv:  {candidate.ProjectProvince}");
    }
}, htmlOpt, baseUrlOpt, buyerOpt);

return await root.InvokeAsync(args);
