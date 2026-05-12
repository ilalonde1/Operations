#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    /// <summary>
    /// Business Development concepts (CRM engagements, Client Intelligence
    /// flags, Opportunities lifecycle + scoring). These aren't financial
    /// ratios — they're funnel/lifecycle/classification semantics — but they
    /// follow the same dictionary shape so the AI bar can cite them via the
    /// same TryGetAiMethodology / BuildAiMethodologyBlock helpers used by
    /// the financial windows.
    /// </summary>
    private static void AddBdMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // ── CRM ──────────────────────────────────────────────────────────

        d["Bd_EngagementStage"] = new FinancialMetricDefinition
        {
            Key = "Bd_EngagementStage", Category = "BD",
            DisplayName = "CRM Engagement Stage",
            Description =
                "WHAT:\n" +
                "Lifecycle stage of a CRM pursuit, one of 9 discrete values: Pursuing, ProposalDraft, ProposalSubmitted, Presenting, Negotiating, Won, Lost, Withdrawn, OnHold.\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives the funnel breakdown on the CRM window and the win-rate denominator (Won + Lost = resolved set).\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Set manually by the engagement owner as the pursuit progresses. Stored on CrmEngagement.Stage. Won / Lost / Withdrawn / OnHold are terminal-ish — only Won and Lost feed win rate; Withdrawn and OnHold are excluded.",
            Formula = "Stage ∈ {Pursuing, ProposalDraft, ProposalSubmitted, Presenting, Negotiating, Won, Lost, Withdrawn, OnHold}"
        };

        d["Bd_WinRate"] = new FinancialMetricDefinition
        {
            Key = "Bd_WinRate", Category = "BD",
            DisplayName = "CRM Win Rate (trailing)",
            Description =
                "WHAT:\n" +
                "Share of resolved pursuits that were won.\n\n" +
                "WHY IT MATTERS:\n" +
                "Headline measure of BD productivity. Compared across buyer types it tells you where KOR's pitch lands and where it doesn't.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Won / (Won + Lost). Withdrawn and OnHold engagements are excluded from both numerator and denominator — they aren't a win/loss outcome.",
            Formula = "WinRate = Won / (Won + Lost)"
        };

        d["Bd_PursuitDuration"] = new FinancialMetricDefinition
        {
            Key = "Bd_PursuitDuration", Category = "BD",
            DisplayName = "Avg Pursuit Duration",
            Description =
                "WHAT:\n" +
                "Average elapsed time from engagement opening to terminal-status resolution, in days.\n\n" +
                "WHY IT MATTERS:\n" +
                "Long pursuit cycles tie up BD bandwidth and signal qualification problems. Combined with WinRate, gives the cost-per-win shape.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "AVG(ClosedAtUtc − OpenedAtUtc) across engagements with a resolved (Won/Lost) terminal status. Withdrawn / OnHold / still-open engagements are excluded so the average isn't pulled by abandoned pursuits.",
            Formula = "AvgPursuitDuration = AVG(ClosedAtUtc − OpenedAtUtc) over resolved (Won|Lost) engagements"
        };

        // ── Client Intelligence ─────────────────────────────────────────

        d["Bd_PriorWork"] = new FinancialMetricDefinition
        {
            Key = "Bd_PriorWork", Category = "BD",
            DisplayName = "Client Flag: Prior Work",
            Description =
                "WHAT:\n" +
                "Binary flag on the KOR client master indicating KOR has done paid work for this client before.\n\n" +
                "WHY IT MATTERS:\n" +
                "Prior-work clients are easier wins (existing relationship, known scope, less estimation risk). BD prioritizes opportunities where this flag is set.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Set manually in the KOR client metadata window. Independent of Deltek's project history — it's an editorial flag, not a project-count derivation.",
            Formula = "(editorial flag — set in the KOR client metadata UI)"
        };

        d["Bd_RecommendFlag"] = new FinancialMetricDefinition
        {
            Key = "Bd_RecommendFlag", Category = "BD",
            DisplayName = "Client Flag: Recommend",
            Description =
                "WHAT:\n" +
                "Binary editorial flag indicating leadership recommends this client for future pursuit.\n\n" +
                "WHY IT MATTERS:\n" +
                "Distinguishes 'has prior work' (factual) from 'we want more work with them' (judgment). A client may have prior work but be on the do-not-pursue list (slow payer, scope-creep history, etc).",
            Formula = "(editorial flag — set in the KOR client metadata UI)"
        };

        d["Bd_GovernmentAgency"] = new FinancialMetricDefinition
        {
            Key = "Bd_GovernmentAgency", Category = "BD",
            DisplayName = "Client Flag: Government Agency",
            Description =
                "WHAT:\n" +
                "Binary flag indicating the client is a government agency (municipal / provincial / federal / institutional).\n\n" +
                "WHY IT MATTERS:\n" +
                "Government work has different procurement dynamics (RFP-driven, longer cycles, slower payment) — affects pursuit qualification and proposal positioning. Used as a scoring input on the Opportunities side.",
            Formula = "(editorial flag — set in the KOR client metadata UI)"
        };

        d["Bd_CompetitorFlag"] = new FinancialMetricDefinition
        {
            Key = "Bd_CompetitorFlag", Category = "BD",
            DisplayName = "Client Flag: Competitor",
            Description =
                "WHAT:\n" +
                "Binary flag indicating the entity is a competitor (another structural engineering firm), not a buyer.\n\n" +
                "WHY IT MATTERS:\n" +
                "Surfaces if a CRM record actually represents a competitor — those should NOT be in the pursuit funnel. BD uses this flag to suppress false positives in the Opportunities feed.",
            Formula = "(editorial flag — set in the KOR client metadata UI)"
        };

        d["Bd_LifetimeFee"] = new FinancialMetricDefinition
        {
            Key = "Bd_LifetimeFee", Category = "BD",
            DisplayName = "Client Lifetime Fee",
            Description =
                "WHAT:\n" +
                "Sum of fee across all Deltek projects ever linked to this client (active + closed), in the client's reporting currency.\n\n" +
                "WHY IT MATTERS:\n" +
                "Concentration / loyalty signal. Combined with project count gives a quick read of relationship depth and breadth.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(PRSummaryMain.BilledFee) GROUP BY WBS1 then SUM by ClientID. Uses the same Earned definition as Financials (BilledFee with legacy Revenue fallback). Cross-currency clients are summed in pr.Org currency without conversion in this view.",
            Formula = "LifetimeFee = SUM(BilledFee) for all WBS1 with ClientID = X"
        };

        // ── Opportunities ───────────────────────────────────────────────

        d["Bd_OpportunityStatus"] = new FinancialMetricDefinition
        {
            Key = "Bd_OpportunityStatus", Category = "BD",
            DisplayName = "Opportunity Status",
            Description =
                "WHAT:\n" +
                "Pursuit-lifecycle status for an ingested opportunity, one of 9 values: Identified (1), Reviewing (2), Qualified (3), Pursuing (4), ProposalSubmitted (5), Won (6), Lost (7), NoBid (8), Withdrawn (9).\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives the funnel breakdown on the Opportunities window. Identified→Qualified is the triage stage; Pursuing→ProposalSubmitted is the active-bid stage; Won/Lost/NoBid/Withdrawn are terminal.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Stored as int on Opportunities.Status. Set automatically by the ingestor (default Identified=1) and advanced manually by BD as the pursuit progresses.",
            Formula = "Status ∈ {Identified=1, Reviewing=2, Qualified=3, Pursuing=4, ProposalSubmitted=5, Won=6, Lost=7, NoBid=8, Withdrawn=9}"
        };

        d["Bd_RelevanceScore"] = new FinancialMetricDefinition
        {
            Key = "Bd_RelevanceScore", Category = "BD",
            DisplayName = "Opportunity Relevance Score",
            Description =
                "WHAT:\n" +
                "Numeric score from the rules-based scoring engine indicating how well an ingested opportunity fits KOR's profile.\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives triage. High scores surface to the top of the queue; HardReject scores are hidden by default.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "RuleBasedOpportunityScoringService composes searchable text from name, buyer, address, construction type, project category, then: applies hard-reject paths (value below threshold OR country-keyword match) → returns HardRejectScore tier. Otherwise sums positive-term weights and subtracts negative-term weights from ScoringOptions. The numeric score maps to a tier via Low/Medium/High thresholds (also in ScoringOptions). Every weight + threshold is admin-tunable — no in-source term arrays.",
            Formula = "Score = Σ positive_term_weights − Σ negative_term_weights; with hard-reject short-circuits for sub-threshold value or country-keyword matches"
        };

        d["Bd_RelevanceTier"] = new FinancialMetricDefinition
        {
            Key = "Bd_RelevanceTier", Category = "BD",
            DisplayName = "Opportunity Relevance Tier",
            Description =
                "WHAT:\n" +
                "Categorical bucket derived from RelevanceScore — one of HardReject (0), Low (1), Medium (2), High (3).\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives UI filtering (HardReject is distinct from Low so the UI can hide vs de-emphasize) and tells BD which opportunities deserve a human read.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Output of the scoring service alongside the numeric score: hard-reject paths return HardReject directly; otherwise Score is bucketed by Low/Medium/High thresholds from ScoringOptions.",
            Formula = "Tier ∈ {HardReject=0, Low=1, Medium=2, High=3} — bucketed from Bd_RelevanceScore"
        };

        d["Bd_OpportunityDiscipline"] = new FinancialMetricDefinition
        {
            Key = "Bd_OpportunityDiscipline", Category = "BD",
            DisplayName = "Opportunity Discipline",
            Description =
                "WHAT:\n" +
                "Classification of the opportunity's engineering scope: Unknown (0), Structural (1), Inspections (2), Mixed (3), OutOfScope (99).\n\n" +
                "WHY IT MATTERS:\n" +
                "KOR is a structural engineering firm. Pure Structural and Mixed are in-scope; Inspections is a side service; OutOfScope is auto-suppressed from pursuit. Used as a scoring + filter input.",
            Formula = "Discipline ∈ {Unknown=0, Structural=1, Inspections=2, Mixed=3, OutOfScope=99}"
        };

        d["Bd_BuyerType"] = new FinancialMetricDefinition
        {
            Key = "Bd_BuyerType", Category = "BD",
            DisplayName = "Buyer Type",
            Description =
                "WHAT:\n" +
                "Buyer classification used for filtering, scoring, and win-rate breakdown: Unknown (0), Municipal (1), Provincial (2), Federal (3), Private (4), InstitutionalEducation (5), InstitutionalHealthcare (6), NonProfit (7), Other (99).\n\n" +
                "WHY IT MATTERS:\n" +
                "Different buyer types have radically different procurement / win-rate profiles. Surfacing the breakdown lets BD see where KOR's pitch actually lands (per the Analytics.ByBuyerType view in CRM).",
            Formula = "BuyerType ∈ {Unknown=0, Municipal=1, Provincial=2, Federal=3, Private=4, InstitutionalEducation=5, InstitutionalHealthcare=6, NonProfit=7, Other=99}"
        };

        d["Bd_IngestionRun"] = new FinancialMetricDefinition
        {
            Key = "Bd_IngestionRun", Category = "BD",
            DisplayName = "Opportunity Ingestion Run",
            Description =
                "WHAT:\n" +
                "A single execution of the Opportunities Worker pulling from one provider (e.g. BCBid, CanadaBuys), with per-run counts: InsertedCount (new opportunities), DuplicateCount (de-duped on OpportunityKey), SkippedCount (filtered by hard-reject/discipline guards), FailedCount (parser/network errors), Success flag, ErrorSummary.\n\n" +
                "WHY IT MATTERS:\n" +
                "Operational health: if Inserted goes to zero across consecutive runs, the source has gone stale or the Worker is broken. The HeartbeatBrush / HeartbeatHealth on the Opportunities window is computed from the freshness of the latest run.",
            Formula = "IngestionRun = { Provider, StartedAtUtc, EndedAtUtc, InsertedCount, DuplicateCount, SkippedCount, FailedCount, Success, ErrorSummary }"
        };
    }
}
