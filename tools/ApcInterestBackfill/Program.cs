// One-shot backfill: for every existing APC posting in opportunities.Opportunities
// (OpportunityKey LIKE 'APCALLBU%'), fetch the detail page, extract the
// "Interested suppliers" supplier-card list, resolve each firm name to a
// CanonicalOrgId via CanonicalOrgResolver, and write to
// opportunities.OpportunityInterestedFirms via the new store.
//
// The same extraction logic will be folded into the production scraper after
// this proves out the selectors + resolver behavior on the existing 70 postings.
//
// Anti-fabrication / robustness:
//   - The supplier-card div is the ONLY trusted source — no inference, no fuzzy
//     "looks like a firm name" outside that selector.
//   - Per-posting in try/catch; one failure does not kill the batch.
//   - Single-row UPSERT in the store, dedup on (OpportunityId, RawFirmName).
//   - Resume-friendly: source query auto-skips postings that already have rows
//     unless --force is set.
//
// Usage:
//   dotnet run --project tools/ApcInterestBackfill --
//     [--limit N]            (smoke test small sample)
//     [--key APCALLBU-...]   (process a single posting)
//     [--force]              (re-scrape even if rows exist)
//     [--headless false]     (run headed for diagnostics)

using System.Data;
using System.Globalization;
using System.Text;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

const string SourcePortal = "APC";
const string PostingUrlTemplate = "https://purchasing.alberta.ca/posting/{0}";

var opts = ParseArgs(args);
var connStr = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB env var first.");
    return 2;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("ApcInterestBackfill");

var canonStore = new SqlCanonicalOrgStore(connStr);
var resolver = new CanonicalOrgResolver(canonStore, loggerFactory.CreateLogger<CanonicalOrgResolver>());
var interestStore = new SqlOpportunityInterestedFirmStore(connStr);

// 1) Pull the target opp set
var targets = await LoadTargetsAsync(connStr, opts, log).ConfigureAwait(false);
if (targets.Count == 0)
{
    log.LogInformation("No targets selected. Nothing to do.");
    return 0;
}
log.LogInformation("Loaded {Count} APC postings to enrich.", targets.Count);

// 2) Spin up Playwright
using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = opts.Headless,
}).ConfigureAwait(false);
await using var ctx = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    Locale = "en-CA",
}).ConfigureAwait(false);
var page = await ctx.NewPageAsync().ConfigureAwait(false);

int processed = 0;
int suppliersWritten = 0;
int canonResolved = 0;
int postingsWithSuppliers = 0;
int failures = 0;

foreach (var t in targets)
{
    processed++;
    try
    {
        var url = string.Format(CultureInfo.InvariantCulture, PostingUrlTemplate, t.ExternalReference);
        log.LogInformation("[{N}/{Total}] {Key} → {Url}", processed, targets.Count, t.ExternalReference, url);

        var suppliers = await ScrapeInterestedSuppliersAsync(page, url, log).ConfigureAwait(false);
        if (suppliers.Count == 0)
        {
            log.LogInformation("  no suppliers found on this posting");
            continue;
        }
        postingsWithSuppliers++;

        foreach (var supplier in suppliers)
        {
            try
            {
                long? resolvedId = null;
                string? resolvedKind = null;
                // Default kind guess from descriptor text — refined by resolver
                var guessKind = GuessKindFromDescriptor(supplier.Name, supplier.Descriptor);
                resolvedId = await resolver.ResolveAsync(
                    supplier.Name,
                    guessKind,
                    OrgAliasSources.Manual + ":APC.InterestedFirms",
                    CancellationToken.None,
                    allowCreate: true,
                    minConfidenceForCreate: 70).ConfigureAwait(false);
                if (resolvedId.HasValue)
                {
                    canonResolved++;
                    resolvedKind = guessKind;
                }

                await interestStore.UpsertAsync(
                    opportunityId: t.OpportunityId,
                    rawFirmName: supplier.Name,
                    resolvedCanonicalOrgId: resolvedId,
                    resolvedKind: resolvedKind,
                    sourcePortal: SourcePortal,
                    sourcePostingUrl: url,
                    expressedAtUtc: null, // APC doesn't publish per-supplier timestamp
                    notes: supplier.Descriptor,
                    rawJson: supplier.RawText,
                    ct: CancellationToken.None).ConfigureAwait(false);
                suppliersWritten++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "    skip supplier '{Name}' on {Key}", supplier.Name, t.ExternalReference);
            }
        }
        log.LogInformation("  {Count} interested suppliers stored", suppliers.Count);
    }
    catch (Exception ex)
    {
        failures++;
        log.LogWarning(ex, "Failure on {Key}; continuing", t.ExternalReference);
    }
}

