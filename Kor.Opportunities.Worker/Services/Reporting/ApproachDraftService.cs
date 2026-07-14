#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Reporting;

/// <summary>
/// Drafts the per-play "Approach" block (who to call, call script, draft email)
/// for the attack sheet — a plain Anthropic synthesis call over the intel we
/// already hold (no web search). Returns a styled HTML fragment or null on any
/// failure (the sheet simply omits that play's block; it never blocks the send).
/// </summary>
public sealed class ApproachDraftService
{
    private const string SystemPrompt =
        "You are KOR Structural's business-development strategist. KOR is a structural engineering firm " +
        "chasing the STRUCTURAL seat on projects. From the intel provided, write concrete, specific outreach " +
        "content — no filler, no invented facts, no names or emails not in the intel.\n" +
        "TEMPORAL RULES (critical — getting this wrong destroys credibility with the prospect): the user " +
        "message states TODAY'S DATE. Any date in the intel that is BEFORE today has already passed — NEVER " +
        "describe a past milestone as upcoming, 'ahead of', or 'this week'. Do NOT state specific dates, " +
        "deadlines, or 'ahead of the <month> application' in the outreach unless the intel clearly marks that " +
        "milestone as still in the future relative to today. When unsure, use safe, general timeline language " +
        "(e.g. 'as the team is being assembled', 'while the consultant roster is still forming') rather than a " +
        "specific date. Do not name a year or month in the email or call script unless you are certain it is " +
        "current or future.\n" +
        "Return ONLY valid JSON, no prose, no code fences, matching exactly this shape:\n" +
        "{\"who\":[{\"name\":\"\",\"email\":\"\",\"why\":\"\"}],\"opener\":\"\",\"points\":[\"\"]," +
        "\"objections\":[{\"q\":\"\",\"a\":\"\"}],\"email_subject\":\"\",\"email_body\":\"\"}\n" +
        "who = up to 3 people to call first (prefer ones with an email), why = one line on their leverage. " +
        "opener = one or two sentences to open the call. points = 3-5 talking points tuned to the procurement " +
        "channel and KOR's edge. objections = 2 likely pushbacks with crisp answers. email = a ready-to-send " +
        "intro to the single best contact (subject + 5-8 sentence body).";

    private readonly BdResearchExecutorOptions _options;
    private readonly ILogger<ApproachDraftService> _logger;

    public ApproachDraftService(IOptions<BdResearchExecutorOptions> options, ILogger<ApproachDraftService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

    /// <summary>Draft one play's Approach block; returns styled HTML or null.</summary>
    public async Task<string?> DraftHtmlAsync(string intel, CancellationToken ct)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            using var client = new AnthropicClient(new APIAuthentication(apiKey), httpClient);
            // Ground the model in the current date so it never phrases a past
            // milestone from the intel as upcoming (the July-2026 "ahead of the
            // March 2026 application" credibility bug).
            var dated = $"TODAY'S DATE: {DateTime.Now:yyyy-MM-dd}.\n\n{intel}";
            var parameters = new MessageParameters
            {
                Model = _options.Model,
                MaxTokens = 2000,
                Stream = false,
                System = new List<SystemMessage> { new(SystemPrompt) },
                Messages = new List<Message> { new(RoleType.User, dated) },
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters, ct).ConfigureAwait(false);
            var text = string.Concat(response.Content.OfType<TextContent>().Select(t => t.Text)).Trim();
            return RenderHtml(text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ApproachDraftService: draft failed");
            return null;
        }
    }

    // ---- parse + render (pure) ---------------------------------------------

    internal static string? RenderHtml(string? modelText)
    {
        var json = ExtractJson(modelText);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sb = new StringBuilder();

            var who = root.TryGetProperty("who", out var w) && w.ValueKind == JsonValueKind.Array
                ? w.EnumerateArray().ToList() : new List<JsonElement>();
            if (who.Count > 0)
            {
                sb.Append("<div class=aptitle>WHO TO CALL</div>");
                foreach (var person in who.Take(3))
                {
                    var name = Str(person, "name");
                    var email = Str(person, "email");
                    var why = Str(person, "why");
                    if (name.Length == 0) continue;
                    sb.Append("<div class=apc><b>").Append(E(name)).Append("</b>");
                    if (email.Length > 0) sb.Append(" <span class=em>").Append(E(email)).Append("</span>");
                    if (why.Length > 0) sb.Append(" — ").Append(E(why));
                    sb.Append("</div>");
                }
            }

            var opener = Str(root, "opener");
            var points = root.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : new List<string>();
            if (opener.Length > 0 || points.Count > 0)
            {
                sb.Append("<div class=aptitle>CALL SCRIPT</div>");
                if (opener.Length > 0) sb.Append("<div class=apo>").Append(E(opener)).Append("</div>");
                foreach (var pt in points.Take(6)) sb.Append("<div class=appt>• ").Append(E(pt)).Append("</div>");
            }

            var objections = root.TryGetProperty("objections", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().ToList() : new List<JsonElement>();
            if (objections.Count > 0)
            {
                sb.Append("<div class=aptitle>IF THEY PUSH BACK</div>");
                foreach (var ob in objections.Take(3))
                {
                    var q = Str(ob, "q");
                    var a = Str(ob, "a");
                    if (q.Length == 0) continue;
                    sb.Append("<div class=apo><b>“").Append(E(q)).Append("”</b> → ").Append(E(a)).Append("</div>");
                }
            }

            var subject = Str(root, "email_subject");
            var body = Str(root, "email_body");
            if (subject.Length > 0 || body.Length > 0)
            {
                sb.Append("<div class=aptitle>DRAFT EMAIL</div>");
                sb.Append("<div class=apmail>");
                if (subject.Length > 0) sb.Append("<div class=apsub><b>Subject:</b> ").Append(E(subject)).Append("</div>");
                if (body.Length > 0) sb.Append("<div>").Append(E(body).Replace("\n", "<br>")).Append("</div>");
                sb.Append("</div>");
            }

            var html = sb.ToString();
            return html.Length == 0 ? null : html;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJson(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null!;
        t = t.Trim();
        // strip ```json fences if present
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            if (nl > 0) t = t[(nl + 1)..];
            var fence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) t = t[..fence];
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        return (start >= 0 && end > start) ? t[start..(end + 1)] : null!;
    }

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string E(string s) => WebUtility.HtmlEncode(s ?? "");

    private string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        env = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return _options.ApiKey;
    }
}
