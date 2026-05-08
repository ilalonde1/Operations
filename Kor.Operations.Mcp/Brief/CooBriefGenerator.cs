using System.Text.Json;
using Kor.Operations.Mcp.Ai;

namespace Kor.Operations.Mcp.Brief;

public sealed class CooBriefGenerator
{
    private readonly AskService _ask;
    private readonly CooBriefRepository _repo;
    private readonly ILogger<CooBriefGenerator> _logger;

    public CooBriefGenerator(
        AskService ask,
        CooBriefRepository repo,
        ILogger<CooBriefGenerator> logger)
    {
        _ask = ask;
        _repo = repo;
        _logger = logger;
    }

    public async Task GenerateForWeekAsync(DateTime weekOf, CancellationToken ct)
    {
        foreach (var section in Enum.GetValues<BriefSection>())
        {
            try
            {
                var (overlay, question) = BriefPrompts.For(section);
                // AskService is single-turn today. Prepending the overlay keeps
                // the strategic brief persona local to this scheduled call.
                var combined = overlay + "\n\n" + question;
                var req = new AskRequest(combined) { UserUpn = "coo-brief@kor" };
                var resp = await _ask.AskAsync(req, ct).ConfigureAwait(false);

                if (TryParseBriefAnswer(
                    resp.Answer,
                    out var rich,
                    section,
                    resp.InputTokens,
                    resp.OutputTokens,
                    resp.ToolCallsExecuted))
                {
                    await _repo.InsertAsync(weekOf, rich, ct).ConfigureAwait(false);
                    _logger.LogInformation("Brief section {Section} generated.", section);
                }
                else
                {
                    _logger.LogWarning("Brief section {Section} returned non-JSON answer; saving raw.", section);
                    await _repo.InsertAsync(weekOf, new RichBriefSection(
                        Section: section,
                        Headline: "(unstructured response)",
                        Body: resp.Answer,
                        Recommendation: null,
                        InputTokens: resp.InputTokens,
                        OutputTokens: resp.OutputTokens,
                        ToolCalls: resp.ToolCallsExecuted), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brief section {Section} failed; skipping.", section);
            }
        }
    }

    private static bool TryParseBriefAnswer(
        string answer,
        out RichBriefSection rich,
        BriefSection section,
        int inputTokens,
        int outputTokens,
        int toolCalls)
    {
        rich = new RichBriefSection(section, string.Empty, string.Empty, null, inputTokens, outputTokens, toolCalls);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        // The gateway's chatty system prompt has the AI narrate tool calls
        // before producing its final JSON. We find every balanced JSON object
        // in the response (string-aware bracketing — quoted braces don't
        // count) and pick the LAST one whose shape matches our schema.
        // That's the AI's final answer regardless of preceding narration.
        foreach (var candidate in EnumerateJsonObjects(answer).Reverse())
        {
            if (TryBuild(candidate, section, inputTokens, outputTokens, toolCalls, out rich))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuild(
        string candidate,
        BriefSection section,
        int inputTokens,
        int outputTokens,
        int toolCalls,
        out RichBriefSection rich)
    {
        rich = new RichBriefSection(section, string.Empty, string.Empty, null, inputTokens, outputTokens, toolCalls);
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var headline = root.TryGetProperty("headline", out var headlineEl) ? headlineEl.GetString() : null;
            var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
            string? recommendation = null;
            if (root.TryGetProperty("recommendation", out var recEl) && recEl.ValueKind != JsonValueKind.Null)
            {
                recommendation = recEl.GetString();
            }

            // Both headline AND body must be present + non-empty for this to
            // count as a valid brief object. This filters out unrelated JSON
            // that might appear in the AI's narration (e.g. tool-result JSON).
            if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(body)) return false;

            rich = new RichBriefSection(
                Section: section,
                Headline: headline.Trim(),
                Body: body.Trim(),
                Recommendation: string.IsNullOrWhiteSpace(recommendation) ? null : recommendation.Trim(),
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                ToolCalls: toolCalls);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Yields every balanced top-level JSON object substring in <paramref name="s"/>,
    /// in order. Tracks string literals + escapes so braces inside quoted values
    /// don't break balance. Used to extract structured AI output from a response
    /// that mixes prose narration with the final JSON answer.
    /// </summary>
    private static IEnumerable<string> EnumerateJsonObjects(string s)
    {
        var n = s.Length;
        var i = 0;
        while (i < n)
        {
            if (s[i] != '{') { i++; continue; }

            var start = i;
            var depth = 0;
            var inString = false;
            var escape = false;

            for (; i < n; i++)
            {
                var c = s[i];
                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return s.Substring(start, i - start + 1);
                        i++;
                        break;
                    }
                }
            }
            // If we fell off the end without closing, abandon and resume after start.
            if (depth != 0) i = start + 1;
        }
    }
}
