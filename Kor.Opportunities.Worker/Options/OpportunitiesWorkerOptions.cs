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

    /// <summary>How often the IngestionTriggerPoller drains the
    /// IngestionTriggers table (default 30s).</summary>
    public int IngestionTriggerPollSeconds { get; init; } = 30;
}