log.LogInformation("=== Summary ===");
log.LogInformation("Postings processed:       {N}", processed);
log.LogInformation("Postings w/ suppliers:    {N}", postingsWithSuppliers);
log.LogInformation("Supplier rows written:    {N}", suppliersWritten);
log.LogInformation("Canonical resolved:       {N}", canonResolved);
log.LogInformation("Failures (posting-level): {N}", failures);

return 0;

// -----------------------------------------------------------------------------

static async Task<IReadOnlyList<TargetOpp>> LoadTargetsAsync(string connStr, Options opts, ILogger log)
{
    var sql = new StringBuilder(@"
SELECT o.Id, o.OpportunityKey,
       REPLACE(o.OpportunityKey, 'APCALLBU-', '') AS ExternalReference
FROM   opportunities.Opportunities o
WHERE  o.OpportunityKey LIKE 'APCALLBU-%'");
    if (!opts.Force)
    {
        sql.Append(@"
  AND NOT EXISTS (
    SELECT 1 FROM opportunities.OpportunityInterestedFirms f
    WHERE f.OpportunityId = o.Id AND f.SourcePortal = 'APC'
  )");
    }
    if (!string.IsNullOrWhiteSpace(opts.SingleKey))
    {
        sql.Append("\n  AND o.OpportunityKey = @key");
    }
    sql.Append("\nORDER BY o.OpportunityKey DESC");

    await using var con = new SqlConnection(connStr);
    await con.OpenAsync().ConfigureAwait(false);
    await using var cmd = new SqlCommand(sql.ToString(), con) { CommandTimeout = 30 };
    if (!string.IsNullOrWhiteSpace(opts.SingleKey))
    {
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = opts.SingleKey;
    }

    var list = new List<TargetOpp>();
    await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await r.ReadAsync().ConfigureAwait(false))
    {
        list.Add(new TargetOpp(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        if (opts.Limit > 0 && list.Count >= opts.Limit) break;
    }
    return list;
}

static async Task<IReadOnlyList<ScrapedSupplier>> ScrapeInterestedSuppliersAsync(IPage page, string url, ILogger log)
{
    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 45_000 }).ConfigureAwait(false);
    await page.WaitForTimeoutAsync(2500).ConfigureAwait(false);

    // Scroll to the interested-suppliers anchor to force any lazy render.
    await page.EvaluateAsync("() => document.querySelector('#interested-suppliers')?.scrollIntoView({behavior:'instant',block:'start'})").ConfigureAwait(false);
    await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);

    // Extract every .supplier-card div's innerText. The first non-empty line is
    // the firm name; remaining lines are service descriptors / contact info.
    var json = await page.EvaluateAsync<string>(@"() => {
        const cards = Array.from(document.querySelectorAll('div.supplier-card'));
        const out = [];
        for (const card of cards) {
            const raw = (card.innerText || '').trim();
            if (!raw) continue;
            const lines = raw.split(/\r?\n/).map(s => s.trim()).filter(s => s);
            if (lines.length === 0) continue;
            out.push({
                name: lines[0],
                descriptor: lines.slice(1).join(' | '),
                rawText: raw,
            });
        }
        return JSON.stringify(out);
    }").ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(json) || json == "[]") return Array.Empty<ScrapedSupplier>();
    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var parsed = System.Text.Json.JsonSerializer.Deserialize<ScrapedSupplier[]>(json, jsonOpts)
        ?? Array.Empty<ScrapedSupplier>();
    return parsed;
}

static string GuessKindFromDescriptor(string name, string? descriptor)
{
    var blob = (name + " " + (descriptor ?? "")).ToLowerInvariant();
    if (blob.Contains("structural")) return OrgKinds.Competitor;        // structural engineering => KOR's direct competitor
    if (blob.Contains("architect"))  return OrgKinds.Architect;
    if (blob.Contains("interior design") || blob.Contains("urban planning")) return OrgKinds.Architect;
    if (blob.Contains("general contract") || blob.Contains("construction services")) return OrgKinds.GeneralContractor;
    if (blob.Contains("engineering")) return OrgKinds.Competitor;       // multi-disc eng firms often have structural arms
    return OrgKinds.Unknown;
}

static Options ParseArgs(string[] args)
{
    var o = new Options();
    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        switch (a)
        {
            case "--limit": o.Limit = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--key":   o.SingleKey = args[++i]; break;
            case "--force": o.Force = true; break;
            case "--headless":
                o.Headless = !bool.TryParse(args[++i], out var b) || b; break;
            default:
                Console.Error.WriteLine($"Unknown arg: {a}");
                Environment.Exit(2);
                break;
        }
    }
    return o;
}

sealed record TargetOpp(long OpportunityId, string OpportunityKey, string ExternalReference);
sealed record ScrapedSupplier(string Name, string? Descriptor, string RawText);
sealed class Options { public int Limit; public string? SingleKey; public bool Force; public bool Headless = true; }
