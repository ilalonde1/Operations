#nullable enable
namespace Kor.Opportunities.Worker.Options;

public sealed class OpportunitiesWorkerOptions
{
    /// <summary>SQL Server connection string for KorOpportunitiesDb.</summary>
    public string OpportunitiesDb { get; init; } = "";

    /// <summary>How often the Worker writes a heartbeat row (default 60s).</summary>
    public int HeartbeatSeconds { get; init; } = 60;

    /// <summary>
    /// Quartz cron expression for the scheduled CanadaBuys CSV pull.
    /// Default <c>0 0 0/2 * * ?</c> = every two hours, on the hour.
    /// </summary>
    public string CanadaBuysCronSchedule { get; init; } = "0 0 0/2 * * ?";

    /// <summary>SAM.gov API key (free public tier, 1000 req/day, rotates every 90 days).
    /// Set via KOR_OPPORTUNITIES_SAMGOVAPIKEY env var on the host. Empty disables the SAM.gov source.</summary>
    public string SamGovApiKey { get; init; } = "";

    /// <summary>Quartz cron for the SAM.gov pull. Default 0 0 6 * * ? = 06:00 daily,
    /// well inside the 1000 req/day quota.</summary>
    public string SamGovCronSchedule { get; init; } = "0 0 6 * * ?";

    /// <summary>How many days back to search SAM.gov on each pull. Default 30.
    /// SAM.gov allows up to 365 days per query; bumping past 90 risks paging-cap
    /// issues with our 5-page (5000 record) safety stop.</summary>
    public int SamGovPostedDaysLookback { get; init; } = 30;

    /// <summary>BCeID Business username for authenticated BC Bid scraping.
    /// Set via KOR_OPPORTUNITIES_BCBIDUSERNAME on the host. Empty disables
    /// the authenticated path - scraper falls back to anonymous (which gets
    /// captcha-gated to zero results).</summary>
    public string BcBidUsername { get; init; } = "";

    /// <summary>BCeID Business password for BC Bid scraping.
    /// Set via KOR_OPPORTUNITIES_BCBIDPASSWORD on the host. Stored in plaintext
    /// on KOR-APP01 (machine env var); same exposure pattern as SamGovApiKey
    /// and GraphEmailClientSecret. Future hardening: move to DPAPI or KeyVault.</summary>
    public string BcBidPassword { get; init; } = "";

    /// <summary>
    /// Rows of HistoricalOpportunities to enrich per scheduled job tick.
    /// Default 25. Each row takes ~5-7s (Playwright nav + extract + SQL write)
    /// so at 25 rows/tick + 5-minute cron the full 9,884-row archive backfills
    /// in about 5-6 hours.
    /// </summary>
    public int BcBidHistoricalEnrichmentBatchSize { get; set; } = 25;

    /// <summary>Max documents to download per scheduled tick. Default 20.</summary>
    public int BcBidHistoricalDocumentBatchSize { get; set; } = 20;

    /// <summary>Per-document max retry attempts before the row stops appearing in ListPending. Default 3.</summary>
    public int BcBidHistoricalDocumentMaxAttempts { get; set; } = 3;

    /// <summary>UNC or local path where document binaries are written. Default C:\OpsArchive\Opportunities local to KOR-APP01.</summary>
    public string BcBidHistoricalDocumentArchiveRoot { get; set; } = @"C:\OpsArchive\Opportunities";

    /// <summary>
    /// Kill switch for the Award agent-enrichment Quartz job. Defaults on; the
    /// job self-skips when Anthropic is not configured.
    /// </summary>
    public bool AwardAgentEnrichmentEnabled { get; set; } = true;

    /// <summary>Anthropic API key for agent enrichment. Defaults to KOR_ANTHROPIC_KEY env var.</summary>
    public string AnthropicApiKey { get; set; } = "";

    /// <summary>
    /// Claude model for agent enrichment. Default Haiku 4.5 (~$0.008/row, ~5x cheaper
    /// than Sonnet). Switch to Sonnet/Opus for the final pass if needed.
    /// </summary>
    public string AgentEnrichmentModel { get; set; } = "claude-haiku-4-5-20251001";

    /// <summary>
    /// Awards rows per agent-enrichment tick. Default 10.
    /// With Haiku that's ~$0.08/tick; hourly cron = ~$2/day burn rate when enabled.
    /// </summary>
    public int AwardAgentEnrichmentBatchSize { get; set; } = 10;

    /// <summary>Max attempts per row before it stops appearing in ListPending. Default 2.</summary>
    public int AwardAgentEnrichmentMaxAttempts { get; set; } = 2;

    // --- Round 7a: vendor site crawler ---
    public bool VendorSiteCrawlEnabled { get; set; } = true;
    public int VendorSiteCrawlBatchSize { get; set; } = 2;
    public int VendorSiteCrawlMaxAttempts { get; set; } = 2;
    public int VendorSiteCrawlTotalCap { get; set; } = 500;
    public string? VendorSiteCrawlCronSchedule { get; set; }

