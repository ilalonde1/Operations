#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Actions;

namespace Kor.Operations.NetworkOps.Service.Store;

// Windows updates. A scan is an observation like any probe result (Probe = 'updates'), so it needs no table of its own:
// the latest one per device is what is waiting there now, the rest is its history until the nightly purge.
internal sealed partial class NetworkOpsStore
{
    public const string UpdatesProbe = "updates";

    public sealed record LatestUpdateScan(int DeviceId, DateTime CollectedUtc, string Status, string? PayloadJson, string? Error);

    /// <summary>Each device's most recent update search: what it found, or why it could not search.</summary>
    public async Task<IReadOnlyDictionary<int, LatestUpdateScan>> LatestUpdateScansAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT DeviceId, CollectedUtc, Status, PayloadJson, Error FROM (
                SELECT DeviceId, CollectedUtc, Status, PayloadJson, Error,
                       ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY CollectedUtc DESC) AS n
                FROM NetworkOps.Observations WHERE Probe = @p) x
            WHERE n = 1;
            """);
        cmd.Parameters.Add("@p", SqlDbType.VarChar, 40).Value = UpdatesProbe;
        var map = new Dictionary<int, LatestUpdateScan>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            map[r.GetInt32(0)] = new LatestUpdateScan(r.GetInt32(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4));
        return map;
    }

    public sealed record LastUpdateInstall(int DeviceId, long ActionId, string Status, string? Detail, DateTime RequestedUtc, DateTime? CompletedUtc);

    /// <summary>Each device's most recent "Install updates" run, whatever became of it.</summary>
    public async Task<IReadOnlyDictionary<int, LastUpdateInstall>> LastUpdateInstallsAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT DeviceId, ActionId, Status, Detail, RequestedUtc, CompletedUtc FROM (
                SELECT DeviceId, ActionId, Status, Detail, RequestedUtc, CompletedUtc,
                       ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY ActionId DESC) AS n
                FROM NetworkOps.Actions WHERE Kind IN (@a, @b)) x
            WHERE n = 1;
            """);
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 64).Value = FixCatalog.InstallUpdates;
        cmd.Parameters.Add("@b", SqlDbType.VarChar, 64).Value = FixCatalog.InstallUpdatesRestart;
        var map = new Dictionary<int, LastUpdateInstall>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            map[r.GetInt32(0)] = new LastUpdateInstall(r.GetInt32(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc), r.IsDBNull(5) ? null : DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc));
        return map;
    }
}
