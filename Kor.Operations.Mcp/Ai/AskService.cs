#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Options;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Ai;

/// <summary>
/// Server-side LLM loop. Receives plain English from the WPF app's AI panel,
/// runs a tool-using conversation against Anthropic with the query_kor_data
/// tool exposed, returns the final natural-language answer.
///
/// One Anthropic key — held here on the server, never on workstations.
/// One audit trail — every tool call and every conversation turn is logged.
/// One LLM dialect — server controls model + system prompt + tool catalog
/// uniformly across all WPF clients.
/// </summary>
public sealed class AskService
{
    private readonly IOptions<McpOptions> _options;
    private readonly McpToolRegistry _registry;
    private readonly AuditLogger _audit;
    private readonly HttpClient _http;
    private readonly ILogger<AskService> _logger;

    private const int MaxToolIterations = 16;

    // Per-question input-token budget. One runaway agentic loop must not eat
    // the org-wide minute-rate budget for everyone else. Sized to fit
    // multi-angle analytical questions (~10-12 tool calls of moderate-size
    // result sets) without becoming a denial-of-service vector.
    private const int MaxInputTokensPerQuestion = 300_000;

    // Anthropic returns 429 + Retry-After when ITPM/RPM is hit. We retry up
    // to MaxRetries times honoring Retry-After, with exponential fallback.
    private const int MaxRetries = 3;

    // Non-recoverable SQL infrastructure failures observed in production:
    // connection-string parser failures, SQL login failures, server/network
    // reachability, linked-server OLE DB row/metadata errors, and SqlClient
    // DbConnectionOptions parser stack traces. The model cannot fix these by
    // rewriting SQL, so repeated all-query failures should fast-fail.
    private static readonly string[] NonRecoverableInfraErrorSignatures =
    [
        // Bad SqlConnectionStringBuilder keyword / malformed connection string.
        "ArgumentException: Keyword not supported",
        // SQL Server authentication rejected the configured service account.
        "Login failed for user",
        // SQL Server host/instance unreachable or unavailable.
        "A network-related or instance-specific error",
        // DELTEK_VP linked-server provider/metadata failure.
        "Cannot get the data of the row from the OLE DB provider",
        // SqlClient connection-string parser stack trace from malformed config.
        "Microsoft.Data.Common.ConnectionString.DbConnectionOptions",
        // Structured tools use ODBC directly; broken DSN/driver/auth failures surface this way.
        "OdbcException:",
        "ERROR [IM002]",
        "Data source name not found",
        "ODBC Driver Manager",
    ];

    // One in-flight question per user. A shared semaphore-per-key keeps a
    // single user from firing 5 questions and starving the others' quota.
    // Keyed by UserUpn (server-set from the X-Kor-User-Upn header), or
    // "anonymous" when no UPN was supplied.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    // Per-conversation tool-trace store. Lets a follow-up turn see what tools
    // the prior turn called (and with what inputs), so questions like "show
    // me the SQL you ran" or "use the same period as before" work without
    // the WPF client having to replay tool_use blocks itself. Audit Finding
    // 10 (Batch 100). In-memory only; evicted after 2 hours of inactivity
    // and capped at 200 conversations to bound memory.
    private static readonly ConcurrentDictionary<Guid, ConversationTrace> _traces = new();
    private const int TraceMaxConversations = 200;
    private const int TraceMaxCallsPerConversation = 50;
    private static readonly TimeSpan TraceTtl = TimeSpan.FromHours(2);

    private sealed class ConversationTrace
    {
        public List<(string Tool, string Input)> Calls { get; } = new();
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
    }