    // --- Round 7b: vendor site extraction (Claude pass over crawl results) ---
    public bool VendorSiteExtractionEnabled { get; set; } = true;
    public int VendorSiteExtractionBatchSize { get; set; } = 5;
    public int VendorSiteExtractionMaxAttempts { get; set; } = 3;
    public int VendorSiteExtractionTotalCap { get; set; } = 500;
    public string? VendorSiteExtractionCronSchedule { get; set; }

    // --- Round 10: enrichment provider framework ---
    public bool EnrichmentDispatchEnabled { get; set; } = true;
    public int EnrichmentDispatchBatchSize { get; set; } = 5;
    public string? EnrichmentDispatchCronSchedule { get; set; }

    /// <summary>
    /// Hard ceiling on total enriched-row count across all runs. Once this many rows
    /// have AgentEnrichedAtUtc set, the job becomes a no-op (until the cap is raised).
    /// Default 5000 — caps total spend around $40 at Haiku pricing. Set to 0 for no cap.
    /// </summary>
    public int AwardAgentEnrichmentTotalCap { get; set; } = 5000;

    /// <summary>Azure AD tenant for GraphEmail polling. Empty disables the provider.</summary>
    public string GraphEmailTenantId { get; init; } = "";

    /// <summary>Azure AD app client id for GraphEmail polling.</summary>
    public string GraphEmailClientId { get; init; } = "";

    /// <summary>Azure AD app client secret for GraphEmail polling.</summary>
    public string GraphEmailClientSecret { get; init; } = "";

    /// <summary>UPN of the shared mailbox to poll.</summary>
    public string GraphEmailUserEmail { get; init; } = "";

    /// <summary>Folder to poll for unread messages.</summary>
    public string GraphEmailMailFolderName { get; init; } = "Inbox";

    /// <summary>Where to move processed messages when mark-as-read mode is disabled.</summary>
    public string GraphEmailProcessedFolderName { get; init; } = "Processed-OpportunityAlerts";

    /// <summary>If true, mark processed messages as read in place. If false, move to the processed folder.</summary>
    public bool GraphEmailMarkAsReadInsteadOfMove { get; init; } = true;

    /// <summary>Maximum messages to fetch per polling cycle.</summary>
    public int GraphEmailMaxEmailsPerRun { get; init; } = 50;

    /// <summary>Smoke-test mode: parse but don't mark or move.</summary>
    public bool GraphEmailSmokeTestMode { get; init; }

    /// <summary>Quartz cron for the GraphEmail polling cycle. Default every 15 minutes.</summary>
    public string GraphEmailCronSchedule { get; init; } = "0 0/15 * * * ?";

    /// <summary>How often the IngestionTriggerPoller drains the
    /// IngestionTriggers table (default 30s).</summary>
    public int IngestionTriggerPollSeconds { get; init; } = 30;

    /// <summary>Maximum Run-Now triggers the poller drains in one wake.</summary>
    public int IngestionTriggerMaxPerWake { get; init; } = 25;

    /// <summary>How often OpportunitySourceCronScheduler checks each enabled
    /// source's CrawlDelaySeconds and queues a trigger if the window has
    /// elapsed (default 300s = 5 min). Lower bound enforced at 30s.</summary>
    public int CronTickIntervalSeconds { get; init; } = 300;

    public int IngestionMaxBytesPerResponse { get; set; } = 50 * 1024 * 1024;
    public int GenericJsonMaxItemsPerRun { get; set; } = 5000;
    public int GenericCsvMaxRowsPerRun { get; set; } = 5000;
    public int VancouverPermitsMaxRowsPerRun { get; set; } = 20000;
    public int NewsFeedMaxItemsPerFeed { get; set; } = 200;

    // --- Round 12: news feed aggregator ---
    public bool NewsFeedPollEnabled { get; set; } = true;
    public string? NewsFeedPollCronSchedule { get; set; }   // default in Program.cs

    // --- Round 12b: news mention classification ---
    public bool NewsClassificationEnabled { get; set; } = true;
    public int NewsClassificationBatchSize { get; set; } = 5;
    public int NewsClassificationTotalCap { get; set; } = 2000;
    public string? NewsClassificationCronSchedule { get; set; }

    // --- Round 13a: building permits ingestion ---
    public bool BuildingPermitsImportEnabled { get; set; } = true;
    public string? BuildingPermitsCronSchedule { get; set; }   // default in Program.cs
    public int BuildingPermitsTotalCap { get; set; } = 100_000;
    public int BuildingPermitsMaxRowsPerSource { get; set; } = 5000;

