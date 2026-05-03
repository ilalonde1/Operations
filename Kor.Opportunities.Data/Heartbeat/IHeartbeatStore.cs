#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Heartbeat;

/// <summary>
/// Liveness signal for the Opportunities Worker.
/// Exposes a SELECT-1 ping plus an upsert into <c>opportunities.ServiceHeartbeat</c>.
/// The WPF admin tab will read this in a later phase to show
/// "Worker last seen X minutes ago, version Y".
/// </summary>
public interface IHeartbeatStore
{
    /// <summary>Cheap connectivity probe used at service start-up.</summary>
    Task<bool> PingAsync(CancellationToken ct);

    /// <summary>
    /// MERGEs a row into <c>opportunities.ServiceHeartbeat</c> keyed on
    /// <paramref name="serviceName"/> + <paramref name="machineName"/>.
    /// </summary>
    Task WriteHeartbeatAsync(
        string serviceName,
        string machineName,
        string? version,
        CancellationToken ct);
}
