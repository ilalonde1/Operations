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
    }

    /// <summary>
    /// Implements Graph operations for uploads, sharing links, and email delivery.
    /// </summary>
    public sealed class GraphFacade : IGraphFacade
    {
        private static readonly HtmlSanitizer Sanitizer = new();
        private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<ServiceException>()
                    .Handle<Microsoft.Graph.Models.ODataErrors.ODataError>(ex => ex.ResponseStatusCode >= 500)
                    .Handle<TaskCanceledException>()
            })
            .Build();

        private readonly string _driveId;

        private readonly GraphServiceClient _graph;

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
            return await RetryPipeline.ExecuteAsync(async innerCt =>
            {
                var folderItem = await EnsureFolderPathAsync(_driveId, folderRelativePath, innerCt);

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

                var session = await _graph.Drives[_driveId]
                                          .Items[folderItem.Id]
                                          .ItemWithPath(fileName)
                                          .CreateUploadSession
                                          .PostAsync(createBody, cancellationToken: innerCt);

                var chunkSize = 5 * 1024 * 1024;
                var fileLength = fs.Length;
                var uploader = new LargeFileUploadTask<DriveItem>(session!, fs, chunkSize);

                IProgress<long> onChunk = new Progress<long>(sent => progress?.Report((fileName, sent, fileLength)));
                var uploadResult = await uploader.UploadAsync(onChunk, cancellationToken: innerCt);

                if (!uploadResult.UploadSucceeded)
                    throw new Exception($"Upload failed for {fileName}");

                var uploadedItem = uploadResult.ItemResponse;

                progress?.Report((fileName, fs.Length, fs.Length));
                return new GraphUploadResult
                {
                    DriveId = _driveId,
                    ItemId = uploadedItem?.Id ?? string.Empty,
                    WebUrl = uploadedItem?.WebUrl ?? string.Empty
                };
            }, ct);
        }

        /// <inheritdoc />
        public async Task<CreateLinksResult> CreateLinksAsync(string folderRelativePath, bool needExternal, CancellationToken ct)
        {
            return await RetryPipeline.ExecuteAsync(async innerCt =>
            {
                var folderItem = await EnsureFolderPathAsync(_driveId, folderRelativePath, innerCt);

                var org = await _graph.Drives[_driveId].Items[folderItem.Id]
                    .CreateLink
                    .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                    {
                        Type = "view",
                        Scope = "organization"
                    }, cancellationToken: innerCt);

                string? anonUrl = null;
                if (needExternal)
                {
                    var anon = await _graph.Drives[_driveId].Items[folderItem.Id]
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
            }, ct);
        }

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
            await RetryPipeline.ExecuteAsync(async innerCt =>
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
            }, ct);
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
                var children = await _graph.Drives[driveId].Items[parent.Id].Children.GetAsync(cancellationToken: ct);
                var existing = children?.Value?.FirstOrDefault(i => i.Folder != null &&
                                                                     string.Equals(i.Name, seg, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    parent = existing;
                    continue;
                }

                var created = await _graph.Drives[driveId].Items[parent.Id].Children.PostAsync(new DriveItem
                {
                    Name = seg,
                    Folder = new Folder(),
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "replace" }
                    }
                }, cancellationToken: ct);

                if (created == null)
                    throw new InvalidOperationException($"Graph API returned null when creating folder segment '{seg}' under path '{relativePath}'.");
                parent = created;
            }

            return parent;
        }
    }
}
