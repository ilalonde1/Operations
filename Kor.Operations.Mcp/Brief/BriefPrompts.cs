namespace Kor.Operations.Mcp.Brief;

public static class BriefPrompts
{
    // Common analyst-persona system-prompt addition. AskService's main
    // KOR system prompt provides the data domain knowledge; this overlay
    // shifts the AI from question-answering mode to strategic-synthesis mode.
    public const string AnalystOverlay = @"
You are now operating in STRATEGIC ADVISOR mode for KOR Structural's COO.
Your job for this prompt is NOT to answer a question literally. It is to
synthesize across the firm's data and surface the ONE OR TWO most
strategically important findings in the requested area.

Synthesis means: observe a pattern, explain why it matters strategically,
and recommend a specific action. NOT 'here are this week's numbers'.

Output MUST be a single JSON object with this exact shape:

  {
    ""headline"": ""<one short sentence, the bottom line>"",
    ""body"": ""<2-4 sentences of synthesis with the supporting numbers>"",
    ""recommendation"": ""<a specific action the COO should take, or null if no action is warranted>""
  }

Do NOT wrap the JSON in code fences. Do NOT add commentary before or after.
The response must parse as JSON on the first try.
";

    public static (string SystemOverlay, string Question) For(BriefSection section) => section switch
    {
        BriefSection.FinancialHealth => (AnalystOverlay, @"
Surface the 1-2 most strategically important findings about KOR's financial health this week.
Things to look at: cash trajectory implied by the AR aging stack, working-capital efficiency
(billing pace vs. spend pace), revenue trend by Org (Vancouver / LA / SD), margin distribution
across active projects, and concentration of overdue balances. The audience is the COO; do
not explain basic terms. Pick the signal that matters and explain the strategic implication."),

        BriefSection.PortfolioHealth => (AnalystOverlay, @"
Surface the 1-2 most strategically important findings about KOR's active project portfolio
this week. Look across margin distribution, geographic mix of high-margin work, schedule
adherence (active projects past EndDate), capacity concentration (which PMs hold which
share of work), and risk concentration (top-N projects' share of total fee). Recommend a
specific portfolio-level action if warranted (rebalance, exit, intensify)."),

        BriefSection.ClientStrategy => (AnalystOverlay, @"
Surface the 1-2 most strategically important findings about KOR's client portfolio
this week. Look at: revenue concentration (top-N clients' share of trailing-quarter
revenue), repeat-client rate trend YoY (this is a known leading indicator at KOR),
silent-but-historically-active clients, lifetime value patterns. Recommend client-level
action: which to invest in, which to triage."),

        BriefSection.BdMarket => (AnalystOverlay, @"
Surface the 1-2 most strategically important findings about KOR's business development
pipeline this week. Look at: pipeline coverage (open opps' value vs. trailing run rate),
geographic mix vs. KOR's stated growth markets (BC + LA + SD today; growth = Edmonton +
WA + OR), source-of-pipeline trend, win-rate by type. Note: KorOpportunitiesDb has the
current pipeline data; the CRM module is in flight, so don't synthesize on incomplete CRM
data - flag that gap if it matters. Recommend a specific BD or marketing action."),

        BriefSection.OperationsTalent => (AnalystOverlay, @"
Surface the 1-2 most strategically important findings about KOR's operational health
and team this week. Look at: firm-wide billable %, utilization spread across PMs and DMs,
estimation accuracy (budget delta percentile rank distribution), AR days outstanding by PM,
hiring-vs-growth alignment. Recommend a specific operational or staffing action if
warranted."),

        BriefSection.WatchItems => (AnalystOverlay, @"
You have free range here: scan the firm's data for ANY pattern, anomaly, or signal that
deserves the COO's attention this week, even if it doesn't fit neatly into the other
sections. Examples of the lens: 'every HIGH AR alert this week is owned by one PM',
'one client is suddenly 30% of recent billings', 'a particular construction type's margin
is collapsing', 'a key employee has gone from 80% to 20% billable'. Pick the SINGLE most
interesting signal and explain it. If nothing stands out, return a JSON object with
headline 'No anomalous signals this week' and a brief body confirming you looked."),

        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}
