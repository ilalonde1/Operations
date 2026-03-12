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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations
{
    public static class QuickTransferRunner
    {
        private const long AttachThreshold = 10L * 1024 * 1024; // 10 MB (not really used now)

        // Same splitter as before
        private static readonly Regex EmailSplit = new(@"[;, \r\n]+", RegexOptions.Compiled);

        // New: robust email extractor (same pattern style as MainWindow)
        private static readonly Regex EmailExtract = new(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        public static async Task RunAsync(
            QuickTransferRequest request,
            CancellationToken cancellationToken = default,
            IProgress<(string file, long sent, long total)>? uploadProgress = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // -----------------------------------------------------------------
            // 1) Basic header + project info (same Header type as MainWindow)
            // -----------------------------------------------------------------
            ParseFolderName(request.ProjectDisplay, out var projNo, out var projName);
            if (string.IsNullOrWhiteSpace(projNo))
                throw new InvalidOperationException("Project is required for Quick Transfer.");

            if (string.IsNullOrWhiteSpace(projName))
                projName = request.ProjectDisplay ?? projNo;

            var wizard = new WizardState();
            var header = wizard.Header;

            header.ProjectNumber = projNo;
            header.ProjectName = projName;

            var userSubject = (request.Subject ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(userSubject))
                userSubject = "(no subject)";

            // Quick Transfer email subject
            var emailSubject = $"RE: {userSubject} - KOR File Transfer";

            header.Subject = emailSubject;
            try { header.GetType().GetProperty("EmailSubject")?.SetValue(header, emailSubject); } catch { }

            header.SendVia = "SharePoint";
            header.FromEmail = request.FromEmail ?? string.Empty;
            header.FromName = request.FromEmail ?? string.Empty;
            header.DateUtc = DateTime.UtcNow;

            // We will overwrite header.Remarks with HTML later
            header.Remarks = "Quick file transfer via Kor Transmittals.";

            var toList = ParseEmails(request.To);
            var ccList = ParseEmails(request.Cc);

            header.Recipients.Clear();
            foreach (var addr in toList.Concat(ccList))
            {
                header.Recipients.Add(new Recipient { Email = addr });
            }

            try { header.GetType().GetProperty("ToRecipients")?.SetValue(header, toList); } catch { }
            try { header.GetType().GetProperty("CcRecipients")?.SetValue(header, ccList); } catch { }

            // -----------------------------------------------------------------
            // 2) Reserve transmittal number + build SharePoint folder path
            // -----------------------------------------------------------------
            header.TransmittalNo = await GraphFacade.Instance
                .ReserveTransmittalNumberAsync(header.ProjectNumber)
                .ConfigureAwait(false);

            var folder = BuildTransmittalFolderPath(header.ProjectNumber, header.ProjectName);
            header.SharePointFolderPath = folder;

            // DB store (same DB as main engine)
            var store = TryCreateStore();
            Guid? transmittalId = null;
            if (store != null)
            {
                transmittalId = Guid.NewGuid();
            }

            var files = request.Files ?? new List<TransmittalFile>();

            // -----------------------------------------------------------------
            // 3) Upload attached files to SharePoint (with progress)
            // -----------------------------------------------------------------
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(f.LocalPath) || !File.Exists(f.LocalPath))
                    continue;

                var name = string.IsNullOrWhiteSpace(f.FileName)
                    ? Path.GetFileName(f.LocalPath)
                    : f.FileName;

                var sp = await GraphFacade.Instance.UploadWithProgressAsync(
                    folder,
                    name,
                    f.LocalPath,
                    uploadProgress,
                    ct: cancellationToken
                ).ConfigureAwait(false);

                f.SharePointPath = sp;
            }

            // -----------------------------------------------------------------
            // 4) Write QuickTransferLog.txt into the SAME SharePoint folder
            // -----------------------------------------------------------------
            await UploadLogAsync(folder, request, files, cancellationToken).ConfigureAwait(false);

            // -----------------------------------------------------------------
            // 5) Create internal/external links + per-recipient redirect targets
            // -----------------------------------------------------------------
            var links = await GraphFacade.Instance.CreateLinksAsync(
                folder,
                needExternal: true,
                ct: cancellationToken
            ).ConfigureAwait(false);

            var internalLink = links.InternalLink;
            var externalLink = links.ExternalLink;

            var allRecipients = toList.Concat(ccList)
                                      .Distinct(StringComparer.OrdinalIgnoreCase)
                                      .ToList();

            string PickShareUrl(string email)
            {
                var isKor = email.EndsWith("@korstructural.com", StringComparison.OrdinalIgnoreCase);
                var candidate = isKor ? internalLink : externalLink;
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate!;
                return internalLink ?? externalLink ?? string.Empty;
            }

            var redirectorBase = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.RedirectorBaseUrl] ?? string.Empty)
                .TrimEnd('/');

            var linkRecords = new List<(Guid LinkId, string Email, string TargetUrl)>();

            foreach (var email in allRecipients)
            {
                var lid = Guid.NewGuid();
                var targetUrl = PickShareUrl(email);
                linkRecords.Add((lid, email, targetUrl));
            }

            // -----------------------------------------------------------------
            // 6) Insert RedirectTargets rows (for tracking links)
            // -----------------------------------------------------------------
            try
            {
                var cs = ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittalsDb]?.ConnectionString;
                if (!string.IsNullOrWhiteSpace(cs) && linkRecords.Count > 0)
                {
                    await InsertRedirectTargetsAsync(cs!, transmittalId, linkRecords, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // tracking is nice-to-have, don't kill the send for this
            }

            // Primary SP link for logging / fallback (no tracking)
            var primaryUrl = internalLink ?? externalLink ?? string.Empty;

            // -----------------------------------------------------------------
            // 7) Log transmittal row to KorTransmittals (optional)
            // -----------------------------------------------------------------
            if (store != null && transmittalId.HasValue)
            {
                try
                {
                    await store.LogTransmittalAsync(
                        id: transmittalId.Value,
                        projectNo: header.ProjectNumber ?? string.Empty,
                        subject: emailSubject,
                        driveId: "drv",
                        itemId: "itm",
                        sharePointUrl: primaryUrl ?? string.Empty,
                        createdUtc: DateTime.UtcNow,
                        createdBy: header.FromEmail ?? string.Empty,
                        appVersion: typeof(QuickTransferRunner).Assembly.GetName().Version?.ToString(),
                        type: "Transfer",                             // IMPORTANT: mark as Transfer
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                catch
                {
                    // Don't block Quick Transfer if logging fails
                }
            }

            // -----------------------------------------------------------------
            // 8) Send email(s) via Graph
            //     - View link goes BETWEEN remarks and signature
            //     - "Files have been uploaded..." line removed
            //     - Link line is bold
            // -----------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(redirectorBase) && linkRecords.Count > 0)
            {
                // Per-recipient tracked emails
                foreach (var rec in linkRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var clickUrl = $"{redirectorBase}/t/{rec.LinkId}";
                    var pixelUrl = $"{redirectorBase}/o/{rec.LinkId}/{Uri.EscapeDataString(rec.Email)}";

                    var bodyHtml = BuildEmailBodyHtml(
                        request.RemarksHtml,
                        request.SignatureHtml,
                        clickUrl,
                        pixelUrl);

                    header.Remarks = bodyHtml;

                    await GraphFacade.Instance.SendMailAsync(
                        header,
                        string.Empty,              // no SharePoint file path for an attachment
                        string.Empty,              // no local file to attach
                        false,                     // do NOT attach anything
                        cancellationToken,
                        request.FromEmail ?? string.Empty,
                        new List<string> { rec.Email } // per-recipient send
                    ).ConfigureAwait(false);
                }
            }
            else
            {
                // Fallback: single non-tracked email with the raw SharePoint URL.
                var bodyHtml = BuildEmailBodyHtml(
                    request.RemarksHtml,
                    request.SignatureHtml,
                    primaryUrl,
                    pixelUrl: null);

                header.Remarks = bodyHtml;

                await GraphFacade.Instance.SendMailAsync(
                    header,
                    string.Empty,
                    string.Empty,
                    false,
                    cancellationToken,
                    request.FromEmail ?? string.Empty,
                    allRecipients
                ).ConfigureAwait(false);
            }

            // -----------------------------------------------------------------
            // 9) Mark as sent in KorTransmittals (if logging is enabled)
            // -----------------------------------------------------------------
            if (store != null && transmittalId.HasValue)
            {
                try
                {
                    await store.MarkSentAsync(
                        transmittalId.Value,
                        DateTime.UtcNow,
                        request.FromEmail ?? string.Empty,
                        typeof(QuickTransferRunner).Assembly.GetName().Version?.ToString(),
                        cancellationToken
                    ).ConfigureAwait(false);
                }
                catch
                {
                    // don't block UX
                }
            }
        }

        // Builds the HTML body:
        //   remarks
        //   <br/><br/>
        //   <b>View files: <a href="...">Click here to view the files</a></b>
        //   <br/><br/>
        //   signature
        //   (hidden pixel img if provided)
        private static string BuildEmailBodyHtml(
            string? remarksHtml,
            string? signatureHtml,
            string? linkUrl,
            string? pixelUrl)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(remarksHtml))
            {
                sb.Append(remarksHtml.Trim());
                sb.Append("<br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                var encoded = WebUtility.HtmlEncode(linkUrl);
                sb.Append("<b>View files: <a href=\"")
                  .Append(encoded)
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

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private static List<string> ParseEmails(string? raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            foreach (var token in EmailSplit.Split(raw))
            {
                var s = token.Trim();
                if (s.Length == 0)
                    continue;

                // Handle "Name <email@domain>" or "<email@domain>"
                var lt = s.IndexOf('<');
                var gt = (lt >= 0) ? s.IndexOf('>', lt + 1) : -1;
                if (lt >= 0 && gt > lt + 1)
                {
                    s = s.Substring(lt + 1, gt - lt - 1).Trim();
                }

                // Extract just the email portion
                var match = EmailExtract.Match(s);
                if (!match.Success)
                    continue;

                var email = match.Value.Trim();

                if (!list.Contains(email, StringComparer.OrdinalIgnoreCase))
                    list.Add(email);
            }

            return list;
        }

        private static void ParseFolderName(string folderName, out string projNo, out string projName)
        {
            projNo = folderName ?? string.Empty;
            projName = folderName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(folderName))
                return;

            var numEnd = folderName.IndexOfAny(new[] { ' ', '(' });
            if (numEnd > 0)
                projNo = folderName.Substring(0, numEnd).Trim();
            else
                projNo = folderName.Trim();

            var open = folderName.IndexOf('(');
            var close = folderName.LastIndexOf(')');
            if (open >= 0 && close > open)
                projName = folderName.Substring(open + 1, close - open - 1).Trim();
        }

        private static string SanitizeSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
            cleaned = cleaned.Trim().Trim('.', ' ');
            return string.IsNullOrEmpty(cleaned) ? "Unknown" : cleaned;
        }

        private static string BuildTransmittalFolderPath(string projectNumber, string projectName)
        {
            var projNo = SanitizeSegment(projectNumber ?? "Unknown");
            var projNam = SanitizeSegment(projectName ?? "Unknown");

            var projectFolder = string.IsNullOrWhiteSpace(projNam)
                ? projNo
                : $"{projNo} - {projNam}";

            var year = DateTime.UtcNow.ToString("yyyy");
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
            return $"{projectFolder}/Transfers/{year}/{stamp}";
        }

        private static ITransmittalsStore? TryCreateStore()
        {
            try
            {
                var cs = ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittalsDb]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(cs)) return null;
                return new SqlTransmittalsStore(cs);
            }
            catch
            {
                return null;
            }
        }

        private static async Task UploadLogAsync(
            string folder,
            QuickTransferRequest request,
            IEnumerable<TransmittalFile> files,
            CancellationToken ct)
        {
            try
            {
                var lines = new List<string>
                {
                    "Quick Transfer",
                    "UTC: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    "From: " + (request.FromEmail ?? string.Empty),
                    "To: " + (request.To ?? string.Empty),
                    "Cc: " + (request.Cc ?? string.Empty),
                    "Project: " + (request.ProjectDisplay ?? string.Empty),
                    "",
                    "Files:"
                };

                foreach (var f in files)
                {
                    var name = f.FileName;
                    if (string.IsNullOrWhiteSpace(name))
                        name = Path.GetFileName(f.LocalPath);
                    lines.Add(" - " + name);
                }

                var tmp = Path.GetTempFileName();
                File.WriteAllLines(tmp, lines, Encoding.UTF8);

                await GraphFacade.Instance.UploadWithProgressAsync(
                    folder,
                    "QuickTransferLog.txt",
                    tmp,
                    progress: null,
                    ct: ct
                ).ConfigureAwait(false);

                try { File.Delete(tmp); } catch { }
            }
            catch
            {
                // logging must never break the transfer
            }
        }

        private static async Task InsertRedirectTargetsAsync(
            string connString,
            Guid? transmittalId,
            IEnumerable<(Guid LinkId, string Email, string TargetUrl)> rows,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.RedirectTargets (LinkId, TransmittalId, RecipientEmail, TargetUrl)
VALUES (@lid, @tid, @email, @url);";

            await using var cnn = new SqlConnection(connString);
            await cnn.OpenAsync(ct);

            foreach (var r in rows)
            {
                await using var cmd = new SqlCommand(sql, cnn);
                cmd.Parameters.AddWithValue("@lid", r.LinkId);
                cmd.Parameters.AddWithValue("@tid", (object?)transmittalId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@email", (object?)r.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@url", (object?)r.TargetUrl ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}

