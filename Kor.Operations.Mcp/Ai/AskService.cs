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

    // One in-flight question per user. A shared semaphore-per-key keeps a
    // single user from firing 5 questions and starving the others' quota.
    // Keyed by UserUpn (server-set from the X-Kor-User-Upn header), or
    // "anonymous" when no UPN was supplied.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public AskService(
        IOptions<McpOptions> options,
        QueryKorDataTool queryTool,
        AuditLogger audit,
        IHttpClientFactory httpFactory,
        ILogger<AskService> logger)
    {
        _options = options;
        _queryTool = queryTool;
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

        // Tool catalog exposed to the LLM. Just one tool today: SQL query.
        var toolDefs = new[]
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

Reach for query_kor_data ONLY when:
  - The user asks for raw values not in [CURRENTLY VIEWING] (e.g., ""list the 10 projects driving that number"", ""show me by PM"").
  - The user explicitly says ""verify"" / ""double-check"" / ""show me the SQL"".
  - The question is about data the screen doesn't show (historical trend, cross-screen comparison, ad-hoc filter the UI doesn't expose).

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
- GLTable / GLDetail / GLSummary — chart of accounts, journal lines, period balances.
    GLDetail columns include: Account, Period, Amount, Org, TransDate, Project.
- ProjectCustomTabFields — KOR-specific custom fields (CustProjectPhase,
    CustWatchlist, CustActualGFA, CustDraftingManager, CustConstructionType,
    CustProjectCategory, CustDraftingType).
- AR — accounts-receivable aging by project (used by the Historicals window).
- apDetail — accounts-payable detail (used for SubCost in Historicals).

Trust this column list — don't burn tool iterations on `SELECT TOP 1` schema
discovery unless a query fails with an unknown-column error.

KOR KPI METHODOLOGY (canonical formulas — Batch 69)
These are mirrored from KOR's Financial Metric Dictionary (Kor.Operations.App\Financials\MetricDefinitions\Definitions.*.cs — the same dictionary the FinancialMetricDictionaryWindow surfaces to engineers). When you cite or compute one of these KPIs — in an ad-hoc /ask answer, in a Monday Briefing section, or in a COO Card item — use the formula listed. Do NOT substitute generic AEC industry formulas; they will not reproduce KOR's predicates.

- Cash Position: maps CFGBanks accounts to GLSummary; sum balances by company (CAD / USA / BCC) at the latest period.
- Liquidity (Cash + AR): Cash Position (CAD-equivalent) + SUM(AR.InvBalanceSourceCurrency) firmwide.
- AR Outstanding: SUM(AR.InvBalanceSourceCurrency) firmwide. The breakdown table is firmwide regardless of the active Scope toggle.
- AR > 60 Days: SUM(InvBalance) where COALESCE(DueDate, InvoiceDate) <= Today − 60.
- DSO (Days Sales Outstanding): (ArFirmwideOutstandingCadEquiv / NetServiceRevenue_T12mo) × 365. Industry benchmark for AEC: < 60d strong, 60–90d typical, > 90d collection problems.
- Net Multiplier (T12mo): SUM(-LedgerAR.Amount where TransType='IN' and Account prefix ∈ {4001,4003,4210,4220,4240}, FX→CAD, trailing 12 months) / SUM(tkDetail.RegAmt + OvtAmt + SpecialOvtAmt where LaborCode ∈ {10,20,30,40,50,60} on direct projects, excluding overhead WBS1 prefixes 99%/9[A-Z]%/[A-Z]%, trailing 12 months). ZweigGroup benchmarks: ≥ 3.0 healthy, ≥ 3.5 strong, < 2.5 margin-compressed.
- Labor Margin (T12mo) [a.k.a. Exec_NetProfit]: same NSR and DLC inputs as Net Multiplier, simply subtracted (NSR − DLC) instead of divided. This is PRE-overhead — NOT bottom-line firm profit. A healthy Net Multiplier of ~3.0 means roughly the first 2× of revenue covers labor + overhead, leaving ~1/3 of NSR as actual profit; so a Labor Margin of $3M typically corresponds to bottom-line ~$1M after overhead.
- Net Income (T12mo) [GL bottom-line]: aggregates GLSummary by GL group-type via the Income Statement table (income groups 4/8 + expense groups 5/6/7 by default, both signed per GL convention, FlipSign-aware). USA-org rows FX→CAD. This IS bottom-line. Gap between Labor Margin and GL Net Income ≈ the firm's total overhead burden over the trailing 12 months.
- Utilization (30d): SUM(hours where LaborCode NOT IN (70 Admin, 80 NonBillable), WBS1 NOT overhead, LineItemApprovalStatus <> 'R') / SUM(all hours), last 30 days from tkDetail.
- WIP (Earned, watchlist): if PRSummaryMain.Unbilled is populated, use it directly; otherwise proxy by Diff = Revenue − Billed per period. Earned = SUM(max(Diff, 0)); Overbilled = SUM(max(−Diff, 0)); Net = Earned − Overbilled.
- Backlog (watchlist): SUM(TotalFees − TotalFeeBilled) across watchlist projects.
- Collection Exposure (AR / 90-day Billed): AROutstanding / Billed90 (last-90-day PRSummaryMain.Billed sum).
- Earned vs Invoiced (latest 1 / 3 closed periods): Earned = SUM(BilledFee else Revenue) per closed period; Invoiced = SUM(PRSummaryMain.Billed) per closed period; UnbilledGap = Earned − Invoiced.
- Billed P&L: Revenue = LedgerAR signed-inverted (canonical accounts {4001,4003,4210,4220,4240}, exclude 4260 intercompany, FX→CAD); Expenses = SUM(LedgerAP+LedgerEX+LedgerMisc) over 5xxx+6xxx+7xxx+8200+8300, EXCLUDING 7290 (Deltek suspense — pre-posting accrual bucket that never reaches GLSummary; verified Feb 2026 with $111K in sub-ledger / $0 in GL) and EXCLUDING 7970 (Realized/Unrealized G&L — non-operating, routed to Other Income); Net = Revenue − Expenses; Margin = Net / Revenue.
- GL P&L (posted, period range): Revenue = SUM(GLSummary.Amount where account-in-income-group); Expenses = SUM(GLSummary.Amount where account-in-expense-group); Net = Revenue + Expenses (both signed per GL convention); Margin = Net / Revenue.

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

PEER / PORTFOLIO QUERIES (read before writing a wide aggregation)
When the user asks for a comparison or rollup across many projects — ""vs peers"", ""vs the firm average"", ""compared to other DD-phase projects"", ""how does this rank"", ""across the portfolio"" — you are about to write a wide aggregation against PR / PRSummaryMain / tkDetail / AR / apDetail. These tables are large; the query_kor_data tool has a hard 30-second SqlCommand timeout, and an unfiltered scan WILL hit it.

Before submitting the SQL, ALWAYS scope it. Pick the narrowest filters the question allows:
- Org (PR.Org IN ('KOR', 'KORUSA') — or just one of them if the question specifies a region / office).
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
