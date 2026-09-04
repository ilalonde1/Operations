using System.CommandLine;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

var htmlOpt = new Option<FileInfo?>("--html", "Path to an HTML file containing one or more BC Bid <tr> rows");
var detailHtmlOpt = new Option<FileInfo?>(
    "--detail-html",
    "Path to a saved BC Bid historical detail-page HTML file. Runs the BcBidHistoricalDetailExtractor against it.");
var enrichBatchOpt = new Option<int?>(
    "--enrich-batch",
    "Run BcBidHistoricalEnrichmentService for N pending rows.");
var downloadBatchOpt = new Option<int?>(
    "--download-batch",
    "Run BcBidHistoricalDocumentDownloadService for N pending docs.");
var baseUrlOpt = new Option<string>(
    "--base-url",
    () => "https://bcbid.gov.bc.ca/page.aspx/en/rfp/request_browse_public",
    "Base URL used to resolve relative hrefs.");
var buyerOpt = new Option<string>("--buyer", () => "TestBuyer", "Buyer string passed to the row mapper.");
var prosperoOpt = new Option<bool>(
    "--prospero",
    "Run TempestProsperoScraper LIVE against --base-url (a Tempest OurCity/Prospero Search.aspx). "
    + "Prints the applications it would ingest. Read-only.");

var root = new RootCommand("Scraper probe  runs row extraction against a saved HTML file.");
root.AddOption(htmlOpt);
root.AddOption(detailHtmlOpt);
root.AddOption(enrichBatchOpt);
root.AddOption(downloadBatchOpt);
root.AddOption(baseUrlOpt);
root.AddOption(buyerOpt);
root.AddOption(prosperoOpt);

root.SetHandler(async (
    FileInfo? htmlFile,
    string baseUrl,
    string buyer,
    FileInfo? detailHtml,
    int? enrichBatch,
    int? downloadBatch,
    bool prospero) =>
{
    if (prospero)
    {
        using var plf = LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.SingleLine = true; }));
        await using var ppool = new PlaywrightBrowserPool(plf.CreateLogger<PlaywrightBrowserPool>());
        var scraper = new Kor.Opportunities.Data.Ingestion.Scraping.TempestProsperoScraper(
            ppool, plf.CreateLogger<Kor.Opportunities.Data.Ingestion.Scraping.TempestProsperoScraper>());

        var src = new Kor.Opportunities.Core.Models.OpportunitySource
        {
            Id = Guid.NewGuid(),
            Name = "prospero-probe",
            SourceType = Kor.Opportunities.Core.Models.OpportunitySourceType.TempestProspero,
            BaseUrl = baseUrl,
            RequestTimeoutSeconds = 120,
        };

        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prospero.buyer"] = buyer,
            ["playwright.maxPages"] = "5",
        };

        var got = await scraper.FetchAsync(src, cfg, CancellationToken.None);
        Console.WriteLine();
        Console.WriteLine($"APPLICATIONS: {got.Count}");
        foreach (var c in got.Take(12))
        {
            Console.WriteLine($"  {c.PostedDateUtc:yyyy-MM-dd}  {c.ExternalReference,-12} {c.Title}");
            Console.WriteLine($"        {c.Location}");
            Console.WriteLine($"        {(c.Description ?? string.Empty).PadRight(0)[..Math.Min(120, (c.Description ?? string.Empty).Length)]}");
            Console.WriteLine($"        {c.Url}");
        }

        var dated = got.Count(x => x.PostedDateUtc is not null);
        Console.WriteLine();
        Console.WriteLine($"dated: {dated} of {got.Count}; distinct refs: {got.Select(x => x.ExternalReference).Distinct().Count()}");
        return;
    }

    if (enrichBatch is { } n && n > 0)
    {
        var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_CONNECTIONSTRING");
        var user = Environment.GetEnvironmentVariable("BCBID_USERNAME");
        var pass = Environment.GetEnvironmentVariable("BCBID_PASSWORD");
        if (string.IsNullOrWhiteSpace(cs) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Console.Error.WriteLine(
                "Set env vars KOR_OPPORTUNITIES_CONNECTIONSTRING, BCBID_USERNAME, BCBID_PASSWORD.");
            Environment.Exit(2);
            return;
        }

        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        }));
        await using var pool = new PlaywrightBrowserPool(loggerFactory.CreateLogger<PlaywrightBrowserPool>());
        var creds = new BcBidCredentials
        {
            Username = user,
            Password = pass,
        };
        var store = new SqlHistoricalOpportunityStore(cs);
        var docStore = new SqlHistoricalOpportunityDocumentStore(cs);
        var svc = new BcBidHistoricalEnrichmentService(
            pool,
            creds,
            store,
            docStore,
            loggerFactory.CreateLogger<BcBidHistoricalEnrichmentService>());
        var result = await svc.EnrichBatchAsync(n, CancellationToken.None);
        Console.WriteLine(
            $"Enrichment batch result: attempted={result.Attempted} enriched={result.Enriched} failed={result.Failed}");
        return;
    }

    if (downloadBatch is { } dn && dn > 0)
    {
        var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_CONNECTIONSTRING");
        var user = Environment.GetEnvironmentVariable("BCBID_USERNAME");
        var pass = Environment.GetEnvironmentVariable("BCBID_PASSWORD");
        var archive = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_ARCHIVE_ROOT")
            ?? @"C:\OpsArchive\Opportunities";
        if (string.IsNullOrWhiteSpace(cs) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Console.Error.WriteLine(
                "Set env vars KOR_OPPORTUNITIES_CONNECTIONSTRING, BCBID_USERNAME, BCBID_PASSWORD.");
            Environment.Exit(2);
            return;
        }

        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        }));
        await using var pool = new PlaywrightBrowserPool(loggerFactory.CreateLogger<PlaywrightBrowserPool>());
        var creds = new BcBidCredentials
        {
            Username = user,
            Password = pass,
        };
        var docStore = new SqlHistoricalOpportunityDocumentStore(cs);
        var svc = new BcBidHistoricalDocumentDownloadService(
            pool,
            creds,
            docStore,
            loggerFactory.CreateLogger<BcBidHistoricalDocumentDownloadService>());
        var result = await svc.DownloadBatchAsync(dn, 3, archive, CancellationToken.None);
        Console.WriteLine(
            $"Download batch result: attempted={result.Attempted} downloaded={result.Downloaded} failed={result.Failed}");
        return;
    }

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
}, htmlOpt, baseUrlOpt, buyerOpt, detailHtmlOpt, enrichBatchOpt, downloadBatchOpt, prosperoOpt);

static string Snip(string? s) =>
    string.IsNullOrEmpty(s) ? "(null)" :
    s.Length > 120 ? s.Substring(0, 120).Replace("\n", " ") + "..." : s.Replace("\n", " ");

return await root.InvokeAsync(args);
