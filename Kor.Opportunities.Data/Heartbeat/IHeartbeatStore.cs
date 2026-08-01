#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Heartbeat;

/// <summary>
/// Liveness signal for the Opportunities Worker.
/// Exposes a SELECT-1 ping, an upsert into <c>opportunities.ServiceHeartbeat</c>,
/// and a list-all reader for the WPF admin tab to display
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

    /// <summary>
    /// Returns every heartbeat row, most recent first. Empty list if the table
    /// has no rows yet (Worker has never run, or schema script has not been applied).
    /// </summary>
    Task<IReadOnlyList<HeartbeatRow>> ListAsync(CancellationToken ct);
}
