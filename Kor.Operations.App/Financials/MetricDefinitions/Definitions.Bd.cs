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
                "Lifecycle stage of a CRM pursuit, one of 4 discrete values: Drafting, Submitted, Won, Lost.\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives the funnel breakdown on the Pursuits window and the win-rate denominator (Won + Lost = resolved set).\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Set manually by the engagement owner as the pursuit progresses. Stored on CrmEngagement.Stage. Won and Lost are terminal and feed win rate.",
            Formula = "Stage ∈ {Drafting=1, Submitted=3, Won=6, Lost=7}"
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
                "Won / (Won + Lost). Drafting and Submitted engagements are excluded from both numerator and denominator — they aren't yet a win/loss outcome.",
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
                "AVG(ClosedAtUtc − OpenedAtUtc) across engagements with a resolved (Won/Lost) terminal status. Drafting / Submitted (still-open) engagements are excluded so the average isn't pulled by in-flight pursuits.",
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
                "Pursuit-lifecycle status for an ingested opportunity, one of 5 values: New (1), Pursuing (4), Submitted (5), Won (6), Lost (7).\n\n" +
                "WHY IT MATTERS:\n" +
                "Drives the funnel breakdown on the Opportunities window. New is the triage state; Pursuing→Submitted is the active-bid stage; Won/Lost are terminal. NoBid and Withdrawn distinctions survive in WonLostOutcome for retrospective reporting.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Stored as int on Opportunities.Status. Set automatically by the ingestor (default New=1) and advanced manually by BD as the pursuit progresses.",
            Formula = "Status ∈ {New=1, Pursuing=4, Submitted=5, Won=6, Lost=7}"
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
                "RuleBasedOpportunityScoringService composes searchable text from name, buyer, address, city, province, construction type, project category, and outcome reason, then:\n" +
                "  (1) Hard-reject short-circuits: EstimatedValue below MinimumValueThresholdCad, OR any match against HardRejectCountryTerms, returns HardRejectScore.\n" +
                "  (2) Sum positive-term weights (PositiveTermWeights) minus negative-term weights (NegativeTermWeights).\n" +
                "  (3) Add region-term weights (RegionTermWeights). Region weights are pre-signed: in-footprint cities are positive, out-of-scope provinces/states are negative.\n" +
                "  (4) Deadline modifier (non-terminal status only): DeadlineCrunchPenalty if days-to-deadline < DeadlineWarningWindowDays; DeadlineFeasibleBonus if days-to-deadline >= window.\n" +
                "  (5) Deltek-linked bonuses when DeltekClientId is set: RepeatDeveloperBonus always; plus (gated on HasAnyHistory) PriorWorkBonus, RecommendBonus, and LifetimeFeeBonus (the latter additionally gated on LifetimeFeeBonusThresholdCad).\n" +
                "  (6) Clamp to [MinScore, MaxScore] and round to 4 decimals (AwayFromZero).\n" +
                "Every weight, threshold, and bonus is admin-tunable in ScoringOptions — no in-source term arrays.",
            Formula = "Score = Σ positive − Σ negative + Σ region (signed) + deadline_modifier + Σ deltek_bonuses; clamped to [MinScore, MaxScore], rounded to 4dp; hard-reject short-circuits to HardRejectScore."
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

        // ── BD Dashboard tiles ──────────────────────────────────────────
        // Round 52: every tile / list / badge on the BD Dashboard gets a
        // definition the user can hover for. The funnel hero (top row) is
        // narrowed to KOR's target sector basket — schools / hospitals /
        // health / recreation / civic / cultural / university / college /
        // library / community / education / housing / institution / care /
        // fire / police plus a small set of explicit values — so the three
        // counts agree on the same denominator.

        d["BdDashboard_OpenSeats"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_OpenSeats", Category = "BD",
            DisplayName = "Funnel — Open Seats",
            Description =
                "WHAT:\n" +
                "Count of Major Projects Inventory (MPI) projects in KOR's institutional / civic sector basket where the structural seat is 'likely-open' — i.e. no competitor is locked in and the architect's structural partner relationship is rotating or undeclared.\n\n" +
                "WHY IT MATTERS:\n" +
                "These are the highest-probability pursuit targets on the board today: the project is real (in the public MPI), in a sector KOR pursues, and the structural slot is reachable.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "MajorProjectsInventory rows where Sector matches the institutional/civic basket (schools, hospitals, health, recreation, civic, cultural, universities, colleges, libraries, community, education, housing, institutions, care, fire, police) AND SeatStatus = 'likely-open' AND RetiredAtUtc IS NULL.",
            Formula = "OpenSeats = COUNT MPI rows WHERE Sector ∈ institutional-basket AND SeatStatus = 'likely-open' AND not retired"
        };

        d["BdDashboard_InBidWindow"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_InBidWindow", Category = "BD",
            DisplayName = "Funnel — In Bid Window",
            Description =
                "WHAT:\n" +
                "Count of MPI projects (same institutional/civic basket) currently in or near procurement — i.e. their Stage matches procurement / RFP / RFQ / tender / design.\n\n" +
                "WHY IT MATTERS:\n" +
                "These are the projects the procurement clock is on. Forward Pipeline (early planning) flows into In Bid Window, which flows out into the Latest RFPs feed once the actual RFP is posted.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "MajorProjectsInventory rows in the same institutional basket where Stage LIKE '%procure%' OR '%RFP%' OR '%RFQ%' OR '%tender%' OR '%design%'. RetiredAtUtc IS NULL.",
            Formula = "InBidWindow = COUNT MPI rows WHERE Sector ∈ basket AND Stage matches procurement/RFP/RFQ/tender/design"
        };

        d["BdDashboard_Radar"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_Radar", Category = "BD",
            DisplayName = "Funnel — Radar",
            Description =
                "WHAT:\n" +
                "Total count of MPI projects in KOR's institutional / civic sector basket. The widest cut on the funnel — everything worth knowing about, regardless of seat status or stage.\n\n" +
                "WHY IT MATTERS:\n" +
                "Radar is the denominator the other two funnel numbers narrow from: Radar → In Bid Window → Open Seats. If Radar shrinks, ingestion is broken or the basket is too tight.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "COUNT(*) of MajorProjectsInventory rows where Sector ∈ institutional-basket AND RetiredAtUtc IS NULL.",
            Formula = "Radar = COUNT MPI rows WHERE Sector ∈ institutional-basket AND not retired"
        };

        d["BdDashboard_LatestRfps"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_LatestRfps", Category = "BD",
            DisplayName = "Latest RFPs",
            Description =
                "WHAT:\n" +
                "The 15 most recently ingested rows from the live RFP feed (Opportunities table) — i.e. live, posted, actively-procuring solicitations from CanadaBuys, BCBid, Bonfire/Euna tenants, APC, SAM.gov, GraphEmail subscriptions, and the other Worker providers.\n\n" +
                "WHY IT MATTERS:\n" +
                "This is the live action queue — RFPs/RFQs with deadlines, where a proposal could be submitted today. Distinct from Forward Pipeline (long-horizon planning, NOT yet at procurement).\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "ORDER BY Opportunity.CreatedAtUtc DESC TAKE 15. Closed / Won / Lost opportunities are excluded. Double-click a row to open the Opportunity entry dialog.",
            Formula = "LatestRfps = TOP 15 Opportunities ORDER BY CreatedAtUtc DESC WHERE not closed"
        };

        d["BdDashboard_ForwardPipeline"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_ForwardPipeline", Category = "BD",
            DisplayName = "Forward Pipeline",
            Description =
                "WHAT:\n" +
                "Top 12 Major Projects Inventory projects in early planning stages ('CapitalPlan' or 'FacilityRenewal'), ranked by estimated cost. These are projects the buyer has publicly committed to but has NOT yet posted an RFP/RFQ for.\n\n" +
                "WHY IT MATTERS:\n" +
                "Forward Pipeline is the long-horizon (multi-year) positioning intel — buyers, architects, and structural seats KOR should be having conversations about before the procurement clock starts. The opposite of Latest RFPs.\n\n" +
                "HOW IS IT DIFFERENT FROM LATEST RFPs?\n" +
                "Latest RFPs = LIVE, today, action-now (Opportunities table — what the Worker ingested in the last few hours).\n" +
                "Forward Pipeline = PLANNED, not-yet-procured, action-this-year (MajorProjectsInventory — capital-plan and facility-renewal stages).\n" +
                "A Forward Pipeline project should ideally appear in Latest RFPs once it actually hits procurement.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "TOP 12 MajorProjectsInventory rows WHERE ProjectStage IN ('CapitalPlan', 'FacilityRenewal') AND RetiredAtUtc IS NULL ORDER BY EstimatedCostCad DESC.",
            Formula = "ForwardPipeline = TOP 12 MPI rows WHERE ProjectStage ∈ {CapitalPlan, FacilityRenewal} ORDER BY EstimatedCostCad DESC"
        };

        d["BdDashboard_OpenStructuralSeats"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_OpenStructuralSeats", Category = "BD",
            DisplayName = "Open Structural Seats (org list)",
            Description =
                "WHAT:\n" +
                "ORG-level list — architects in KOR's relationship graph whose structural-partner status is 'open' or 'rotating' AND whose KOR priority is 'high'. These are firms KOR should be courting for joint pursuit.\n\n" +
                "WHY IT MATTERS:\n" +
                "Distinct from the 'Open Seats' funnel count at the top of the page, which counts PROJECTS where a seat is reachable. This list answers 'which RELATIONSHIPS should we deepen' — architects where KOR has the strategic permission to displace an incumbent.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "CanonicalOrgEnrichment rows where ProviderName = 'StructuralPartnerMap' AND structuralPartnerStatus ∈ ('open','rotating') AND korPriority = 'high'. Joined to CanonicalOrg for the display name. Double-click to open the org dossier.",
            Formula = "OpenStructuralSeats = CanonicalOrgs WHERE StructuralPartnerMap.status ∈ {open, rotating} AND korPriority = 'high'"
        };

        d["BdDashboard_CompetitorWatch"] = new FinancialMetricDefinition
        {
            Key = "BdDashboard_CompetitorWatch", Category = "BD",
            DisplayName = "Competitor Watch",
            Description =
                "WHAT:\n" +
                "List of structural-engineering competitors with a capacity-read enrichment on file — e.g. 'fully booked through Q3', 'lost a senior PM, capacity gap', 'aggressive on BC institutional this cycle'.\n\n" +
                "WHY IT MATTERS:\n" +
                "Capacity reads tell BD which competitors are stretched (better win odds) vs which are leaning into a market (harder pursuit). Drives lane selection: where to push, where to wait.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "CanonicalOrgEnrichment rows where ProviderName = 'CompetitorSignals'. Joined to CanonicalOrg for the display name; the capacityRead JSON field renders alongside. Double-click to open the org dossier for full context.",
            Formula = "CompetitorWatch = CanonicalOrgs WHERE CompetitorSignals enrichment is present"
        };
    }
}
