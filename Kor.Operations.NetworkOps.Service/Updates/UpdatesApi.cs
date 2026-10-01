#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Updates;
using Kor.Operations.NetworkOps.Service.Api;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kor.Operations.NetworkOps.Service.Updates;

// The Command Center's Updates view: what is waiting where (GET /api/updates), search again now (POST /api/updates/scan),
// and install on the ticked machines at once (POST /api/updates/install). Each machine's install is an ordinary fix run
// (NetworkOps.Actions: who, when, what it printed), so it shows in that machine's history like any other.
internal static class UpdatesApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/updates", (NetworkOpsStore s, UpdateScanner u, CancellationToken ct) => RowsAsync(s, u, ct));

        api.MapPost("/updates/scan", async (HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Results.Accepted(null, new { triggerId = await s.QueueJobAsync(UpdateScanJob.JobName, ApiAccess.UserOf(h.User), ct) }));

        api.MapPost("/updates/install", async (UpdateInstallRequest body, HttpContext h, NetworkOpsStore s, UpdateScanner u, CancellationToken ct) =>
        {
            if (body.Restart is not ("none" or "if-needed")) return Results.BadRequest(new { error = "restart must be none or if-needed" });
            var ids = (body.DeviceIds ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.BadRequest(new { error = "tick at least one machine" });
            if (ids.Count > 100) return Results.BadRequest(new { error = "at most 100 machines in one batch" });

            var targets = (await u.TargetsAsync(ct)).ToDictionary(t => t.DeviceId);
            var presence = (await s.FleetSnapshotAsync(ct)).Devices.ToDictionary(d => d.DeviceId);
            var installs = await s.LastUpdateInstallsAsync(ct);
            var by = ApiAccess.UserOf(h.User);
            var outcomes = new List<UpdateInstallOutcome>();
            foreach (var unknown in ids.Where(id => !targets.ContainsKey(id)))
                outcomes.Add(new UpdateInstallOutcome(unknown, $"device {unknown}", null, "not a machine updates can be installed on", false));

            var members = ids.Where(targets.ContainsKey).Select(id => new BatchMember(targets[id],
                presence.GetValueOrDefault(id)?.PresenceState, presence.GetValueOrDefault(id)?.Presence,
                installs.GetValueOrDefault(id) is { Status: "Requested" or "Running" })).ToList();
            foreach (var d in UpdateBatch.Decide(members, body.Restart == "if-needed", body.Confirmed))
            {
                var request = JsonSerializer.Serialize(new { batch = ids.Count, restart = body.Restart, confirmed = body.Confirmed, presence = presence.GetValueOrDefault(d.DeviceId)?.Presence });
                if (d.FixId is null)
                {
                    await s.RecordRefusedActionAsync(d.DeviceId, body.Restart == "if-needed" ? Core.Actions.FixCatalog.InstallUpdatesRestart : Core.Actions.FixCatalog.InstallUpdates,
                        by, request, d.Refused!, ct);
                    outcomes.Add(new UpdateInstallOutcome(d.DeviceId, d.Name, null, d.Refused, d.NeedsConfirmation));
                }
                else
                    outcomes.Add(new UpdateInstallOutcome(d.DeviceId, d.Name, await s.QueueActionAsync(d.DeviceId, d.FixId, by, request, ct), null, false, d.Note));
            }
            return Results.Ok(outcomes);
        });
    }

    /// <summary>Every PC and Windows server: what its last search found, what is due, and its last install.</summary>
    internal static async Task<IReadOnlyList<UpdateRow>> RowsAsync(NetworkOpsStore s, UpdateScanner u, CancellationToken ct)
    {
        var targets = await u.TargetsAsync(ct);
        var scans = await s.LatestUpdateScansAsync(ct);
        var installs = await s.LastUpdateInstallsAsync(ct);
        var fleet = await s.FleetSnapshotAsync(ct);
        var rack = await s.FleetSnapshotAsync(ct, rack: true);
        var devices = fleet.Devices.Concat(rack.Devices).GroupBy(d => d.DeviceId).ToDictionary(g => g.Key, g => g.First());
        var due = fleet.OpenFindings.Concat(rack.OpenFindings).Where(f => UpdateRules.Owns(f.RuleKey))
            .GroupBy(f => f.Device, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return targets.Select(t =>
        {
            var dev = devices.GetValueOrDefault(t.DeviceId);
            var scan = scans.GetValueOrDefault(t.DeviceId);
            UpdateScan? parsed = null;
            string? status = null;
            if (scan is not null)
            {
                if (scan.Status == "Ok" && scan.PayloadJson is { } json)
                {
                    try { parsed = UpdateScan.Parse(json); status = "Ok"; }
                    catch (JsonException ex) { status = "Unreadable: " + ex.Message; }
                }
                else status = scan.Status + (scan.Error is { Length: > 0 } e ? ": " + e : "");
            }
            var f = due.GetValueOrDefault(t.Name);
            var last = installs.GetValueOrDefault(t.DeviceId);
            return new UpdateRow(t.DeviceId, t.Name, t.Kind, t.IsServer, t.Host is not null, t.Why, t.Guard,
                dev?.Presence, dev?.PresenceState, scan?.CollectedUtc, status, parsed?.RebootPending ?? false,
                parsed?.Updates ?? [], f?.Severity, f?.Title,
                last is null ? null : $"{last.Status}{(last.Detail is { Length: > 0 } d ? ": " + d : "")}", last?.CompletedUtc ?? last?.RequestedUtc);
        }).ToList();
    }
}
