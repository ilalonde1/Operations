#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

// Port of _Scripts Rebuild/FileSync/Production/Send-Weekly-PM-Deadlines.ps1.
// Behavior parity is verbatim: same week window, same subject, same HTML body,
// same PM directory. The only structural change is Shadow vs Live: Shadow
// writes one CSV per PM under ShadowOutputDir + a summary.txt, never sending;
// Live sends via Graph the same way the PS1 script did.
internal sealed class WeeklyPmDeadlinesRunner : IJobRunner
{
    public const string Name = "WeeklyPmDeadlines";

    private readonly IControlPlaneStore _store;
    private readonly GraphServiceClient _graph;
    private readonly ILogger<WeeklyPmDeadlinesRunner> _logger;

    public WeeklyPmDeadlinesRunner(
        IControlPlaneStore store,
        GraphServiceClient graph,
        ILogger<WeeklyPmDeadlinesRunner> logger)
    {
        _store = store;
        _graph = graph;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = WeeklyPmDeadlinesOptions.FromKnobs(knobs);

        if (!File.Exists(opts.ExcelPath))
        {
            var msg = $"Excel file not found: {opts.ExcelPath}";
            _logger.LogError("{Msg}", msg);
            return new JobRunResult(false, msg);
        }

        var localPath = CopyToTemp(opts.ExcelPath);
        if (localPath is null)
        {
            return new JobRunResult(false, $"Failed to copy workbook to temp from '{opts.ExcelPath}'.");
        }

        try
        {
            var rows = ReadDeadlines(localPath, opts);
            _logger.LogInformation("Read {Count} candidate rows from '{Sheet}'.", rows.Count, opts.SheetName);

            var window = GetWeekWindow(DateTime.Today);
            _logger.LogInformation("Week window: {Start:yyyy-MM-dd} -> {End:yyyy-MM-dd}.", window.Start, window.End);

            var ignore = new HashSet<string>(opts.IgnorePms, StringComparer.OrdinalIgnoreCase);
            var weekRows = rows
                .Where(r => r.Date >= window.Start && r.Date <= window.End)
                .Where(r => !ignore.Contains(r.Pm))
                .ToList();

            if (weekRows.Count == 0)
            {
                var msg = $"No deadlines for week {window.Start:yyyy-MM-dd} -> {window.End:yyyy-MM-dd} (after IgnorePM filter).";
                _logger.LogInformation("{Msg}", msg);
                return new JobRunResult(true, msg);
            }

            var groups = weekRows
                .GroupBy(r => r.Pm, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
            var runStamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var shadowDir = isShadow
                ? Directory.CreateDirectory(Path.Combine(opts.ShadowOutputDir, runStamp)).FullName
                : null;

            int sent = 0, skippedNoEmail = 0;
            var summary = new StringBuilder();
            summary.Append(CultureInfo.InvariantCulture, $"Mode={config.Mode}; Window={window.Start:yyyy-MM-dd}->{window.End:yyyy-MM-dd}; Groups={groups.Count}");

            foreach (var group in groups)
            {
                var pmName = group.Key;
                var to = PmDirectory.TryGetEmail(pmName);
                var ordered = group.OrderBy(r => r.Date).ThenBy(r => r.Project, StringComparer.OrdinalIgnoreCase).ToList();
                var subject = $"Weekly Project Deadlines for {pmName} (Week of {window.Start.ToString("MMM d", CultureInfo.InvariantCulture)} - {window.End.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)})";

                if (to is null)
                {
                    skippedNoEmail++;
                    _logger.LogWarning("No email mapping for PM '{Pm}'; skipping {Count} row(s).", pmName, ordered.Count);
                    if (shadowDir is not null)
                        WriteShadowCsv(shadowDir, pmName, "(no email)", subject, ordered);
                    continue;
                }

                var html = BuildHtmlBody(pmName, window, ordered);

                if (isShadow)
                {
                    WriteShadowCsv(shadowDir!, pmName, to, subject, ordered);
                    _logger.LogInformation("WOULD SEND From={From} To={To} Cc={Cc} Subj='{Subj}' Rows={Rows}.", opts.SenderAddress, to, opts.GlobalCc, subject, ordered.Count);
                }
                else
                {
                    await SendMailAsync(opts.SenderAddress, to, opts.GlobalCc, subject, html, ct).ConfigureAwait(false);
                    _logger.LogInformation("Sent From={From} To={To} Cc={Cc} Subj='{Subj}'.", opts.SenderAddress, to, opts.GlobalCc, subject);
                }

                sent++;
            }

            if (shadowDir is not null)
            {
                File.WriteAllText(
                    Path.Combine(shadowDir, "_summary.txt"),
                    $"{summary}{Environment.NewLine}Sent={sent} SkippedNoEmail={skippedNoEmail}{Environment.NewLine}",
                    Encoding.UTF8);
            }

            var verb = isShadow ? "Would send" : "Sent";
            return new JobRunResult(
                true,
                $"{verb} {sent} email(s); {skippedNoEmail} PM(s) had no email mapping. Window {window.Start:yyyy-MM-dd}..{window.End:yyyy-MM-dd}. {(shadowDir is null ? string.Empty : $"Shadow output: {shadowDir}")}");
        }
        finally
        {
            TryDelete(localPath);
        }
    }

    private static string? CopyToTemp(string source)
    {
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), "WeeklyDeadlines_" + Path.GetFileName(source));
            File.Copy(source, dest, overwrite: true);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    private List<DeadlineRow> ReadDeadlines(string xlsxPath, WeeklyPmDeadlinesOptions opts)
    {
        var rows = new List<DeadlineRow>();
        using var wb = new XLWorkbook(xlsxPath);
        var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, opts.SheetName, StringComparison.OrdinalIgnoreCase));
        if (ws is null)
        {
            _logger.LogError("Sheet '{Sheet}' not found in workbook '{Path}'.", opts.SheetName, xlsxPath);
            return rows;
        }

        var headerRow = ws.FirstRowUsed();
        if (headerRow is null) return rows;

        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= lastCol; c++)
        {
            var name = headerRow.Cell(c).GetString().Trim();
            if (!string.IsNullOrEmpty(name) && !headers.ContainsKey(name))
                headers[name] = c;
        }

        if (!headers.TryGetValue(opts.PmColumn, out var pmCol) ||
            !headers.TryGetValue(opts.DateColumn, out var dateCol))
        {
            _logger.LogError("Required columns not found. Headers seen: [{Headers}].", string.Join(", ", headers.Keys));
            return rows;
        }

        headers.TryGetValue(opts.ProjectColumn, out var projCol);
        headers.TryGetValue(opts.CustIssueColumn, out var custIssueCol);
        headers.TryGetValue(opts.CustRemarksColumn, out var custRemarksCol);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var pm = NormalizePm(row.Cell(pmCol).GetString());
            if (string.IsNullOrEmpty(pm)) continue;

            var date = ReadCellDate(row.Cell(dateCol));
            if (!date.HasValue) continue;

            var proj = projCol > 0 ? row.Cell(projCol).GetString().Trim() : string.Empty;
            var custIssue = custIssueCol > 0 ? row.Cell(custIssueCol).GetString() : string.Empty;
            var custRemarks = custRemarksCol > 0 ? row.Cell(custRemarksCol).GetString() : string.Empty;

            rows.Add(new DeadlineRow(pm, date.Value.Date, proj, custIssue, custRemarks));
        }

        return rows;
    }

    private static string NormalizePm(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var collapsed = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");
        return collapsed.Trim();
    }

    private static DateTime? ReadCellDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        switch (cell.DataType)
        {
            case XLDataType.DateTime:
                return cell.GetDateTime();
            case XLDataType.Number:
                try { return DateTime.FromOADate(cell.GetDouble()); }
                catch { return null; }
            case XLDataType.Text:
                var s = cell.GetString();
                if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)) return d;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa))
                {
                    try { return DateTime.FromOADate(oa); }
                    catch { return null; }
                }

                return null;
            default:
                return null;
        }
    }

    private static (DateTime Start, DateTime End) GetWeekWindow(DateTime today)
    {
        // Mirrors Send-Weekly-PM-Deadlines.ps1 §156-161: pick the upcoming
        // Monday (today if today is Monday). Monday cron lands on the
        // current week; manual fire mid-week lands on next week.
        var delta = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var monday = today.Date.AddDays(delta);
        var sundayEnd = monday.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);
        return (monday, sundayEnd);
    }

    private static string BuildHtmlBody(string pmName, (DateTime Start, DateTime End) window, IEnumerable<DeadlineRow> ordered)
    {
        var sb = new StringBuilder();
        foreach (var it in ordered)
        {
            var dateTxt = it.Date.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{HtmlEnc(dateTxt)}</td><td>{HtmlEnc(it.Project)}</td><td>{HtmlEnc(it.CustIssue)}</td><td>{HtmlEnc(it.CustRemarks)}</td></tr>\n");
        }

        var rowsHtml = sb.ToString();
        return $"""
<!DOCTYPE html>
<html>
  <body style="font-family:Arial,Helvetica,sans-serif; font-size:13px; color:#222;">
    <p>Hi {HtmlEnc(pmName)},</p>
    <p>Here are your deadlines for the week of {window.Start.ToString("ddd MMM d", CultureInfo.InvariantCulture)} through {window.End.ToString("ddd MMM d, yyyy", CultureInfo.InvariantCulture)}.</p>
    <table border="1" cellpadding="6" cellspacing="0" style="border-collapse:collapse; font-family:Arial,Helvetica,sans-serif; font-size:12px;">
      <thead>
        <tr style="background:#f2f2f2;">
          <th align="left">Date</th><th align="left">Project</th><th align="left">CustIssue</th><th align="left">CustRemarks</th>
        </tr>
      </thead>
      <tbody>
        {rowsHtml}
      </tbody>
    </table>
    <p style="margin-top:16px;">If anything is missing or needs adjustment, please email Ian - ilalonde@korstructural.com</p>
    <p>Thanks,<br>KOR Automated Deadlines</p>
  </body>
</html>
""";
    }

    private static string HtmlEnc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return WebUtility.HtmlEncode(s).Replace("\r\n", "<br>", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static void WriteShadowCsv(string dir, string pmName, string toEmail, string subject, IEnumerable<DeadlineRow> ordered)
    {
        var safeName = string.Concat(pmName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(safeName)) safeName = "_unknown";
        var path = Path.Combine(dir, safeName + ".csv");
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# To: {toEmail}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Subject: {subject}");
        sb.AppendLine("Date,Project,CustIssue,CustRemarks");
        foreach (var r in ordered)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{CsvEsc(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))},{CsvEsc(r.Project)},{CsvEsc(r.CustIssue)},{CsvEsc(r.CustRemarks)}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var needsQuote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var escaped = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }

    private async Task SendMailAsync(string from, string to, string? cc, string subject, string html, CancellationToken ct)
    {
        var ccList = new List<Recipient>();
        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in cc.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ccList.Add(new Recipient { EmailAddress = new EmailAddress { Address = addr } });
            }
        }

        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal),
                Body = new ItemBody { ContentType = BodyType.Html, Content = html },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } },
                },
                CcRecipients = ccList,
            },
            SaveToSentItems = true,
        };

        await _graph.Users[from].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
    }
}
