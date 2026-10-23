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
        api.MapGet("/updates", (NetworkOpsStore s, UpdateScanner u, FleetView f, CancellationToken ct) => RowsAsync(s, u, f, ct));

        api.MapPost("/updates/scan", async (HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Results.Accepted(null, new { triggerId = await s.QueueJobAsync(UpdateScanJob.JobName, ApiAccess.UserOf(h.User), ct) }));

        api.MapPost("/updates/install", async (UpdateInstallRequest body, HttpContext h, NetworkOpsStore s, UpdateScanner u, FleetView fleetView, CancellationToken ct) =>
        {
            if (body.Restart is not ("none" or "if-needed")) return Results.BadRequest(new { error = "restart must be none or if-needed" });
            var ids = (body.DeviceIds ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.BadRequest(new { error = "tick at least one machine" });
            if (ids.Count > 100) return Results.BadRequest(new { error = "at most 100 machines in one batch" });
            return Results.Ok(await InstallAsync(ids, body.Restart == "if-needed", body.Confirmed, ApiAccess.UserOf(h.User), s, u, fleetView, ct));
        });
    }

    /// <summary>
    /// Install updates on these machines under the batch rules (UpdateBatch): never a second install on a machine already
    /// installing, the domain controller only on its own, APP01 never restarted, every restart confirmed. The ONE way an
    /// install is queued -- the Updates view, and To clear's "Install updates" (POST /fixes/run-many hands install fixes
    /// here), so the rules cannot be skipped by starting the same install from another button. (2026-10-22: To clear's
    /// button went through the generic fan-out instead, queued DC01 inside a batch of 28, and queued all 28 twice.)
    /// </summary>
    internal static async Task<List<UpdateInstallOutcome>> InstallAsync(IReadOnlyList<int> ids, bool restartIfNeeded, bool confirmed, string by,
        NetworkOpsStore s, UpdateScanner u, FleetView fleetView, CancellationToken ct)
    {
        var targets = (await u.TargetsAsync(ct)).ToDictionary(t => t.DeviceId);
        var presence = (await fleetView.PcsAsync(ct)).Devices.ToDictionary(d => d.DeviceId);
        var installs = await s.LastUpdateInstallsAsync(ct);
        var outcomes = new List<UpdateInstallOutcome>();
        foreach (var unknown in ids.Where(id => !targets.ContainsKey(id)))
            outcomes.Add(new UpdateInstallOutcome(unknown, $"device {unknown}", null, "not a machine updates can be installed on", false));

        var members = ids.Where(targets.ContainsKey).Select(id => new BatchMember(targets[id],
            presence.GetValueOrDefault(id)?.PresenceState, presence.GetValueOrDefault(id)?.Presence,
            installs.GetValueOrDefault(id) is { Status: "Requested" or "Running" })).ToList();
        var restart = restartIfNeeded ? "if-needed" : "none";
        foreach (var d in UpdateBatch.Decide(members, restartIfNeeded, confirmed))
        {
            var request = JsonSerializer.Serialize(new { batch = ids.Count, restart, confirmed, presence = presence.GetValueOrDefault(d.DeviceId)?.Presence });
            var refused = d.Refused;
            long? actionId = null;
            // The batch read above is a moment old; the insert itself refuses a second install that slipped in since.
            if (d.FixId is not null && (actionId = await s.QueueFixOnceAsync(d.DeviceId, d.FixId, by, request, ct)) is null)
                refused = NetworkOpsStore.AlreadyInFlight;
            if (actionId is null)
            {
                await s.RecordRefusedActionAsync(d.DeviceId, restartIfNeeded ? Core.Actions.FixCatalog.InstallUpdatesRestart : Core.Actions.FixCatalog.InstallUpdates,
                    by, request, refused!, ct);
                outcomes.Add(new UpdateInstallOutcome(d.DeviceId, d.Name, null, refused, d.NeedsConfirmation));
            }
            else
                outcomes.Add(new UpdateInstallOutcome(d.DeviceId, d.Name, actionId, null, false, d.Note));
        }
        return outcomes;
    }

    /// <summary>Every PC and Windows server: what its last search found, what is due, and its last install.</summary>
    internal static async Task<IReadOnlyList<UpdateRow>> RowsAsync(NetworkOpsStore s, UpdateScanner u, FleetView fleetView, CancellationToken ct)
    {
        var targets = await u.TargetsAsync(ct);
        var scans = await s.LatestUpdateScansAsync(ct);
        var installs = await s.LastUpdateInstallsAsync(ct);
        var (fleet, rack) = await fleetView.BothAsync(ct);
        var devices = fleet.Devices.Concat(rack.Devices).GroupBy(d => d.DeviceId).ToDictionary(g => g.Key, g => g.First());
        var due = fleet.OpenFindings.Concat(rack.OpenFindings).Where(f => UpdateRules.Owns(f.RuleKey))
            .GroupBy(f => f.Device, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return targets.Select(t =>
        {
            var dev = devices.GetValueOrDefault(t.DeviceId);
            var f = due.GetValueOrDefault(t.Name);
            var last = installs.GetValueOrDefault(t.DeviceId);
            var (status, parsed, asOf) = Read(scans.GetValueOrDefault(t.DeviceId));
            return new UpdateRow(t.DeviceId, t.Name, t.Kind, t.IsServer, t.Host is not null, t.Why, t.Guard,
                dev?.Presence, dev?.PresenceState, scans.GetValueOrDefault(t.DeviceId)?.CollectedUtc, status, parsed?.RebootPending ?? false,
                parsed?.Updates ?? [], f?.Severity, f?.Title,
                last is null ? null : $"{last.Status}{(last.Detail is { Length: > 0 } d ? ": " + d : "")}", last?.CompletedUtc ?? last?.RequestedUtc,
                asOf);
        }).ToList();
    }

    /// <summary>
    /// What a machine's searches say: the latest one's status, and the list from the latest one that WORKED, with its time.
    /// A failed search keeps the last good list (dated), instead of showing "nothing waiting" until the next search works.
    /// </summary>
    internal static (string? Status, UpdateScan? Pending, DateTime? PendingAsOfUtc) Read(NetworkOpsStore.LatestUpdateScan? scan)
    {
        if (scan is null) return (null, null, null);
        var (latestStatus, latestParsed) = Parse(scan);
        if (latestParsed is not null) return (latestStatus, latestParsed, scan.CollectedUtc);
        return scan.LastGood is { } good && Parse(good).Parsed is { } kept ? (latestStatus, kept, good.CollectedUtc) : (latestStatus, null, null);

        static (string Status, UpdateScan? Parsed) Parse(NetworkOpsStore.LatestUpdateScan s)
        {
            if (s.Status != "Ok" || s.PayloadJson is not { } json) return (s.Status + (s.Error is { Length: > 0 } e ? ": " + e : ""), null);
            try { return ("Ok", UpdateScan.Parse(json)); }
            catch (JsonException ex) { return ("Unreadable: " + ex.Message, null); }
        }
    }
}
