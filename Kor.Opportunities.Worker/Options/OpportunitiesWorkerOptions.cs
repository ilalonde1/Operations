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

    /// <summary>How often the IngestionTriggerPoller drains the
    /// IngestionTriggers table (default 30s).</summary>
    public int IngestionTriggerPollSeconds { get; init; } = 30;
}