    // --- Round 16a: Deltek won-project signal refresh ---
    public bool CanonicalOrgKorProjectSignalRefreshEnabled { get; set; } = true;
    public string? CanonicalOrgKorProjectSignalRefreshCronSchedule { get; set; }

    // --- L1: data freshness / retirement lifecycle ---
    public bool DataRetirementEnabled { get; set; } = true;
    public int StaleOppDays { get; set; } = 60;
    public int StaleProjectMonths { get; set; } = 6;
    public string? DataRetirementCronSchedule { get; set; }

    // --- Round 46: weekly canonical-org dedup (auto-merge name-equivalent
    // canonicals introduced by background ingestion). Only the canonical-name
    // pass runs in the worker; --pairs / --merge-dba / honing-pass merges
    // stay manual via the CLI tool because they require curation. Hard cap on
    // groups merged per run prevents a runaway algorithm regression from
    // collapsing the org graph silently every Sunday.
    public bool CanonicalOrgDedupEnabled { get; set; } = true;
    public int CanonicalOrgDedupMaxGroupsPerRun { get; set; } = 200;
    public string? CanonicalOrgDedupCronSchedule { get; set; }

    public bool DataHealthAuditEnabled { get; set; } = true;
    public string? DataHealthAuditCronSchedule { get; set; }
    public string DataHealthAuditOutputDir { get; set; } =
        @"C:\ProgramData\KorOperations\DataHealthAudit";

    public bool JobTriggerPollerEnabled { get; set; } = true;
    public string? JobTriggerPollerCronSchedule { get; set; }
    public int JobTriggerPollerBatchSize { get; set; } = 50;

    // Round 61b: APC InterestedFirms continuous enrichment. After each APC
    // list-scrape, the new postings have no detail-page data. This job
    // queries unenriched APC opps and walks the detail page to populate
    // opportunities.OpportunityInterestedFirms via the same scrape logic
    // tools/ApcInterestBackfill uses.
    public bool ApcInterestEnrichmentEnabled { get; set; } = true;
    public string? ApcInterestEnrichmentCronSchedule { get; set; }
    public int ApcInterestEnrichmentBatchSize { get; set; } = 30;

    public bool BcBidPlanTakerEnrichmentEnabled { get; set; } = true;
    public string? BcBidPlanTakerEnrichmentCronSchedule { get; set; }
    public int BcBidPlanTakerEnrichmentBatchSize { get; set; } = 20;

    public bool BdDeltekLinkDryRunEnabled { get; set; } = true;
    public int BdDeltekLinkDryRunMaxTargets { get; set; } = 5000;
    public int BdDeltekLinkDryRunAlertThreshold { get; set; } = 25;
    public string BdDeltekLinkDryRunOutputDir { get; set; } =
        @"C:\ProgramData\KorOperations\BdDeltekLink\nightly";

    // 2026-06-12: BdResearchQueueBuilderJob now runs the ported dev-box
    // nightly-refresh trigger (Scripts\Nightly-Refresh.ps1) against the
    // server-local QueueDrain root. Dev-box sessions reach the same queues
    // via the \\KOR-APP01\QueueDrain share.
    public bool BdResearchQueueEnabled { get; set; } = true;
    public string BdResearchQueueDrainRoot { get; set; } =
        @"C:\ProgramData\KorOperations\QueueDrain";
    public string BdResearchQueuePwshPath { get; set; } =
        @"C:\Program Files\PowerShell\7\pwsh.exe";

    /// <summary>Round 83: Anthropic-backed BD research executor settings.</summary>
    public BdResearchExecutorOptions BdResearchExecutor { get; set; } = new();

    public string DeltekDsn { get; set; } = "Deltek";
    public string DeltekUser { get; set; } = "";
    public string DeltekPassword { get; set; } = "";
    public string DeltekCatalog { get; set; } = "";

    // --- Round 16c: Deltek PR pursuit sync ---
    public bool KorPursuitDeltekSyncEnabled { get; set; } = true;
    public string? KorPursuitDeltekSyncCronSchedule { get; set; }

    // --- 2026-06-11: BD morning report email (BdMorningReportJob) ---
    // Graph creds = the FileSync app registration (Mail.Send application
    // permission); set via KOR_OPPORTUNITIES_MORNINGREPORT* Machine env
    // vars on KOR-APP01. Missing creds disable the job with a logged warning.
    public bool MorningReportEnabled { get; set; } = true;
    public string MorningReportCronSchedule { get; set; } = "0 0 6 * * ?";
    public string MorningReportTenantId { get; set; } = "";
    public string MorningReportClientId { get; set; } = "";
    public string MorningReportClientSecret { get; set; } = "";
    public string MorningReportSenderUpn { get; set; } = "ilalonde@korstructural.com";
    public string MorningReportRecipient { get; set; } = "ilalonde@korstructural.com";
}