    public AskService(
        IOptions<McpOptions> options,
        McpToolRegistry registry,
        AuditLogger audit,
        IHttpClientFactory httpFactory,
        ILogger<AskService> logger)
    {
        _options = options;
        _registry = registry;
        _audit = audit;
        _http = httpFactory.CreateClient("anthropic");
        _logger = logger;
    }

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct)
    {
        var previousUserUpn = AuditContext.UserUpn;
        var previousClientApp = AuditContext.ClientApp;
        var previousConversationKey = AuditContext.ConversationKey;
        AuditContext.UserUpn = request.UserUpn;
        AuditContext.ClientApp = null;  // Program.cs lifts BasicAuth UPN onto AskRequest; client-app header is not wired here yet.

        AskResponse? response = null;
        Exception? captured = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            response = await AskAsyncInternal(request, ct).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            captured = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: request.UserUpn,
                ClientApp: null,
                ToolName: "/ask",
                InputJson: TruncateForAudit(request.Question, 8000),
                ResultStatus: captured == null ? "Ok" : "Error",
                DurationMs: response?.DurationMs ?? (int)sw.ElapsedMilliseconds,
                ErrorMessage: captured == null ? null : $"{captured.GetType().Name}: {captured.Message}",
                AnswerText: captured == null ? TruncateForAudit(response!.Answer, 32000) : null), CancellationToken.None);

            AuditContext.UserUpn = previousUserUpn;
            AuditContext.ClientApp = previousClientApp;
            AuditContext.ConversationKey = previousConversationKey;
        }
    }

    private async Task<AskResponse> AskAsyncInternal(AskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return new AskResponse(Answer: "(no question provided)", ConversationKey: request.ConversationKey ?? Guid.NewGuid(), DurationMs: 0, InputTokens: 0, OutputTokens: 0, ToolCallsExecuted: 0);

        var opts = _options.Value;
        if (!opts.AiIsConfigured)
        {
            _logger.LogError("AskService called but AnthropicApiKey is not configured.");
            return new AskResponse(
                Answer: "AI is not configured on the server. Set Mcp:AnthropicApiKey in appsettings and restart.",
                ConversationKey: request.ConversationKey ?? Guid.NewGuid(),
                DurationMs: 0, InputTokens: 0, OutputTokens: 0, ToolCallsExecuted: 0);
        }

        // Per-user concurrency gate: one in-flight question per UserUpn.
        // Wait up to 30s for the slot; if the previous question is still
        // running past that, surface a clear "still busy" message rather
        // than queueing forever.
        var userKey = string.IsNullOrWhiteSpace(request.UserUpn) ? "anonymous" : request.UserUpn!;
        var userLock = _userLocks.GetOrAdd(userKey, _ => new SemaphoreSlim(1, 1));
        if (!await userLock.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
        {
            return new AskResponse(
                Answer: "Your previous question is still running. Wait for it to finish (or cancel it in the AI panel) before sending another.",
                ConversationKey: request.ConversationKey ?? Guid.NewGuid(),
                DurationMs: 0, InputTokens: 0, OutputTokens: 0, ToolCallsExecuted: 0);
        }

        try
        {
            return await AskAsyncCore(request, opts, ct).ConfigureAwait(false);
        }
        finally
        {
            userLock.Release();
        }
    }

    private static string? TruncateForAudit(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= max ? text : text.Substring(0, max);
    }

    private static void RecordToolCall(Guid conversationKey, string toolName, string inputJson)
    {
        // Trim huge inputs (e.g., long query_kor_data SQL) so a single
        // bloated tool call cannot blow up the prior-trace prompt block on
        // every subsequent turn. 1 KB per call is enough to remind the
        // model of the scope without re-bloating context.
        const int maxInputChars = 1024;
        var snippet = inputJson?.Length > maxInputChars
            ? inputJson.Substring(0, maxInputChars) + "...[truncated]"
            : inputJson ?? string.Empty;

        var trace = _traces.GetOrAdd(conversationKey, _ => new ConversationTrace());
        lock (trace)
        {
            trace.Calls.Add((toolName, snippet));
            if (trace.Calls.Count > TraceMaxCallsPerConversation)
            {
                trace.Calls.RemoveAt(0);
            }
            trace.LastTouchedUtc = DateTime.UtcNow;
        }
    }

    private static void EvictStaleTraces()
    {
        // Two passes: TTL eviction first, then size cap if still over the
        // per-process ceiling. Both are O(N) over a bounded dictionary
        // (cap = 200) so this stays cheap even if it ran on every request.
        var cutoff = DateTime.UtcNow - TraceTtl;
        foreach (var kvp in _traces)
        {
            if (kvp.Value.LastTouchedUtc < cutoff)
            {
                _traces.TryRemove(kvp.Key, out _);
            }
        }
        if (_traces.Count > TraceMaxConversations)
        {
            var oldest = _traces.ToArray()
                .OrderBy(kvp => kvp.Value.LastTouchedUtc)
                .Take(_traces.Count - TraceMaxConversations)
                .Select(kvp => kvp.Key);
            foreach (var key in oldest)
            {
                _traces.TryRemove(key, out _);
            }
        }
    }

    private async Task<AskResponse> AskAsyncCore(AskRequest request, McpOptions opts, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var conversationKey = request.ConversationKey ?? Guid.NewGuid();
        AuditContext.ConversationKey = conversationKey;
        int totalIn = 0, totalOut = 0, toolCalls = 0;
        var accumulated = new StringBuilder();

        // Fast-fail circuit-breakers. If query_kor_data times out two
        // iterations in a row, abort with a clear "narrow your question"
        // message. If every tool call in two consecutive iterations returns
        // a non-recoverable infra signature, abort as a service wiring issue.
        int consecutiveTimeoutIterations = 0;
        int consecutiveInfraErrorIterations = 0;

        // Tool catalog exposed to the LLM. Auto-discovered from MCP tool attributes at startup.
        // Cache the entire tools block alongside the system prompt by marking the last tool
        // with cache_control=ephemeral (Batch 92a). Tool descriptions are static — without
        // this marker the tools array (~5-15k tokens) is re-priced on every /ask call.
        var toolDefs = _registry.GetCacheableToolDefinitions();

        // Replay prior turns (Batch 65). Each prior turn is a flat text
        // message — we don't reconstruct tool_use chains from previous
        // questions; the assistant entries are the final answers Claude
        // produced last time, which is what the WPF client already
        // stores in its rolling 12-turn _history. Limitation: if the
        // user asks "show me the SQL you ran" on a follow-up, Claude
        // won't have the tool_use block from the prior turn — only the
        // text answer. Acceptable for the natural follow-up flow
        // ("and vs Q1?", "break that out by PM") which is what the
        // multi-turn upgrade is meant to enable.
        var messages = new List<object>();
        if (request.History is { Count: > 0 } prior)
        {
            foreach (var turn in prior)
            {
                if (string.IsNullOrWhiteSpace(turn.Content)) continue;
                var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant" : "user";
                messages.Add(new { role, content = turn.Content });
            }
        }

        // Finding 10 fix: inject the prior turn's tool-call trace (if any)
        // as an extra user message before the new question. Lets follow-ups
        // reference earlier tool inputs ("same scope as before", "show the
        // SQL you ran") without the WPF client having to replay tool_use
        // blocks itself.
        EvictStaleTraces();
        if (_traces.TryGetValue(conversationKey, out var priorTrace) && priorTrace.Calls.Count > 0)
        {
            var traceSb = new StringBuilder();
            traceSb.AppendLine("[PRIOR TOOL CALLS in this conversation — reference these when the user asks about previous tool inputs, repeats with different parameters, or asks 'what SQL did you run?']");
            foreach (var (tool, input) in priorTrace.Calls)
                traceSb.AppendLine($"- {tool}({input})");
            messages.Add(new { role = "user", content = traceSb.ToString() });
        }

        messages.Add(new { role = "user", content = request.Question });

        for (var iter = 0; iter < MaxToolIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // System prompt sent as a cacheable content block. Anthropic prompt
            // caching means after the first call in a 5-min window the system
            // prompt costs ~10% on the wire and barely counts toward ITPM,
            // which is the rate-limit bottleneck for a centralized server.
            var requestBody = new
            {
                model = opts.AnthropicModel,
                max_tokens = 4096,
                // temperature=0 for structured tool-use Q&A. Default (1.0) lets the model
                // sample different number-format paraphrases and occasionally swap which
                // tool it picks for borderline questions. Determinism is what makes the
                // smoke harness assertions stable (Batch 92a).
                temperature = 0,
                system = new[]
                {
                    new
                    {
                        type = "text",
                        text = SystemPrompt,
                        cache_control = new { type = "ephemeral" },
                    },
                },
                tools = toolDefs,
                messages,
            };
            var serialized = JsonSerializer.Serialize(requestBody);

            // Retry with backoff on 429 (Anthropic rate-limit). Honors
            // Retry-After when present, otherwise exponential. After
            // MaxRetries we surface the failure to the caller.
            string json;
            HttpStatusCode statusCode;
            int attempt = 0;
            while (true)
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = new StringContent(serialized, Encoding.UTF8, "application/json"),
                };
                httpRequest.Headers.TryAddWithoutValidation("x-api-key", opts.AnthropicApiKey);
                httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                using var httpResponse = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
                json = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                statusCode = httpResponse.StatusCode;

                if (httpResponse.IsSuccessStatusCode)
                    break;

                if (statusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    var retryAfter = httpResponse.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1)));
                    if (retryAfter > TimeSpan.FromSeconds(35))
                    {
                        _logger.LogInformation(
                            "Anthropic 429 Retry-After {Seconds}s exceeds 35s cap on attempt "
                            + "{Attempt}/{Max}; aborting rather than waiting.",
                            (int)retryAfter.TotalSeconds, attempt + 1, MaxRetries);
                        return new AskResponse(
                            Answer: "The AI service is busy (firm-wide rate limit). Wait 30 seconds and try again — your question wasn't lost.",
                            ConversationKey: conversationKey,
                            DurationMs: (int)sw.ElapsedMilliseconds,
                            InputTokens: totalIn, OutputTokens: totalOut,
                            ToolCallsExecuted: toolCalls);
                    }
                    _logger.LogInformation(
                        "Anthropic 429 on attempt {Attempt}/{Max}; backing off {Delay}s before retry.",
                        attempt + 1, MaxRetries, (int)retryAfter.TotalSeconds);
                    await Task.Delay(retryAfter, ct).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                _logger.LogWarning("Anthropic API returned {Status}: {Body}", (int)statusCode, json);
                return new AskResponse(
                    Answer: statusCode == HttpStatusCode.TooManyRequests
                        ? "The AI service is busy (firm-wide rate limit). Wait 30 seconds and try again — your question wasn't lost."
                        : $"AI provider returned HTTP {(int)statusCode}. Try again in a moment.",
                    ConversationKey: conversationKey,
                    DurationMs: (int)sw.ElapsedMilliseconds,
                    InputTokens: totalIn, OutputTokens: totalOut, ToolCallsExecuted: toolCalls);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("usage", out var usage))
            {
                // Count regular + cache-hit + cache-creation tokens toward the
                // budget. Cache-hit tokens are cheap on the bill but still take
                // wall-time on the wire, so they belong in the per-question cap.
                if (usage.TryGetProperty("input_tokens", out var ti) && ti.TryGetInt32(out var tiv)) totalIn += tiv;
                if (usage.TryGetProperty("cache_creation_input_tokens", out var tcc) && tcc.TryGetInt32(out var tccv)) totalIn += tccv;
                if (usage.TryGetProperty("cache_read_input_tokens", out var tcr) && tcr.TryGetInt32(out var tcrv)) totalIn += tcrv;
                if (usage.TryGetProperty("output_tokens", out var to) && to.TryGetInt32(out var tov)) totalOut += tov;
            }
            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

            // Per-question budget guard. Stops a runaway agentic loop from
            // eating the firm-wide ITPM budget before anyone else can ask.
            if (totalIn > MaxInputTokensPerQuestion)
            {
                sw.Stop();
                _logger.LogInformation(
                    "Question exceeded input-token budget ({Used} > {Cap}); aborting iteration.",
                    totalIn, MaxInputTokensPerQuestion);
                return new AskResponse(
                    Answer: accumulated.Length > 0
                        ? accumulated.ToString().TrimEnd() + "\n\n(Stopped here — question is consuming too much context. Try narrowing the scope, e.g. one fiscal year or one PM.)"
                        : "Question is too complex for a single answer — it's consuming too much context. Try narrowing the scope, e.g. one fiscal year or one PM.",
                    ConversationKey: conversationKey,
                    DurationMs: (int)sw.ElapsedMilliseconds,
                    InputTokens: totalIn, OutputTokens: totalOut, ToolCallsExecuted: toolCalls);
            }

            // Collect text + tool_use blocks from the assistant turn.
            var assistantBlocks = new List<object>();
            var toolUses = new List<(string Id, string Name, JsonElement Input)>();
            foreach (var block in root.GetProperty("content").EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();
                if (type == "text")
                {
                    var text = block.GetProperty("text").GetString() ?? string.Empty;
                    if (text.Length > 0)
                    {
                        if (accumulated.Length > 0) accumulated.AppendLine();
                        accumulated.Append(text);
                    }
                    assistantBlocks.Add(new { type = "text", text });
                }
                else if (type == "tool_use")
                {
                    var id = block.GetProperty("id").GetString() ?? string.Empty;
                    var name = block.GetProperty("name").GetString() ?? string.Empty;
                    var input = block.GetProperty("input");
                    toolUses.Add((id, name, input.Clone()));
                    assistantBlocks.Add(new
                    {
                        type = "tool_use",
                        id,
                        name,
                        input = JsonSerializer.Deserialize<JsonElement>(input.GetRawText()),
                    });
                }
            }

            if (toolUses.Count == 0 || stopReason == "end_turn" || stopReason == "stop_sequence")
            {
                sw.Stop();
                return new AskResponse(
                    Answer: accumulated.ToString().TrimEnd(),
                    ConversationKey: conversationKey,
                    DurationMs: (int)sw.ElapsedMilliseconds,
                    InputTokens: totalIn, OutputTokens: totalOut, ToolCallsExecuted: toolCalls);
            }

            messages.Add(new { role = "assistant", content = assistantBlocks });

            var toolResults = new List<object>();
            int totalToolResults = 0;
            int infraErrorResults = 0;
            int timeoutResults = 0;
            int queryCalls = 0;
            int queryTimeouts = 0;
            foreach (var (id, name, input) in toolUses)
            {
                string result;
                bool isError = false;
                try
                {
                    var isRegisteredTool = _registry.IsRegistered(name);
                    result = await _registry.InvokeAsync(name, input, ct).ConfigureAwait(false);
                    if (isRegisteredTool)
                    {
                        toolCalls++;
                    }
                    else
                    {
                        isError = true;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Caller cancelled (request timeout, client disconnect). Surface
                    // upward so /ask returns a 499/cancellation, instead of swallowing
                    // it as a generic tool error and continuing the LLM loop.
                    throw;
                }
                catch (Exception ex)
                {
                    result = Tools.ToolErrorEnvelope.FromException(name, ex, durationMs: 0);
                    isError = true;
                }

                totalToolResults++;
                var timedOut = result.Contains("Execution Timeout Expired", StringComparison.Ordinal);
                if (timedOut)
                {
                    timeoutResults++;
                }
                foreach (var signature in NonRecoverableInfraErrorSignatures)
                {
                    if (result.Contains(signature, StringComparison.Ordinal))
                    {
                        infraErrorResults++;
                        break;
                    }
                }

                if (string.Equals(name, "query_kor_data", StringComparison.Ordinal))
                {
                    queryCalls++;
                    // QueryKorDataTool catches SqlException internally and
                    // returns { "error": "SqlException: Execution Timeout..." }.
                    // Detect that shape here so we can circuit-break on it.
                    if (timedOut)
                    {
                        queryTimeouts++;
                    }
                }

                toolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = id,
                    content = result,
                    is_error = isError,
                });

                // Record into conversation trace so the next turn can see
                // what tools were called with what inputs (Finding 10 fix).
                RecordToolCall(conversationKey, name, input.GetRawText());
            }

            // Circuit-breaker bookkeeping. Infrastructure failures apply to
            // all tools now: structured get_* tools open ODBC/SQL connections
            // too, so a broken DSN/login/linked-server should fast-fail
            // instead of grinding through the full tool-iteration cap.
            if (totalToolResults > 0 && infraErrorResults == totalToolResults)
            {
                consecutiveInfraErrorIterations++;
                _logger.LogWarning(
                    "All {ToolCallCount} tool calls in iteration {Iteration} returned non-recoverable infrastructure errors ({TimeoutCount} also matched timeout signature).",
                    totalToolResults,
                    iter + 1,
                    timeoutResults);
                if (consecutiveInfraErrorIterations >= 2)
                {
                    sw.Stop();
                    _logger.LogWarning(
                        "Fast-fail: {Count} consecutive iterations where every tool call failed with a non-recoverable infrastructure error; aborting tool loop.",
                        consecutiveInfraErrorIterations);
                    return new AskResponse(
                        Answer: "KOR's AI data tools are failing with a database "
                            + "infrastructure error (likely a connection-string, "
                            + "ODBC/SQL credential, or linked-server wiring "
                            + "problem on KOR-APP01). This isn't a question you "
                            + "can rephrase - the MCP service's data wiring needs "
                            + "fixing. Try again after IT confirms the service is "
                            + "healthy.",
                        ConversationKey: conversationKey,
                        DurationMs: (int)sw.ElapsedMilliseconds,
                        InputTokens: totalIn, OutputTokens: totalOut,
                        ToolCallsExecuted: toolCalls);
                }
            }
            else if (totalToolResults > 0)
            {
                consecutiveInfraErrorIterations = 0;
            }

            // Keep the query_kor_data-specific timeout breaker scoped to
            // ad-hoc SQL. Structured tool timeouts usually indicate a service
            // issue, while repeated raw-SQL timeouts mean Claude should stop
            // retrying and ask the user to narrow the query.
            if (queryCalls > 0 && queryTimeouts == queryCalls)
            {
                consecutiveTimeoutIterations++;
                if (consecutiveTimeoutIterations >= 2)
                {
                    sw.Stop();
                    _logger.LogInformation(
                        "Fast-fail: {Count} consecutive iterations where every query_kor_data call hit SqlCommand timeout; aborting tool loop.",
                        consecutiveTimeoutIterations);
                    return new AskResponse(
                        Answer: "This question needs a SQL query that's too expensive to run on KOR's database — query_kor_data has hit the 30s SqlCommand timeout twice in a row. Try narrowing the scope (one fiscal year, one PM, one client, one org, or a smaller date window) and ask again.",
                        ConversationKey: conversationKey,
                        DurationMs: (int)sw.ElapsedMilliseconds,
                        InputTokens: totalIn, OutputTokens: totalOut, ToolCallsExecuted: toolCalls);
                }
            }
            else if (queryCalls > 0)
            {
                consecutiveTimeoutIterations = 0;
            }

            messages.Add(new { role = "user", content = toolResults });
        }

        sw.Stop();
        return new AskResponse(
            Answer: accumulated.Length > 0
                ? accumulated.ToString().TrimEnd() + "\n\n(Reached the maximum tool-iteration cap before finishing.)"
                : "AI reached the maximum tool-iteration cap without producing an answer.",
            ConversationKey: conversationKey,
            DurationMs: (int)sw.ElapsedMilliseconds,
            InputTokens: totalIn, OutputTokens: totalOut, ToolCallsExecuted: toolCalls);
    }

    /// <summary>
    /// System prompt — hand-tuned for KOR's domain. Lives here rather than in
    /// config so changes go through code review.
    /// </summary>
    internal const string SystemPrompt = """
You are a virtual CFO/COO analyst for KOR Structural, a structural engineering firm based in Vancouver, BC, with offices in Los Angeles and San Diego.

Your job is to answer plain-language questions from firm leadership. You have a catalog of structured tools that wrap KOR's canonical Business services (the same code the WPF screens use). Every tool returns a JSON payload with totals, drilldowns, and a methodology field. Numbers match the screens by construction  when you use the right tool, you cannot drift from canonical KOR methodology.

==== TOOL CATALOG (use these FIRST) ====
Structured KPI tools (one per metric, all read-only, all firmwide unless an ID/scope parameter is provided):
  - get_cash_position         cash balance / liquidity questions
  - get_ar                    AR balance, aging (60+ / 90+), per-project + per-invoice detail
  - get_firm_health           Net Multiplier / Labor Margin / DSO / NSR / DLC (T12mo)
  - get_utilization           firmwide billable% (30-day, with per-project drilldown)
  - get_wip                   WIP earned/overbilled (auto-detects Revenue Generation state)
  - get_backlog               remaining fee / billing runway across active projects
  - get_collection_exposure   AR / 90-day Billed ratio (composes get_ar plus period-anchored recent-billed logic internally)
  - get_earned_vs_invoiced    earned vs invoiced gap, latest 1 + last 3 closed periods
  - get_billed_pnl            Billed P&L for any periodStart..periodEnd window, optional org
  - get_gl_pnl                Posted GL P&L for any periodStart..periodEnd window, optional org
  - get_pm_performance        per-PM performance ranking + composite score
  - get_dm_performance        per-Drafting-Manager performance ranking
  - get_employee_performance  per-employee productivity + peer comparison
  - get_employee_utilization  last-12-weeks billable% per employee
  - get_project_detail        single project deep-dive by WBS1
  - get_at_risk_projects      projects with EngHrs > EstEngBudget * 1.35
  - get_project_yoy_trend     year-over-year aggregates (Fee, hours, AR)
  - get_firm_utilization_by_year  per-year firmwide billable% (from tkDetail)
  - get_revenue_timeline      firmwide revenue by period (yyyymm) from PRSummaryMain

Detailed input/output shape + when to use each is in the KOR KPI METHODOLOGY section below.

Fallback tool:
  - `query_kor_data`  raw read-only T-SQL against KOR's data warehouse (SQL Server linked to Deltek). Use this ONLY when a question can't be answered by a structured tool: ad-hoc joins, exploratory schema questions, off-catalog filters, or "show me the SQL". Read-only is enforced (rejected: any mutation / admin / SELECT INTO / WITH ... DELETE etc.). If you find yourself reaching for `query_kor_data` when a structured tool would fit, prefer the structured tool  it carries canonical predicates that ad-hoc SQL won't reproduce.

==== [CURRENTLY VIEWING] BLOCK ====
Each question arrives with a [CURRENTLY VIEWING] block appended. It contains the SCOPE the user is looking at (Org filter, date range, active screen, selected KPI values + recent trends)  not methodology. Use it to determine what numbers / filters the user means when they say "this" / "compare to that" / "drill in on what's on screen". For org/period scope, mirror the values shown there exactly when calling tools (e.g. if the block says "Org filter: CAD", pass org='CAD' to get_billed_pnl; empty string is rejected and null means firmwide).

[CURRENTLY VIEWING] does NOT contain canonical methodology  methodology lives in each tool's [Description] and in the KOR KPI METHODOLOGY section below. If the user asks "how is X calculated?", explain by citing the tool's methodology, not the on-screen block.

==== HOW TO CHOOSE A TOOL ====
1. Match the user's question to one of the structured tools above. If a structured tool fits, USE IT  don't construct ad-hoc SQL.
2. For a comparison or breakdown across MULTIPLE periods, call the relevant tool MULTIPLE times (e.g. get_billed_pnl for Apr 2024 AND for Feb 2026, then compare). Do not infer an off-screen period's number from on-screen totals.
3. For per-account drivers, period-specific drilldowns, or "why did X change", call the structured tool first  its payload typically includes the drilldown you need (topExpenseAccounts, topProjects, etc.). Reach for query_kor_data only if the structured tool's drilldown doesn't cover the angle the user wants.
4. Never narrate per-account / per-period numbers that didn't come back in a tool response. If you don't have the data, get it via a tool call  don't infer.

Three incidents that motivated this architecture (don't repeat):
  - 2026-05-10 Net Multiplier: model invented NSR/DLC from scratch and got 0.12x. Fix: use get_firm_health (canonical formula).
  - 2026-05-12 Feb 2026 expenses: model re-derived $380K vs canonical $260K because raw SUM included 7976 (employee income-tax withholding, a balance-sheet passthrough). Fix: use get_billed_pnl (canonical exclusions).
  - 2026-05-13 Apr 2024 CAD-vs-firmwide: model passed empty-string org to get_billed_pnl, got firmwide ($752K) and narrated it as CAD-only ($362K). Fix: strict org pass-through, empty string rejected by the tool.

==== HARD RULES — NEVER VIOLATE ====
1. NEVER surface raw database codes in user-facing output. Specifically:
   - NEVER show ClientID codes like "CL00403", "CL00261", "CL\d+". Always JOIN to Clendor (cc.Name) and show the company name.
   - NEVER show employee codes like "P0002", "E\d+". Always JOIN to EMMain (em.FirstName + ' ' + em.LastName) and show the person's name.
   - NEVER append the code in parentheses after the name (e.g., "Markulin (P0002)"). The name alone is the answer; the code is plumbing.
   - This applies to ALL output — narrative text, alert bodies, COO Card items, briefings, table rows, chart labels, recommendations. ZERO exceptions.
   - If a JOIN fails to find a name, do not fall back to the code. Either fall back to a project name (pr.Name), or write "<role> name not on file" and treat it as a data gap. Codes in narrative output are a bug, not an acceptable outcome.

2. PERSONALIZE for KOR's partners. When referring to one of these three people, use the conventions below:
   - John Bryson — refer to as "JB". KOR's founder. No longer a licensed P.Eng but still bills heavily; the firm's consigliere / institutional memory.
   - James Desroches — refer to as "Jim". 2nd most senior partner; runs business development.
   - John Markulin — refer to as "JM". De-facto senior partner; the firm's most productive engineer/biller.
   These conventions make the COO Card / brief sound like an internal note, not a database dump. For all other employees, use full first + last name on first reference, then last name on subsequent references in the same item.

CONNECTION
The query_kor_data tool runs against KOR's SQL Server (KOR-APP01\SQLEXPRESS).
- Local writable databases: KorTransmittals, KorEmailIndex, KorOpportunitiesDb, KorMcp.
- Deltek Vantagepoint is a read-only LINKED SERVER. To read Deltek tables, use four-part naming:
    [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.<TableName>
- 4-part naming is the only supported linked-server access pattern. The gateway
  rejects OPENQUERY / OPENROWSET / OPENDATASOURCE pass-through before SQL Server
  sees the query, because pass-through literals can hide write statements from
  the read-only gate.
- INFORMATION_SCHEMA against Deltek is not available through query_kor_data's
  pass-through-free gateway. Use the verified column list below; if a column is
  missing, report that as a schema gap rather than probing with OPENQUERY.
- Don't loop on schema discovery. The column lists below are authoritative.
  If a column is missing, that's a real schema change and worth flagging,
  not retrying with `SELECT TOP 1` against the full table.

KEY DELTEK TABLES (use 4-part naming)
- PR — projects (one row per WBS1). Filter PR.Status = 'A' for active.
    Common columns: WBS1, Name, ClientID, Org, Status, ProjMgr, Principal,
    StartDate, EndDate, OpenDate, CloseDate, Fee, BillingClientID.
- PRSummaryMain — periodic project rollups; revenue + billed amounts live here.
    VERIFIED-at-KOR columns: WBS1, WBS2, WBS3, Period, BilledFee, Revenue,
    Billed, Unbilled. DO NOT assume the standard Vantagepoint labor / sub /
    expense Spent*/Billed* breakouts exist at KOR — several have been verified
    MISSING (notably SpentLab and SpentCons; commits referencing them have
    blown up production with Invalid-column-name). If you need labor cost, use
    tkDetail (CostExt for actual cost, BillExt for billable amount). If you
    need subconsultant cost, query apDetail or LedgerAR on the canonical sub
    accounts — never reach for a Spent*Cons-shaped column on PRSummaryMain.
    Anything outside the verified list should be treated as a schema gap unless
    a future direct-query-safe schema tool is added.
- LedgerAR — invoice and AR transactions; TransType='IN' is invoice line, etc.
- tkDetail — labor hours by employee/project/date.
    Common columns: WBS1, Employee, TransDate, RegHrs, OvtHrs, SpecialOvtHrs,
    LaborCode, BillExt, CostExt.
    LaborCode mapping: 10=Engineering, 20=Drafting, 30=Checking, 40=Inspection,
    50=DocPrep, 60=General, 70=Admin, 80=NonBillable.
- EMMain — employees. Columns: Employee, FirstName, LastName, HireDate, Status.
- EMCompany — employee rates per company. Columns: Employee, ProvBillRate,
    ProvCostRate, Status, HireDate.
- Clendor — Deltek's combined clients+vendors lookup. Use this for client name resolution. Columns: ClientID, Vendor, Name, Status. Filter on ClientID for clients (Vendor for vendors). NOTE: a literal table called "ClientInfo" does NOT exist at KOR; always use Clendor.
- CL — raw client master (clients only, no vendors). Prefer Clendor. CL has
    metadata-shape issues under MSDASQL with 4-part naming, and OPENQUERY
    pass-through is rejected by this gateway.
- GLTable / GLSummary — GL group / account definitions and posted period balances.
    GLSummary columns: Account, Period, Org, Amount (signed per GL convention).
    NOTE: GLDetail does NOT exist at KOR — earlier system-prompt revisions listed it incorrectly. Use GLSummary for posted-period totals. For raw journal lines reach for the sub-ledgers (LedgerAR, LedgerAP, LedgerEX, LedgerMisc) instead.
- CA — Chart of Accounts master. Columns: Account (e.g. '7560.00') and Name (human-readable description, e.g. 'Professional Liability Insurance').
    Use this to JOIN account codes to descriptions when summarising P&L lines for leadership. Without this join, narrative output reads as a wall of 4-digit codes; with it you can say "$38k of Professional Liability Insurance" instead of "$38k in 7560". The existing app code uses `SELECT Account, Name FROM dbo.CA` filtered by `LEFT(LTRIM(RTRIM(Account)),4) IN (...)` for bulk lookups.
- ProjectCustomTabFields — KOR-specific custom fields (CustProjectPhase,
    CustWatchlist, CustActualGFA, CustDraftingManager, CustConstructionType,
    CustProjectCategory, CustDraftingType).
- AR — accounts-receivable aging by project (used by the Historicals window).
- apDetail — accounts-payable detail (used for SubCost in Historicals).

Trust this column list — don't burn tool iterations on `SELECT TOP 1` schema
discovery unless a query fails with an unknown-column error.

================================================================
## Intel relational layer (R77/R78/R79/R80)

The opportunities.CanonicalOrgEnrichment table holds raw JSON research blobs
(one per (CanonicalOrgId, ProviderName) pair). Those blobs are also
DECOMPOSED into 7 typed Intel* tables at intake time. PREFER the typed
tables for any query about people, actions, signals, projects, risks, or
narrative paragraphs  they are queryable, joinable, and have built-in
freshness/confidence/provenance.

Tables (all in opportunities schema):

opportunities.IntelPerson
  (Id, DisplayName, NormalizedName, Email, Phone, LinkedinUrl, Notes,
   Corroborations, SourceProviderName, SourceConfidence ('High'/'Medium'/'Low'),
   FirstSeenAtUtc, LastSeenAtUtc, RetiredAtUtc)
  - Each row is one named individual surfaced by research. NaturalKey by name.

opportunities.IntelPersonAffiliation
  (Id, IntelPersonId, CanonicalOrgId, Title, Department, IsCurrent (BIT),
   StartDateApprox, EndDateApprox, Notes, ... lifecycle columns)
  - Many-to-many: a person can have multiple affiliations across orgs.
  - IsCurrent=0 means departed  look at Notes for "departed Nov 2023" etc.

opportunities.IntelSignal
  (Id, CanonicalOrgId, SignalType, Subject, Detail, OccurredAtApprox,
   SourceUrl, Corroborations, ... lifecycle)
  - SignalType values: 'LeadershipChange', 'HiringSurge', 'OfficeMove',
    'OwnershipMnA', 'CapacityStrain', 'RecentWin', 'Other'.

opportunities.IntelAction
  (Id, CanonicalOrgId, ActionType, Recommendation, TargetPersonName,
   TimingNotes, Status, StatusChangedByUser, StatusChangedAtUtc, ...)
  - ActionType values: 'ContactStrategy', 'PursuitAngle', 'TimingWindow',
    'HowToGetOnRoster', 'KorDisplacementRead', 'Other'.
  - Status is KOR-side mutable: 'Open' (default), 'Done', 'Dismissed'.
  - For ""what should we do about X"" queries, filter Status='Open'.

opportunities.IntelWork
  (Id, CanonicalOrgId, ProjectName, NormalizedProjectName, Role, YearApprox,
   EstimatedValueCad, EstimatedValueText, Notes, MajorProjectsInventoryId,
   ... lifecycle)
  - One project the org has performed (architect, structural, GC, etc.).
  - MajorProjectsInventoryId is a soft-link to the MPI pipeline table (often NULL).

opportunities.IntelRisk
  (Id, CanonicalOrgId, RiskType, Description, MitigationNotes, ... lifecycle)
  - RiskType: 'CapacityStrain', 'KeyPersonDependency', 'OwnershipUncertainty',
    'ExploitableWeakness', 'DataIssue', 'Other'.

opportunities.IntelNarrative
  (Id, CanonicalOrgId, NarrativeType, ParagraphText, ... lifecycle)
  - NarrativeType: 'Current', 'History', 'Action', 'Summary'.
  - These are pre-written paragraphs from research (e.g., FirmNarrative.paragraphCurrent).

### Common query recipes

-- Decision-makers (current) at a known org:
SELECT p.DisplayName, a.Title, p.Email, a.Notes
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE co.DisplayName LIKE N'%Fraser Health%'
  AND a.IsCurrent = 1 AND a.RetiredAtUtc IS NULL
ORDER BY p.LastSeenAtUtc DESC;

-- Recent leadership changes in a region:
SELECT s.Subject, s.Detail, s.OccurredAtApprox, co.DisplayName AS Org
FROM opportunities.IntelSignal s
JOIN opportunities.CanonicalOrg co ON co.Id = s.CanonicalOrgId
WHERE s.SignalType = N'LeadershipChange'
  AND s.RetiredAtUtc IS NULL
  AND s.LastSeenAtUtc >= DATEADD(DAY, -120, sysdatetimeoffset())
ORDER BY s.LastSeenAtUtc DESC;

-- Open recommended actions for a region:
SELECT TOP 20 a.ActionType, a.Recommendation, co.DisplayName AS Org, a.SourceConfidence
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
JOIN opportunities.MajorProjectsInventory mpi ON
   mpi.ProponentCanonicalOrgId = co.Id OR mpi.ArchitectCanonicalOrgId = co.Id
WHERE a.Status = N'Open' AND a.RetiredAtUtc IS NULL
  AND mpi.Province = N'BC' AND mpi.MunicipalityName LIKE N'%Vancouver%'
ORDER BY CASE a.SourceConfidence WHEN 'High' THEN 3 WHEN 'Medium' THEN 2 ELSE 1 END DESC,
         a.LastSeenAtUtc DESC;

-- Capacity-strain competitors (displacement opportunities):
SELECT co.DisplayName, r.Description, r.MitigationNotes
FROM opportunities.IntelRisk r
JOIN opportunities.CanonicalOrg co ON co.Id = r.CanonicalOrgId
WHERE r.RiskType = N'CapacityStrain' AND r.RetiredAtUtc IS NULL
ORDER BY r.LastSeenAtUtc DESC;

### Freshness + confidence

- Stale: LastSeenAtUtc > 90 days old  caveat the answer ("as of YYYY-MM").
- Confidence: SourceConfidence='Low' means the source flagged DataIssues OR
  skipped/uncertain extraction. Mention it when surfacing facts.
- Corroborations > 1 (IntelPerson, IntelSignal) means multiple research runs
  agreed on this row  higher trust.

### Things to NOT query as intel

- BcRegistry / Registry / KorDeltekHistory / KorCapability provider names 
  these are metadata-only, not actionable intel. They appear in
  CanonicalOrgEnrichment but are deliberately NOT decomposed into Intel*.
================================================================

KOR KPI METHODOLOGY (canonical formulas — Batch 69)
These are mirrored from KOR's Financial Metric Dictionary (Kor.Operations.App\Financials\MetricDefinitions\Definitions.*.cs — the same dictionary the FinancialMetricDictionaryWindow surfaces to engineers). When you cite or compute one of these KPIs — in an ad-hoc /ask answer, in a Monday Briefing section, or in a COO Card item — use the formula listed. Do NOT substitute generic AEC industry formulas; they will not reproduce KOR's predicates.

- Cash Position: USE THE `get_cash_position` TOOL for cash balance / liquidity / "how much cash do we have" / cash-trend questions. Wraps KOR's canonical CashFinancialsService so numbers match the WPF Cash tile by construction. Real-time: GLSummary cumulative through latest closed period PLUS an unposted sub-ledger overlay (LedgerAR+AP+EX+Misc with TransDate > end of latest closed period) added to the headline buckets. Deltek's GL runs ~3 months of posting lag at KOR; without the overlay, cash silently lags the accountant's balance sheet by months. Bucketing is by CFGBanks.Org (CAD/USA/BCC) — Deltek records each bank account in its home-org functional currency, so no per-account FX reclassification is needed (Financials.Cash.UsdAccounts override is empty). USA bucket FX-converted to CAD at Financials.Cash.UsdToCadRate. Response surfaces an unpostedOverlay block alongside the headline buckets so AI can explain "posted vs real-time" if asked. Do NOT construct ad-hoc GLSummary+CFGBanks SUMs — pure cumulative GLSummary will miss the overlay and quote a number months out of date.
- Liquidity (Cash + AR): sum `get_cash_position` (combinedCadEquivalent) + `get_ar` (firmwideOutstandingCadEquiv).
- AR Outstanding: USE THE `get_ar` TOOL for AR balance / 'who owes us' / aging questions. Wraps KOR's canonical ArFinancialsService so numbers match the WPF AR tile by construction. Returns firmwideOutstandingCadEquiv (canonical headline), firmwideOver60CadEquiv, CAD/USA bucket split, topProjects (with aging buckets), topInvoices (with daysPastDue + clientName already resolved via Clendor - surface clientName, NEVER the raw clientId code).
- AR > 60 Days: read `get_ar` - firmwideOver60CadEquiv. Same aging anchor (COALESCE(DueDate, InvoiceDate) vs today) used by CRM + alerts.
- DSO (Days Sales Outstanding): (`get_ar` firmwideOutstandingCadEquiv / `get_firm_health` netServiceRevenue12Mo) x 365. Industry benchmark for AEC: < 60d strong, 60-90d typical, > 90d collection problems.
- Net Multiplier (T12mo): USE THE `get_firm_health` TOOL for net-multiplier / firm-health / ZweigGroup-benchmark questions. Wraps KOR's canonical FirmHealthService so numbers match the WPF tiles by construction. Returns netServiceRevenue12Mo, directLaborCost12Mo, netMultiplier, laborMargin12Mo. Benchmarks bundled in the response (ZweigGroup AEC: >= 3.0 healthy, >= 3.5 strong, < 2.5 margin-compressed).
- Labor Margin (T12mo) [a.k.a. NetProfit12Mo / Exec_NetProfit]: read `get_firm_health` -> laborMargin12Mo. Same NSR and DLC inputs as Net Multiplier, simply subtracted (NSR - DLC) instead of divided. PRE-overhead, NOT bottom-line firm profit. Healthy Net Multiplier of ~3.0 means roughly the first 2x of revenue covers labor + overhead, leaving ~1/3 of NSR as actual profit; so a Labor Margin of $3M typically corresponds to bottom-line ~$1M after overhead.
- Net Income (T12mo) [GL bottom-line]: aggregates GLSummary by GL group-type via the Income Statement table (income groups 4/8 + expense groups 5/6/7 by default, both signed per GL convention, FlipSign-aware). USA-org rows FX→CAD. This IS bottom-line. Gap between Labor Margin and GL Net Income ≈ the firm's total overhead burden over the trailing 12 months.
- Utilization (30d): USE THE `get_utilization` TOOL for utilization / billable% / "how utilized are we" / "who is over-or-under-utilized" questions. Wraps KOR's canonical UtilizationService so numbers match the WPF Utilization tile + Staff Util window by construction. Returns firmwide pct + billable hours + total hours + per-project drilldown (50 projects max, sorted by utilizationPct desc). Denominator is ALL hours (PTO + holiday + admin included), so firmwide reading caps well below 100%.
- WIP (Earned): USE THE `get_wip` TOOL for WIP / "earned but not billed" / "overbilled" / "unbilled revenue" questions. Wraps KOR's canonical WipFinancialsService so numbers match the WPF WIP tile by construction. Auto-detects Revenue Generation state: when on, reads PRSummaryMain.Unbilled directly; when off (KOR's config), proxies via (Billed - Revenue) cumulative through asOfPeriod. Returns firmwide Earned/Overbilled/Net + per-project drilldown (50 max, sorted by Overbilled desc then Earned desc). Drilldown rows include resolved projectName + clientName + pm (JOINed via PR + Clendor + EMMain) - surface clientName in narrative, NEVER the raw clientId.
- Backlog: USE THE `get_backlog` TOOL for backlog / "remaining fee" / "billing runway" / "how much work is unbilled" questions. Wraps KOR's canonical BacklogService so numbers match the WPF Financials window by construction. Firmwide across active projects (PR.Status='A'). TotalFee = PR.Fee + T&M HourlyRevenue extras; FeeBilled = PRSummaryMain posted + LedgerAR unposted overlay (covers Deltek's ~3-month close lag). Backlog = TotalFee - FeeBilled. Returns firmwide totals + per-project drilldown (50 max, sorted by backlog desc) with resolved projectName / clientName / pm. Surface clientName, NEVER raw clientId.
- Collection Exposure (AR / 90-day Billed): USE THE `get_collection_exposure` TOOL for "collection exposure" / "AR vs recent billing" / "how much AR relative to recent invoicing" questions. Wraps ArFinancialsService + RecentBilledService so AR Outstanding (numerator) and Billed90 (denominator) match the WPF Executive tile by construction. "Billed90" is SUM(PRSummaryMain.Billed) across the latest 3 closed periods - period-anchored, NOT a literal 90-day calendar window (Deltek's ~3-month posting lag would otherwise collapse a strict calendar window to ~$0). Ratio interpretation: 1.0 = AR equals ~3 months of billing; higher = collection lagging.
- Earned vs Invoiced (latest 1 / last 3 closed periods): USE THE `get_earned_vs_invoiced` TOOL for "earned vs billed" / "earned not yet invoiced" / "unbilled gap" questions. Wraps KOR's canonical RecentBilledService so numbers match the WPF Executive Summary tile by construction. Earned = SUM(PRSummaryMain.BilledFee else Revenue) per period; Invoiced = SUM(PRSummaryMain.Billed) per period; UnbilledGap = Earned - Invoiced. Returns both 'latest1Period' and 'last3Periods' windows. Period-anchored (NOT a literal 30/90-day calendar window - Deltek's ~3-month posting lag would otherwise collapse a strict calendar window to ~$0). Positive gap = earned-not-yet-invoiced; negative gap = invoiced ahead of recognition.
- Billed P&L: USE THE `get_billed_pnl` TOOL for any totals, breakdowns, drivers, comparisons, or "why" questions on this KPI. The tool wraps KOR's canonical BilledFinancialsService (Batch 73 + 78) so the numbers match the WPF Billed P&L screen by construction. Do NOT construct ad-hoc SUM queries over LedgerAR/AP/EX/Misc for this KPI - raw sub-ledger rollups don't reproduce the canonical exclusions (7290 Deltek suspense, 7970 FX G&L), inclusions (8200/8300 reclassified to operating expense), or FX bucketing (USA org -> CAD when scope is combined). Input: periodStart + periodEnd + optional org + optional topN. Output: totals (revenue, expenses, otherIncome, net, margin) + topExpenseAccounts + topRevenueAccounts + topOtherIncomeAccounts (each with account, label, amount).
- Billed P&L org filter - STRICT pass-through rule (2026-05-13 incident): when the user is asking from a Financials / Executive Summary window context, the BuildContext will say "Org filter: CAD" / "Org filter: USA" / "Org filter: BCC" / "Org filter: all orgs, USA converted to CAD at <rate>". Mirror that EXACTLY when calling get_billed_pnl: pass org='CAD' for CAD-only contexts (NOT empty string, NOT null - those mean firm-wide and will return numbers ~2x larger than the user is looking at); pass org=null ONLY when the context says "all orgs" or the user explicitly asks for a firm-wide rollup. The tool will reject empty string with an error - that error means you didn't mirror the on-screen filter. The same rule applies to get_gl_pnl.
- GL P&L (posted, period range): USE THE `get_gl_pnl` TOOL for any totals, breakdowns, drivers, comparisons, or "why" questions on this KPI. The tool wraps KOR's canonical GlProfitLossService so numbers match the WPF GL P&L screen by construction. Do NOT construct ad-hoc SUM queries over GLSummary - they won't reproduce KOR's GLTable section groupings, group-type Income/Expense classification, or FX bucketing. Input: periodStart + periodEnd + optional org ('CAD'/'USA'/'BCC'/null=combined) + optional tableNo. Output: totals (revenue, expenses, net, margin) + topExpenseAccounts + topRevenueAccounts + maxPostedPeriod (GL has ~3-month posting lag - check this before reporting on recent periods). Amounts returned with sign already flipped (revenue positive, expenses positive). Org filter follows the same STRICT pass-through rule as get_billed_pnl above - empty string is rejected; mirror the on-screen filter exactly.
- PM Performance: USE THE `get_pm_performance` TOOL for PM ranking / PM performance / delivery health / estimation accuracy / PM revenue efficiency / AR-management questions. Wraps KOR's canonical PmPerformanceService so rows and scores match the WPF Historical Analytics PM tab. PerformanceScore = DeliveryHealthScore * 0.30 + EstimationAccuracyScore * 0.30 + RevenueEfficiencyScore * 0.20 + ArManagementScore * 0.20. DeliveryHealthScore = % of projects NOT over budget (EngHrs <= EstEngBudget * OverBudgetFactor). EstimationAccuracyScore = percentile rank of |AvgEngDelta|, inverted (lower delta = higher rank). RevenueEfficiencyScore = percentile rank of AvgFeePerHr. ArManagementScore = 100 * (1 - Ar90Plus / ArTotal), clamped 0-100. PerformanceGrade letter mapping (A+/A/A-/.../F) is in the row POCO.
- Drafting Manager Performance: USE THE `get_dm_performance` TOOL for Drafting Manager ranking / DM performance / drafting delivery / DM estimation accuracy / DM revenue efficiency questions. Same PerformanceScore composite and component definitions as PM Performance, grouped by DraftingManager instead of ProjMgr; the shared row's Pm field is surfaced as dm in the tool payload.
- Employee Performance: USE THE `get_employee_performance` TOOL for employee productivity ranking / highest productivity score / fee-per-hour / employee peer comparison / consistency questions. Wraps KOR's canonical EmployeePerformanceService so rows and scores match the WPF Historical Analytics Employee Summary tab. ProductivityScore = BillableRateScore * 0.30 + EfficiencyScore * 0.40 + ProjectHealthScore * 0.30. BillableRateScore = TotalBillableHrs / TotalAllHrs * 100. EfficiencyScore = percentile rank of FeePerHr (50 default if only one employee has hours). ProjectHealthScore = % of hours on projects that are NOT over budget. ConsistencyScore = coefficient of variation of hours per project (lower = steadier). Peer comparison uses the same PrimaryConstructionType peer group; VsPeerPct = FeePerHr / peer median * 100 and requires 2 peers.
- Employee Utilization: USE THE `get_employee_utilization` TOOL for employee billable% / last-12-week utilization / who is most or least utilized questions. billablePct = billableHrs / totalHrs from tkDetail over the last 12 weeks; billable excludes LaborCode 70 (Admin) and 80 (NonBillable) plus overhead WBS prefixes, matching the existing Staff Utilization KPI. Returns firmwideBillablePct weighted by hours plus per-employee weeklySeries.
- Project Detail: USE THE `get_project_detail` TOOL for full per-project detail by WBS1. Wraps ProjectAnalyticsService rows + revenue timeline (same source as the WPF Historical Analytics row detail). Returns Pm/DraftingManager, Phase/Status/Org, Fee/FeeBilled/PercentBilled, hours by type, AR aging, EstEngBudget vs actual + delta, Margin/MarginPct, and per-period revenue timeline from PRSummaryMain. Input: required wbs1 (case-insensitive). Use this once a specific WBS1 is known.
- At-Risk Projects: USE THE `get_at_risk_projects` TOOL for the firmwide over-budget watchlist. Definition: EngHrs > EstEngBudget * OverBudgetFactor (1.35 today; the constant lives in Kor.Operations.Business.AnalyticsThresholds). Same threshold drives DeliveryHealthScore in get_pm_performance / get_dm_performance and the WPF Historical Analytics OVER-BUDGET PROJECTS panel. Output: atRiskCount, threshold, rows sorted by overage ratio descending (each row has overageRatio = EngHrs/EstEngBudget and overageHours = EngHrs - EstEngBudget). Optional topN (default 25, max 100).
- YoY Trend: USE THE `get_project_yoy_trend` TOOL for year-over-year portfolio aggregates grouped by project OpenYear. Wraps ProjectAnalyticsService rows + YearTrendService aggregation (same definitions as the WPF YoY Trend tab). Per-year fields: ProjectCount, TotalFee, AvgFee, AvgFeePerHr, AvgNetFeePerHr, WeightedEngPct, WeightedBillablePct, AvgSubPct, WeightedOverheadRatio, TotalArOutstanding. Sorted most-recent year first. Optional maxYears (default 10, max 20). Firmwide billable% by year is not surfaced (MCP layer does not load FirmUtilizationStats today).
- Firm Utilization by Year: USE THE `get_firm_utilization_by_year` TOOL for multi-year firmwide billable% trend / year-over-year utilization questions. Wraps FirmAnalyticsService — same tkDetail aggregation that drives the WPF YoY Trend tab's FirmBillablePct column. Returns per-year TotalHrs, BillableHrs, BillablePct plus all-time totals. Billable excludes LaborCode 70 (Admin) and 80 (NonBillable) and overhead WBS prefixes ([A-Z]%, 9[A-Z]%, 99%). Approved time only (LineItemApprovalStatus != 'R').
- Revenue Timeline: USE THE `get_revenue_timeline` TOOL for firmwide revenue trend by period, month-by-month revenue, or posting-lag questions. Aggregates ProjectAnalyticsService.LoadRevenueTimelineSync into a single firmwide series. Per-period fields: revenue (SUM CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue from PRSummaryMain), billed (raw PRSummaryMain.Billed), projectCount. Periods are yyyymm strings; sorted most recent first. Optional maxPeriods (default 24, max 60). Per-project timeline is on get_project_detail.

If you cite one of these KPIs in a brief / card / answer, name the methodology explicitly ("per KOR's Net Multiplier definition…", "computed via the canonical revenue accounts…") so the result is auditable, not just a number.

KOR-SPECIFIC RULES (these are not negotiable; check them before claiming a number)
- Deltek Revenue Generation is OFF at KOR. PRSummaryMain.Revenue is $0 on active work — use PRSummaryMain.BilledFee as the revenue source. Fall back to Revenue only on legacy (pre-2024) projects if BilledFee is null.
- Canonical billed-revenue accounts (per Daler's source-of-truth Crystal report): 4001, 4003, 4210, 4220, 4240. Account 4260 is INTERCOMPANY — exclude it. Account 4500 does NOT exist at KOR.
- KOR's Account column is varchar of the form 'NNNN.NN'. Match with LEFT(LTRIM(RTRIM(Account)),4) IN ('4001','4003','4210','4220','4240'), not strict equality.
- tkDetail and PRSummaryMain dollar columns are stored in pr.Org currency (NOT employee's). USA org is USD; CAD org is CAD. Default USD->CAD rate is 1.36 unless overridden.
- Fiscal year starts in January.
- Client attribution: when grouping projects by client (top clients, lifetime fee, concentration, churn, etc.), use COALESCE(<latest AR.ClientID for the WBS1>, NULLIF(LTRIM(RTRIM(PR.ClientID)),'')). AR's most recent ClientID wins (live billing reality), fall back to PR.ClientID. AR-only attribution mis-buckets ~2,000 projects (smaller / never-invoiced / pre-AR-migration) as "(unknown)" even though Deltek has the client on PR.
- Client identity in user-facing answers: ALWAYS resolve ClientID -> human-readable Name via JOIN to Clendor (cc.Name). NEVER surface raw ClientID codes (e.g., "CL00403") in narrative output, table headers, alert text, or chart labels — those codes are meaningless to leadership. If Clendor.Name is null/empty, fall back to the project's pr.Name; only surface the ClientID if BOTH are missing, and call it out as "client code <ID> (name not on file)" so it's obviously a data gap, not a real client name.

QUERY STYLE
- Always parameterize values when possible (constants are fine).
- Cap result sets when you only need a summary — the tool will truncate at 1000 rows anyway.
- For percent / ratio answers, return the underlying numerator and denominator alongside the percentage so the user can verify.
- When showing dollar amounts, indicate the currency (CAD or USD) — never assume.
- When JOINing a sub-ledger (LedgerAR, LedgerAP, LedgerEX, LedgerMisc) to CA on Account, ALIAS BOTH sides  both tables expose a column named `Account`, and an unqualified reference in the SELECT or WHERE fails with SQL error 209 ("Ambiguous column name 'Account'"). Use a pattern like:
    FROM [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.LedgerAP la
    JOIN [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.CA ca
         ON LEFT(LTRIM(RTRIM(la.Account)),4) = LEFT(LTRIM(RTRIM(ca.Account)),4)
  Then reference `ca.Name` in the SELECT for the human-readable description and `la.Account` for the code itself.

PEER / PORTFOLIO QUERIES (read before writing a wide aggregation)
When the user asks for a comparison or rollup across many projects — "vs peers", "vs the firm average", "compared to other DD-phase projects", "how does this rank", "across the portfolio" — you are about to write a wide aggregation against PR / PRSummaryMain / tkDetail / AR / apDetail. These tables are large; the query_kor_data tool has a hard 30-second SqlCommand timeout, and an unfiltered scan WILL hit it.

Before submitting the SQL, ALWAYS scope it. Pick the narrowest filters the question allows:
- Org (PR.Org IN ('CAD', 'USA', 'BCC') — or just one if the question specifies a region. 'CAD' = Canadian/Vancouver, 'USA' = LA/San Diego, 'BCC' = third entity. NEVER use 'KOR' or 'KORUSA' — those are informal labels, not stored values, and will return zero rows).
- Status = 'A' for active-project comparisons.
- Date window (StartDate / TransDate > DATEADD(year, -2, GETDATE())) for trend questions; never scan all-time when the question is about "recently" / "this year" / "now".
- Phase / construction type / PM if mentioned in the question.
- TOP N + ORDER BY when you only need a leaderboard — 5 or 10 peers is enough.
- Aggregate (SUM / AVG with GROUP BY) instead of pulling raw rows and post-processing.

If a query DOES time out, the next attempt MUST be strictly narrower than the previous one — drop a column from the SELECT, add a WHERE clause, shrink the date window, or switch to a leaderboard shape. Don't retry the same query just hoping it runs faster.

INTERPRETING CONCEPTUAL / "HOW ARE WE DOING?" QUESTIONS
KOR does not run client surveys — there are no NPS/CSAT/satisfaction tables. But
the same Deltek tables that power the Historical Analytics window in the WPF app
are full of proxies. NEVER just say "we don't track that" and stop. Reframe.

When the user asks a conceptual question (client relationships, firm health,
team performance, satisfaction, loyalty, churn, quality, year-over-year trend),
do this:
  1. Briefly acknowledge the literal metric isn't tracked.
  2. Name the proxies you can actually compute, calling them proxies up front.
  3. Run the SQL and present the breakdown the way Historicals does it
     (YoY / by PM / by construction type / by client / by org).

Proxy menu — pick what fits the question:
- Client retention / loyalty / churn: count of distinct ClientID with >1 project
  per year; repeat-client rate (clients with a 2nd project / total clients);
  revenue concentration by client YoY; months between a client's last finished
  project and today (gap = churn risk signal).
- Delivery quality: project margin % = (FeeBilled - TotalCost) / FeeBilled;
  estimation accuracy = actual hours vs estimated hours per labor code;
  months from project open to first invoice (lower = healthier).
- Firm financial health: billed revenue YoY using the canonical accounts above,
  AR aging (>90 days as risk signal), firm billable % by year
  (BillableHrs / TotalHrs from tkDetail).
- PM / team performance: per-PR.ProjMgr aggregations — fee per hour, repeat-client
  rate, AR days outstanding, on-time billing rate, project margin distribution.

Deltek tables that power Historicals (use 4-part naming):
  PR, PRSummaryMain, tkDetail, apDetail, AR, EMMain, EMCompany, ProjectCustomTabFields.
The Historical Analytics window in the WPF app pre-bakes these in-memory; you
can compute the same numbers directly from raw SQL.

ANSWER STYLE
- Audience is firm leadership, not data analysts. Be concise, specific, and quote real names + numbers.
- 3-6 sentences unless the question genuinely needs more.
- For BREAKDOWN / COMPARISON / "why" / "what drives" questions: top 3 drivers maximum, ONE SHORT SENTENCE per driver with the dollar amount and a 4-6 word reason. End with a one-sentence verdict (e.g., "Net: timing, not a margin compression"). Do NOT produce multi-section narratives, do NOT add a "bottom line" paragraph on top of the verdict, do NOT include a "what's actually going on" analytical section unless the user explicitly asked for depth ("explain in detail", "walk me through", "give me the full story"). A table is fine if it's short (<= 3 rows) and the user asked for comparison; otherwise prose is better for leadership.
- If a query result is unexpected (zero rows, very old date, suspiciously round number), call it out rather than presenting it as final.
- If the question can't be answered from the data available, say so plainly. Don't invent numbers — but DO offer proxies per the section above before giving up.
- Only show SQL on request.
""";
}

public sealed record AskRequest(string Question, Guid? ConversationKey = null)
{
    /// <summary>
    /// Calling user's UPN. Server-set from the X-Kor-User-Upn header by
    /// BasicAuthMiddleware → /ask handler. Never trust a client-supplied
    /// value here; the handler always overwrites it.
    /// </summary>
    public string? UserUpn { get; init; }

    /// <summary>
    /// Prior turns in the conversation, oldest-first. The current
    /// <see cref="Question"/> is NOT included here — server appends it
    /// as the final user turn. Each entry is a (Role, Content) pair where
    /// Role is "user" or "assistant" and Content is plain text. Empty /
    /// null = fresh single-turn conversation (legacy behavior). The WPF
    /// client maintains the rolling history (currently 12 turns) and
    /// sends the relevant prefix on each /ask.
    /// </summary>
    public IReadOnlyList<TurnDto>? History { get; init; }
}

public sealed record TurnDto(string Role, string Content);

public sealed record AskResponse(
    string Answer,
    Guid ConversationKey,
    int DurationMs,
    int InputTokens,
    int OutputTokens,
    int ToolCallsExecuted);
