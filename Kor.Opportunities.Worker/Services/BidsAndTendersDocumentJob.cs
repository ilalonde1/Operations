#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Stores the bid documents of every pursuit on a Bids&amp;Tenders tender in the pursuit itself (opportunities.OpportunityFiles).
///
/// Bids&amp;Tenders serves a tender's whole package (documents, addenda, file summary) as one zip at
/// /Module/Tenders/{lang}/Tender/DownloadBidDocuments/{tender id}, to a signed-in vendor. One company account signs in on every
/// city's site (KOR_OPPORTUNITIES_BIDSANDTENDERSEMAIL / ...PASSWORD on the host). Downloading registers KOR as a plan taker on
/// that tender, which other bidders can see, so only PURSUED tenders are fetched: a CRM pursuit, live (stage 1 or 3).
///
/// A pursuit is fetched once, then at most daily until its deadline so addenda arrive (opportunities.PursuitDocumentFetch,
/// migration 375). A file already on the pursuit with the same name and size is not stored twice. Measured 2026-10-20 on Leduc
/// 2026-RFP-IP-061: one 45 MB zip, nine files.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class BidsAndTendersDocumentJob : IJob
{
    internal const string Portal = "BIDSTENDERS";
    private const string UploadedBy = "bids-and-tenders";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BidsAndTendersDocumentJob> _logger;
    private readonly PlaywrightBrowserPool _browserPool;

    public BidsAndTendersDocumentJob(IOptions<OpportunitiesWorkerOptions> options, ILogger<BidsAndTendersDocumentJob> logger,
        PlaywrightBrowserPool browserPool)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _browserPool = browserPool ?? throw new ArgumentNullException(nameof(browserPool));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BidsAndTendersDocumentsEnabled || string.IsNullOrWhiteSpace(opt.BidsAndTendersEmail) || string.IsNullOrWhiteSpace(opt.BidsAndTendersPassword))
        {
            _logger.LogInformation("{Job}: disabled or no Bids&Tenders login on the host", nameof(BidsAndTendersDocumentJob));
            return;
        }

        var ct = context.CancellationToken;
        var db = opt.OpportunitiesDb!;
        var targets = await LoadTargetsAsync(db, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            context.Result = "No pursued Bids&Tenders tender due a fetch";
            return;
        }

        var files = new SqlPursuitFileStore(db);
        await using var ctxRef = await _browserPool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await ctxRef.NewPageAsync().ConfigureAwait(false);
        var signedIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fetched = 0, stored = 0, failed = 0;
        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            var downloadUrl = DownloadUrlFor(t.DetailUrl);
            if (downloadUrl is null) continue;
            try
            {
                var host = new Uri(t.DetailUrl).Host;
                if (!signedIn.Contains(host))
                {
                    await SignInAsync(page, t.DetailUrl, opt.BidsAndTendersEmail, opt.BidsAndTendersPassword, ct).ConfigureAwait(false);
                    signedIn.Add(host);
                }

                var response = await page.Context.APIRequest.GetAsync(downloadUrl, new APIRequestContextOptions { Timeout = 300_000 }).ConfigureAwait(false);
                var body = await response.BodyAsync().ConfigureAwait(false);
                if (!response.Ok || body.Length < 4 || body[0] != 0x50 || body[1] != 0x4B)
                {
                    failed++;
                    await LogAsync(db, t.EngagementId, false, 0, 0, $"HTTP {response.Status}, not a zip ({body.Length} bytes)", ct).ConfigureAwait(false);
                    continue;
                }

                var existing = await files.ListByEngagementAsync(t.EngagementId, ct).ConfigureAwait(false);
                using var zip = new ZipArchive(new MemoryStream(body), ZipArchiveMode.Read);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                var added = 0;
                foreach (var entry in entries)
                {
                    if (existing.Any(f => string.Equals(f.FileName, entry.Name, StringComparison.OrdinalIgnoreCase) && f.SizeBytes == entry.Length)) continue;
                    SqlPursuitFileStore.ValidateLength(entry.Length);
                    await using var content = entry.Open();
                    await files.AddAsync(t.EngagementId, t.OpportunityId, entry.Name, content, entry.Length, ContentType(entry.Name), UploadedBy, ct).ConfigureAwait(false);
                    added++;
                }

                fetched++;
                stored += added;
                await LogAsync(db, t.EngagementId, true, entries.Count, added, null, ct).ConfigureAwait(false);
                _logger.LogInformation("{Job}: pursuit {Id}: {Files} files in the package, {Added} stored", nameof(BidsAndTendersDocumentJob), t.EngagementId, entries.Count, added);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "{Job}: pursuit {Id} fetch failed", nameof(BidsAndTendersDocumentJob), t.EngagementId);
                await LogAsync(db, t.EngagementId, false, 0, 0, ex.GetType().Name + ": " + ex.Message, ct).ConfigureAwait(false);
            }
        }

        context.Result = $"pursuits={targets.Count}; fetched={fetched}; filesStored={stored}; failures={failed}";
        _logger.LogInformation("{Job}: {Result}", nameof(BidsAndTendersDocumentJob), context.Result);
    }

    /// <summary>Pure: a tender detail URL to its bid-package download URL; null for anything else.</summary>
    internal static string? DownloadUrlFor(string detailUrl)
    {
        if (!Uri.TryCreate(detailUrl, UriKind.Absolute, out var uri) || !uri.Host.EndsWith(".bidsandtenders.ca", StringComparison.OrdinalIgnoreCase)) return null;
        var path = uri.AbsolutePath;
        var at = path.IndexOf("/Tender/Detail/", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var id = path[(at + "/Tender/Detail/".Length)..].Trim('/');
        return Guid.TryParse(id, out _) ? $"{uri.Scheme}://{uri.Host}{path[..at]}/Tender/DownloadBidDocuments/{id}" : null;
    }

    /// <summary>Signs in on the tender's own city site: its login link, the Username / Password form, Login.</summary>
    private static async Task SignInAsync(IPage page, string detailUrl, string email, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await page.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 }).ConfigureAwait(false);
        var login = page.Locator("a[href*='/Login/Index']").First;
        if (await login.CountAsync().ConfigureAwait(false) == 0) return; // already signed in on this site
        var href = await login.GetAttributeAsync("href").ConfigureAwait(false);
        await page.GotoAsync(new Uri(new Uri(detailUrl), href!).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 }).ConfigureAwait(false);
        await page.FillAsync("#Username", email).ConfigureAwait(false);
        await page.FillAsync("#Password", password).ConfigureAwait(false);
        await page.ClickAsync("button[type=submit]:has-text('Login')").ConfigureAwait(false);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle).ConfigureAwait(false);
        if (await page.Locator("#Password").CountAsync().ConfigureAwait(false) > 0)
            throw new InvalidOperationException("Bids&Tenders sign-in was refused on " + new Uri(detailUrl).Host + ".");
    }

    /// <summary>
    /// Live pursuits on a Bids&amp;Tenders tender that have never been fetched, or whose tender is still open and were last
    /// fetched over 20 hours ago.
    /// </summary>
    internal const string TargetsSql = @"
