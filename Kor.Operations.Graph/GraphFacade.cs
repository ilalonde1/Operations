#nullable enable
#pragma warning disable SA1649
using Ganss.Xss;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Microsoft.Graph.Users.Item.SendMail;
using Kor.Operations.Core;
using Polly;
using Polly.Retry;
namespace Kor.Operations.Graph
{
    /// <summary>
    /// Represents the metadata returned after a file upload completes.
    /// </summary>
    public sealed class GraphUploadResult
    {
        /// <summary>
        /// Gets the drive identifier that received the upload.
        /// </summary>
        public string DriveId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the uploaded item identifier.
        /// </summary>
        public string ItemId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the web URL for the uploaded item.
        /// </summary>
        public string WebUrl { get; init; } = string.Empty;
    }

    /// <summary>
    /// Defines Graph operations used by the KOR transmittal workflows.
    /// </summary>
    public interface IGraphFacade
    {
        /// <summary>
        /// Reserves a transmittal number for the specified project.
        /// </summary>
        /// <param name="projectNumber">The project number, if one is available.</param>
        /// <returns>The generated transmittal number.</returns>
        Task<string> ReserveTransmittalNumberAsync(string? projectNumber);

        /// <summary>
        /// Uploads a file and returns metadata describing the uploaded item.
        /// </summary>
        /// <param name="folderRelativePath">The destination folder path relative to the configured drive root.</param>
        /// <param name="fileName">The file name to create in SharePoint.</param>
        /// <param name="localFilePath">The local file path to upload.</param>
        /// <param name="progress">An optional progress callback.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The uploaded item metadata.</returns>
        Task<GraphUploadResult> UploadWithMetadataAsync(
            string folderRelativePath,
            string fileName,
            string localFilePath,
            IProgress<(string file, long sent, long total)>? progress,
            CancellationToken ct);

        /// <summary>
        /// Uploads a file and returns the resulting web URL.
        /// </summary>
        /// <param name="folderRelativePath">The destination folder path relative to the configured drive root.</param>
        /// <param name="fileName">The file name to create in SharePoint.</param>
        /// <param name="localFilePath">The local file path to upload.</param>
        /// <param name="progress">An optional progress callback.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The web URL for the uploaded item.</returns>
        Task<string> UploadWithProgressAsync(
            string folderRelativePath,
            string fileName,
            string localFilePath,
            IProgress<(string file, long sent, long total)>? progress,
            CancellationToken ct);

        /// <summary>
        /// Creates internal and optional external sharing links for a folder.
        /// </summary>
        /// <param name="folderRelativePath">The folder path relative to the configured drive root.</param>
        /// <param name="needExternal">Whether an external sharing link should also be created.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The sharing links for the folder.</returns>
        Task<CreateLinksResult> CreateLinksAsync(string folderRelativePath, bool needExternal, CancellationToken ct);

        /// <summary>
        /// Sends a transmittal email by using Microsoft Graph.
        /// </summary>
        /// <param name="header">The header object describing the message content.</param>
        /// <param name="coverSheetServerUrl">The server URL for the cover sheet.</param>
        /// <param name="coverSheetLocalPath">The local cover sheet path when an attachment should be sent.</param>
        /// <param name="attachCover">Whether the cover sheet should be attached.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <param name="senderUpn">The sender user principal name.</param>
        /// <param name="toAndCcEmails">Optional explicit recipient addresses.</param>
        Task SendMailAsync(
            object header,
            string coverSheetServerUrl,
            string? coverSheetLocalPath,
            bool attachCover,
            CancellationToken ct,
            string? senderUpn,
            IEnumerable<string>? toAndCcEmails);

        /// <summary>
        /// Sends a plain notification email (HTML body) as <paramref name="senderUpn"/>.
        /// Unlike <see cref="SendMailAsync"/> this carries no transmittal/cover-sheet
        /// payload — it is for simple in-app notifications (e.g. pursuit reassignment).
        /// </summary>
        /// <param name="senderUpn">Mailbox to send as (the signed-in user).</param>
        /// <param name="toEmails">Recipient addresses.</param>
        /// <param name="subject">Subject line.</param>
        /// <param name="htmlBody">HTML message body.</param>
        /// <param name="ct">Cancellation token.</param>
        Task SendSimpleMailAsync(
            string senderUpn,
            IEnumerable<string> toEmails,
            string subject,
            string htmlBody,
            CancellationToken ct);

        /// <summary>
        /// Attempts to retrieve the profile photo for a user.
        /// </summary>
        /// <param name="userPrincipalName">The user principal name to query.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The photo stream when one is available; otherwise, <see langword="null"/>.</returns>
        Task<Stream?> TryGetUserPhotoAsync(string userPrincipalName, CancellationToken ct = default);

        /// <summary>
        /// Ensures that a drive folder path exists and returns the resulting item.
        /// </summary>
        /// <param name="driveId">The drive identifier.</param>
        /// <param name="relativePath">The relative folder path to ensure.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resulting drive item.</returns>
        Task<DriveItem> EnsureFolderPathAsync(string driveId, string relativePath, CancellationToken ct);

        /// <summary>
        /// Ensures the folder path exists in the default drive and returns its item ID.
        /// Use this to resolve the folder once, then pass the ID to <see cref="UploadToFolderAsync"/>
        /// and <see cref="CreateLinksForFolderAsync"/> to avoid redundant round trips.
        /// </summary>
        Task<string> EnsureFolderAsync(string folderRelativePath, CancellationToken ct);

