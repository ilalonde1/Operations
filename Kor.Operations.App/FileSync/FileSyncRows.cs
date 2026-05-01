#nullable enable
namespace Kor.Operations.App.FileSync;

public sealed class HeartbeatRow
{
    public string HostName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset LastHeartbeatAt { get; init; }

    public string GlobalMode { get; init; } = string.Empty;

    public string? ServiceVersion { get; init; }

    public int? WatcherGen { get; init; }

    public int JobsRegistered { get; init; }

    public TimeSpan SinceLastHeartbeat => DateTimeOffset.Now - LastHeartbeatAt;

    public bool IsStale => SinceLastHeartbeat > TimeSpan.FromMinutes(5);
}

public sealed class JobRow
{
    public string JobName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Mode { get; init; } = "Shadow";

    public string? CronExpression { get; init; }

    public bool Enabled { get; init; }

    public DateTimeOffset LastConfigChangedAt { get; init; }

    public string? Notes { get; init; }
}
