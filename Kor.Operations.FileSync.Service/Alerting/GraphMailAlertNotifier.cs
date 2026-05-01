#nullable enable
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.FileSync.Service.Alerting;

internal sealed class GraphMailAlertNotifier : IAlertNotifier
{
    private readonly GraphServiceClient _graph;
    private readonly IOptionsMonitor<FileSyncOptions> _options;
    private readonly ILogger<GraphMailAlertNotifier> _logger;

    public GraphMailAlertNotifier(
        GraphServiceClient graph,
        IOptionsMonitor<FileSyncOptions> options,
        ILogger<GraphMailAlertNotifier> logger)
    {
        _graph = graph;
        _options = options;
        _logger = logger;
    }

    public async Task SendAlertAsync(string subject, string body, CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var from = opts.AlertFromAddress;
        var to = opts.AlertRecipient;

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Alert send skipped: AlertFromAddress or AlertRecipient is not configured.");
            return;
        }

        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } },
                },
            },
            SaveToSentItems = false,
        };

        try
        {
            await _graph.Users[from].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
            _logger.LogInformation("Alert sent: {Subject} -> {Recipient}.", subject, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alert send failed: {Subject}.", subject);
        }
    }
}
