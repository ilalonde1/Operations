#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Graph;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
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

        private readonly IUploadOrchestrator _uploadOrchestrator;
        private readonly ITransmittalsStore? _transmittalsStore;
        private readonly string? _transmittalsConnectionString;
        private readonly string _redirectorBase;
        private readonly string? _appVersion;

        public TransmittalService(
            IUploadOrchestrator uploadOrchestrator,
            ITransmittalsStore? transmittalsStore,
            string? transmittalsConnectionString,
            string? redirectorBase,
            string? appVersion)
        {
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
            header.TransmittalNo = await GraphFacade.Instance
                .ReserveTransmittalNumberAsync(header.ProjectNumber)
                .ConfigureAwait(false);
            header.SharePointFolderPath = request.Folder;

            var transmittalId = _transmittalsStore == null ? (Guid?)null : Guid.NewGuid();
            var subject = header.Subject ?? string.Empty;

            var upload = await _uploadOrchestrator.UploadAsync(
                header,
                request.Files,
                request.Folder,
                request.NeedExternal,
                request.UploadProgress,
                request.Status,
                ct).ConfigureAwait(false);

            if (_transmittalsStore != null && transmittalId.HasValue)
            {
                try
                {
                    await _transmittalsStore.LogTransmittalAsync(
                        transmittalId.Value,
                        header.ProjectNumber ?? string.Empty,
                        subject,
                        "drv",
                        "itm",
                        upload.CoverSharePointUrl ?? string.Empty,
                        DateTime.UtcNow,
                        request.SenderUpn ?? string.Empty,
                        _appVersion,
                        ct: ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            var sharePointUrl = upload.ExternalLink ?? upload.InternalLink ?? string.Empty;
            var allRecipients = request.ToRecipients
                .Concat(request.CcRecipients)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var recipientRecords = new List<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)>();

            foreach (var email in allRecipients)
            {
                ct.ThrowIfCancellationRequested();

                var linkId = Guid.NewGuid();
                var clickUrl = sharePointUrl;
                string? pixelUrl = null;

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
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(_redirectorBase))
                {
                    clickUrl = $"{_redirectorBase}/t/{linkId}";
                    pixelUrl = $"{_redirectorBase}/o/{linkId}/{Uri.EscapeDataString(email)}";
                }

                recipientRecords.Add((
                    email,
                    request.ToRecipients.Contains(email, StringComparer.OrdinalIgnoreCase) ? "To" : "Cc",
                    linkId,
                    clickUrl));

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

                await GraphFacade.Instance.SendMailAsync(
                    header,
                    $"{request.Folder}/{header.CoverSheetFileName}",
                    upload.CoverLocalPath,
                    request.AttachIfSmall && request.Files.Sum(x => x.SizeBytes) < AttachThreshold,
                    ct,
                    request.SenderUpn,
                    new[] { email }).ConfigureAwait(false);
            }

            if (_transmittalsStore != null && transmittalId.HasValue && recipientRecords.Count > 0)
            {
                try
                {
                    await _transmittalsStore.AddRecipientsAsync(
                        transmittalId.Value,
                        recipientRecords,
                        ct).ConfigureAwait(false);
                }
                catch
                {
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
                catch
                {
                }
            }

            request.Status?.Report("Done.");

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

            if (!string.IsNullOrWhiteSpace(remarksHtml))
            {
                sb.Append(remarksHtml.Trim());
                sb.Append("<br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                sb.Append("<b>View files: <a href=\"")
                  .Append(WebUtility.HtmlEncode(linkUrl))
                  .Append("\">Click here to view the files</a></b><br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(signatureHtml))
            {
                sb.Append(signatureHtml.Trim());
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
                cmd.Parameters.AddWithValue("@Id", record.LinkId);
                cmd.Parameters.AddWithValue("@TargetUrl", record.TargetUrl ?? string.Empty);
                cmd.Parameters.AddWithValue("@RecipientEmail", record.Email ?? string.Empty);
                cmd.Parameters.AddWithValue("@TransmittalId", (object?)transmittalId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
