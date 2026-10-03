#nullable enable
using System.Security.Cryptography.X509Certificates;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kor.Operations.NetworkOps.Service.Api;

// The Command Center API: HTTPS on APP01, Entra tokens only (MFA enforced by Conditional Access at
// issue), every call logged against the signed-in user. It is its own small web host started by the
// service, sharing the service's store, so the scheduler, sweeps and heartbeat run exactly as before
// and a service without the API configured has no listening port at all.
//
// Reachable only from the LAN and the VPN (Windows firewall rule on APP01), and the page pins this
// certificate by its SHA-256 hash: there is no internal CA, and a pin is stricter than one.
//
// The endpoint agents call in on the same listener (/agent/v1, Agents/AgentApi.cs) with their own per-PC keys;
// neither kind of caller can use the other's routes.
internal sealed class ApiHost(IOptions<NetworkOpsOptions> options, NetworkOpsStore store, Power.PowerState power, Agents.AgentHub agents,
    Mesh.MeshState mesh, Prompts.PromptLibrary prompts, Updates.UpdateScanner updates, Agents.MachineRunner runner, ILoggerFactory loggers, ILogger<ApiHost> log) : BackgroundService
{
    private static readonly TimeSpan MaxSnooze = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (o.ApiPort <= 0 || string.IsNullOrWhiteSpace(o.ApiCertThumbprint) || string.IsNullOrWhiteSpace(o.ApiTenantId) || string.IsNullOrWhiteSpace(o.ApiAudience))
        {
            log.LogInformation("Command Center API off: ApiPort, ApiCertThumbprint, ApiTenantId and ApiAudience are not all set");
            return;
        }
        X509Certificate2 cert;
        try { cert = LoadCertificate(o.ApiCertThumbprint); }
        catch (InvalidOperationException ex) { log.LogError("Command Center API NOT started: {Reason}", ex.Message); return; }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggers);
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(power);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(agents);
        builder.Services.AddSingleton(mesh);
        builder.Services.AddSingleton(prompts);
        builder.Services.AddSingleton(updates);
        builder.Services.AddSingleton(runner);
        builder.Services.AddSingleton<Agents.IAgentDirectory>(store);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            // Body limits stay at Kestrel's default here; the agent gate raises it for an authenticated agent's result only.
            k.ListenAnyIP(o.ApiPort, l => l.UseHttps(cert));
        });
        builder.Services.ConfigureHttpJsonOptions(j => j.SerializerOptions.PropertyNameCaseInsensitive = true);
        var issuer = $"https://login.microsoftonline.com/{o.ApiTenantId}/v2.0";
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(j =>
        {
            j.Authority = issuer;
            j.MapInboundClaims = false;   // keep scp, roles, tid, preferred_username as Entra names them
            j.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudiences = [o.ApiAudience, $"api://{o.ApiAudience}"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
        });
        builder.Services.AddAuthorization(a => a.AddPolicy("CommandCenter", p => p.RequireAssertion(c => ApiAccess.Deny(c.User, o.ApiTenantId) is null)));

        var app = builder.Build();
        var audit = loggers.CreateLogger("NetworkOps.Api.Audit");
        // Audit every call: who, what, outcome. Denials say why, in the log only.
        app.Use(async (http, next) =>
        {
            await next(http);
            // Agents poll around the clock: only their refusals and failures are worth a line (their jobs are logged by AgentApi).
            if (http.Request.Path.StartsWithSegments("/agent") && http.Response.StatusCode < 400) return;
            var who = http.User.Identity?.IsAuthenticated == true ? ApiAccess.UserOf(http.User) : "anonymous";
            var why = http.Response.StatusCode is 401 or 403 ? ApiAccess.Deny(http.User, o.ApiTenantId) : null;
            audit.LogInformation("API {Who} {Method} {Path} -> {Status}{Why}", who, http.Request.Method, http.Request.Path, http.Response.StatusCode, why is null ? "" : $" ({why})");
        });
        Agents.AgentApi.UseAgentGate(app);   // /agent: key checked before any body is read
        app.UseAuthentication();
        app.UseAuthorization();
        Map(app);
        Agents.AgentApi.Map(app);

        log.LogInformation("Command Center API listening on https://*:{Port} (certificate {Subject}, expires {Expiry:yyyy-MM-dd})", o.ApiPort, cert.Subject, cert.NotAfter);
        await app.RunAsync(ct).ConfigureAwait(false);
    }

    private static void Map(WebApplication app)
    {
        // Unauthenticated liveness: says only that the API is up and which version, so reachability and
        // the certificate can be checked without a token.
        app.MapGet("/api/ping", () => Results.Ok(new { status = "ok", version = Jobs.JobDispatcher.Version }));

        // A Claude session reporting its outcome (Prompts/PromptLibrary.cs). It has no Entra token -- it runs in a
        // terminal -- so it is outside the group: the run's own one-time token is the credential, checked in SQL
        // against its hash, and the body is small and read only after the token header is present.
        app.MapPost("/api/prompt-runs/{id:long}/outcome", async (long id, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
        {
            if (h.Request.Headers["X-Prompt-Token"].ToString() is not { Length: >= 20 and <= 100 } token) return Results.Unauthorized();
            if (h.Request.ContentLength is null or > 32_768) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);   // a card is up to ~12 KB
            PromptOutcome? body;
            try { body = await h.Request.ReadFromJsonAsync<PromptOutcome>(ct); }
            catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "the body must be JSON: outcome, summary, learned" }); }
            if (body is null || !Prompts.PromptLibrary.Outcomes.Contains(body.Outcome ?? ""))
                return Results.BadRequest(new { error = "outcome must be solved, partly, not-solved or no-action" });
            if (string.IsNullOrWhiteSpace(body.Summary)) return Results.BadRequest(new { error = "summary: say what was wrong and what was done" });
            if (!await s.PromptRunsAvailableAsync(ct)) return NoPromptRuns();
            if (body.Card is { } card)
            {
                if (Prompts.PromptLibrary.InvalidCard(card) is { } why) return Results.BadRequest(new { error = why });
                if (!await s.KnowledgeAvailableAsync(ct))
                    return Results.Json(new { error = "knowledge cards are not switched on (db/KorNetworkOps/008): send the report again without the card" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            var run = await s.RecordPromptOutcomeAsync(id, Prompts.PromptLibrary.HashToken(token), body.Outcome!, body.Summary.Trim(), body.Learned, body.Card, ct);
            if (run is null) return Results.Unauthorized();   // unknown run, wrong token, or already reported: the same answer for all three
            if (run.DeviceId is { } device)
                await s.AddNoteAsync(device, $"Claude session (run {run.RunId}, {run.CreatedBy})",
                    $"[{body.Outcome}] {body.Summary.Trim()}{(string.IsNullOrWhiteSpace(body.Learned) ? "" : $" -- proposed learning: {body.Learned.Trim()}")}", ct);
            return Results.Ok(new { recorded = run.RunId, card = body.Card is null ? null : "proposed: it waits for Ian's decision in the Prompt Library" });
        });

        var api = app.MapGroup("/api").RequireAuthorization("CommandCenter");

        // ---- Windows updates: what is waiting where, and installing it on many machines at once (Updates/UpdatesApi.cs).
        Updates.UpdatesApi.Map(api);

        // ---- a Claude session working a machine through APP01: netops run / last-check / knowledge (SessionApi.cs).
        SessionApi.Map(api);

        // ---- the Prompt Library: prompts written from the live database when opened; their runs and what came back.
        api.MapGet("/prompts", (Prompts.PromptLibrary p, CancellationToken ct) => p.CatalogAsync(ct));
        api.MapPost("/prompts/render", async (PromptRequest body, HttpContext h, Prompts.PromptLibrary p, CancellationToken ct) =>
            await p.RenderAsync(body, ApiAccess.UserOf(h.User), ct) switch
            {
                (Prompts.PromptLibrary.Refusal.None, _, { } prompt) => Results.Ok(prompt),
                (Prompts.PromptLibrary.Refusal.NotFound, var e, _) => Results.NotFound(new { error = e }),
                (_, var e, _) => Results.BadRequest(new { error = e }),
            });
        api.MapGet("/prompt-runs", (NetworkOpsStore s, CancellationToken ct) => s.PromptRunsAsync(100, ct));
        api.MapPost("/prompt-runs/{id:long}/learned", async (long id, LearnedDecision body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            body.Decision is not ("accept" or "reject") ? Results.BadRequest(new { error = "decision must be accept or reject" })
            : !await s.PromptRunsAvailableAsync(ct) ? NoPromptRuns()
            : await s.DecideLearnedAsync(id, body.Decision == "accept", ApiAccess.UserOf(h.User), ct) ? Results.NoContent()
            : Results.Conflict(new { error = "that run has no learning waiting for a decision" }));
        api.MapGet("/fleet", async (NetworkOpsStore s, Agents.AgentHub hub, Mesh.MeshState m, CancellationToken ct) =>
            WithMesh(WithAgents(await s.FleetSnapshotAsync(ct), await s.AgentRecordsAsync(ct), hub), m, await s.MeshRecordsAsync(ct)));
        // The rack (hosts, storage, UPSes, backup, network, internet): the same shape as /fleet, served apart so a
        // page that predates the rack never lists a host or a UPS as a PC.
        api.MapGet("/rack", async (NetworkOpsStore s, Mesh.MeshState m, CancellationToken ct) =>
            WithMesh(await s.FleetSnapshotAsync(ct, rack: true), m, await s.MeshRecordsAsync(ct)));
        api.MapGet("/devices/{id:int}/history", (int id, NetworkOpsStore s, CancellationToken ct) => s.DeviceHistoryAsync(id, ct));
        // What changed since a moment (default: the last 24 h; at most 31 days back): the morning brief, from the database.
        api.MapGet("/changes", (DateTime? since, NetworkOpsStore s, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var from = since is { } x ? (x.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : x.ToUniversalTime()) : now.AddHours(-24);
            if (from < now.AddDays(-31)) from = now.AddDays(-31);
            return s.ChangesSinceAsync(from, ct);
        });
        api.MapGet("/resolutions", (NetworkOpsStore s, CancellationToken ct) => s.ResolutionRowsAsync(ct));

        api.MapPost("/devices/{name}/check", async (string name, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            // A rack device's "check now" re-reads that device through the rack sweep; a PC's runs its health probe.
            (await s.IsRackDeviceAsync(name, ct) ? await s.QueueRackCheckAsync(name, ApiAccess.UserOf(h.User), ct) : await s.QueueCheckAsync(name, ApiAccess.UserOf(h.User), ct))
                is { } id ? Results.Accepted($"/api/triggers/{id}", new { triggerId = id }) : Results.NotFound());
        api.MapGet("/triggers/{id:long}", async (long id, NetworkOpsStore s, CancellationToken ct) =>
            await s.TriggerStateAsync(id, ct) is { } t ? Results.Ok(t) : Results.NotFound());
        api.MapPost("/triggers/{id:long}/cancel", async (long id, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Results.Ok(new { cancelled = await s.CancelCheckAsync(id, ApiAccess.UserOf(h.User), ct) }));

        api.MapPost("/findings/{id:long}/acknowledge", (long id, AnnotateRequest? body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Annotate(s, id, NetworkOpsStore.Annotation.Acknowledge, h, body?.Note, null, ct));
        api.MapPost("/findings/{id:long}/snooze", (long id, AnnotateRequest body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            body.UntilUtc is not { } until || until <= DateTime.UtcNow || until - DateTime.UtcNow > MaxSnooze
                ? Task.FromResult(Results.BadRequest(new { error = "untilUtc must be in the future and within 90 days" }))
                : Annotate(s, id, NetworkOpsStore.Annotation.Snooze, h, body.Note, DateTime.SpecifyKind(until, DateTimeKind.Utc), ct));
        api.MapPost("/findings/{id:long}/reopen", (long id, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Annotate(s, id, NetworkOpsStore.Annotation.Reopen, h, null, null, ct));

        // Rack power: the live UPS readings, the verdict, whether the chain is armed, and the recent timeline.
        api.MapGet("/power", async (Power.PowerState ps, IOptions<NetworkOpsOptions> o, NetworkOpsStore s, CancellationToken ct) =>
        {
            IReadOnlyList<PowerEventRow> events;
            try { events = await s.RecentPowerEventsAsync(40, ct); }
            catch (Microsoft.Data.SqlClient.SqlException) { events = []; }   // before 004 has run
            return ps.Snapshot(o.Value.PowerChainArmed, events);
        });
        // Rehearse the chain now (a dry run, same as the 06:45 one). Arming is NOT an API: it is a
        // configuration change on APP01, made after the live test -- never a button.
        api.MapPost("/power/rehearse", async (HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Results.Accepted(null, new { triggerId = await s.QueueJobAsync(Jobs.PowerRehearsalJob.JobName, ApiAccess.UserOf(h.User), ct) }));

        // ---- fixes. The catalog is the whole list of what can run; a request names one fix. A DISRUPTIVE fix (a
        // restart) is refused while the last check saw someone actively using the machine, unless the person
        // confirmed after being told -- enforced here, not only in the page. Refusals are recorded like runs.
        api.MapGet("/fixes", (string ruleKey) => Results.Ok(Kor.Operations.NetworkOps.Core.Actions.FixCatalog.For(ruleKey)
            .Select(f => new FixOption(f.Id, f.Title, f.Explain, f.Disruptive, f.ParamLabel, Kor.Operations.NetworkOps.Core.Actions.FixCatalog.ParamFromFinding(f, ruleKey)))));
        api.MapPost("/devices/{id:int}/fixes", async (int id, FixRequest body, HttpContext h, NetworkOpsStore s, IOptions<NetworkOpsOptions> o, CancellationToken ct) =>
        {
            var fix = Kor.Operations.NetworkOps.Core.Actions.FixCatalog.Get(body.ActionId);
            if (fix is null) return Results.BadRequest(new { error = $"'{body.ActionId}' is not a fix NetworkOps knows" });
            if (await s.DeviceForActionAsync(id, ct) is not { } dev) return Results.NotFound();
            var by = ApiAccess.UserOf(h.User);
            var request = System.Text.Json.JsonSerializer.Serialize(new { param = body.Param, finding = body.FindingKey, note = body.Note, confirmed = body.Confirmed, presence = dev.Presence });
            string? refuse = Kor.Operations.NetworkOps.Core.Actions.FixCatalog.Invalid(fix, body.Param)
                ?? (dev.Source == "Rack" && Sweep.ActionRunner.HostOf(o.Value, dev.Name) is null
                    ? $"{dev.Name} is not a Windows server APP01 can run fixes on" : null)
                ?? (fix.Disruptive && dev.PresenceState == "Active" && !body.Confirmed
                    ? $"someone is using {dev.Name} right now ({dev.Presence}): confirm to go ahead" : null);
            if (refuse is not null)
            {
                await s.RecordRefusedActionAsync(id, fix.Id, by, request, refuse, ct);
                return Results.Conflict(new { error = refuse, needsConfirmation = fix.Disruptive && dev.PresenceState == "Active" && !body.Confirmed });
            }
            var runId = await s.QueueActionAsync(id, fix.Id, by, request, ct);
            return Results.Accepted($"/api/actions/{runId}", new { actionId = runId });
        });
        // ---- the endpoint agent: install (or upgrade, which is installing again) and remove, queued and audited like a fix.
        api.MapPost("/devices/{id:int}/agent", async (int id, AgentRequest body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
        {
            var kind = body.Action switch { "install" => Agents.AgentInstaller.InstallKind, "remove" => Agents.AgentInstaller.RemoveKind, _ => null };
            if (kind is null) return Results.BadRequest(new { error = "action must be install or remove" });
            if (await s.DeviceForActionAsync(id, ct) is not { } dev) return Results.NotFound();
            if (dev.Source == "Rack") return Results.BadRequest(new { error = "the agent is for PCs; the rack is read by the rack sweep" });
            var runId = await s.QueueActionAsync(id, kind, ApiAccess.UserOf(h.User), System.Text.Json.JsonSerializer.Serialize(new { action = body.Action }), ct);
            return Results.Accepted($"/api/actions/{runId}", new { actionId = runId });
        });
        // Remote control: install the Mesh agent on a PC or a Windows rack server, queued and audited like a fix.
        api.MapPost("/devices/{id:int}/mesh", async (int id, MeshRequest body, HttpContext h, NetworkOpsStore s, IOptions<NetworkOpsOptions> o, CancellationToken ct) =>
        {
            if (body.Action != "install") return Results.BadRequest(new { error = "action must be install" });
            if (await s.DeviceForActionAsync(id, ct) is not { } dev) return Results.NotFound();
            if (dev.Source == "Rack" && Sweep.ActionRunner.HostOf(o.Value, dev.Name) is null)
                return Results.BadRequest(new { error = $"{dev.Name} is not a Windows server: remote control installs on PCs and Windows servers" });
            var runId = await s.QueueActionAsync(id, Mesh.MeshInstaller.InstallKind, ApiAccess.UserOf(h.User), System.Text.Json.JsonSerializer.Serialize(new { action = "install" }), ct);
            return Results.Accepted($"/api/actions/{runId}", new { actionId = runId });
        });
        // Wake-on-LAN: a magic packet from APP01, queued and audited like a fix; the run waits for the PC to answer.
        api.MapPost("/devices/{id:int}/wake", async (int id, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
        {
            if (await s.DeviceForActionAsync(id, ct) is not { } dev) return Results.NotFound();
            if (dev.Source == "Rack") return Results.BadRequest(new { error = "Wake is for PCs: the rack is never shut down" });
            var runId = await s.QueueActionAsync(id, Kor.Operations.NetworkOps.Core.Actions.MagicPacket.Kind, ApiAccess.UserOf(h.User), "{}", ct);
            return Results.Accepted($"/api/actions/{runId}", new { actionId = runId });
        });
        // The next batch of the fleet rollout (Agents/AgentRollout.cs): one at a time, stops at the first failure.
        api.MapPost("/agents/rollout", async (HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            Results.Accepted(null, new { triggerId = await s.QueueJobAsync(Agents.AgentRollout.JobName, ApiAccess.UserOf(h.User), ct) }));
        api.MapGet("/actions/{id:long}", async (long id, NetworkOpsStore s, CancellationToken ct) =>
            await s.ActionAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound());

        api.MapPost("/devices/{id:int}/notes", async (int id, NoteRequest body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(body.Body) ? Results.BadRequest(new { error = "a note needs text" })
            : await s.AddNoteAsync(id, ApiAccess.UserOf(h.User), body.Body, ct) ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>Each PC's agent: installed (SQL) and live (the hub, which knows within seconds; SQL is written every few minutes).</summary>
    internal static FleetSnapshot WithAgents(FleetSnapshot fleet, IReadOnlyDictionary<string, NetworkOpsStore.AgentRecord> installed, Agents.AgentHub hub)
        => fleet with
        {
            Devices = fleet.Devices.Select(d =>
            {
                if (!installed.TryGetValue(d.Name, out var rec)) return d;
                var live = hub.Status(d.Name);
                return d with
                {
                    AgentVersion = live?.Version is { Length: > 0 } v ? v : rec.LastVersion ?? rec.Version,
                    AgentConnected = live?.Connected == true,
                    AgentLastContactUtc = new[] { live is { LastPollUtc: var p } && p != default ? p : (DateTime?)null, rec.LastContactUtc }.Max(),
                };
            }).ToList(),
        };

    /// <summary>Each device's remote control: the live read when it is fresh, else what was stored (just after a restart).</summary>
    internal static FleetSnapshot WithMesh(FleetSnapshot fleet, Mesh.MeshState live, IReadOnlyDictionary<int, NetworkOpsStore.MeshRecord> stored)
        => fleet with
        {
            Devices = fleet.Devices.Select(d =>
                live.Fresh
                    ? live.For(d.DeviceId) is { } l ? d with { MeshNodeId = l.Node.Id, MeshConnected = l.Node.AgentConnected } : d
                    : stored.TryGetValue(d.DeviceId, out var r) ? d with { MeshNodeId = r.NodeId, MeshConnected = r.Connected } : d).ToList(),
        };

    private static IResult NoPromptRuns()
        => Results.Json(new { error = "reporting is not switched on: run db/KorNetworkOps/007_PromptLibrary.sql" }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> Annotate(NetworkOpsStore s, long id, NetworkOpsStore.Annotation kind, HttpContext h, string? note, DateTime? until, CancellationToken ct)
        => await s.AnnotateAsync(id, kind, ApiAccess.UserOf(h.User), note, until, ct).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.Conflict(new { error = "that finding has cleared since the page loaded; refresh to see it in the history" });

    private static X509Certificate2 LoadCertificate(string thumbprint)
    {
        using var storeMy = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        storeMy.Open(OpenFlags.ReadOnly);
        var found = storeMy.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), validOnly: false);
        if (found.Count == 0) throw new InvalidOperationException($"no certificate {thumbprint} in LocalMachine\\My");
        var cert = found[0];
        if (!cert.HasPrivateKey) throw new InvalidOperationException($"certificate {thumbprint} has no private key this account can use");
        if (cert.NotAfter < DateTime.Now) throw new InvalidOperationException($"certificate {thumbprint} expired {cert.NotAfter:yyyy-MM-dd}");
        return cert;
    }
}