SELECT e.Id, e.OpportunityId, obs.Url
FROM opportunities.CrmEngagements e
JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
CROSS APPLY (SELECT TOP (1) x.Url FROM opportunities.OpportunityObservations x
             WHERE x.OpportunityId = o.Id AND x.Url LIKE N'%.bidsandtenders.ca/Module/Tenders/%/Tender/Detail/%'
             ORDER BY x.IngestedAtUtc DESC, x.Id DESC) obs
OUTER APPLY (SELECT MAX(f.FetchedAtUtc) AS LastAt FROM opportunities.PursuitDocumentFetch f
             WHERE f.EngagementId = e.Id AND f.Portal = N'BIDSTENDERS' AND f.Succeeded = 1) last
WHERE e.Stage IN (1, 3)
  AND (last.LastAt IS NULL
       OR (o.SubmissionDeadlineUtc > SYSDATETIMEOFFSET() AND last.LastAt < DATEADD(hour, -20, SYSDATETIMEOFFSET())))
ORDER BY e.Id;";

    private static async Task<IReadOnlyList<Target>> LoadTargetsAsync(string db, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TargetsSql, con) { CommandTimeout = 30 };
        var list = new List<Target>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new Target(r.GetInt64(0), r.GetInt64(1), r.GetString(2)));
        return list;
    }

    private static async Task LogAsync(string db, long engagementId, bool ok, int inPackage, int stored, string? note, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
INSERT INTO opportunities.PursuitDocumentFetch (EngagementId, Portal, Succeeded, FilesInPackage, FilesStored, Note)
VALUES (@e, @p, @ok, @n, @s, @note);", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 40).Value = Portal;
        cmd.Parameters.Add("@ok", SqlDbType.Bit).Value = ok;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = inPackage;
        cmd.Parameters.Add("@s", SqlDbType.Int).Value = stored;
        cmd.Parameters.Add("@note", SqlDbType.NVarChar, 400).Value = (object?)(note is { Length: > 400 } ? note[..400] : note) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".dwg" => "application/acad",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    private sealed record Target(long EngagementId, long OpportunityId, string DetailUrl);
}
