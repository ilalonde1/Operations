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
internal sealed class ApiHost(IOptions<NetworkOpsOptions> options, NetworkOpsStore store, Power.PowerState power, ILoggerFactory loggers, ILogger<ApiHost> log) : BackgroundService
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
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
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
            var who = http.User.Identity?.IsAuthenticated == true ? ApiAccess.UserOf(http.User) : "anonymous";
            var why = http.Response.StatusCode is 401 or 403 ? ApiAccess.Deny(http.User, o.ApiTenantId) : null;
            audit.LogInformation("API {Who} {Method} {Path} -> {Status}{Why}", who, http.Request.Method, http.Request.Path, http.Response.StatusCode, why is null ? "" : $" ({why})");
        });
        app.UseAuthentication();
        app.UseAuthorization();
        Map(app);

        log.LogInformation("Command Center API listening on https://*:{Port} (certificate {Subject}, expires {Expiry:yyyy-MM-dd})", o.ApiPort, cert.Subject, cert.NotAfter);
        await app.RunAsync(ct).ConfigureAwait(false);
    }

    private static void Map(WebApplication app)
    {
        // Unauthenticated liveness: says only that the API is up and which version, so reachability and
        // the certificate can be checked without a token.
        app.MapGet("/api/ping", () => Results.Ok(new { status = "ok", version = Jobs.JobDispatcher.Version }));

        var api = app.MapGroup("/api").RequireAuthorization("CommandCenter");
        api.MapGet("/fleet", (NetworkOpsStore s, CancellationToken ct) => s.FleetSnapshotAsync(ct));
        // The rack (hosts, storage, UPSes, backup, network, internet): the same shape as /fleet, served apart so a
        // page that predates the rack never lists a host or a UPS as a PC.
        api.MapGet("/rack", (NetworkOpsStore s, CancellationToken ct) => s.FleetSnapshotAsync(ct, rack: true));
        api.MapGet("/devices/{id:int}/history", (int id, NetworkOpsStore s, CancellationToken ct) => s.DeviceHistoryAsync(id, ct));
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
                ?? (dev.Source == "Rack" && !o.Value.Rack.Any(r => r.Name.Equals(dev.Name, StringComparison.OrdinalIgnoreCase) && r.Collector == "WindowsServer")
                    ? $"{dev.Name} is not a Windows machine: fixes run through Windows' service manager" : null)
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
        api.MapGet("/actions/{id:long}", async (long id, NetworkOpsStore s, CancellationToken ct) =>
            await s.ActionAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound());

        api.MapPost("/devices/{id:int}/notes", async (int id, NoteRequest body, HttpContext h, NetworkOpsStore s, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(body.Body) ? Results.BadRequest(new { error = "a note needs text" })
            : await s.AddNoteAsync(id, ApiAccess.UserOf(h.User), body.Body, ct) ? Results.NoContent() : Results.NotFound());
    }

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