        /// <summary>
        /// Uploads a file to an already-resolved folder item ID, skipping folder resolution.
        /// </summary>
        Task<GraphUploadResult> UploadToFolderAsync(
            string folderId,
            string fileName,
            string localFilePath,
            IProgress<(string file, long sent, long total)>? progress,
            CancellationToken ct);

        /// <summary>
        /// Creates sharing links for an already-resolved folder item ID, skipping folder resolution.
        /// </summary>
        Task<CreateLinksResult> CreateLinksForFolderAsync(string folderId, bool needExternal, CancellationToken ct);

        /// <summary>
        /// Enumerates all immediate children of a folder, transparently following @odata.nextLink.
        /// </summary>
        /// <param name="driveId">The drive identifier.</param>
        /// <param name="folderItemId">The folder item id whose children should be listed.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>An async stream of <see cref="DriveItem"/>.</returns>
        IAsyncEnumerable<DriveItem> ListChildrenAsync(string driveId, string folderItemId, CancellationToken ct);

        /// <summary>
        /// Enumerates all immediate children of a folder addressed by relative path.
        /// </summary>
        IAsyncEnumerable<DriveItem> ListChildrenByPathAsync(string driveId, string folderRelativePath, CancellationToken ct);

        /// <summary>
        /// Downloads the content of a drive item as a stream. Caller disposes.
        /// </summary>
        Task<Stream> DownloadAsync(string driveId, string itemId, CancellationToken ct);

        /// <summary>
        /// Downloads the content of a drive item addressed by relative path. Caller disposes.
        /// </summary>
        Task<Stream> DownloadByPathAsync(string driveId, string relativePath, CancellationToken ct);

        /// <summary>
        /// Deletes a drive item. Throws on 404; use <see cref="TryDeleteItemAsync"/> for idempotent cleanup.
        /// </summary>
        Task DeleteItemAsync(string driveId, string itemId, CancellationToken ct);

        /// <summary>
        /// Idempotent delete: swallows 404 and returns false; returns true when the delete actually fired.
        /// </summary>
        Task<bool> TryDeleteItemAsync(string driveId, string itemId, CancellationToken ct);

        /// <summary>
        /// Renames a drive item via PATCH (Name only). Returns the patched item.
        /// </summary>
        Task<DriveItem> RenameItemAsync(string driveId, string itemId, string newName, CancellationToken ct);

        /// <summary>
        /// Moves a drive item to another folder on the same drive via PATCH (parentReference).
        /// Optionally renames in the same call.
        /// </summary>
        Task<DriveItem> MoveItemAsync(string driveId, string itemId, string destinationFolderId, string? newName, CancellationToken ct);

        /// <summary>
        /// Uploads a file via the simple single-shot PUT endpoint
        /// (/drives/{id}/items/{folderId}:/{name}:/content). Best for files
        /// under ~4 MB; callers using this path should size-gate themselves.
        /// Replaces an existing item by default (conflictBehavior=replace).
        /// </summary>
        Task<DriveItem> UploadSimpleAsync(string driveId, string folderId, string fileName, string localFilePath, CancellationToken ct);

        /// <summary>
        /// GET item-by-path that returns null on 404 instead of creating
        /// the path. Use for read-only/Shadow probes; never call
        /// EnsureFolderPathAsync on a path you haven't decided to write.
        /// </summary>
        Task<DriveItem?> TryGetItemByPathAsync(string driveId, string relativePath, CancellationToken ct);

        /// <summary>
        /// Streams children of a folder addressed by relative path, yielding
        /// nothing if the folder is missing. Unlike ListChildrenByPathAsync
        /// this never auto-creates the folder, so it's the right primitive
        /// for any read-only scan (Shadow audits, optional Reports/ pulls, ...).
        /// </summary>
        IAsyncEnumerable<DriveItem> ListChildrenByPathIfExistsAsync(string driveId, string folderRelativePath, CancellationToken ct);

        /// <summary>
        /// Same as <see cref="UploadToFolderAsync"/> but lets the caller pick
        /// the upload-session chunk size (driven from the Watcher's
        /// ImageUploadChunkBytes knob). Pass null to use the 5 MB default.
        /// </summary>
        Task<GraphUploadResult> UploadToFolderAsync(
            string folderId,
            string fileName,
            string localFilePath,
            IProgress<(string file, long sent, long total)>? progress,
            int? chunkSizeBytes,
            CancellationToken ct);
    }

