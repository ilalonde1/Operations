using System.CommandLine;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Playwright;

var htmlOpt = new Option<FileInfo?>("--html", "Path to an HTML file containing one or more BC Bid <tr> rows");
var detailHtmlOpt = new Option<FileInfo?>(
    "--detail-html",
    "Path to a saved BC Bid historical detail-page HTML file. Runs the BcBidHistoricalDetailExtractor against it.");
var baseUrlOpt = new Option<string>(
    "--base-url",
    () => "https://bcbid.gov.bc.ca/page.aspx/en/rfp/request_browse_public",
    "Base URL used to resolve relative hrefs.");
var buyerOpt = new Option<string>("--buyer", () => "TestBuyer", "Buyer string passed to the row mapper.");

var root = new RootCommand("Scraper probe  runs row extraction against a saved HTML file.");
root.AddOption(htmlOpt);
root.AddOption(detailHtmlOpt);
root.AddOption(baseUrlOpt);
root.AddOption(buyerOpt);

root.SetHandler(async (FileInfo? htmlFile, string baseUrl, string buyer, FileInfo? detailHtml) =>
{
    if (detailHtml is not null)
    {
        if (!detailHtml.Exists)
        {
            Console.Error.WriteLine($"File not found: {detailHtml.FullName}");
            Environment.Exit(1);
            return;
        }

        using var pw2 = await Playwright.CreateAsync();
        await using var browser2 = await pw2.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page2 = await browser2.NewPageAsync();
        var uri = new Uri(detailHtml.FullName).AbsoluteUri;
        await page2.GotoAsync(uri);

        var extraction = await BcBidHistoricalDetailExtractor.ExtractAsync(page2);

        Console.WriteLine("=== BcBidHistoricalDetailExtractor ===");
        Console.WriteLine($"  NoticeType:           {extraction.NoticeType}");
        Console.WriteLine($"  Commodities:          {extraction.Commodities}");
        Console.WriteLine($"  AmendmentCount:       {extraction.AmendmentCount}");
        Console.WriteLine($"  EstimatedAmountText:  {extraction.EstimatedAmountText}");
        Console.WriteLine($"  InitialDurationMths:  {extraction.InitialDurationMonths}");
        Console.WriteLine($"  Background:           {Snip(extraction.Background)}");
        Console.WriteLine($"  Scope:                {Snip(extraction.Scope)}");
        Console.WriteLine($"  MinistryResp:         {Snip(extraction.MinistryResponsibility)}");
        Console.WriteLine($"  FullDescription:      {Snip(extraction.FullDescription)}");
        Console.WriteLine($"  Documents ({extraction.Documents.Count}):");
        foreach (var d in extraction.Documents)
        {
            Console.WriteLine($"    - {d.FileName}  =>  {d.SourceUrl}");
        }

        return;
    }

    if (htmlFile is null)
    {
        Console.Error.WriteLine("Missing required option: --html");
        Environment.Exit(1);
        return;
    }

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
}, htmlOpt, baseUrlOpt, buyerOpt, detailHtmlOpt);

static string Snip(string? s) =>
    string.IsNullOrEmpty(s) ? "(null)" :
    s.Length > 120 ? s.Substring(0, 120).Replace("\n", " ") + "..." : s.Replace("\n", " ");

return await root.InvokeAsync(args);
