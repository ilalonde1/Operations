#nullable enable
namespace Kor.Opportunities.Worker.Options;

public sealed class OpportunitiesWorkerOptions
{
    /// <summary>SQL Server connection string for KorOpportunitiesDb.</summary>
    public string OpportunitiesDb { get; init; } = "";

    /// <summary>How often the Worker writes a heartbeat row (default 60s).</summary>
    public int HeartbeatSeconds { get; init; } = 60;
}
