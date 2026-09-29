#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.NetworkOps.Service.Alerting;

internal interface IDigestSender
{
    /// <returns>True only when the digest was actually MAILED -- findings are marked notified on that and nothing else.</returns>
    Task<bool> SendAsync(IReadOnlyList<DeviceChanges> changes, IReadOnlyList<Kor.Operations.NetworkOps.Core.Learning.FleetInsight> newPatterns, DateTime sweptAtLocal, CancellationToken ct);

    /// <summary>A plain alert about the service itself (a failed run).</summary>
    Task SendAlertAsync(string subject, string body, CancellationToken ct);
}

// Mails the digest through Graph when AlertsEnabled; otherwise writes it to
// %ProgramData%\KorOperations\NetworkOps\digests so a week of them can be read before anyone is
// emailed. A digest that fails to send is logged and returns false, so its findings stay
// unnotified and are carried by the next change instead of silently marked as told.
internal sealed class DigestSender(GraphServiceClient graph, IOptions<NetworkOpsOptions> options, ILogger<DigestSender> log) : IDigestSender
{
    public static readonly string DigestDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KorOperations", "NetworkOps", "digests");

    public async Task<bool> SendAsync(IReadOnlyList<DeviceChanges> changes, IReadOnlyList<Kor.Operations.NetworkOps.Core.Learning.FleetInsight> newPatterns, DateTime sweptAtLocal, CancellationToken ct)
    {
        var msg = AlertDigest.Compose(changes, newPatterns, sweptAtLocal);
        if (msg is null) return false;

        if (!options.Value.AlertsEnabled)
        {
            Directory.CreateDirectory(DigestDirectory);
            var path = Path.Combine(DigestDirectory, $"digest-{sweptAtLocal:yyyyMMdd-HHmmss}.html");
            await File.WriteAllTextAsync(path, $"<h2>{System.Net.WebUtility.HtmlEncode(msg.Subject)}</h2>{msg.HtmlBody}", ct);
            log.LogInformation("Alerts disabled: digest written to {Path} ({Subject})", path, msg.Subject);
            return false;
        }
        return await MailAsync(msg.Subject, msg.HtmlBody, BodyType.Html, ct);
    }

    public async Task SendAlertAsync(string subject, string body, CancellationToken ct)
    {
        if (!options.Value.AlertsEnabled) { log.LogWarning("Alerts disabled; not mailed: {Subject}", subject); return; }
        await MailAsync(subject, body, BodyType.Text, ct);
    }

    private async Task<bool> MailAsync(string subject, string body, BodyType type, CancellationToken ct)
    {
        var o = options.Value;
        var request = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = type, Content = body },
                ToRecipients = [new Recipient { EmailAddress = new EmailAddress { Address = o.AlertRecipient } }],
            },
            SaveToSentItems = false,
        };
        try
        {
            await graph.Users[o.AlertFromAddress].SendMail.PostAsync(request, cancellationToken: ct);
            log.LogInformation("Mailed: {Subject} -> {To}", subject, o.AlertRecipient);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Mail failed: {Subject}", subject);
            return false;
        }
    }
}
