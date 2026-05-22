#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

/// <summary>
/// Downloads BC Bid historical-archive documents to a UNC archive root.
/// Each row in HistoricalOpportunityDocuments has a SourceUrl pointing into
/// BC Bid; this service walks pending rows, fetches each via authenticated
/// Playwright, writes to disk at &lt;ArchiveRoot&gt;\&lt;HistOppId&gt;\&lt;safe-name&gt;,
/// computes SHA256, and updates the row with the local path.
///
/// Failures bump DownloadAttemptCount; once MaxAttempts is reached the row
/// stops appearing in ListPendingAsync.
/// </summary>
public sealed class BcBidHistoricalDocumentDownloadService
{
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private readonly PlaywrightBrowserPool _pool;
    private readonly BcBidCredentials _credentials;
    private readonly IHistoricalOpportunityDocumentStore _store;
    private readonly ILogger<BcBidHistoricalDocumentDownloadService> _logger;

    public BcBidHistoricalDocumentDownloadService(
        PlaywrightBrowserPool pool,
        BcBidCredentials credentials,
        IHistoricalOpportunityDocumentStore store,
        ILogger<BcBidHistoricalDocumentDownloadService> logger)
    {
        _pool = pool;
        _credentials = credentials;
        _store = store;
        _logger = logger;
    }

    public sealed record DownloadBatchResult(int Attempted, int Downloaded, int Failed);

    public async Task<DownloadBatchResult> DownloadBatchAsync(
        int batchSize,
        int maxAttempts,
        string archiveRoot,
        CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            _logger.LogWarning("BcBid credentials not configured; document download skipped.");
            return new DownloadBatchResult(0, 0, 0);
        }

        var pending = await _store.ListPendingAsync(batchSize, maxAttempts, ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            _logger.LogInformation("No HistoricalOpportunityDocuments pending download.");
            return new DownloadBatchResult(0, 0, 0);
        }

        _logger.LogInformation("Downloading batch of {Count} documents into {Root}.", pending.Count, archiveRoot);

        Directory.CreateDirectory(archiveRoot);
        await using var context = await _pool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        await LoginAsync(page).ConfigureAwait(false);

        var downloaded = 0;
        var failed = 0;
        foreach (var doc in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var safeName = MakeSafeFileName(doc.FileName);
                var rowDir = Path.Combine(archiveRoot, doc.HistoricalOpportunityId.ToString());
                Directory.CreateDirectory(rowDir);
                var destPath = Path.Combine(rowDir, safeName);

                // Fetch the file bytes via the context's APIRequest so we re-use
                // the authenticated session cookies. page.GotoAsync(pdfUrl) doesn't
                // work — Chromium's built-in viewer renders the PDF inline instead
                // of firing a Download event.
                var resp = await context.APIRequest.GetAsync(doc.SourceUrl, new APIRequestContextOptions
                {
                    Timeout = 60_000,
                }).ConfigureAwait(false);

                if (!resp.Ok)
                {
                    await _store.RecordFailureAsync(doc.Id, $"HTTP {resp.Status} {resp.StatusText}", ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                var bodyBytes = await resp.BodyAsync().ConfigureAwait(false);
                if (bodyBytes is null || bodyBytes.Length == 0)
                {
                    await _store.RecordFailureAsync(doc.Id, "Empty response body.", ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                await File.WriteAllBytesAsync(destPath, bodyBytes, ct).ConfigureAwait(false);

                var fi = new FileInfo(destPath);
                if (!fi.Exists || fi.Length == 0)
                {
                    await _store.RecordFailureAsync(
                        doc.Id,
                        "Wrote empty file to disk.",
                        ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                byte[] sha;
                await using (var fs = File.OpenRead(destPath))
                {
                    using var sha256 = SHA256.Create();
                    sha = await sha256.ComputeHashAsync(fs, ct).ConfigureAwait(false);
                }

                string? contentType = null;
                if (resp.Headers != null && resp.Headers.TryGetValue("content-type", out var ctHeader))
                {
                    contentType = ctHeader;
                }
                contentType ??= GuessContentType(safeName);
                await _store.RecordSuccessAsync(doc.Id, destPath, sha, fi.Length, contentType, ct)
                    .ConfigureAwait(false);
                downloaded++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Download failed for document {Id} ({FileName}).", doc.Id, doc.FileName);
                try
                {
                    await _store.RecordFailureAsync(doc.Id, ex.Message, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort bookkeeping after the original failure.
                }
            }
        }

        _logger.LogInformation(
            "Download batch complete: attempted={A} downloaded={D} failed={F}",
            pending.Count,
            downloaded,
            failed);
        return new DownloadBatchResult(pending.Count, downloaded, failed);
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(LoginEntryUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            Name = "Login",
            Exact = true,
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            NameRegex = new Regex(
                @"Business\s+or\s+Basic\s+BCeID",
                RegexOptions.IgnoreCase),
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.Locator("input[type='text']:visible")
            .First.FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);
        await page.Locator("input[type='password']:visible")
            .First.FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
        {
            Name = "Continue",
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
    }

    private static string MakeSafeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            clean.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var result = clean.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result)) result = "document";
        if (result.Length > 180) result = result.Substring(0, 180);
        return result;
    }

    private static string? GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => null,
        };
    }
}
