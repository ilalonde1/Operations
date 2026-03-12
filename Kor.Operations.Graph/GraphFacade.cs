#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Kor.Operations.Core;
namespace Kor.Operations.Graph
{
    public sealed class GraphFacade
    {
        private readonly string _driveId;

        private readonly GraphServiceClient _graph;

        private GraphFacade(GraphServiceClient graph, string driveId)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _driveId = string.IsNullOrWhiteSpace(driveId) ? throw new ArgumentNullException(nameof(driveId)) : driveId;
        }

        private static readonly object _initLock = new();
        private static GraphFacade? _instance;

        public static GraphFacade Instance =>
            _instance ?? throw new InvalidOperationException(
                "GraphFacade is not initialized. Call GraphFacade.Initialize(IAuthenticationProvider, driveId) at app startup.");

        public static void Initialize(IAuthenticationProvider authenticationProvider, string driveId)
        {
            if (authenticationProvider is null) throw new ArgumentNullException(nameof(authenticationProvider));
            if (string.IsNullOrWhiteSpace(driveId)) throw new ArgumentNullException(nameof(driveId));

            lock (_initLock)
            {
                if (_instance != null) return; // idempotent
                _instance = new GraphFacade(new GraphServiceClient(authenticationProvider), driveId);
            }
        }

        // ---------------------------------------------------
        // Public API
        // ---------------------------------------------------

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
            var folderItem = await EnsureFolderPathAsync(_driveId, folderRelativePath, ct);

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
                                      .PostAsync(createBody, cancellationToken: ct);

            var chunkSize = 5 * 1024 * 1024; // 5 MiB (multiple of 320 KiB; fewer round trips for large files)
            var uploader = new LargeFileUploadTask<DriveItem>(session!, fs, chunkSize);

            IProgress<long> onChunk = new Progress<long>(sent => progress?.Report((fileName, sent, fs.Length)));
            var uploadResult = await uploader.UploadAsync(onChunk, cancellationToken: ct);

            if (!uploadResult.UploadSucceeded)
                throw new Exception($"Upload failed for {fileName}");

            var uploadedItem = await _graph.Drives[_driveId]
                                           .Items[folderItem.Id]
                                           .ItemWithPath(fileName)
                                           .GetAsync(cancellationToken: ct);

            progress?.Report((fileName, fs.Length, fs.Length));
            return uploadedItem?.WebUrl ?? string.Empty;
        }

        public sealed class CreateLinksResult
        {
            public string InternalLink { get; init; } = string.Empty;
            public string? ExternalLink { get; init; }
        }

        public async Task<CreateLinksResult> CreateLinksAsync(string folderRelativePath, bool needExternal, CancellationToken ct)
        {
            var folderItem = await EnsureFolderPathAsync(_driveId, folderRelativePath, ct);

            var org = await _graph.Drives[_driveId].Items[folderItem.Id]
                .CreateLink
                .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                {
                    Type = "view",
                    Scope = "organization"
                }, cancellationToken: ct);

            string? anonUrl = null;
            if (needExternal)
            {
                var anon = await _graph.Drives[_driveId].Items[folderItem.Id]
                    .CreateLink
                    .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
                    {
                        Type = "view",
                        Scope = "anonymous"
                    }, cancellationToken: ct);
                anonUrl = anon?.Link?.WebUrl;
            }

            return new CreateLinksResult
            {
                InternalLink = org?.Link?.WebUrl ?? string.Empty,
                ExternalLink = anonUrl
            };
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
            if (string.IsNullOrWhiteSpace(senderUpn))
                throw new InvalidOperationException("Sender (From) address is required.");

            if (header is not IGraphMailHeader mailHeader)
                throw new InvalidOperationException("Header must implement IGraphMailHeader.");

            string trNo = mailHeader.TransmittalNo ?? "(no number)";
            string projectNo = mailHeader.ProjectNumber ?? "(no project)";
            string projectName = mailHeader.ProjectName ?? "";
            string purpose = mailHeader.Purpose ?? "";
            // Remarks is treated as HTML, built by the caller (MainWindow / QuickTransferRunner)
            string remarksHtml = mailHeader.Remarks ?? "";
            string? internalLink = mailHeader.InternalLink;
            string? externalLink = mailHeader.ExternalLink;

            // Quick-transfer detection: no cover sheet paths and no attachment requested
            bool isQuickTransfer =
                string.IsNullOrWhiteSpace(coverSheetLocalPath) &&
                string.IsNullOrWhiteSpace(coverSheetServerUrl) &&
                !attachCover;

            // If Quick Transfer, use the subject already set on the header
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

            // -------- SUBJECT SELECTION --------
            string subject;
            if (isQuickTransfer && !string.IsNullOrWhiteSpace(headerSubject))
            {
                // Quick Transfer: use the subject built in QuickTransferRunner,
                // e.g. "RE: <original subject> - File Transfer"
                subject = headerSubject!;
            }
            else
            {
                // Full transmittal: keep existing pattern
                subject = $"Transmittal {trNo} — {projectNo}{(string.IsNullOrWhiteSpace(projectName) ? "" : " - " + projectName)}";
            }

            // ---------- Build HTML body ----------
            static string E(string s) => WebUtility.HtmlEncode(s);

            var bodyHtml = new System.Text.StringBuilder();
            bodyHtml.Append("<html><body>");

            if (!string.IsNullOrWhiteSpace(purpose))
            {
                bodyHtml.Append("<p><strong>Purpose:</strong> ")
                        .Append(E(purpose))
                        .Append("</p>");
            }

            // NOTE:
            // We intentionally no longer output the raw "Internal link:" /
            // "External link:" lines here. Any friendly "View files: Click here
            // to view the files" link should be built into remarksHtml by the
            // caller (QuickTransferRunner / MainWindow), so we just render
            // remarksHtml below.

            if (!string.IsNullOrWhiteSpace(remarksHtml))
            {
                // remarksHtml is already HTML or plain text with whatever formatting
                bodyHtml.Append("<div>")
                        .Append(remarksHtml)
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
                var bytes = await File.ReadAllBytesAsync(coverSheetLocalPath, ct);
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

            // Prefer explicit sender for service/app contexts; fall back to /me for delegated contexts.
            if (!string.IsNullOrWhiteSpace(senderUpn))
            {
                await _graph.Users[senderUpn].SendMail.PostAsync(
                        new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                        {
                            Message = message,
                            SaveToSentItems = true
                        },
                        cancellationToken: ct);
            }
            else
            {
                await _graph.Me.SendMail.PostAsync(
                        new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                        {
                            Message = message,
                            SaveToSentItems = true
                        },
                        cancellationToken: ct);
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

        private async Task<DriveItem> EnsureFolderPathAsync(string driveId, string relativePath, CancellationToken ct)
        {
            relativePath = (relativePath ?? "").Trim().TrimStart('/').Replace('\\', '/');
            if (string.IsNullOrEmpty(relativePath))
                return await _graph.Drives[driveId].Root.GetAsync(cancellationToken: ct)
                       ?? throw new Exception("Drive root not found.");

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

                parent = created!;
            }

            return parent;
        }
    }
}
