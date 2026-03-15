#nullable enable
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public sealed class MainWindowWorkflowService
    {
        private const string DefaultEmailDomain = "korstructural.com";
        private static readonly Regex EmailSplit = new(@"[;, \r\n]+", RegexOptions.Compiled);
        private static readonly Regex EmailExtract = new(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        private readonly IUserPreferencesStore? _userPrefsStore;
        private readonly IUploadOrchestrator _uploadOrchestrator;
        private readonly ITransmittalService _transmittalService;
        private readonly string _userUpn;
        private readonly DatabaseOptions _databaseOptions;
        private readonly UserOptions _userOptions;

        public MainWindowWorkflowService(
            string userUpn,
            IUserPreferencesStore? userPrefsStore,
            IUploadOrchestrator uploadOrchestrator,
            ITransmittalService transmittalService,
            DatabaseOptions databaseOptions,
            UserOptions userOptions)
        {
            _userUpn = userUpn;
            _userPrefsStore = userPrefsStore;
            _uploadOrchestrator = uploadOrchestrator;
            _transmittalService = transmittalService;
            _databaseOptions = databaseOptions ?? throw new ArgumentNullException(nameof(databaseOptions));
            _userOptions = userOptions ?? throw new ArgumentNullException(nameof(userOptions));
        }

        public async Task<UserPreferences?> LoadUserPreferencesAsync()
        {
            if (_userPrefsStore == null)
            {
                return null;
            }

            try
            {
                return await _userPrefsStore.GetAsync(_userUpn).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowWorkflowService] LoadUserPreferencesAsync failed for '{_userUpn}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public string InitializeCurrentUser()
        {
            try
            {
                var cfgEmail = _userOptions.DefaultFromEmail;
                if (!string.IsNullOrWhiteSpace(cfgEmail))
                {
                    return cfgEmail;
                }

                if (OperatingSystem.IsWindows())
                {
                    var derived = TryGetEmailFromWindowsIdentity() ?? TryGuessEmailFromWindows();
                    if (!string.IsNullOrWhiteSpace(derived))
                    {
                        return derived;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowWorkflowService] InitializeCurrentUser failed: {ex.GetType().Name}: {ex.Message}");
            }

            return GuessFromAppSettingsOrDefault();
        }

        public void MergeFiles(WizardState state, IEnumerable<string> files)
        {
            foreach (var path in files.Where(File.Exists))
            {
                if (state.Files.Any(f => string.Equals(f.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var fi = new FileInfo(path);
                state.Files.Add(new TransmittalFile
                {
                    LocalPath = path,
                    FileName = fi.Name,
                    SizeBytes = fi.Length
                });
            }
        }

        public IReadOnlyList<string> ValidateRequiredFields(
            string? projectText,
            string? toText,
            string? subject,
            IReadOnlyCollection<TransmittalFile> files)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(projectText)) errors.Add("Project is required.");
            if (ParseEmails(toText).Count == 0) errors.Add("At least one recipient in the To field is required.");
            if (string.IsNullOrWhiteSpace(subject)) errors.Add("Subject is required.");
            if (files.Count == 0) errors.Add("At least one file must be added.");
            return errors;
        }

        public PreparedHeader PrepareHeader(
            Transmittal header,
            string? projectText,
            string? subject,
            string sendVia,
            DateTime? selectedDate,
            string? toText,
            string? ccText,
            string? fromName,
            string? fromEmail,
            string? purpose,
            bool useBasicRemarksEditor,
            string basicRemarksHtml,
            string basicRemarksText,
            string richRemarksHtml)
        {
            var to = ParseEmails(toText);
            var cc = ParseEmails(ccText);

            header.Subject = subject?.Trim();
            header.SendVia = sendVia;

            var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var pacDate = selectedDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacific).Date;
            var unspecified = DateTime.SpecifyKind(pacDate, DateTimeKind.Unspecified);
            header.DateUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, pacific);

            header.Recipients.Clear();
            foreach (var addr in to.Concat(cc))
            {
                header.Recipients.Add(new Recipient { Email = addr });
            }

            header.ToRecipients = to;
            header.CcRecipients = cc;

            header.FromName = fromName;
            header.FromEmail = fromEmail;

            var remarksHtml = useBasicRemarksEditor ? basicRemarksHtml : richRemarksHtml;
            var remarksText = useBasicRemarksEditor ? basicRemarksText : StripHtmlToPlainText(richRemarksHtml);
            header.Remarks = remarksText;

            if (string.IsNullOrWhiteSpace(header.ProjectNumber))
            {
                header.ProjectNumber = "—";
            }

            if (string.IsNullOrWhiteSpace(header.ProjectName))
            {
                header.ProjectName = "—";
            }

#pragma warning disable CS0618
            header.Purpose = purpose ?? header.Purpose;
#pragma warning restore CS0618

            return new PreparedHeader(to, cc, remarksHtml, remarksText);
        }

        public async Task<string> RenderPreviewAsync(
            string outputPath,
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            CancellationToken ct)
        {
            await _uploadOrchestrator.RenderPreviewAsync(outputPath, header, files, ct).ConfigureAwait(false);
            return outputPath;
        }

        public Task<TransmittalSendResult> SendAsync(TransmittalSendRequest request, CancellationToken ct)
            => _transmittalService.SendAsync(request, ct);

        public string BuildTransmittalFolderPath(Transmittal header)
        {
            var projNo = SanitizeSegment(header.ProjectNumber ?? "Unknown");
            var projName = SanitizeSegment(header.ProjectName ?? "Unknown");
            var projectFolder = string.IsNullOrWhiteSpace(projName) ? projNo : $"{projNo} - {projName}";
            return $"{projectFolder}/Transmittals/{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:yyyy-MM-dd_HHmm}";
        }

        public List<SimpleTeam> LoadEmailTeamsFromDatabase()
        {
            var results = new List<SimpleTeam>();

            try
            {
                var cs = !string.IsNullOrWhiteSpace(_databaseOptions.KorTransmittalsDb)
                    ? _databaseOptions.KorTransmittalsDb
                    : _databaseOptions.KorTransmittals;

                if (string.IsNullOrWhiteSpace(cs) || string.IsNullOrWhiteSpace(_userUpn))
                {
                    return results;
                }

                using var conn = new SqlConnection(cs);
                conn.Open();

                var teamCmd = new SqlCommand(
                    "SELECT TeamId, Name FROM dbo.UserTeams WHERE UserUpn = @upn ORDER BY Name;",
                    conn);
                var teamUpn = teamCmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
                teamUpn.Value = _userUpn ?? (object)DBNull.Value;

                var dict = new Dictionary<Guid, SimpleTeam>();
                using (var r = teamCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        dict[r.GetGuid(0)] = new SimpleTeam
                        {
                            TeamId = r.GetGuid(0),
                            Name = r.IsDBNull(1) ? string.Empty : r.GetString(1)
                        };
                    }
                }

                if (dict.Count == 0)
                {
                    return results;
                }

                var ids = dict.Keys.ToList();
                var sb = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("@id" + i);
                }

                using var memCmd = new SqlCommand(
                    $"SELECT TeamId, Email FROM dbo.UserTeamMembers WHERE TeamId IN ({sb}) ORDER BY Email;",
                    conn);

                for (int i = 0; i < ids.Count; i++)
                {
                    var memberId = memCmd.Parameters.Add("@id" + i, SqlDbType.UniqueIdentifier);
                    memberId.Value = ids[i];
                }

                using var r2 = memCmd.ExecuteReader();
                while (r2.Read())
                {
                    var teamId = r2.GetGuid(0);
                    var email = r2.IsDBNull(1) ? null : r2.GetString(1);
                    if (!string.IsNullOrWhiteSpace(email) && dict.TryGetValue(teamId, out var team))
                    {
                        team.Emails.Add(email);
                    }
                }

                results.AddRange(dict.Values);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowWorkflowService] LoadEmailTeamsFromDatabase failed for '{_userUpn}': {ex.GetType().Name}: {ex.Message}");
            }

            return results;
        }

        public static List<string> ParseEmails(string? raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            foreach (var token in EmailSplit.Split(raw))
            {
                var s = token.Trim();
                if (s.Length == 0) continue;

                var lt = s.IndexOf('<');
                var gt = lt >= 0 ? s.IndexOf('>', lt + 1) : -1;
                if (lt >= 0 && gt > lt + 1)
                {
                    s = s.Substring(lt + 1, gt - lt - 1).Trim();
                }

                var match = EmailExtract.Match(s);
                if (!match.Success) continue;

                var email = match.Value.Trim();
                if (!list.Contains(email, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(email);
                }
            }

            return list;
        }

        public static string GetLastEmailToken(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var parts = EmailSplit.Split(raw);
            return parts.Length == 0 ? string.Empty : parts[^1].Trim();
        }

        public static string StripHtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            return WebUtility.HtmlDecode(Regex.Replace(html, "<.*?>", string.Empty)).Trim();
        }

        public static string BuildHtmlFromRemarks(System.Windows.Controls.RichTextBox rtb)
        {
            var doc = rtb.Document;
            var sb = new StringBuilder();
            foreach (var block in doc.Blocks)
            {
                AppendBlockHtml(sb, block);
            }

            return sb.ToString();
        }

        public static string ReadText(System.Windows.Controls.RichTextBox rtb)
        {
            var range = new System.Windows.Documents.TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            return range.Text.Trim();
        }

        private static void AppendBlockHtml(StringBuilder sb, System.Windows.Documents.Block block)
        {
            if (block is System.Windows.Documents.Paragraph p)
            {
                sb.Append("<p>");
                foreach (System.Windows.Documents.Inline inline in p.Inlines) AppendInlineHtml(sb, inline);
                sb.Append("</p>");
            }
            else if (block is System.Windows.Documents.List list)
            {
                sb.Append("<ul>");
                foreach (System.Windows.Documents.ListItem li in list.ListItems)
                {
                    foreach (System.Windows.Documents.Block inner in li.Blocks)
                    {
                        sb.Append("<li>");
                        AppendBlockHtml(sb, inner);
                        sb.Append("</li>");
                    }
                }
                sb.Append("</ul>");
            }
        }

        private static void AppendInlineHtml(StringBuilder sb, System.Windows.Documents.Inline inline)
        {
            var range = new System.Windows.Documents.TextRange(inline.ContentStart, inline.ContentEnd);
            var text = range.Text;
            if (string.IsNullOrEmpty(text)) return;

            var encoded = WebUtility.HtmlEncode(text)
                .Replace("\r\n", "<br/>")
                .Replace("\n", "<br/>")
                .Replace("\r", "<br/>");

            bool isBold = inline is System.Windows.Documents.Bold || inline.FontWeight == System.Windows.FontWeights.Bold;
            bool isItalic = inline is System.Windows.Documents.Italic || inline.FontStyle == System.Windows.FontStyles.Italic;
            bool isUnderline = inline.TextDecorations == System.Windows.TextDecorations.Underline;

            if (isBold) sb.Append("<strong>");
            if (isItalic) sb.Append("<em>");
            if (isUnderline) sb.Append("<u>");
            sb.Append(encoded);
            if (isUnderline) sb.Append("</u>");
            if (isItalic) sb.Append("</em>");
            if (isBold) sb.Append("</strong>");
        }

        private string GuessFromAppSettingsOrDefault()
        {
            var domain = _userOptions.DefaultFromDomain;
            if (string.IsNullOrWhiteSpace(domain)) domain = DefaultEmailDomain;
            var user = Environment.UserName;
            return string.IsNullOrWhiteSpace(user) ? $"noreply@{domain}" : $"{user}@{domain}";
        }

        [SupportedOSPlatform("windows")]
        private static string? TryGetEmailFromWindowsIdentity()
        {
            try
            {
                using var wi = WindowsIdentity.GetCurrent();
                var email = wi?.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email || c.Type.EndsWith("/emailaddress", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(email)) return email;

                var upn = wi?.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Upn || c.Type.EndsWith("/upn", StringComparison.OrdinalIgnoreCase))?.Value;
                return string.IsNullOrWhiteSpace(upn) ? null : upn;
            }
            catch (Exception ex) { Debug.WriteLine($"[MainWindowWorkflowService] TryGetEmailFromWindowsIdentity failed: {ex.GetType().Name}: {ex.Message}"); return null; }
        }

        [SupportedOSPlatform("windows")]
        private static string? TryGuessEmailFromWindows()
        {
            try
            {
                var user = Environment.UserName;
                if (string.IsNullOrWhiteSpace(user)) return null;
                var domain = DefaultEmailDomain;
                var rawDomain = Environment.UserDomainName;
                if (!string.IsNullOrWhiteSpace(rawDomain) && !rawDomain.Contains('\\') && !rawDomain.Contains(' ') && rawDomain.Contains('.'))
                {
                    domain = rawDomain.ToLowerInvariant();
                }
                return $"{user}@{domain}";
            }
            catch (Exception ex) { Debug.WriteLine($"[MainWindowWorkflowService] TryGuessEmailFromWindows failed: {ex.GetType().Name}: {ex.Message}"); return null; }
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

    public sealed record PreparedHeader(
        IReadOnlyList<string> ToRecipients,
        IReadOnlyList<string> CcRecipients,
        string RemarksHtml,
        string RemarksText);

    public sealed class SimpleTeam
    {
        public Guid TeamId { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<string> Emails { get; } = new();
    }

    public sealed class EmailSuggestion
    {
        public string Email { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
    }
}

