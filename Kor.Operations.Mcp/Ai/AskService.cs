#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Options;
using Kor.Operations.Mcp.Tools;
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
    private readonly QueryKorDataTool _queryTool;
    private readonly BilledPnLTool _billedPnLTool;
    private readonly GlPnLTool _glPnLTool;
    private readonly CashTool _cashTool;
    private readonly ArTool _arTool;
    private readonly FirmHealthTool _firmHealthTool;
    private readonly UtilizationTool _utilizationTool;
    private readonly WipTool _wipTool;
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
    ];

    // One in-flight question per user. A shared semaphore-per-key keeps a
    // single user from firing 5 questions and starving the others' quota.
    // Keyed by UserUpn (server-set from the X-Kor-User-Upn header), or
    // "anonymous" when no UPN was supplied.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public AskService(
        IOptions<McpOptions> options,
        QueryKorDataTool queryTool,
        BilledPnLTool billedPnLTool,
        GlPnLTool glPnLTool,
        CashTool cashTool,
        ArTool arTool,
        FirmHealthTool firmHealthTool,
        UtilizationTool utilizationTool,
        WipTool wipTool,
        AuditLogger audit,
        IHttpClientFactory httpFactory,
        ILogger<AskService> logger)
    {
        _options = options;
        _queryTool = queryTool;
        _billedPnLTool = billedPnLTool;
        _glPnLTool = glPnLTool;
        _cashTool = cashTool;
        _arTool = arTool;
        _firmHealthTool = firmHealthTool;
        _utilizationTool = utilizationTool;
        _wipTool = wipTool;
        _audit = audit;
        _http = httpFactory.CreateClient("anthropic");
        _logger = logger;
    }

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct)
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

    private async Task<AskResponse> AskAsyncCore(AskRequest request, McpOptions opts, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var conversationKey = request.ConversationKey ?? Guid.NewGuid();
        int totalIn = 0, totalOut = 0, toolCalls = 0;
        var accumulated = new StringBuilder();

        // Fast-fail circuit-breaker (Batch 66). If query_kor_data times out
        // two iterations in a row, abort the tool loop with a clear
        // "narrow your question" message rather than grinding through up
        // to MaxToolIterations * 30s = 8 minutes of hopeless retries.
        int consecutiveTimeoutIterations = 0;
        int consecutiveInfraErrorIterations = 0;

        // Tool catalog exposed to the LLM.
        var toolDefs = new object[]
        {
            new
            {
                name = "query_kor_data",
                description =
                    "Run a read-only SELECT query against KOR's SQL Server. " +
                    "Reaches both KOR's local databases AND Deltek Vantagepoint via the DELTEK_VP linked server. " +
                    "For Deltek tables use 4-part naming: [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.<TableName>. " +
                    "Only SELECT and WITH (CTE) statements are allowed. Result rows are capped; if you need more rows, refine the WHERE clause.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        sql = new { type = "string", description = "T-SQL SELECT statement." },
                    },
                    required = new[] { "sql" },
                },
            },
            new
            {
                name = "get_billed_pnl",
                description =
                    "Get KOR-canonical Billed P&L totals + top account drivers for a period range. " +
                    "ALWAYS use this for Billed P&L breakdown/comparison/why questions instead of " +
                    "querying LedgerAR/AP/EX/Misc directly - this wraps the same canonical code path " +
                    "as the WPF Billed P&L screen, so numbers match by construction.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        periodStart = new { type = "string", description = "ISO 8601 start date inclusive, e.g. '2024-04-01'." },
                        periodEnd = new { type = "string", description = "ISO 8601 end date inclusive, e.g. '2024-04-30'." },
                        org = new { type = "string", description = "'CAD' (Canadian entity, Vancouver), 'USA' (US entity, LA/San Diego), 'BCC' (third entity), or null for combined CAD-equivalent rollup. NOTE: the literal Deltek Org values are 'CAD'/'USA'/'BCC'; 'KOR'/'KORUSA' are informal naming and will return zero rows." },
                        topN = new { type = "integer", description = "Top N accounts per section, default 10, max 25." },
                    },
                    required = new[] { "periodStart", "periodEnd" },
                },
            },
            new
            {
                name = "get_gl_pnl",
                description =
                    "Get KOR-canonical GL (posted) P&L totals + top account drivers for a period range. " +
                    "ALWAYS use this for GL P&L breakdown/comparison/why questions instead of querying " +
                    "GLSummary directly - this wraps the same canonical code path as the WPF GL P&L screen. " +
                    "GL has a ~3-month posting lag; the tool surfaces `maxPostedPeriod` so you can confirm " +
                    "the latest period that actually has data. Amounts returned with sign already flipped " +
                    "to user convention (revenue positive, expenses positive).",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        periodStart = new { type = "string", description = "ISO 8601 start date inclusive, e.g. '2025-01-01'." },
                        periodEnd = new { type = "string", description = "ISO 8601 end date inclusive, e.g. '2025-12-31'." },
                        org = new { type = "string", description = "'CAD' (Canadian entity, Vancouver), 'USA' (US entity, LA/San Diego), 'BCC' (third entity), or null for combined CAD-equivalent rollup. NOTE: literal Deltek Org values are 'CAD'/'USA'/'BCC'; 'KOR'/'KORUSA' return zero rows." },
                        topN = new { type = "integer", description = "Top N accounts per section, default 10, max 25." },
                        tableNo = new { type = "integer", description = "Optional: specific GLTable TableNo. If omitted, the first Income-Statement table is used." },
                    },
                    required = new[] { "periodStart", "periodEnd" },
                },
            },
            new
            {
                name = "get_cash_position",
                description =
                    "Get KOR-canonical cash position: latest CAD/USA/BCC bucket balances, combined CAD-equivalent, " +
                    "12-month history, and per-account breakdown. Wraps CashFinancialsService (same code path as the " +
                    "WPF Cash tile). ALWAYS use this for cash balance / liquidity / 'how much cash do we have' / " +
                    "'cash trend' questions instead of querying GLSummary+CFGBanks directly - the canonical version " +
                    "applies the Financials.Cash.UsdAccounts override that reclassifies individual accounts.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new
            {
                name = "get_ar",
                description =
                    "Get KOR-canonical AR (accounts-receivable) firmwide: open balance in CAD-equiv, over-60 aging, " +
                    "CAD vs USA org split, top open projects with aging buckets (Current/31-60/61-90/90+), and top " +
                    "open invoices with daysPastDue + resolved client name. Wraps ArFinancialsService (same code path " +
                    "as the WPF AR tile). ALWAYS use this for AR balance / 'who owes us' / aging / over-60 / DSO " +
                    "denominator questions instead of querying AR+PR+Clendor directly - ad-hoc SUMs miss the Org " +
                    "bucketing and FX conversion.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new
            {
                name = "get_firm_health",
                description =
                    "Get KOR-canonical trailing-12mo firm-health KPIs: Net Service Revenue, Direct Labor Cost, " +
                    "Net Multiplier (NSR/DLC), Labor Margin (NSR-DLC, a.k.a. NetProfit12Mo). Wraps FirmHealthService " +
                    "(same code path as the WPF Net Multiplier / Labor Margin tiles). ALWAYS use this for " +
                    "'how healthy is the firm', 'net multiplier', 'labor margin', 'are we beating ZweigGroup', or " +
                    "ZweigGroup-benchmark questions instead of querying LedgerAR + tkDetail directly. For DSO, also " +
                    "call get_ar and compute (get_ar firmwideOutstandingCadEquiv / get_firm_health netServiceRevenue12Mo) * 365.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new
            {
                name = "get_utilization",
                description =
                    "Get KOR-canonical 30-day firmwide utilization: billable %, billable hours, total hours, " +
                    "and per-project rows sorted by utilizationPct desc. Wraps UtilizationService (same code path " +
                    "as the WPF Utilization tile and Staff Util window). ALWAYS use this for 'utilization', " +
                    "'billable %', 'how utilized are we', 'who is over/under-utilized' questions instead of " +
                    "querying tkDetail directly - the canonical version applies the LaborCode + overhead-WBS1 " +
                    "billable predicate. Denominator is ALL hours (PTO + holiday + admin included), so firmwide " +
                    "utilization caps well below 100%.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new
            {
                name = "get_wip",
                description =
                    "Get KOR-canonical WIP firmwide at the latest posted period: Earned (revenue recognized " +
                    "but not yet billed), Overbilled (billed in advance), Net, plus per-project drilldown. " +
                    "Wraps WipFinancialsService (same code path as the WPF WIP tile). Auto-detects whether " +
                    "Deltek Revenue Generation is on (direct PRSummaryMain.Unbilled) or off (Billed - Revenue " +
                    "proxy). KOR runs with RG OFF so the proxy path is what produces production numbers. " +
                    "ALWAYS use this for WIP / 'earned but not billed' / 'overbilled' / 'unbilled revenue' " +
                    "questions instead of querying PRSummaryMain directly - ad-hoc SUMs miss the Org FX " +
                    "bucketing and the RG auto-detection.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
        };

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
            int queryCalls = 0;
            int queryTimeouts = 0;
            int queryInfraErrors = 0;
            foreach (var (id, name, input) in toolUses)
            {
                string result;
                bool isError = false;
                try
                {
                    if (string.Equals(name, "query_kor_data", StringComparison.Ordinal))
                    {
                        var sql = input.TryGetProperty("sql", out var sqlEl) ? (sqlEl.GetString() ?? string.Empty) : string.Empty;
                        result = await _queryTool.QueryKorDataAsync(sql, ct).ConfigureAwait(false);
                        toolCalls++;
                        queryCalls++;
                        // QueryKorDataTool catches SqlException internally and
                        // returns { "error": "SqlException: Execution Timeout..." }.
                        // Detect that shape here so we can circuit-break on it.
                        if (result.Contains("Execution Timeout Expired", StringComparison.Ordinal))
                        {
                            queryTimeouts++;
                        }
                        foreach (var signature in NonRecoverableInfraErrorSignatures)
                        {
                            if (result.Contains(signature, StringComparison.Ordinal))
                            {
                                queryInfraErrors++;
                                break;
                            }
                        }
                    }
                    else if (string.Equals(name, "get_billed_pnl", StringComparison.Ordinal))
                    {
                        var periodStart = input.TryGetProperty("periodStart", out var psEl) ? psEl.GetString() ?? "" : "";
                        var periodEnd = input.TryGetProperty("periodEnd", out var peEl) ? peEl.GetString() ?? "" : "";
                        string? orgArg = input.TryGetProperty("org", out var orgEl) && orgEl.ValueKind == JsonValueKind.String
                            ? orgEl.GetString()
                            : null;
                        int? topNArg = input.TryGetProperty("topN", out var nEl) && nEl.TryGetInt32(out var nv)
                            ? nv
                            : null;
                        result = await _billedPnLTool.GetBilledPnLAsync(periodStart, periodEnd, orgArg, topNArg, ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_gl_pnl", StringComparison.Ordinal))
                    {
                        var periodStart = input.TryGetProperty("periodStart", out var psEl2) ? psEl2.GetString() ?? "" : "";
                        var periodEnd = input.TryGetProperty("periodEnd", out var peEl2) ? peEl2.GetString() ?? "" : "";
                        string? orgArg = input.TryGetProperty("org", out var orgEl2) && orgEl2.ValueKind == JsonValueKind.String
                            ? orgEl2.GetString()
                            : null;
                        int? topNArg = input.TryGetProperty("topN", out var nEl2) && nEl2.TryGetInt32(out var nv2)
                            ? nv2
                            : null;
                        short? tableNoArg = input.TryGetProperty("tableNo", out var tEl) && tEl.TryGetInt16(out var tv)
                            ? tv
                            : null;
                        result = await _glPnLTool.GetGlPnLAsync(periodStart, periodEnd, orgArg, topNArg, tableNoArg, ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_cash_position", StringComparison.Ordinal))
                    {
                        result = await _cashTool.GetCashPositionAsync(ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_ar", StringComparison.Ordinal))
                    {
                        result = await _arTool.GetArAsync(ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_firm_health", StringComparison.Ordinal))
                    {
                        result = await _firmHealthTool.GetFirmHealthAsync(ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_utilization", StringComparison.Ordinal))
                    {
                        result = await _utilizationTool.GetUtilizationAsync(ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else if (string.Equals(name, "get_wip", StringComparison.Ordinal))
                    {
                        result = await _wipTool.GetWipAsync(ct).ConfigureAwait(false);
                        toolCalls++;
                    }
                    else
                    {
                        result = JsonSerializer.Serialize(new { error = $"Unknown tool: {name}" });
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
                    result = JsonSerializer.Serialize(new { error = $"{ex.GetType().Name}: {ex.Message}" });
                    isError = true;
                }

                toolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = id,
                    content = result,
                    is_error = isError,
                });
            }

            // Circuit-breaker bookkeeping. Only count an iteration as a
            // "timeout iteration" if every query_kor_data call in it timed
            // out — a mixed iteration (one timeout, one success) means
            // Claude is making progress and shouldn't be aborted.
            if (queryCalls > 0 && queryInfraErrors == queryCalls)
            {
                consecutiveInfraErrorIterations++;
                if (consecutiveInfraErrorIterations >= 2)
                {
                    sw.Stop();
                    _logger.LogWarning(
                        "Fast-fail: {Count} consecutive iterations where every "
                        + "query_kor_data call failed with a non-recoverable "
                        + "infrastructure error (connection string, auth, or "
                        + "linked-server). Aborting tool loop.",
                        consecutiveInfraErrorIterations);
                    return new AskResponse(
                        Answer: "query_kor_data is failing with a database "
                            + "infrastructure error (likely a connection-string, "
                            + "SQL Server credential, or DELTEK_VP linked-server "
                            + "wiring problem on KOR-APP01). This isn't a "
                            + "question you can rephrase - the MCP service's "
                            + "data wiring needs fixing. Try again after IT "
                            + "confirms the service is healthy.",
                        ConversationKey: conversationKey,
                        DurationMs: (int)sw.ElapsedMilliseconds,
                        InputTokens: totalIn, OutputTokens: totalOut,
                        ToolCallsExecuted: toolCalls);
                }
            }
            else if (queryCalls > 0)
            {
                consecutiveInfraErrorIterations = 0;
            }

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
    private const string SystemPrompt = @"
You are a virtual CFO/COO analyst for KOR Structural, a structural engineering firm based in Vancouver, BC, with offices in Los Angeles and San Diego.

Your job is to answer plain-language questions from firm leadership. You have one tool: query_kor_data (read-only T-SQL against KOR's data warehouse). But the AI bar also pushes rich on-screen context with every question — READ THAT FIRST. Tool calls are for what context cannot answer, not the default.

==== READ [CURRENTLY VIEWING] BEFORE CALLING ANY TOOL ====
Every question arrives with a [CURRENTLY VIEWING] block appended. It contains:
  - Live snapshots from every KOR Operations screen the user has loaded (KPI tile values, project rows, etc.).
  - For each visible KPI: a ""KPI methodology"" sub-block sourced from KOR's Financial Metric Dictionary (the same dictionary the FinancialMetricDictionaryWindow surfaces to engineers). Each entry has the canonical How / Formula text — predicates, exclusions, FX handling, and the precise data sources KOR uses.

When the user asks ABOUT a KPI on screen — ""why is the Net Multiplier this number?"", ""how is utilization calculated?"", ""what does Cash Position include?"", ""explain X"" — the KPI methodology block in [CURRENTLY VIEWING] IS the authoritative answer. Quote / summarise it. DO NOT call query_kor_data to re-derive a formula that's already in the prompt; ad-hoc SQL will not reproduce the carefully-tuned predicates and FX bucketing baked into the dictionary, and you will produce a wrong number (2026-05-10 Net Multiplier incident — Claude invented Net Billed Revenue ÷ Direct Labor Cost from scratch, got 0.12x against a 3.0+ target, instead of citing the trailing-12mo NSR/DLC formula sitting two paragraphs above the question).

When the user asks for a BREAKDOWN, COMPARISON, or ""why"" question that references a value already shown in [CURRENTLY VIEWING] (e.g., ""why were Feb 2026 expenses high"", ""compare April 2024 to the Feb 2026 expenses on screen"", ""break down this month's $260K""), TRUST THE ON-SCREEN VALUE as your reference total  do NOT re-derive it via SQL. Raw sub-ledger SUMs against 5xxx/6xxx/7xxx will not reproduce KOR's canonical Billed P&L predicates (7290 suspense excluded, 7970 FX G&L excluded, balance-sheet passthroughs filtered, FX bucketed per pr.Org), and your number will disagree with the screen. Only query for the OFF-screen period(s) needed to answer the question, and use the same canonical methodology for those. (2026-05-12 incident  Claude re-derived Feb 2026 expenses as $380K when the screen showed the canonical $260K, because the raw rollup included 7976 employee income tax withholding remittances  a balance-sheet passthrough, not a P&L expense.)

Reach for query_kor_data ONLY when:
  - The user asks for raw values not in [CURRENTLY VIEWING] (e.g., ""list the 10 projects driving that number"", ""show me by PM"").
  - The user explicitly says ""verify"" / ""double-check"" / ""show me the SQL"".
  - The question is about data the screen doesn't show (historical trend, cross-screen comparison, ad-hoc filter the UI doesn't expose).
  - The user's question references a DATE WINDOW that [CURRENTLY VIEWING] doesn't cover. If the screen shows Jan-Apr 2026 and the user asks ""why were expenses higher in April 2024"", ""how does this compare to Q3 2023"", ""YoY"", etc., RUN THE SQL  do not refuse with ""I don't have that data"". query_kor_data can fetch any historical period from LedgerAR / LedgerAP / PRSummaryMain / tkDetail / GLSummary. The methodology block in [CURRENTLY VIEWING] tells you HOW to compute the metric; the date window is just a WHERE-clause parameter. (2026-05-12 incident  Claude answered ""no April 2024 data on screen"" instead of querying LedgerAR for the 2024 expense accounts.)

Methodology-first is faster (no tool round-trips), cheaper (smaller token budget), and answers in KOR's exact voice — not generic-AE-firm guesses.

==== HARD RULES — NEVER VIOLATE ====
1. NEVER surface raw database codes in user-facing output. Specifically:
   - NEVER show ClientID codes like ""CL00403"", ""CL00261"", ""CL\d+"". Always JOIN to Clendor (cc.Name) and show the company name.
   - NEVER show employee codes like ""P0002"", ""E\d+"". Always JOIN to EMMain (em.FirstName + ' ' + em.LastName) and show the person's name.
   - NEVER append the code in parentheses after the name (e.g., ""Markulin (P0002)""). The name alone is the answer; the code is plumbing.
   - This applies to ALL output — narrative text, alert bodies, COO Card items, briefings, table rows, chart labels, recommendations. ZERO exceptions.
   - If a JOIN fails to find a name, do not fall back to the code. Either fall back to a project name (pr.Name), or write ""<role> name not on file"" and treat it as a data gap. Codes in narrative output are a bug, not an acceptable outcome.

2. PERSONALIZE for KOR's partners. When referring to one of these three people, use the conventions below:
   - John Bryson — refer to as ""JB"". KOR's founder. No longer a licensed P.Eng but still bills heavily; the firm's consigliere / institutional memory.
   - James Desroches — refer to as ""Jim"". 2nd most senior partner; runs business development.
   - John Markulin — refer to as ""JM"". De-facto senior partner; the firm's most productive engineer/biller.
   These conventions make the COO Card / brief sound like an internal note, not a database dump. For all other employees, use full first + last name on first reference, then last name on subsequent references in the same item.

CONNECTION
The query_kor_data tool runs against KOR's SQL Server (KOR-APP01\SQLEXPRESS).
- Local writable databases: KorTransmittals, KorEmailIndex, KorOpportunitiesDb, KorMcp.
- Deltek Vantagepoint is a read-only LINKED SERVER. To read Deltek tables, use four-part naming:
    [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.<TableName>
- 4-part naming works for the vast majority of queries. Reach for OPENQUERY
  ONLY when (a) you hit a linked-server timeout on a heavy aggregate, or
  (b) you're hitting INFORMATION_SCHEMA. Don't pre-emptively rewrite a
  failing query as OPENQUERY — fix the query itself first.
- INFORMATION_SCHEMA against Deltek MUST be wrapped in OPENQUERY (linked server quirks otherwise).
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
    Anything outside the verified list must be confirmed against the live
    schema (INFORMATION_SCHEMA via OPENQUERY) before being referenced.
- LedgerAR — invoice and AR transactions; TransType='IN' is invoice line, etc.
- tkDetail — labor hours by employee/project/date.
    Common columns: WBS1, Employee, TransDate, RegHrs, OvtHrs, SpecialOvtHrs,
    LaborCode, BillExt, CostExt.
    LaborCode mapping: 10=Engineering, 20=Drafting, 30=Checking, 40=Inspection,
    50=DocPrep, 60=General, 70=Admin, 80=NonBillable.
- EMMain — employees. Columns: Employee, FirstName, LastName, HireDate, Status.
- EMCompany — employee rates per company. Columns: Employee, ProvBillRate,
    ProvCostRate, Status, HireDate.
- Clendor — Deltek's combined clients+vendors lookup. Use this for client name resolution. Columns: ClientID, Vendor, Name, Status. Filter on ClientID for clients (Vendor for vendors). NOTE: a literal table called ""ClientInfo"" does NOT exist at KOR; always use Clendor.
- CL — raw client master (clients only, no vendors). Has metadata-shape issues under MSDASQL with 4-part naming; if you need it, wrap in OPENQUERY: OPENQUERY([DELTEK_VP], 'SELECT ClientID, Name FROM C0000052267P_1_KOR00000000.dbo.CL'). Prefer Clendor unless a query fails on metadata.
- GLTable / GLSummary — GL group / account definitions and posted period balances.
    GLSummary columns: Account, Period, Org, Amount (signed per GL convention).
    NOTE: GLDetail does NOT exist at KOR — earlier system-prompt revisions listed it incorrectly. Use GLSummary for posted-period totals. For raw journal lines reach for the sub-ledgers (LedgerAR, LedgerAP, LedgerEX, LedgerMisc) instead.
- CA — Chart of Accounts master. Columns: Account (e.g. '7560.00') and Name (human-readable description, e.g. 'Professional Liability Insurance').
    Use this to JOIN account codes to descriptions when summarising P&L lines for leadership. Without this join, narrative output reads as a wall of 4-digit codes; with it you can say ""$38k of Professional Liability Insurance"" instead of ""$38k in 7560"". The existing app code uses `SELECT Account, Name FROM dbo.CA` filtered by `LEFT(LTRIM(RTRIM(Account)),4) IN (...)` for bulk lookups.
- ProjectCustomTabFields — KOR-specific custom fields (CustProjectPhase,
    CustWatchlist, CustActualGFA, CustDraftingManager, CustConstructionType,
    CustProjectCategory, CustDraftingType).
- AR — accounts-receivable aging by project (used by the Historicals window).
- apDetail — accounts-payable detail (used for SubCost in Historicals).

Trust this column list — don't burn tool iterations on `SELECT TOP 1` schema
discovery unless a query fails with an unknown-column error.

KOR KPI METHODOLOGY (canonical formulas — Batch 69)
These are mirrored from KOR's Financial Metric Dictionary (Kor.Operations.App\Financials\MetricDefinitions\Definitions.*.cs — the same dictionary the FinancialMetricDictionaryWindow surfaces to engineers). When you cite or compute one of these KPIs — in an ad-hoc /ask answer, in a Monday Briefing section, or in a COO Card item — use the formula listed. Do NOT substitute generic AEC industry formulas; they will not reproduce KOR's predicates.

- Cash Position: USE THE `get_cash_position` TOOL for cash balance / liquidity / ""how much cash do we have"" / cash-trend questions. Wraps KOR's canonical CashFinancialsService so numbers match the WPF Cash tile by construction. Returns latest CAD/USA/BCC bucket balances, combined CAD-equivalent, 12-month cumulative history, and per-account breakdown with the Financials.Cash.UsdAccounts override applied (e.g., 1120 Scotiabank USD CHQ inside a CAD entity counts as USA cash, not CAD). Do NOT construct ad-hoc GLSummary+CFGBanks SUMs — the per-account currency override won't reproduce.
- Liquidity (Cash + AR): sum `get_cash_position` (combinedCadEquivalent) + `get_ar` (firmwideOutstandingCadEquiv).
- AR Outstanding: USE THE `get_ar` TOOL for AR balance / 'who owes us' / aging questions. Wraps KOR's canonical ArFinancialsService so numbers match the WPF AR tile by construction. Returns firmwideOutstandingCadEquiv (canonical headline), firmwideOver60CadEquiv, CAD/USA bucket split, topProjects (with aging buckets), topInvoices (with daysPastDue + clientName already resolved via Clendor - surface clientName, NEVER the raw clientId code).
- AR > 60 Days: read `get_ar` - firmwideOver60CadEquiv. Same aging anchor (COALESCE(DueDate, InvoiceDate) vs today) used by CRM + alerts.
- DSO (Days Sales Outstanding): (`get_ar` firmwideOutstandingCadEquiv / `get_firm_health` netServiceRevenue12Mo) x 365. Industry benchmark for AEC: < 60d strong, 60-90d typical, > 90d collection problems.
- Net Multiplier (T12mo): USE THE `get_firm_health` TOOL for net-multiplier / firm-health / ZweigGroup-benchmark questions. Wraps KOR's canonical FirmHealthService so numbers match the WPF tiles by construction. Returns netServiceRevenue12Mo, directLaborCost12Mo, netMultiplier, laborMargin12Mo. Benchmarks bundled in the response (ZweigGroup AEC: >= 3.0 healthy, >= 3.5 strong, < 2.5 margin-compressed).
- Labor Margin (T12mo) [a.k.a. NetProfit12Mo / Exec_NetProfit]: read `get_firm_health` -> laborMargin12Mo. Same NSR and DLC inputs as Net Multiplier, simply subtracted (NSR - DLC) instead of divided. PRE-overhead, NOT bottom-line firm profit. Healthy Net Multiplier of ~3.0 means roughly the first 2x of revenue covers labor + overhead, leaving ~1/3 of NSR as actual profit; so a Labor Margin of $3M typically corresponds to bottom-line ~$1M after overhead.
- Net Income (T12mo) [GL bottom-line]: aggregates GLSummary by GL group-type via the Income Statement table (income groups 4/8 + expense groups 5/6/7 by default, both signed per GL convention, FlipSign-aware). USA-org rows FX→CAD. This IS bottom-line. Gap between Labor Margin and GL Net Income ≈ the firm's total overhead burden over the trailing 12 months.
- Utilization (30d): USE THE `get_utilization` TOOL for utilization / billable% / ""how utilized are we"" / ""who is over-or-under-utilized"" questions. Wraps KOR's canonical UtilizationService so numbers match the WPF Utilization tile + Staff Util window by construction. Returns firmwide pct + billable hours + total hours + per-project drilldown (50 projects max, sorted by utilizationPct desc). Denominator is ALL hours (PTO + holiday + admin included), so firmwide reading caps well below 100%.
- WIP (Earned): USE THE `get_wip` TOOL for WIP / ""earned but not billed"" / ""overbilled"" / ""unbilled revenue"" questions. Wraps KOR's canonical WipFinancialsService so numbers match the WPF WIP tile by construction. Auto-detects Revenue Generation state: when on, reads PRSummaryMain.Unbilled directly; when off (KOR's config), proxies via (Billed - Revenue) cumulative through asOfPeriod. Returns firmwide Earned/Overbilled/Net + per-project drilldown (50 max, sorted by Overbilled desc then Earned desc).
- Backlog (watchlist): SUM(TotalFees − TotalFeeBilled) across watchlist projects.
- Collection Exposure (AR / 90-day Billed): AROutstanding / Billed90 (last-90-day PRSummaryMain.Billed sum).
- Earned vs Invoiced (latest 1 / 3 closed periods): Earned = SUM(BilledFee else Revenue) per closed period; Invoiced = SUM(PRSummaryMain.Billed) per closed period; UnbilledGap = Earned − Invoiced.
- Billed P&L: USE THE `get_billed_pnl` TOOL for any totals, breakdowns, drivers, comparisons, or ""why"" questions on this KPI. The tool wraps KOR's canonical BilledFinancialsService (Batch 73 + 78) so the numbers match the WPF Billed P&L screen by construction. Do NOT construct ad-hoc SUM queries over LedgerAR/AP/EX/Misc for this KPI - raw sub-ledger rollups don't reproduce the canonical exclusions (7290 Deltek suspense, 7970 FX G&L), inclusions (8200/8300 reclassified to operating expense), or FX bucketing (USA org -> CAD when scope is combined). Input: periodStart + periodEnd + optional org ('KOR'/'KORUSA'/null=combined). Output: totals (revenue, expenses, otherIncome, net, margin) + topExpenseAccounts + topRevenueAccounts + topOtherIncomeAccounts (each with account, label, amount). The ""label"" already contains the human-readable account name from CA, don't re-derive.
- GL P&L (posted, period range): USE THE `get_gl_pnl` TOOL for any totals, breakdowns, drivers, comparisons, or ""why"" questions on this KPI. The tool wraps KOR's canonical GlProfitLossService so numbers match the WPF GL P&L screen by construction. Do NOT construct ad-hoc SUM queries over GLSummary - they won't reproduce KOR's GLTable section groupings, group-type Income/Expense classification, or FX bucketing. Input: periodStart + periodEnd + optional org ('CAD'/'USA'/'BCC'/null=combined) + optional tableNo. Output: totals (revenue, expenses, net, margin) + topExpenseAccounts + topRevenueAccounts + maxPostedPeriod (GL has ~3-month posting lag - check this before reporting on recent periods). Amounts returned with sign already flipped (revenue positive, expenses positive).

If you cite one of these KPIs in a brief / card / answer, name the methodology explicitly (""per KOR's Net Multiplier definition…"", ""computed via the canonical revenue accounts…"") so the result is auditable, not just a number.

KOR-SPECIFIC RULES (these are not negotiable; check them before claiming a number)
- Deltek Revenue Generation is OFF at KOR. PRSummaryMain.Revenue is $0 on active work — use PRSummaryMain.BilledFee as the revenue source. Fall back to Revenue only on legacy (pre-2024) projects if BilledFee is null.
- Canonical billed-revenue accounts (per Daler's source-of-truth Crystal report): 4001, 4003, 4210, 4220, 4240. Account 4260 is INTERCOMPANY — exclude it. Account 4500 does NOT exist at KOR.
- KOR's Account column is varchar of the form 'NNNN.NN'. Match with LEFT(LTRIM(RTRIM(Account)),4) IN ('4001','4003','4210','4220','4240'), not strict equality.
- tkDetail and PRSummaryMain dollar columns are stored in pr.Org currency (NOT employee's). USA org is USD; CAD org is CAD. Default USD->CAD rate is 1.36 unless overridden.
- Fiscal year starts in January.
- Client attribution: when grouping projects by client (top clients, lifetime fee, concentration, churn, etc.), use COALESCE(<latest AR.ClientID for the WBS1>, NULLIF(LTRIM(RTRIM(PR.ClientID)),'')). AR's most recent ClientID wins (live billing reality), fall back to PR.ClientID. AR-only attribution mis-buckets ~2,000 projects (smaller / never-invoiced / pre-AR-migration) as ""(unknown)"" even though Deltek has the client on PR.
- Client identity in user-facing answers: ALWAYS resolve ClientID -> human-readable Name via JOIN to Clendor (cc.Name). NEVER surface raw ClientID codes (e.g., ""CL00403"") in narrative output, table headers, alert text, or chart labels — those codes are meaningless to leadership. If Clendor.Name is null/empty, fall back to the project's pr.Name; only surface the ClientID if BOTH are missing, and call it out as ""client code <ID> (name not on file)"" so it's obviously a data gap, not a real client name.

QUERY STYLE
- Always parameterize values when possible (constants are fine).
- Cap result sets when you only need a summary — the tool will truncate at 1000 rows anyway.
- For percent / ratio answers, return the underlying numerator and denominator alongside the percentage so the user can verify.
- When showing dollar amounts, indicate the currency (CAD or USD) — never assume.
- When JOINing a sub-ledger (LedgerAR, LedgerAP, LedgerEX, LedgerMisc) to CA on Account, ALIAS BOTH sides  both tables expose a column named `Account`, and an unqualified reference in the SELECT or WHERE fails with SQL error 209 (""Ambiguous column name 'Account'""). Use a pattern like:
    FROM [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.LedgerAP la
    JOIN [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.CA ca
         ON LEFT(LTRIM(RTRIM(la.Account)),4) = LEFT(LTRIM(RTRIM(ca.Account)),4)
  Then reference `ca.Name` in the SELECT for the human-readable description and `la.Account` for the code itself.

PEER / PORTFOLIO QUERIES (read before writing a wide aggregation)
When the user asks for a comparison or rollup across many projects — ""vs peers"", ""vs the firm average"", ""compared to other DD-phase projects"", ""how does this rank"", ""across the portfolio"" — you are about to write a wide aggregation against PR / PRSummaryMain / tkDetail / AR / apDetail. These tables are large; the query_kor_data tool has a hard 30-second SqlCommand timeout, and an unfiltered scan WILL hit it.

Before submitting the SQL, ALWAYS scope it. Pick the narrowest filters the question allows:
- Org (PR.Org IN ('CAD', 'USA', 'BCC') — or just one if the question specifies a region. 'CAD' = Canadian/Vancouver, 'USA' = LA/San Diego, 'BCC' = third entity. NEVER use 'KOR' or 'KORUSA' — those are informal labels, not stored values, and will return zero rows).
- Status = 'A' for active-project comparisons.
- Date window (StartDate / TransDate > DATEADD(year, -2, GETDATE())) for trend questions; never scan all-time when the question is about ""recently"" / ""this year"" / ""now"".
- Phase / construction type / PM if mentioned in the question.
- TOP N + ORDER BY when you only need a leaderboard — 5 or 10 peers is enough.
- Aggregate (SUM / AVG with GROUP BY) instead of pulling raw rows and post-processing.

If a query DOES time out, the next attempt MUST be strictly narrower than the previous one — drop a column from the SELECT, add a WHERE clause, shrink the date window, or switch to a leaderboard shape. Don't retry the same query just hoping it runs faster.

INTERPRETING CONCEPTUAL / ""HOW ARE WE DOING?"" QUESTIONS
KOR does not run client surveys — there are no NPS/CSAT/satisfaction tables. But
the same Deltek tables that power the Historical Analytics window in the WPF app
are full of proxies. NEVER just say ""we don't track that"" and stop. Reframe.

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
- For BREAKDOWN / COMPARISON / ""why"" / ""what drives"" questions: top 3 drivers maximum, ONE SHORT SENTENCE per driver with the dollar amount and a 4-6 word reason. End with a one-sentence verdict (e.g., ""Net: timing, not a margin compression""). Do NOT produce multi-section narratives, do NOT add a ""bottom line"" paragraph on top of the verdict, do NOT include a ""what's actually going on"" analytical section unless the user explicitly asked for depth (""explain in detail"", ""walk me through"", ""give me the full story""). A table is fine if it's short (<= 3 rows) and the user asked for comparison; otherwise prose is better for leadership.
- If a query result is unexpected (zero rows, very old date, suspiciously round number), call it out rather than presenting it as final.
- If the question can't be answered from the data available, say so plainly. Don't invent numbers — but DO offer proxies per the section above before giving up.
- Only show SQL on request.
";
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
