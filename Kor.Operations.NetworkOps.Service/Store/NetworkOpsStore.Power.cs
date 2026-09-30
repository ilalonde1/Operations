#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Power;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// Rack power (db/KorNetworkOps/004_RackPower.sql): UPS readings and what the watcher decided and did.
internal sealed partial class NetworkOpsStore
{
    public async Task RecordPowerReadingAsync(UpsReading r, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.PowerReadings (Ups, AtUtc, Reachable, Source, SecondsOnBattery, MinutesRemaining, ChargePercent, LoadPercent, BatteryLow, ReplaceBattery, Error)
            VALUES (@u, @at, @reach, @src, @sob, @min, @chg, @load, @low, @repl, @err);
            """);
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 32).Value = r.Ups;
        cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = r.AtUtc;
        cmd.Parameters.Add("@reach", SqlDbType.Bit).Value = r.Reachable;
        cmd.Parameters.Add("@src", SqlDbType.VarChar, 12).Value = r.Source.ToString();
        cmd.Parameters.Add("@sob", SqlDbType.Int).Value = (object?)r.SecondsOnBattery ?? DBNull.Value;
        cmd.Parameters.Add("@min", SqlDbType.Int).Value = (object?)r.MinutesRemaining ?? DBNull.Value;
        cmd.Parameters.Add("@chg", SqlDbType.TinyInt).Value = r.ChargePercent is { } ch ? (byte)Math.Clamp(ch, 0, 255) : DBNull.Value;
        cmd.Parameters.Add("@load", SqlDbType.TinyInt).Value = r.LoadPercent is { } l ? (byte)Math.Clamp(l, 0, 255) : DBNull.Value;
        cmd.Parameters.Add("@low", SqlDbType.Bit).Value = r.BatteryLow;
        cmd.Parameters.Add("@repl", SqlDbType.Bit).Value = r.ReplaceBattery;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 400).Value = (object?)Truncate(r.Error, 400) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Written with CancellationToken.None: the record of what the chain did must survive the service being stopped by it.</summary>
    public async Task AddPowerEventAsync(Guid? chainId, string kind, bool dryRun, bool ok, string text)
    {
        await using var c = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var cmd = Cmd(c, "INSERT NetworkOps.PowerEvents (ChainId, Kind, DryRun, Ok, Text) VALUES (@id, @k, @d, @ok, @t);");
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = (object?)chainId ?? DBNull.Value;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        cmd.Parameters.Add("@d", SqlDbType.Bit).Value = dryRun;
        cmd.Parameters.Add("@ok", SqlDbType.Bit).Value = ok;
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 4000).Value = Truncate(text, 4000)!;
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PowerEventRow>> RecentPowerEventsAsync(int count, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT TOP (@n) AtUtc, Kind, DryRun, Ok, Text FROM NetworkOps.PowerEvents ORDER BY EventId DESC;");
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = count;
        var list = new List<PowerEventRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new PowerEventRow(DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.GetString(4)));
        return list;
    }

    public async Task<int> PurgePowerReadingsAsync(int keepDays, DateTime nowUtc, CancellationToken ct)
    {
        var total = 0;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = Cmd(c, "DELETE TOP (5000) FROM NetworkOps.PowerReadings WHERE AtUtc < DATEADD(day, -@keep, @now);");
            cmd.Parameters.Add("@keep", SqlDbType.Int).Value = keepDays;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            total += n;
            if (n < 5000) return total;
        }
    }

    /// <summary>Queues a catalog job by name (the page's "rehearse the chain now").</summary>
    public async Task<long> QueueJobAsync(string jobName, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "INSERT NetworkOps.JobTriggers (JobName, RequestedBy) OUTPUT inserted.TriggerId VALUES (@j, @by);");
        cmd.Parameters.Add("@j", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<bool> PowerSchemaPresentAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'NetworkOps.PowerReadings', N'U') IS NOT NULL AND OBJECT_ID(N'NetworkOps.PowerEvents', N'U') IS NOT NULL THEN 1 ELSE 0 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }
}