    /// <summary>
    /// Implements Graph operations for uploads, sharing links, and email delivery.
    /// </summary>
    public sealed class GraphFacade : IGraphFacade
    {
        private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
        private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<ServiceException>(ex => ex.ResponseStatusCode == 429 || ex.ResponseStatusCode >= 500)
                    .Handle<Microsoft.Graph.Models.ODataErrors.ODataError>(ex => ex.ResponseStatusCode == 429 || ex.ResponseStatusCode >= 500)
                    .Handle<TaskCanceledException>(),
                // Honor Retry-After when the server supplied one (Graph 429 always does);
                // otherwise fall back to the exponential schedule.
                DelayGenerator = static args =>
                {
                    TimeSpan? hint = args.Outcome.Exception switch
                    {
                        Microsoft.Graph.Models.ODataErrors.ODataError oe => RetryAfterFromODataError(oe),
                        ServiceException se => RetryAfterFromServiceException(se),
                        _ => null,
                    };
                    return ValueTask.FromResult(hint);
                },
            })
            .Build();

        private static TimeSpan? RetryAfterFromODataError(Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            // Graph surfaces Retry-After in the response headers; the SDK exposes them
            // through the inner exception's Data bag for ODataError. Fall back to no
            // hint if we can't parse one.
            try
            {
                if (ex.ResponseHeaders is null) return null;
                if (!ex.ResponseHeaders.TryGetValue("Retry-After", out var values)) return null;
                foreach (var v in values)
                {
                    if (int.TryParse(v, out var seconds) && seconds > 0)
                        return TimeSpan.FromSeconds(Math.Min(seconds, 60));
                }
            }
            catch { /* defensive */ }
            return null;
        }

        private static TimeSpan? RetryAfterFromServiceException(ServiceException ex)
        {
            try
            {
                if (ex.ResponseHeaders is null) return null;
                if (ex.ResponseHeaders.TryGetValues("Retry-After", out var values))
                {
                    foreach (var v in values)
                    {
                        if (int.TryParse(v, out var seconds) && seconds > 0)
                            return TimeSpan.FromSeconds(Math.Min(seconds, 60));
                    }
                }
            }
            catch { /* defensive */ }
            return null;
        }

        private readonly string _driveId;

        private readonly GraphServiceClient _graph;

        // Per-process bulkhead on concurrent uploads. Without a cap, a flood of
        // FileSystemWatcher events can fan out N parallel UploadCoreAsync calls;
        // each opens an upload session and consumes per-tenant Graph quota,
        // which eventually triggers a 429 storm that the retry pipeline can't
        // dig out of. 6 is conservative -- well below the documented per-app
        // throttle threshold and high enough to keep typical bursts saturated.
        private static readonly SemaphoreSlim UploadGate = new(6, 6);

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphFacade"/> class.
        /// </summary>
        /// <param name="graph">The Microsoft Graph client to use.</param>
        /// <param name="driveId">The default drive identifier for file operations.</param>
        public GraphFacade(GraphServiceClient graph, string driveId)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _driveId = string.IsNullOrWhiteSpace(driveId) ? throw new ArgumentNullException(nameof(driveId)) : driveId;
        }

        // ---------------------------------------------------
        // Public API
        // ---------------------------------------------------

        /// <inheritdoc />
        public Task<string> ReserveTransmittalNumberAsync(string? projectNumber)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var prefix = string.IsNullOrWhiteSpace(projectNumber) ? "TR" : projectNumber.Trim();
            return Task.FromResult($"{prefix}-{stamp}");
        }

        /// <summary>
        /// Upload a file to DriveId/folderRelativePath with progress; returns a web URL.
        /// </summary>
        public async Task<string> UploadWithProgressAsync(
            string folderRelativePath, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, CancellationToken ct)
        {
            var result = await UploadWithMetadataAsync(
                folderRelativePath,
                fileName,
                localFilePath,
                progress,
                ct).ConfigureAwait(false);
            return result.WebUrl;
        }

        /// <inheritdoc />
        public async Task<GraphUploadResult> UploadWithMetadataAsync(
            string folderRelativePath, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, CancellationToken ct)
        {
            var folderId = await EnsureFolderAsync(folderRelativePath, ct).ConfigureAwait(false);
            return await UploadToFolderAsync(folderId, fileName, localFilePath, progress, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> EnsureFolderAsync(string folderRelativePath, CancellationToken ct)
        {
            var item = await RetryPipeline.ExecuteAsync(
                async innerCt => await EnsureFolderPathAsync(_driveId, folderRelativePath, innerCt), ct);
            return item.Id ?? throw new InvalidOperationException($"Folder item returned no ID for path: {folderRelativePath}");
        }

        /// <inheritdoc />
        public Task<GraphUploadResult> UploadToFolderAsync(
            string folderId, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, CancellationToken ct)
            => UploadToFolderAsync(folderId, fileName, localFilePath, progress, chunkSizeBytes: null, ct);

        /// <inheritdoc />
        public Task<GraphUploadResult> UploadToFolderAsync(
            string folderId, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, int? chunkSizeBytes, CancellationToken ct)
            => UploadCoreAsync(folderId, fileName, localFilePath, progress, chunkSizeBytes, ct);
            // Note: NO outer RetryPipeline wrap here. Wrapping the whole upload
            // in retry restarts UploadAsync from byte zero on transient mid-
            // stream errors, abandoning the server-side upload session (which
            // sticks around for ~7 days, consumes tenant quota, and shows up
            // as ghost items). UploadCoreInnerAsync owns its own retry semantics
            // using LargeFileUploadTask.ResumeAsync, which queries the server's
            // NextExpectedRanges and continues mid-stream instead of restarting.

        private async Task<GraphUploadResult> UploadCoreAsync(
            string folderId, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, int? chunkSizeBytes, CancellationToken ct)
        {
            // Bound concurrent uploads to protect tenant Graph quota. The gate
            // wraps the whole upload (session creation + UploadAsync + Resume
            // retries + best-effort cancel) so one upload's worth of slot use
            // covers all attempts on the same file.
            await UploadGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await UploadCoreInnerAsync(folderId, fileName, localFilePath, progress, chunkSizeBytes, ct).ConfigureAwait(false);
            }
            finally
            {
                UploadGate.Release();
            }
        }

        private async Task<GraphUploadResult> UploadCoreInnerAsync(
            string folderId, string fileName, string localFilePath,
            IProgress<(string file, long sent, long total)>? progress, int? chunkSizeBytes, CancellationToken ct)
        {
            using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var createBody = new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "replace" }
                    }
                }
            };

            // Session creation IS retry-safe (POST CreateUploadSession is idempotent
            // for the same item path -- the server returns a fresh session URL on
            // each call but doesn't persist any partial upload state).
            var session = await RetryPipeline.ExecuteAsync(
                async innerCt => await _graph.Drives[_driveId]
                                             .Items[folderId]
                                             .ItemWithPath(fileName)
                                             .CreateUploadSession
                                             .PostAsync(createBody, cancellationToken: innerCt),
                ct).ConfigureAwait(false);
            if (session is null)
                throw new InvalidOperationException($"CreateUploadSession returned null for '{fileName}'.");

            // Graph requires chunk sizes that are multiples of 320 KiB; the SDK's
            // LargeFileUploadTask doesn't enforce this, so we round down. 5 MiB
            // is the SDK default and Microsoft's recommendation.
            const int alignment = 320 * 1024;
            var chunkSize = chunkSizeBytes is > 0
                ? Math.Max(alignment, (chunkSizeBytes.Value / alignment) * alignment)
                : 5 * 1024 * 1024;
            var fileLength = fs.Length;
            var uploader = new LargeFileUploadTask<DriveItem>(session, fs, chunkSize);

            IProgress<long> onChunk = new Progress<long>(sent => progress?.Report((fileName, sent, fileLength)));

            // Up to 3 attempts. The first is UploadAsync (fresh upload). On
            // a transient failure we ResumeAsync, which queries the server for
            // NextExpectedRanges and continues from there -- no orphaned session,
            // no re-uploading bytes the server already has.
            //
            // Cleanup is centralized in the outer finally so the server-side
            // upload session is always cancelled on ANY non-success path:
            //   - loop completed naturally with UploadSucceeded=false
            //   - final attempt threw a transient that escaped the catch-when
            //     (attempt < maxAttempts) filter
            //   - cancellation propagated out of the inner OCE rethrow
            // Without the finally, the third path leaked a ~7d server-side
            // session on every shutdown-mid-upload.
            const int maxAttempts = 3;
            UploadResult<DriveItem>? uploadResult = null;
            Exception? lastEx = null;
            bool succeeded = false;
            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        uploadResult = attempt == 1
                            ? await uploader.UploadAsync(onChunk, cancellationToken: ct).ConfigureAwait(false)
                            : await uploader.ResumeAsync(onChunk, cancellationToken: ct).ConfigureAwait(false);

                        if (uploadResult.UploadSucceeded)
                        {
                            succeeded = true;
                            break;
                        }

                        // The SDK reports a logical failure without throwing.
                        // Treat as transient and let the next iteration ResumeAsync.
                        lastEx = new InvalidOperationException($"UploadAsync returned UploadSucceeded=false for '{fileName}'.");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Outer finally handles session cleanup; just propagate.
                        throw;
                    }
                    catch (Exception ex) when (attempt < maxAttempts && IsTransientUploadException(ex))
                    {
                        lastEx = ex;
                        var delayMs = 1500 * attempt;
                        try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    }
                    // NOTE: a transient on the FINAL attempt no longer matches the
                    // catch-when filter (attempt == maxAttempts). It escapes this
                    // try, escapes the for loop, and the outer finally cleans up
                    // the session before the exception propagates to the caller.
                }

                if (!succeeded)
                    throw lastEx ?? new InvalidOperationException($"Upload failed for {fileName}");
            }
            finally
            {
                if (!succeeded)
                {
                    // Fresh CTS so cancellation propagation (if any) doesn't kill
                    // the cleanup before the DELETE can leave the wire.
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await TryCancelUploadSessionAsync(session.UploadUrl, cleanupCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // TryCancel already swallows; defense-in-depth so a
                        // cleanup failure can't mask the original exception.
                    }
                }
            }

            // Only reached when succeeded == true.
            var uploadedItem = uploadResult!.ItemResponse;
            progress?.Report((fileName, fileLength, fileLength));

            return new GraphUploadResult
            {
                DriveId = _driveId,
                ItemId = uploadedItem?.Id ?? string.Empty,
                WebUrl = uploadedItem?.WebUrl ?? string.Empty
            };
        }

        // Same predicate shape as the static RetryPipeline above, kept in sync
        // by hand. Used by the upload Resume loop so we only retry on the
        // categories we'd retry anywhere else (transport, 429, 5xx).
        // OCE is excluded -- the caller's `when (ct.IsCancellationRequested)`
        // catches that ahead of this predicate so we don't accidentally
        // resume a cancelled upload.
        private static bool IsTransientUploadException(Exception ex) => ex switch
        {
            HttpRequestException => true,
            TaskCanceledException => true, // network timeout, NOT user cancel (filtered above)
            Microsoft.Graph.Models.ODataErrors.ODataError o => o.ResponseStatusCode == 429 || o.ResponseStatusCode >= 500,
            ServiceException s => s.ResponseStatusCode == 429 || s.ResponseStatusCode >= 500,
            _ => false,
        };

        // Graph upload session URLs are pre-authenticated (the URL contains an
        // embedded short-lived token), so a bare HttpClient DELETE works without
        // OAuth. Best-effort: any failure is logged-but-ignored at the caller.
        private static async Task TryCancelUploadSessionAsync(string? uploadUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(uploadUrl)) return;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var req = new HttpRequestMessage(HttpMethod.Delete, uploadUrl);
                await http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch
            {
                // Original upload failure already surfaces. Session will time
                // out server-side after ~7 days even without our cancel.
            }
        }

        /// <inheritdoc />
        public async Task<CreateLinksResult> CreateLinksAsync(string folderRelativePath, bool needExternal, CancellationToken ct)
        {
            var folderId = await EnsureFolderAsync(folderRelativePath, ct).ConfigureAwait(false);
            return await CreateLinksForFolderAsync(folderId, needExternal, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<CreateLinksResult> CreateLinksForFolderAsync(string folderId, bool needExternal, CancellationToken ct)
            => RetryPipeline.ExecuteAsync(async innerCt =>
            {
                var org = await _graph.Drives[_driveId].Items[folderId]
                    .CreateLink
                    .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                    {
                        Type = "view",
                        Scope = "organization"
                    }, cancellationToken: innerCt);

                string? anonUrl = null;
                if (needExternal)
                {
                    var anon = await _graph.Drives[_driveId].Items[folderId]
                        .CreateLink
                        .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                        {
                            Type = "view",
                            Scope = "anonymous"
                        }, cancellationToken: innerCt);
                    anonUrl = anon?.Link?.WebUrl;
                }

                return new CreateLinksResult
                {
                    InternalLink = org?.Link?.WebUrl ?? string.Empty,
                    ExternalLink = anonUrl
                };
            }, ct).AsTask();

        /// <summary>
        /// Send mail as the specified sender UPN (app-only cannot use /me).
        /// Recipients are passed explicitly to avoid header/reflection mismatches.
        /// Body is HTML: Purpose + HTML Remarks from header.
        /// Any "View files: Click here..." link should already be present in Remarks.
        /// </summary>
        public async Task SendMailAsync(
            object header,
            string coverSheetServerUrl,
            string? coverSheetLocalPath,
            bool attachCover,
            CancellationToken ct,
            string? senderUpn,
            IEnumerable<string>? toAndCcEmails)
        {
            // Deliberately NOT wrapped in RetryPipeline. Graph /sendMail is not
            // safely retryable: a 5xx returned AFTER the message was queued for
            // delivery results in a duplicate send on the next attempt. Better
            // to fail loudly than to silently double-send a transmittal email.
            // The single-attempt invocation below is inlined where the retry
            // pipeline used to be.
            await SendMailInnerAsync(header, coverSheetServerUrl, coverSheetLocalPath, attachCover, ct, senderUpn, toAndCcEmails).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SendSimpleMailAsync(
            string senderUpn,
            IEnumerable<string> toEmails,
            string subject,
            string htmlBody,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(senderUpn))
                throw new InvalidOperationException("Sender (From) address is required.");

            var recipients = (toEmails ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => new Microsoft.Graph.Models.Recipient
                {
                    EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = e.Trim() },
                })
                .ToList();
            if (recipients.Count == 0)
                throw new InvalidOperationException("At least one recipient is required.");

            var requestBody = new SendMailPostRequestBody
            {
                Message = new Message
                {
                    Subject = subject,
                    Body = new ItemBody { ContentType = BodyType.Html, Content = htmlBody },
                    ToRecipients = recipients,
                },
                SaveToSentItems = true,
            };

            // Single attempt, like SendMailAsync: /sendMail is not safely retryable.
            await _graph.Users[senderUpn].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
        }

        private async Task SendMailInnerAsync(
            object header,
            string coverSheetServerUrl,
            string? coverSheetLocalPath,
            bool attachCover,
            CancellationToken innerCt,
            string? senderUpn,
            IEnumerable<string>? toAndCcEmails)
        {
            // Original body (was inside RetryPipeline.ExecuteAsync). Renamed
            // parameter from ct->innerCt to keep the existing token-handling.
            {
                if (string.IsNullOrWhiteSpace(senderUpn))
                    throw new InvalidOperationException("Sender (From) address is required.");

                if (header is not IGraphMailHeader mailHeader)
                    throw new InvalidOperationException("Header must implement IGraphMailHeader.");

                string trNo = mailHeader.TransmittalNo ?? "(no number)";
                string projectNo = mailHeader.ProjectNumber ?? "(no project)";
                string projectName = mailHeader.ProjectName ?? "";
                string purpose = mailHeader.Purpose ?? "";
                string remarksHtml = mailHeader.Remarks ?? "";
                string sanitizedRemarksHtml = string.IsNullOrWhiteSpace(remarksHtml) ? string.Empty : Sanitizer.Sanitize(remarksHtml);

                bool isQuickTransfer =
                    string.IsNullOrWhiteSpace(coverSheetLocalPath) &&
                    string.IsNullOrWhiteSpace(coverSheetServerUrl) &&
                    !attachCover;

                string? headerSubject = mailHeader.Subject;

                var toList = new List<Microsoft.Graph.Models.Recipient>();
                if (toAndCcEmails != null)
                {
                    foreach (var addr in toAndCcEmails
                             .Where(a => !string.IsNullOrWhiteSpace(a))
                             .Select(a => a.Trim())
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!addr.Contains('@')) continue;

                        toList.Add(new Microsoft.Graph.Models.Recipient
                        {
                            EmailAddress = new EmailAddress { Address = addr, Name = addr }
                        });
                    }
                }

                if (toList.Count == 0)
                {
                    var recips = mailHeader.Recipients;
                    if (recips != null)
                    {
                        foreach (var r in recips)
                        {
                            var email = r.Email;
                            var name = string.IsNullOrWhiteSpace(r.DisplayName) ? email : r.DisplayName;
                            if (!string.IsNullOrWhiteSpace(email))
                            {
                                toList.Add(new Microsoft.Graph.Models.Recipient
                                {
                                    EmailAddress = new EmailAddress
                                    {
                                        Address = email.Trim(),
                                        Name = name?.Trim()
                                    }
                                });
                            }
                        }
                    }
                }

                if (toList.Count == 0)
                    throw new InvalidOperationException("No valid recipients were supplied.");

                string subject = isQuickTransfer && !string.IsNullOrWhiteSpace(headerSubject)
                    ? headerSubject
                    : $"Transmittal {trNo} — {projectNo}{(string.IsNullOrWhiteSpace(projectName) ? "" : " - " + projectName)}";

                static string E(string s) => WebUtility.HtmlEncode(s);

                var bodyHtml = new System.Text.StringBuilder();
                bodyHtml.Append("<html><body>");

                if (!string.IsNullOrWhiteSpace(purpose))
                {
                    bodyHtml.Append("<p><strong>Purpose:</strong> ")
                            .Append(E(purpose))
                            .Append("</p>");
                }

                if (!string.IsNullOrWhiteSpace(sanitizedRemarksHtml))
                {
                    bodyHtml.Append("<div>")
                            .Append(sanitizedRemarksHtml)
                            .Append("</div>");
                }

                bodyHtml.Append("</body></html>");

                var message = new Message
                {
                    Subject = subject,
                    Body = new ItemBody
                    {
                        ContentType = BodyType.Html,
                        Content = bodyHtml.ToString()
                    },
                    ToRecipients = toList
                };

                if (attachCover && !string.IsNullOrWhiteSpace(coverSheetLocalPath) && File.Exists(coverSheetLocalPath))
                {
                    var bytes = await File.ReadAllBytesAsync(coverSheetLocalPath, innerCt);
                    message.Attachments = new List<Attachment>
                    {
                        new FileAttachment
                        {
                            Name = Path.GetFileName(coverSheetLocalPath),
                            ContentBytes = bytes,
                            ContentType = "application/pdf",
                            IsInline = false
                        }
                    };
                }

                await _graph.Users[senderUpn].SendMail.PostAsync(
                    new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                    {
                        Message = message,
                        SaveToSentItems = true
                    },
                    cancellationToken: innerCt);
            }
        }

        /// <summary>
        /// Returns the profile photo stream for the specified UPN.
        /// Requires Graph Application permission: User.Read.All (admin-consented).
        /// Returns null if the user has no photo or access is denied.
        /// </summary>
        public async Task<Stream?> TryGetUserPhotoAsync(string userPrincipalName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userPrincipalName)) return null;

            try
            {
                // Try the original endpoint
                var stream = await _graph.Users[userPrincipalName]
                                         .Photo
                                         .Content
                                         .GetAsync(requestConfiguration: null, cancellationToken: ct);
                if (stream != null) return stream;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError e)
            {
                System.Diagnostics.Debug.WriteLine($"Graph photo ODataError: {e.Error?.Code} - {e.Error?.Message}");
                // falls through to try the sized endpoint
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Graph photo error: {ex.Message}");
                // falls through to try the sized endpoint
            }

            // Some tenants only return sized photos
            try
            {
                var sized = await _graph.Users[userPrincipalName]
                                        .Photos["48x48"]
                                        .Content
                                        .GetAsync(requestConfiguration: null, cancellationToken: ct);
                return sized;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Graph sized photo error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Attempts to retrieve the user's given name, surname, and display name.
        /// </summary>
        /// <param name="userPrincipalName">The user principal name to query.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The available user name values.</returns>
        public async Task<(string? Given, string? Surname, string? Display)> TryGetUserNamesAsync(
            string userPrincipalName, CancellationToken ct = default)
        {
            try
            {
                var user = await _graph.Users[userPrincipalName].GetAsync(rc =>
                {
                    rc.QueryParameters.Select = new[] { "givenName", "surname", "displayName" };
                }, ct);

                return (user?.GivenName, user?.Surname, user?.DisplayName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return (null, null, null);
            }
        }

        // ---------------------------------------------------
        // Drive/folder helpers
        // ---------------------------------------------------

        /// <inheritdoc />
        public async Task<DriveItem> EnsureFolderPathAsync(string driveId, string relativePath, CancellationToken ct)
        {
            relativePath = (relativePath ?? "").Trim().TrimStart('/').Replace('\\', '/');
            if (string.IsNullOrEmpty(relativePath))
            {
                return await _graph.Drives[driveId].Root.GetAsync(cancellationToken: ct)
                       ?? throw new Exception("Drive root not found.");
            }

            try
            {
                var existingPathItem = await _graph.Drives[driveId]
                    .Root
                    .ItemWithPath(relativePath)
                    .GetAsync(cancellationToken: ct);

                if (existingPathItem != null)
                    return existingPathItem;
            }
            catch (Exception ex) when (ex is Microsoft.Graph.Models.ODataErrors.ODataError odata
                    ? odata.ResponseStatusCode == (int)HttpStatusCode.NotFound
                    : ex is ServiceException se && se.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                // Path does not exist yet; fall back to creating missing segments.
            }

            var segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            var parent = await _graph.Drives[driveId].Root.GetAsync(cancellationToken: ct)
                         ?? throw new Exception("Drive root not found.");

            foreach (var seg in segments)
            {
                // Resolve segment by name within the parent. ItemWithPath is O(1)
                // server-side -- no need to LIST and scan, which previously broke
                // for parents with >200 children (default page size) when the
                // target wasn't on page 1. Returns null on 404.
                var existing = await TryGetChildByNameAsync(driveId, parent.Id!, seg, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.Folder is null)
                        throw new InvalidOperationException(
                            $"Path segment '{seg}' under '{relativePath}' is occupied by a non-folder item (id={existing.Id}). " +
                            "Move or delete it on SharePoint before retrying.");
                    parent = existing;
                    continue;
                }

                DriveItem? created;
                try
                {
                    created = await _graph.Drives[driveId].Items[parent.Id].Children.PostAsync(new DriveItem
                    {
                        Name = seg,
                        Folder = new Folder(),
                        AdditionalData = new Dictionary<string, object>
                        {
                            // "fail" gives us a deterministic 409 we can adopt below.
                            // "replace" is unreliable for folders and can return errors
                            // when the existing target already has children.
                            { "@microsoft.graph.conflictBehavior", "fail" }
                        }
                    }, cancellationToken: ct);
                }
                catch (Microsoft.Graph.Models.ODataErrors.ODataError odata) when (IsNameConflict(odata))
                {
                    // Another caller created this segment between our existence
                    // probe and this POST. Re-resolve by name (NOT a LIST scan --
                    // that's the bug we just stopped above) and adopt.
                    created = await TryGetChildByNameAsync(driveId, parent.Id!, seg, ct).ConfigureAwait(false);
                    if (created is null) throw;
                    if (created.Folder is null)
                        throw new InvalidOperationException(
                            $"Path segment '{seg}' under '{relativePath}' is occupied by a non-folder item (id={created.Id}) " +
                            "after a 409 race. Move or delete it on SharePoint before retrying.");
                }

                if (created == null)
                    throw new InvalidOperationException($"Graph API returned null when creating folder segment '{seg}' under path '{relativePath}'.");
                parent = created;
            }

            return parent;
        }

        // Resolve a single child by name within parent. Returns null on 404.
        // Used by EnsureFolderPathAsync's segment loop and 409-adopt branch.
        private async Task<DriveItem?> TryGetChildByNameAsync(string driveId, string parentId, string name, CancellationToken ct)
        {
            try
            {
                return await _graph.Drives[driveId].Items[parentId].ItemWithPath(name).GetAsync(cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError oe) when (oe.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (ServiceException se) when (se.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        // Graph signals "this name already taken under this parent" via either
        // HTTP 409 or an error code of "nameAlreadyExists"; older Kiota builds
        // surface only the message text. Match all three to stay future-proof.
        // Public so callers like ProjectCleanOp can reuse the same classification
        // instead of duplicating brittle string-matching.
        public static bool IsNameConflict(Microsoft.Graph.Models.ODataErrors.ODataError odata)
        {
            if (odata.ResponseStatusCode == 409) return true;
            if (string.Equals(odata.Error?.Code, "nameAlreadyExists", StringComparison.OrdinalIgnoreCase)) return true;
            return odata.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) ?? false;
        }

        // ---------------------------------------------------
        // Listing / download / delete / rename / move
        // ---------------------------------------------------

        /// <inheritdoc />
        public async IAsyncEnumerable<DriveItem> ListChildrenAsync(
            string driveId,
            string folderItemId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // Manual pagination so callers get a true streaming IAsyncEnumerable;
            // PageIterator buffers internally and fights cancellation.
            DriveItemCollectionResponse? page = await RetryPipeline.ExecuteAsync(
                async innerCt => await _graph.Drives[driveId].Items[folderItemId].Children.GetAsync(cancellationToken: innerCt),
                ct).ConfigureAwait(false);

            while (page is not null)
            {
                if (page.Value is not null)
                {
                    foreach (var item in page.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return item;
                    }
                }

                if (string.IsNullOrEmpty(page.OdataNextLink))
                    yield break;

                var nextLink = page.OdataNextLink;
                page = await RetryPipeline.ExecuteAsync(
                    async innerCt => await _graph.Drives[driveId].Items[folderItemId].Children
                        .WithUrl(nextLink)
                        .GetAsync(cancellationToken: innerCt),
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<DriveItem> ListChildrenByPathAsync(
            string driveId,
            string folderRelativePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var folder = await EnsureFolderPathAsync(driveId, folderRelativePath, ct).ConfigureAwait(false);
            if (folder.Id is null)
                yield break;
            await foreach (var child in ListChildrenAsync(driveId, folder.Id, ct).ConfigureAwait(false))
                yield return child;
        }

        /// <inheritdoc />
        public async Task<DriveItem?> TryGetItemByPathAsync(string driveId, string relativePath, CancellationToken ct)
        {
            relativePath = (relativePath ?? string.Empty).Trim().TrimStart('/').Replace('\\', '/');
            try
            {
                // Run the GET inside the resilience pipeline so 429/5xx are
                // retried (with Retry-After) just like every other Graph call.
                // 404 is NOT in the retry predicate, so it tunnels straight
                // out to the catch below and becomes a null result.
                return await RetryPipeline.ExecuteAsync(
                    async innerCt =>
                    {
                        if (string.IsNullOrEmpty(relativePath))
                            return await _graph.Drives[driveId].Root.GetAsync(cancellationToken: innerCt).ConfigureAwait(false);
                        return await _graph.Drives[driveId].Root.ItemWithPath(relativePath).GetAsync(cancellationToken: innerCt).ConfigureAwait(false);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<DriveItem> ListChildrenByPathIfExistsAsync(
            string driveId,
            string folderRelativePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var folder = await TryGetItemByPathAsync(driveId, folderRelativePath, ct).ConfigureAwait(false);
            if (folder?.Id is null) yield break;
            await foreach (var child in ListChildrenAsync(driveId, folder.Id, ct).ConfigureAwait(false))
                yield return child;
        }

        /// <inheritdoc />
        public Task<Stream> DownloadAsync(string driveId, string itemId, CancellationToken ct)
            => RetryPipeline.ExecuteAsync(
                async innerCt =>
                {
                    var s = await _graph.Drives[driveId].Items[itemId].Content.GetAsync(cancellationToken: innerCt).ConfigureAwait(false);
                    return s ?? throw new InvalidOperationException($"Drive item '{itemId}' returned no content stream.");
                },
                ct).AsTask();

        /// <inheritdoc />
        public async Task<Stream> DownloadByPathAsync(string driveId, string relativePath, CancellationToken ct)
        {
            // Retry the metadata lookup too. A transient 429/503 here used
            // to fail the whole monthly EOR run because EOR.csv was loaded
            // via this single shot.
            var item = await RetryPipeline.ExecuteAsync(
                async innerCt => await _graph.Drives[driveId].Root.ItemWithPath(relativePath).GetAsync(cancellationToken: innerCt).ConfigureAwait(false),
                ct).ConfigureAwait(false);
            if (item?.Id is null)
                throw new InvalidOperationException($"No item at path '{relativePath}'.");
            return await DownloadAsync(driveId, item.Id, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task DeleteItemAsync(string driveId, string itemId, CancellationToken ct)
            => RetryPipeline.ExecuteAsync(
                async innerCt => await _graph.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: innerCt).ConfigureAwait(false),
                ct).AsTask();

        /// <inheritdoc />
        public async Task<bool> TryDeleteItemAsync(string driveId, string itemId, CancellationToken ct)
        {
            try
            {
                await DeleteItemAsync(driveId, itemId, ct).ConfigureAwait(false);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public Task<DriveItem> RenameItemAsync(string driveId, string itemId, string newName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is required.", nameof(newName));

            return RetryPipeline.ExecuteAsync(
                async innerCt =>
                {
                    var patched = await _graph.Drives[driveId].Items[itemId].PatchAsync(
                        new DriveItem { Name = newName },
                        cancellationToken: innerCt).ConfigureAwait(false);
                    return patched ?? throw new InvalidOperationException($"PATCH name returned null for item '{itemId}'.");
                },
                ct).AsTask();
        }

        /// <inheritdoc />
        // Hard ceiling for the simple PUT path. Graph allows up to 250 MiB
        // per /content PUT, but at that size the lack of chunk recovery means
        // any transient mid-stream failure restarts from byte zero. Force
        // anything bigger to go through UploadToFolderAsync's resumable path.
        // 4 MiB matches the comment on the interface and is well below the
        // size at which a single failure becomes painful.
        private const long SimplePutMaxBytes = 4L * 1024 * 1024;

        public Task<DriveItem> UploadSimpleAsync(string driveId, string folderId, string fileName, string localFilePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name is required.", nameof(fileName));
            if (string.IsNullOrWhiteSpace(localFilePath))
                throw new ArgumentException("Local file path is required.", nameof(localFilePath));

            var fi = new FileInfo(localFilePath);
            if (!fi.Exists)
                throw new FileNotFoundException($"Source file not found: {localFilePath}", localFilePath);
            if (fi.Length > SimplePutMaxBytes)
                throw new InvalidOperationException(
                    $"UploadSimpleAsync called with a {fi.Length:n0}-byte file (max {SimplePutMaxBytes:n0}). " +
                    "Use UploadToFolderAsync for files past the simple PUT threshold -- it survives mid-stream failures via resumable sessions.");

            return RetryPipeline.ExecuteAsync(
                async innerCt =>
                {
                    using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var item = await _graph.Drives[driveId]
                        .Items[folderId]
                        .ItemWithPath(fileName)
                        .Content
                        .PutAsync(fs, cancellationToken: innerCt)
                        .ConfigureAwait(false);
                    return item ?? throw new InvalidOperationException($"Simple PUT returned null for '{fileName}'.");
                },
                ct).AsTask();
        }

        /// <inheritdoc />
        public Task<DriveItem> MoveItemAsync(string driveId, string itemId, string destinationFolderId, string? newName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(destinationFolderId))
                throw new ArgumentException("Destination folder id is required.", nameof(destinationFolderId));

            return RetryPipeline.ExecuteAsync(
                async innerCt =>
                {
                    var body = new DriveItem
                    {
                        ParentReference = new ItemReference { Id = destinationFolderId },
                    };
                    if (!string.IsNullOrWhiteSpace(newName))
                        body.Name = newName;

                    var patched = await _graph.Drives[driveId].Items[itemId].PatchAsync(body, cancellationToken: innerCt).ConfigureAwait(false);
                    return patched ?? throw new InvalidOperationException($"PATCH parentReference returned null for item '{itemId}'.");
                },
                ct).AsTask();
        }

        private static HtmlSanitizer CreateSanitizer()
        {
            var s = new HtmlSanitizer();
            s.AllowedSchemes.Add("data");
            return s;
        }
    }
}
