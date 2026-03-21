#nullable enable
using Ganss.Xss;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Graph;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public sealed class TransmittalService : ITransmittalService
    {
        private const long AttachThreshold = 10L * 1024 * 1024;
        private static readonly HtmlSanitizer Sanitizer = new();

        private readonly IGraphFacade _graphFacade;
        private readonly IUploadOrchestrator _uploadOrchestrator;
        private readonly ITransmittalsStore? _transmittalsStore;
        private readonly string? _transmittalsConnectionString;
        private readonly string _redirectorBase;
        private readonly string? _appVersion;

        public TransmittalService(
            IGraphFacade graphFacade,
            IUploadOrchestrator uploadOrchestrator,
            ITransmittalsStore? transmittalsStore,
            string? transmittalsConnectionString,
            string? redirectorBase,
            string? appVersion)
        {
            _graphFacade = graphFacade ?? throw new ArgumentNullException(nameof(graphFacade));
            _uploadOrchestrator = uploadOrchestrator ?? throw new ArgumentNullException(nameof(uploadOrchestrator));
            _transmittalsStore = transmittalsStore;
            _transmittalsConnectionString = transmittalsConnectionString;
            _redirectorBase = (redirectorBase ?? string.Empty).TrimEnd('/');
            _appVersion = appVersion;
        }

        public async Task<TransmittalSendResult> SendAsync(TransmittalSendRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var header = request.Header;
            if (string.IsNullOrWhiteSpace(header.ProjectNumber))
                throw new ArgumentException("ProjectNumber is required.", nameof(request));

            header.TransmittalNo = await _graphFacade
                .ReserveTransmittalNumberAsync(header.ProjectNumber)
                .ConfigureAwait(false);
            header.SharePointFolderPath = request.Folder;

            var transmittalId = _transmittalsStore == null ? (Guid?)null : Guid.NewGuid();
            var subject = header.Subject ?? string.Empty;
            var persistenceWarning = false;

            var upload = await _uploadOrchestrator.UploadAsync(
                header,
                request.Files,
                request.Folder,
                request.NeedExternal,
                request.UploadProgress,
                request.Status,
                ct).ConfigureAwait(false);

            var sharePointUrl = upload.ExternalLink ?? upload.InternalLink ?? string.Empty;
            var allRecipients = request.ToRecipients
                .Concat(request.CcRecipients)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recipientRecords = allRecipients
                .Select(email =>
                {
                    var linkId = Guid.NewGuid();
                    string? clickUrl = string.IsNullOrWhiteSpace(_redirectorBase)
                        ? sharePointUrl
                        : $"{_redirectorBase}/t/{linkId}";

                    return (
                        Email: email,
                        Kind: request.ToRecipients.Contains(email, StringComparer.OrdinalIgnoreCase) ? "To" : "Cc",
                        LinkId: linkId,
                        PersonalShareLink: (string?)clickUrl);
                })
                .ToList();

            if (_transmittalsStore != null && transmittalId.HasValue)
            {
                try
                {
                    await _transmittalsStore.LogTransmittalWithRecipientsAsync(
                        transmittalId.Value,
                        header.ProjectNumber ?? string.Empty,
                        subject,
                        upload.DriveId,
                        upload.ItemId,
                        upload.CoverSharePointUrl ?? string.Empty,
                        DateTime.UtcNow,
                        request.SenderUpn ?? string.Empty,
                        _appVersion,
                        recipientRecords,
                        ct: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    persistenceWarning = true;
                    Debug.WriteLine($"[TransmittalService] LogTransmittalWithRecipientsAsync failed for transmittal '{transmittalId}': {ex}");
                }
            }

            try
            {
                foreach (var recipient in recipientRecords)
                {
                    ct.ThrowIfCancellationRequested();

                    var email = recipient.Email;
                    var linkId = recipient.LinkId;
                    var clickUrl = recipient.PersonalShareLink ?? sharePointUrl;
                    var pixelUrl = string.IsNullOrWhiteSpace(_redirectorBase)
                        ? null
                        : $"{_redirectorBase}/o/{linkId}/{Uri.EscapeDataString(email)}";

                    if (!string.IsNullOrWhiteSpace(_transmittalsConnectionString))
                    {
                        try
                        {
                            await InsertRedirectTargetsAsync(
                                _transmittalsConnectionString,
                                transmittalId,
                                new[] { (linkId, email, sharePointUrl) },
                                ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            persistenceWarning = true;
                            Debug.WriteLine($"[TransmittalService] InsertRedirectTargetsAsync failed for recipient '{email}' and link '{linkId}': {ex}");
                        }
                    }

                    header.Remarks = BuildEmailBodyHtml(
                        request.RemarksHtml,
                        request.SignatureHtml,
                        clickUrl,
                        pixelUrl,
                        string.Join("; ", request.ToRecipients),
                        string.Join("; ", request.CcRecipients));

                    if (!string.IsNullOrWhiteSpace(clickUrl))
                    {
                        header.ExternalLink = clickUrl;
                        header.InternalLink = clickUrl;
                    }

                    await _graphFacade.SendMailAsync(
                        header,
                        $"{request.Folder}/{header.CoverSheetFileName}",
                        upload.CoverLocalPath,
                        request.AttachIfSmall && request.Files.Sum(x => x.SizeBytes) < AttachThreshold,
                        ct,
                        request.SenderUpn,
                        new[] { email }).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(upload.CoverLocalPath) &&
                    System.IO.File.Exists(upload.CoverLocalPath))
                {
                    System.IO.File.Delete(upload.CoverLocalPath);
                }
            }

            if (_transmittalsStore != null && transmittalId.HasValue)
            {
                try
                {
                    await _transmittalsStore.MarkSentAsync(
                        transmittalId.Value,
                        DateTime.UtcNow,
                        request.SenderUpn ?? string.Empty,
                        _appVersion,
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    persistenceWarning = true;
                    Debug.WriteLine($"[TransmittalService] MarkSentAsync failed for transmittal '{transmittalId}': {ex}");
                }
            }

            request.Status?.Report(persistenceWarning
                ? "Done. Some persistence operations failed."
                : "Done.");

            return new TransmittalSendResult(request.Folder, upload.CoverLocalPath, allRecipients);
        }

        private static string BuildEmailBodyHtml(
            string? remarksHtml,
            string? signatureHtml,
            string? linkUrl,
            string? pixelUrl,
            string? toRecipients,
            string? ccRecipients)
        {
            var sb = new StringBuilder();
            var sanitizedRemarksHtml = string.IsNullOrWhiteSpace(remarksHtml) ? string.Empty : Sanitizer.Sanitize(remarksHtml);
            var sanitizedSignatureHtml = string.IsNullOrWhiteSpace(signatureHtml) ? string.Empty : Sanitizer.Sanitize(signatureHtml);

            if (!string.IsNullOrWhiteSpace(sanitizedRemarksHtml))
            {
                sb.Append(sanitizedRemarksHtml.Trim());
                sb.Append("<br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                sb.Append("<b>View files: <a href=\"")
                  .Append(WebUtility.HtmlEncode(linkUrl))
                  .Append("\">Click here to view the files</a></b><br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(sanitizedSignatureHtml))
            {
                sb.Append(sanitizedSignatureHtml.Trim());
            }

            if (!string.IsNullOrWhiteSpace(pixelUrl))
            {
                sb.Append("<img src=\"")
                  .Append(WebUtility.HtmlEncode(pixelUrl))
                  .Append("\" alt=\"\" style=\"display:none;width:1px;height:1px;\" />");
            }

            return sb.ToString();
        }

        private static async Task InsertRedirectTargetsAsync(
            string connectionString,
            Guid? transmittalId,
            IEnumerable<(Guid LinkId, string Email, string TargetUrl)> records,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.RedirectTargets
    (Id, TargetUrl, RecipientEmail, CreatedAt, TransmittalId)
VALUES
    (@Id, @TargetUrl, @RecipientEmail, SYSUTCDATETIME(), @TransmittalId);";

            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            foreach (var record in records)
            {
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.Parameters.AddWithValue("@Id", record.LinkId);
                cmd.Parameters.AddWithValue("@TargetUrl", record.TargetUrl ?? string.Empty);
                cmd.Parameters.AddWithValue("@RecipientEmail", record.Email ?? string.Empty);
                cmd.Parameters.AddWithValue("@TransmittalId", (object?)transmittalId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
