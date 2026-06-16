#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Graph;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations
{
    /// <summary>
    /// Engine for inbound uploads (external -> KOR).
    /// Mirrors QuickTransferRunner but in reverse:
    /// - Files are on disk (temp paths) in request.Files
    /// - Uploads them into an "Incoming Files" area in SharePoint
    /// - Logs a row into dbo.Transmittals with Type = 'Upload'
    /// - Sends a notification email to the internal recipient
    ///
    /// Does not touch existing transmittal / quick transfer code.
    /// </summary>
    public sealed class InboundUploadRunner
    {
        private readonly ITransmittalsStore? _store;
        private readonly IGraphFacade _graphFacade;

        public InboundUploadRunner(ITransmittalsStore? store, IGraphFacade graphFacade)
        {
            _store = store;
            _graphFacade = graphFacade ?? throw new ArgumentNullException(nameof(graphFacade));
        }

        public async Task RunAsync(
            InboundUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
                throw new InvalidOperationException("RecipientEmail is required for InboundUploadRunner.");

            if (request.Files == null || request.Files.Count == 0)
                throw new InvalidOperationException("At least one file is required for InboundUploadRunner.");

            // ------------------------------------------------------------
            // 1) Build header via WizardState (same pattern as MainWindow)
            // ------------------------------------------------------------
            var wizard = new WizardState();
            var header = wizard.Header;

            header.ProjectNumber = request.ProjectNumber ?? "External";
            header.ProjectName = string.IsNullOrWhiteSpace(request.ProjectNumber)
                ? "External Upload"
                : request.ProjectNumber;

            header.FromEmail = request.SenderEmail ?? string.Empty;
            header.FromName = request.SenderName ?? string.Empty;
            header.DateUtc = DateTime.UtcNow;
            header.SendVia = "SharePoint";

            var subject = BuildSubject(request);
            header.Subject = subject;
            try { header.GetType().GetProperty("EmailSubject")?.SetValue(header, subject); } catch (Exception) { /* optional property — may not exist on all header types */ }

            // ------------------------------------------------------------
            // 2) Reserve a number + build inbound folder path
            // ------------------------------------------------------------
            header.TransmittalNo = await _graphFacade
                .ReserveTransmittalNumberAsync(header.ProjectNumber)
                .ConfigureAwait(false);

            var folder = BuildInboundFolderPath(request.RecipientEmail);
            header.SharePointFolderPath = folder;

            // Same DB as other flows, but optional
            Guid? transmittalId = null;
            if (_store != null)
            {
                transmittalId = Guid.NewGuid();
            }

            // ------------------------------------------------------------
            // 3) Upload files to SharePoint
            // ------------------------------------------------------------
            foreach (var f in request.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(f.LocalPath) || !File.Exists(f.LocalPath))
                    continue;

                var name = string.IsNullOrWhiteSpace(f.FileName)
                    ? Path.GetFileName(f.LocalPath)
                    : f.FileName;

                var sp = await _graphFacade.UploadWithProgressAsync(
                    folder,
                    name,
                    f.LocalPath,
                    null,                // no UI progress here
                    cancellationToken
                ).ConfigureAwait(false);

                f.SharePointPath = sp;
            }

            // ------------------------------------------------------------
            // 4) Optional metadata JSON in the same folder
            // ------------------------------------------------------------
            try
            {
                var meta = new
                {
                    RecipientEmail = request.RecipientEmail,
                    SenderName = request.SenderName,
                    SenderEmail = request.SenderEmail,
                    SenderCompany = request.SenderCompany,
                    ProjectNumber = request.ProjectNumber,
                    Message = request.Message,
                    UploadedAtUtc = DateTime.UtcNow
                };

                var json = System.Text.Json.JsonSerializer.Serialize(
                    meta,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                var tmpPath = Path.GetTempFileName();
                await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8, cancellationToken);

                await _graphFacade.UploadWithProgressAsync(
                    folder,
                    "FileDropInfo.json",
                    tmpPath,
                    null,
                    cancellationToken
                ).ConfigureAwait(false);

                try { File.Delete(tmpPath); } catch (Exception) { /* temp file cleanup — best effort */ }
            }
            catch
            {
                // Nice-to-have; don't fail the upload if this breaks.
            }

            // ------------------------------------------------------------
            // 5) Create an internal-only link to the folder
            // ------------------------------------------------------------
            var links = await _graphFacade.CreateLinksAsync(
                folder,
                needExternal: false,
                cancellationToken
            ).ConfigureAwait(false);

            var sharePointUrl = links.InternalLink ?? string.Empty;
            header.InternalLink = sharePointUrl;
            header.ExternalLink = null;

            // ------------------------------------------------------------
            // 6) Log to dbo.Transmittals with Type = 'Upload'
            // ------------------------------------------------------------
            if (_store != null && transmittalId.HasValue)
            {
                try
                {
                    await _store.LogTransmittalAsync(
                        id: transmittalId.Value,
                        projectNo: header.ProjectNumber ?? string.Empty,
                        subject: subject,
                        driveId: "drv",
                        itemId: "itm",
                        sharePointUrl: sharePointUrl,
                        createdUtc: DateTime.UtcNow,
                        createdBy: request.SenderEmail ?? string.Empty,
                        appVersion: typeof(InboundUploadRunner).Assembly.GetName().Version?.ToString(),
                        type: "Upload",
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                catch
                {
                    // Do not block the upload if logging fails.
                }
            }

            // ------------------------------------------------------------
            // 7) Send notification email to the internal recipient
            // ------------------------------------------------------------
            var bodyHtml = BuildNotificationHtml(request, sharePointUrl);
            header.Remarks = bodyHtml;

            await _graphFacade.SendMailAsync(
                header,
                coverSheetServerUrl: string.Empty,
                coverSheetLocalPath: null,
                attachCover: false,
                ct: cancellationToken,
                // Sender UPN: internal KOR user receiving the files
                senderUpn: request.RecipientEmail,
                toAndCcEmails: new[] { request.RecipientEmail }
            ).ConfigureAwait(false);

            // ------------------------------------------------------------
            // 8) Mark sent in DB (if logged)
            // ------------------------------------------------------------
            if (_store != null && transmittalId.HasValue)
            {
                try
                {
                    await _store.MarkSentAsync(
                        transmittalId.Value,
                        DateTime.UtcNow,
                        request.SenderEmail ?? string.Empty,
                        typeof(InboundUploadRunner).Assembly.GetName().Version?.ToString(),
                        cancellationToken
                    ).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore; upload + email are already done.
                }
            }
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static string BuildSubject(InboundUploadRequest request)
        {
            var name = string.IsNullOrWhiteSpace(request.SenderName)
                ? request.SenderEmail
                : request.SenderName;

            var company = string.IsNullOrWhiteSpace(request.SenderCompany)
                ? null
                : request.SenderCompany;

            var proj = string.IsNullOrWhiteSpace(request.ProjectNumber)
                ? null
                : request.ProjectNumber;

            var sb = new StringBuilder("Files from ");
            sb.Append(name ?? "external sender");

            if (!string.IsNullOrWhiteSpace(company))
            {
                sb.Append(" (").Append(company).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(proj))
            {
                sb.Append(" for ").Append(proj);
            }

            return sb.ToString();
        }

        private static string BuildNotificationHtml(InboundUploadRequest request, string linkUrl)
        {
            static string E(string? s) => KorEmailTemplate.E(s);

            var inner = new StringBuilder();

            inner.Append("<p style=\"margin:0 0 4px;\">You have received files from <b>")
                 .Append(E(request.SenderName ?? request.SenderEmail ?? "external sender"))
                 .Append("</b> (")
                 .Append(E(request.SenderEmail ?? string.Empty))
                 .Append(").</p>");

            // Compact fact list (company / project) styled like the brief header facts.
            var facts = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(request.SenderCompany))
            {
                facts.Append("<p style=\"margin:2px 0;color:#6B7280;font-size:13px;\">")
                     .Append("<span style=\"font-weight:600;color:#111827;\">Company:</span> ")
                     .Append(E(request.SenderCompany)).Append("</p>");
            }
            if (!string.IsNullOrWhiteSpace(request.ProjectNumber))
            {
                facts.Append("<p style=\"margin:2px 0;color:#6B7280;font-size:13px;\">")
                     .Append("<span style=\"font-weight:600;color:#111827;\">Project:</span> ")
                     .Append(E(request.ProjectNumber)).Append("</p>");
            }
            if (facts.Length > 0)
            {
                inner.Append("<div style=\"margin:12px 0;\">").Append(facts).Append("</div>");
            }

            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                inner.Append("<blockquote style=\"margin:12px 0;padding:10px 16px;border-left:3px solid #FF5B35;")
                     .Append("background-color:#F8FAFC;color:#111827;font-size:13px;line-height:1.5;\">")
                     .Append(E(request.Message))
                     .Append("</blockquote>");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                inner.Append("<p style=\"margin:16px 0 12px;color:#111827;\">")
                     .Append("The uploaded files are available in SharePoint.")
                     .Append("</p>")
                     .Append(KorEmailTemplate.Button(linkUrl, "View files"));
            }

            return KorEmailTemplate.Shell("Files Received", inner.ToString());
        }

        private static string BuildInboundFolderPath(string recipientEmail)
        {
            var safeRecipient = SanitizeSegment(recipientEmail);
            var year = DateTime.UtcNow.ToString("yyyy");
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
            return $"Incoming Files/{safeRecipient}/{year}/{stamp}";
        }

        private static string SanitizeSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
            cleaned = cleaned.Trim().Trim('.', ' ');
            return string.IsNullOrEmpty(cleaned) ? "Unknown" : cleaned;
        }

    }

    /// <summary>
    /// DTO for handing files into InboundUploadRunner.
    /// Files are expected to be temp files on disk (LocalPath).
    /// </summary>
    public sealed class InboundUploadRequest
    {
        public string RecipientEmail { get; set; } = string.Empty;

        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string? SenderCompany { get; set; }
        public string? ProjectNumber { get; set; }
        public string? Message { get; set; }

        public List<TransmittalFile> Files { get; } = new List<TransmittalFile>();
    }
}
