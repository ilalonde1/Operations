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

    /// <summary>How often OpportunitySourceCronScheduler checks each enabled
    /// source's CrawlDelaySeconds and queues a trigger if the window has
    /// elapsed (default 300s = 5 min). Lower bound enforced at 30s.</summary>
    public int CronTickIntervalSeconds { get; init; } = 300;
}
