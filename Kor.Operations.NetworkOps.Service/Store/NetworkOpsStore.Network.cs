#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Network;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// The port map (db/KorNetworkOps/010_NetworkMap.sql): the fleet as the map needs it (network cards, person), and the
// placements and moves the map writes.
internal sealed partial class NetworkOpsStore
{
    /// <summary>
    /// Every fleet PC's network cards (its last health check's NICs) and its person: the USUAL one -- most often signed in
    /// across the checks kept for <paramref name="days"/> days, a built-in Administrator only when nobody else ever is -- or,
    /// with no history, whoever was signed in at the last check. People sign out at night (13 of 32 had anyone signed in at
    /// 21:00 on 2026-10-02), so "who is on it now" would leave most desks nameless.
    /// </summary>
    public async Task<IReadOnlyList<FleetPc>> FleetForMapAsync(int days, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var latest = new List<(int Id, string Name, string Json)>();
        await using (var cmd = Cmd(c, """
            SELECT d.DeviceId, d.Name, o.PayloadJson
            FROM NetworkOps.Devices d
            CROSS APPLY (SELECT TOP (1) PayloadJson FROM NetworkOps.Observations
                         WHERE DeviceId = d.DeviceId AND Probe = 'health' AND Status = 'Ok' AND PayloadJson IS NOT NULL
                         ORDER BY CollectedUtc DESC) o
            WHERE d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) latest.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        }

        var users = new Dictionary<int, List<(string User, int Count, DateTime Last)>>();
        await using (var cmd = Cmd(c, """
            SELECT o.DeviceId, u.UserName, COUNT(*), MAX(o.CollectedUtc)
            FROM NetworkOps.Observations o
            CROSS APPLY (SELECT COALESCE(JSON_VALUE(o.PayloadJson, '$.Session.ConsoleUser'), JSON_VALUE(o.PayloadJson, '$[0].Session.ConsoleUser')) AS UserName) u
            WHERE o.Probe = 'health' AND o.Status = 'Ok' AND o.CollectedUtc >= @since AND ISJSON(o.PayloadJson) = 1
              AND u.UserName IS NOT NULL AND u.UserName <> ''
            GROUP BY o.DeviceId, u.UserName;
            """))
        {
            cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = nowUtc.AddDays(-days);
            cmd.CommandTimeout = 120;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!users.TryGetValue(r.GetInt32(0), out var list)) users[r.GetInt32(0)] = list = [];
                list.Add((r.GetString(1), r.GetInt32(2), r.GetDateTime(3)));
            }
        }

        var fleet = new List<FleetPc>();
        foreach (var (id, name, json) in latest)
        {
            HealthSnapshot? s;
            try { s = HealthSnapshot.Parse(json); }
            catch (System.Text.Json.JsonException) { continue; }
            var macs = (s.Wake?.Nics ?? []).Select(n => n.Mac).Where(m => m is { Length: > 0 }).ToList();
            var (user, source) = UsualUser(users.GetValueOrDefault(id), s.Session?.ConsoleUser);
            fleet.Add(new FleetPc(name, macs, user, source));
        }
        return fleet;
    }

    /// <summary>Most often signed in (a built-in Administrator only when nobody else is); else whoever was on at the last check.</summary>
    internal static (string? User, string? Source) UsualUser(IReadOnlyList<(string User, int Count, DateTime Last)>? seen, string? now)
    {
        static bool Admin(string u) => u.EndsWith("\\Administrator", StringComparison.OrdinalIgnoreCase) || u.Equals("Administrator", StringComparison.OrdinalIgnoreCase);
        if (seen is { Count: > 0 })
        {
            var pick = seen.Where(x => !Admin(x.User)).OrderByDescending(x => x.Count).ThenByDescending(x => x.Last).FirstOrDefault();
            if (pick.User is null) pick = seen.OrderByDescending(x => x.Count).ThenByDescending(x => x.Last).First();
            return (pick.User, "usual");
        }
        return now is { Length: > 0 } ? (now, "signed in now") : (null, null);
    }

    /// <summary>
    /// Writes this map's placements (one row per MAC) and, for a device that sat on one port and is now on another, a move.
    /// Rows for devices no longer in the map are kept (it may be off): their UpdatedUtc says when they were last placed.
    /// False before 010 has run (the live map still works).
    /// </summary>
    public async Task<(bool Saved, int Moves)> SaveNetworkMapAsync(NetworkMap map, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            bool macColumn;
            await using (var has = Cmd(c, "SELECT CASE WHEN COL_LENGTH(N'NetworkOps.NetworkPlacements', N'SwitchMac') IS NULL THEN 0 ELSE 1 END;"))
                macColumn = (int)(await has.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;   // 011 run
            var before = new Dictionary<string, PlacedAt>(StringComparer.Ordinal);
            await using (var read = Cmd(c, $"SELECT Mac, Placement, SwitchName, Port, {(macColumn ? "SwitchMac" : "NULL")} FROM NetworkOps.NetworkPlacements;"))
            {
                await using var r = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                    before[r.GetString(0)] = new(r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4));
            }

            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
            var moves = 0;
            foreach (var p in map.Everything().Select(x => (E: x.Endpoint, x.Placement, x.Switch, x.Port, x.SwitchMac)))
            {
                if (before.TryGetValue(p.E.Mac, out var was) && IsMove(was, new(p.Placement, p.Switch, p.Port, p.SwitchMac)))
                {
                    await using var mv = Cmd(c, """
                        INSERT NetworkOps.NetworkMoves (Mac, Name, FromSwitch, FromPort, ToSwitch, ToPort, AtUtc) VALUES (@m, @n, @fs, @fp, @ts, @tp, @now);
                        """, tx);
                    mv.Parameters.Add("@m", SqlDbType.Char, 17).Value = p.E.Mac;
                    mv.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = Truncate(p.E.Name, 200)!;
                    mv.Parameters.Add("@fs", SqlDbType.NVarChar, 200).Value = (object?)was.Switch ?? DBNull.Value;
                    mv.Parameters.Add("@fp", SqlDbType.Int).Value = (object?)was.Port ?? DBNull.Value;
                    mv.Parameters.Add("@ts", SqlDbType.NVarChar, 200).Value = (object?)p.Switch ?? DBNull.Value;
                    mv.Parameters.Add("@tp", SqlDbType.Int).Value = (object?)p.Port ?? DBNull.Value;
                    mv.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                    await mv.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    moves++;
                }
                await using var up = Cmd(c, $"""
                    MERGE NetworkOps.NetworkPlacements WITH (HOLDLOCK) AS t USING (SELECT @m AS Mac) AS s ON t.Mac = s.Mac
                    WHEN MATCHED THEN UPDATE SET Name = @n, NameSource = @ns, Placement = @pl, SwitchName = @sw, Port = @p, Ip = @ip, Pc = @pc,
                                                 UserName = @u, UserSource = @us, Maker = @mk, SeenUtc = @seen, UpdatedUtc = @now{(macColumn ? ", SwitchMac = @smac" : "")}
                    WHEN NOT MATCHED THEN INSERT (Mac, Name, NameSource, Placement, SwitchName, Port, Ip, Pc, UserName, UserSource, Maker, SeenUtc, FirstSeenUtc, UpdatedUtc{(macColumn ? ", SwitchMac" : "")})
                                          VALUES (@m, @n, @ns, @pl, @sw, @p, @ip, @pc, @u, @us, @mk, @seen, @now, @now{(macColumn ? ", @smac" : "")});
                    """, tx);
                up.Parameters.Add("@smac", SqlDbType.Char, 17).Value = (object?)p.SwitchMac ?? DBNull.Value;
                up.Parameters.Add("@m", SqlDbType.Char, 17).Value = p.E.Mac;
                up.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = Truncate(p.E.Name, 200)!;
                up.Parameters.Add("@ns", SqlDbType.VarChar, 32).Value = Truncate(p.E.NameSource, 32)!;
                up.Parameters.Add("@pl", SqlDbType.VarChar, 16).Value = p.Placement;
                up.Parameters.Add("@sw", SqlDbType.NVarChar, 200).Value = (object?)Truncate(p.Switch, 200) ?? DBNull.Value;
                up.Parameters.Add("@p", SqlDbType.Int).Value = (object?)p.Port ?? DBNull.Value;
                up.Parameters.Add("@ip", SqlDbType.VarChar, 45).Value = (object?)Truncate(p.E.Ip, 45) ?? DBNull.Value;
                up.Parameters.Add("@pc", SqlDbType.NVarChar, 100).Value = (object?)Truncate(p.E.Pc, 100) ?? DBNull.Value;
                up.Parameters.Add("@u", SqlDbType.NVarChar, 200).Value = (object?)Truncate(p.E.User, 200) ?? DBNull.Value;
                up.Parameters.Add("@us", SqlDbType.VarChar, 32).Value = (object?)Truncate(p.E.UserSource, 32) ?? DBNull.Value;
                up.Parameters.Add("@mk", SqlDbType.NVarChar, 100).Value = (object?)Truncate(p.E.Maker, 100) ?? DBNull.Value;
                up.Parameters.Add("@seen", SqlDbType.DateTime2).Value = (object?)p.E.SeenUtc ?? DBNull.Value;
                up.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return (true, moves);
        }
        catch (SqlException ex) when (ex.Number == 208) { return (false, 0); }   // 010 not run yet
    }

    /// <summary>Where a device was or is: placement, switch (name and, from 011, MAC), port.</summary>
    internal sealed record PlacedAt(string Placement, string? Switch, int? Port, string? SwitchMac);

    /// <summary>
    /// A move is a device on a port before and on a DIFFERENT port now. The switch is compared by MAC when both rows have it
    /// (011): renaming BMZ-SW01 is not every device on it moving. By name only for a row written before 011.
    /// </summary>
    internal static bool IsMove(PlacedAt was, PlacedAt now)
        => was.Placement == "port" && now.Placement == "port"
           && (was.Port != now.Port || (was.SwitchMac is not null && now.SwitchMac is not null ? was.SwitchMac != now.SwitchMac : was.Switch != now.Switch));

    public sealed record NetworkMoveRow(string Mac, string Name, string? FromSwitch, int? FromPort, string? ToSwitch, int? ToPort, DateTime AtUtc);

    /// <summary>Moves since <paramref name="sinceUtc"/>, newest first. Empty before 010.</summary>
    public async Task<IReadOnlyList<NetworkMoveRow>> NetworkMovesAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var list = new List<NetworkMoveRow>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, "SELECT TOP (500) Mac, Name, FromSwitch, FromPort, ToSwitch, ToPort, AtUtc FROM NetworkOps.NetworkMoves WHERE AtUtc >= @s ORDER BY AtUtc DESC;");
            cmd.Parameters.Add("@s", SqlDbType.DateTime2).Value = sinceUtc;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new NetworkMoveRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetInt32(5), DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc)));
        }
        catch (SqlException ex) when (ex.Number == 208) { }
        return list;
    }
}
