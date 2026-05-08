#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kor.Operations.Services;

/// <summary>
/// Describes a tool Claude can call. InputSchemaJson must be a complete JSON Schema
/// object (e.g. {"type":"object","properties":{...},"required":[...]}).
/// </summary>
internal sealed record AiTool(string Name, string Description, string InputSchemaJson);

/// <summary>
/// Dispatcher invoked when Claude requests a tool call. Receives the tool name
/// and input JSON, returns the result string fed back to Claude.
/// Implementations should be side-effect safe (UI mutations must marshal to the UI thread).
/// </summary>
internal delegate Task<string> AiToolDispatcher(string toolName, JsonElement input, CancellationToken ct);

internal sealed class AppAiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _apiKey;
    private readonly AppAiContextBuilder _contextBuilder;

    private static readonly string SystemPromptBase =
        "You are an analytics assistant for KOR Structural, a structural engineering firm in Vancouver, BC. " +
        "You have access to the firm's complete project, employee, and financial performance data provided below.\n\n" +
        "Your audience is firm principals and project managers who are NOT data analysts. " +
        "Explain metrics, scores, and trends in plain, actionable language. Use specific names and numbers. " +
        "Be concise but thorough — answer in 3-6 sentences unless the question needs more.\n\n" +
        "You can compare employees, identify trends, flag concerns, rank projects, analyze clients, " +
        "and make recommendations based on the data. " +
        "If asked about something not in the data, say so clearly. Do NOT make up data or reference " +
        "information outside of what's provided below.\n\n" +
        "SCORING METHODOLOGY:\n" +
        "Employee Productivity Score (0-100, maps to A+ through F):\n" +
        "  Billable Rate (30%) — % of total hours on billable projects vs overhead/admin\n" +
        "  Efficiency (40%) — Fee/Hr percentile rank vs all employees. 50 = median.\n" +
        "  Project Health (30%) — % of hours on projects NOT over budget\n\n" +
        "PM/DM Performance Score (0-100, maps to A+ through F):\n" +
        "  Delivery Health (30%) — % of projects not over budget\n" +
        "  Estimation Accuracy (30%) — Budget delta percentile rank\n" +
        "  Revenue Efficiency (20%) — Fee/Hr percentile rank\n" +
        "  AR Management (20%) — % of AR not 90+ days overdue\n\n" +
        "Delivery Confidence (per project): Critical / At Risk / Watch / High Confidence\n" +
        "Consistency: CV of hours across projects. Steady < 0.3, Variable < 0.6, Erratic ≥ 0.6\n" +
        "Peer Comparison: Fee/Hr compared against employees working on same construction type\n\n";

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    internal AppAiService(string apiKey, AppAiContextBuilder contextBuilder)
    {
        _apiKey = (apiKey ?? "").Trim();
        _contextBuilder = contextBuilder;
    }

    internal async Task<string> AskAsync(
        IReadOnlyList<(string Role, string Content)> conversation,
        string? localContext = null,
        CancellationToken ct = default,
        string? systemPromptOverride = null)
    {
        if (!IsConfigured) return "AI is not configured. Set the KOR_ANTHROPIC_KEY environment variable.";
        if (conversation.Count == 0) return "";

        string systemPrompt;
        if (systemPromptOverride is not null)
        {
            // Caller supplied a complete system prompt (context already embedded).
            systemPrompt = systemPromptOverride;
        }
        else
        {
            var fullContext = _contextBuilder.BuildFullContext(localContext);
            systemPrompt = SystemPromptBase + "FIRM DATA:\n" + fullContext;
        }

        var messages = conversation.Select(m => new { role = m.Role, content = m.Content }).ToArray();

        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 4096,
            system = systemPrompt,
            messages
        };

        try
        {
            using var response = await HttpRetryPolicy.SendAsync(
                _http,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                    {
                        Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("x-api-key", _apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    return request;
                },
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        }
        catch (OperationCanceledException)
        {
            return "";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI request failed after retries.");
            return $"Unable to get AI response: {ex.Message}";
        }
    }

    /// <summary>
    /// Tool-use conversation. Sends the conversation + tool definitions, executes any
    /// tool calls Claude requests via <paramref name="toolDispatcher"/>, and loops
    /// until Claude returns a final text response. The caller's <paramref name="conversation"/>
    /// is NOT mutated; any assistant/tool messages added during the loop are internal only.
    /// Returns the final assistant text (may be empty if Claude only called tools).
    /// </summary>
    internal async Task<AiToolsResult> AskWithToolsAsync(
        IReadOnlyList<(string Role, string Content)> conversation,
        IReadOnlyList<AiTool> tools,
        AiToolDispatcher toolDispatcher,
        string systemPrompt,
        int maxIterations = 10,
        CancellationToken ct = default,
        byte[]? firstMessageImagePng = null)
    {
        if (!IsConfigured)
            return new AiToolsResult("AI is not configured. Set the KOR_ANTHROPIC_KEY environment variable.", 0);
        if (conversation.Count == 0) return new AiToolsResult("", 0);

        // Build messages as a mutable list of objects so we can mix string-content
        // turns (initial user/assistant) with structured-content turns
        // (image + tool_use + tool_result).
        var messages = new List<object>(conversation.Count + maxIterations * 2);
        for (int i = 0; i < conversation.Count; i++)
        {
            var (role, content) = conversation[i];
            // Attach the seed image to the first user message, if provided.
            bool isFirstUser = i == 0 && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase);
            if (isFirstUser && firstMessageImagePng is { Length: > 0 })
            {
                var base64 = Convert.ToBase64String(firstMessageImagePng);
                messages.Add(new
                {
                    role,
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/png", data = base64 } },
                        new { type = "text", text = content }
                    }
                });
            }
            else
            {
                messages.Add(new { role, content });
            }
        }

        var toolsPayload = tools.Select(t => new Dictionary<string, object?>
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson)
        }).ToArray();

        var accumulatedText = new StringBuilder();
        int toolCallsExecuted = 0;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            var requestBody = new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 4096,
                system = systemPrompt,
                tools = toolsPayload,
                messages
            };

            JsonDocument? doc = null;
            try
            {
                using var response = await HttpRetryPolicy.SendAsync(
                    _http,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                        };
                        request.Headers.Add("x-api-key", _apiKey);
                        request.Headers.Add("anthropic-version", "2023-06-01");
                        return request;
                    },
                    ct).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                doc = JsonDocument.Parse(json);
            }
            catch (OperationCanceledException)
            {
                return new AiToolsResult(accumulatedText.ToString(), toolCallsExecuted);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AI tool request failed after retries.");
                return new AiToolsResult(
                    accumulatedText.Length > 0
                        ? accumulatedText.ToString()
                        : $"Unable to get AI response: {ex.Message}",
                    toolCallsExecuted);
            }

            if (doc is null)
                return new AiToolsResult(accumulatedText.ToString(), toolCallsExecuted);

            using (doc)
            {
                var root = doc.RootElement;
                var stopReason = root.TryGetProperty("stop_reason", out var srEl) ? srEl.GetString() : null;
                var contentArr = root.GetProperty("content");

                // Collect text and tool_use blocks from this response.
                var toolUses = new List<(string Id, string Name, JsonElement Input, JsonElement RawBlock)>();
                var assistantBlocks = new List<object>();

                foreach (var block in contentArr.EnumerateArray())
                {
                    var type = block.GetProperty("type").GetString();
                    if (type == "text")
                    {
                        var text = block.GetProperty("text").GetString() ?? "";
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (accumulatedText.Length > 0) accumulatedText.AppendLine();
                            accumulatedText.Append(text);
                        }
                        assistantBlocks.Add(new { type = "text", text });
                    }
                    else if (type == "tool_use")
                    {
                        var id = block.GetProperty("id").GetString() ?? "";
                        var name = block.GetProperty("name").GetString() ?? "";
                        var input = block.GetProperty("input");
                        toolUses.Add((id, name, input.Clone(), block.Clone()));
                        assistantBlocks.Add(new
                        {
                            type = "tool_use",
                            id,
                            name,
                            input = JsonSerializer.Deserialize<JsonElement>(input.GetRawText())
                        });
                    }
                }

                // No tool calls → we're done.
                if (toolUses.Count == 0 || stopReason == "end_turn" || stopReason == "stop_sequence")
                    return new AiToolsResult(accumulatedText.ToString(), toolCallsExecuted);

                // Append the assistant message (tool_use blocks and any text).
                messages.Add(new { role = "assistant", content = assistantBlocks });

                // Execute each tool and build the user response with tool_result blocks.
                var toolResultBlocks = new List<object>();
                foreach (var (id, name, input, _) in toolUses)
                {
                    string resultStr;
                    bool isError = false;
                    try
                    {
                        resultStr = await toolDispatcher(name, input, ct);
                        toolCallsExecuted++;
                    }
                    catch (OperationCanceledException)
                    {
                        return new AiToolsResult(accumulatedText.ToString(), toolCallsExecuted);
                    }
                    catch (Exception ex)
                    {
                        resultStr = $"Tool '{name}' failed: {ex.Message}";
                        isError = true;
                        Log.Warning(ex, "AI tool dispatch failed for {Tool}", name);
                    }

                    toolResultBlocks.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = id,
                        content = resultStr,
                        is_error = isError
                    });
                }

                messages.Add(new { role = "user", content = toolResultBlocks });
                // Loop to send tool results back to Claude.
            }
        }

        // Iteration cap hit — return what we have.
        return new AiToolsResult(
            accumulatedText.Length > 0
                ? accumulatedText.ToString()
                : "AI reached the maximum tool-use iterations without finishing.",
            toolCallsExecuted);
    }
}

internal sealed record AiToolsResult(string Text, int ToolCallsExecuted);
