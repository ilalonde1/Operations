#nullable enable
namespace Kor.Operations.FileSync.Service.ControlPlane;

internal interface IControlPlaneStore
{
    Task WriteHeartbeatAsync(
        string hostName,
        DateTimeOffset startedAt,
        string mode,
        string? version,
        int jobsRegistered,
        int? watcherGen,
        CancellationToken ct);

    Task<bool> PingAsync(CancellationToken ct);
}
