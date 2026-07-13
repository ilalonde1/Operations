#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services.Reporting;

/// <summary>
/// Minimal Graph sendMail client (client-credential flow, raw REST).
/// Deliberately NOT the Graph SDK: the Worker has no Graph dependency and
/// the morning report needs exactly two calls — token + sendMail. Uses the
/// same app registration as FileSync (Mail.Send application permission,
/// sending as the configured UPN).
/// </summary>
public sealed class GraphMailSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GraphMailSender> _logger;

    public GraphMailSender(IHttpClientFactory httpFactory, ILogger<GraphMailSender> logger)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SendHtmlAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        string senderUpn,
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken ct)
        => SendHtmlAsync(tenantId, clientId, clientSecret, senderUpn, recipient, subject, htmlBody,
            attachmentName: null, attachmentBytes: null, attachmentContentType: null, ct: ct);

    /// <summary>
    /// Same minimal sendMail with one optional file attachment (base64
    /// fileAttachment per the Graph contract). Used by the weekly attack sheet.
    /// </summary>
    public async Task SendHtmlAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        string senderUpn,
        string recipient,
        string subject,
        string htmlBody,
        string? attachmentName,
        byte[]? attachmentBytes,
        string? attachmentContentType,
        CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(nameof(GraphMailSender));

        using var tokenReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", clientId),
                new System.Collections.Generic.KeyValuePair<string, string>("client_secret", clientSecret),
                new System.Collections.Generic.KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default"),
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials"),
            }),
        };

        using var tokenResp = await http.SendAsync(tokenReq, ct).ConfigureAwait(false);
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!tokenResp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Graph token request failed ({(int)tokenResp.StatusCode}): {Truncate(tokenJson, 300)}");
        }

        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Graph token response had no access_token.");

        using var mailReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(senderUpn)}/sendMail");
        mailReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        object[]? attachments = null;
        if (attachmentBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(attachmentName))
        {
            attachments = new object[]
            {
                // Dictionary because the Graph discriminator key "@odata.type"
                // is not a legal anonymous-type property name.
                new System.Collections.Generic.Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = attachmentName!,
                    ["contentType"] = attachmentContentType ?? "application/octet-stream",
                    ["contentBytes"] = Convert.ToBase64String(attachmentBytes),
                },
            };
        }

        mailReq.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = htmlBody },
                toRecipients = new[] { new { emailAddress = new { address = recipient } } },
                attachments,
            },
            saveToSentItems = false,
        }, options: new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        using var mailResp = await http.SendAsync(mailReq, ct).ConfigureAwait(false);
        if (!mailResp.IsSuccessStatusCode)
        {
            var body = await mailResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Graph sendMail failed ({(int)mailResp.StatusCode}): {Truncate(body, 300)}");
        }

        _logger.LogInformation("Morning report sent to {Recipient} as {Sender}.", recipient, senderUpn);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
