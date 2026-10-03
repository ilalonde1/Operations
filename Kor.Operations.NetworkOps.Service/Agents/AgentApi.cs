#nullable enable
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.NetworkOps.Service.Agents;

// The two calls an agent makes (Agent/Protocol.cs is the other side), on the Command Center's own HTTPS listener:
// the same port, already open to the LAN and the VPN, and the same certificate, which the agent pins.
//
// Who is calling: every request names its PC (X-Kor-Agent) and carries that PC's key (Authorization: KorAgent ...).
// The installer made the key on the PC, where only SYSTEM and Administrators can read it, and stored only its
// SHA-256 here; the installer could write it because only an administrator can install. So a key that hashes to a
// PC's row is that PC. Nothing here is reachable with an Entra token, and nothing in /api with an agent key.
/// <summary>What the agent endpoints need from the store (NetworkOpsStore in the service; a fake in tests).</summary>
internal interface IAgentDirectory
{
    Task<NetworkOpsStore.AgentCredential?> AgentCredentialAsync(string deviceName, CancellationToken ct);
    /// <summary>Records contact for the agent holding THIS key: an old key's request can never mark a new install confirmed.</summary>
    Task TouchAgentAsync(int deviceId, byte[] secretSha256, string version, string? address, DateTime nowUtc, CancellationToken ct);
    Task<long?> QueueCheckIfStaleAsync(string deviceName, TimeSpan maxAge, string by, CancellationToken ct);
}

internal static class AgentApi
{
    public const string DeviceHeader = "X-Kor-Agent";
    public const string Scheme = "KorAgent";

    /// <summary>How often a connected agent's contact is written to SQL. Live status comes from the hub, not from here.</summary>
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<int, DateTime> LastTouch = new();

    /// <summary>Health checked within this long needs no catch-up when a PC's agent comes back.</summary>
    public static readonly TimeSpan CatchUpAfter = TimeSpan.FromMinutes(60);

    /// <summary>A PC whose agent reconnects (AgentHub.RestartGap) is checked unless it was checked within this. Short on
    /// purpose: a check just BEFORE a restart (the one every fix queues) must not stand in for the check after it.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(1);

    /// <summary>Agent requests in flight at once, whoever sends them: ~40 agents each hold one poll; the rest is headroom.</summary>
    public const int MaxInFlight = 256;
    /// <summary>A poll is a few hundred bytes; a result can be as large as the network route allows (8 M characters of JSON).</summary>
    public const long MaxPollBytes = 16 * 1024, MaxResultBytes = 40L * 1024 * 1024;
    /// <summary>
    /// Key look-ups (one SQL read each) running at once. Separate from <see cref="MaxInFlight"/> and released as soon as
    /// the look-up ends, so bogus keys -- however many, however slow -- can only ever wait here, never take a slot a
    /// real agent's held poll or result needs (Codex re-check 2026-09-30, finding 9).
    /// </summary>
    public const int MaxAuthenticating = 16;
    private static readonly SemaphoreSlim InFlight = new(MaxInFlight, MaxInFlight);
    private static readonly SemaphoreSlim Authenticating = new(MaxAuthenticating, MaxAuthenticating);
    private const string Caller = "kor.agent";

    /// <summary>
    /// In front of every /agent request, BEFORE its body is read (Codex audit 2026-09-30, finding 9): set the body limit
    /// for that route, check the key within the look-up budget, and only then take a working slot. Only an authenticated
    /// request reaches an endpoint, and only authenticated requests hold slots.
    /// </summary>
    /// <summary>The two /agent routes a PC uses BEFORE it has a key (AgentEnrolment): the package download, which holds no
    /// secret, and the code-for-key trade, which the one-time code and its rate limit guard. Everything else needs the key.</summary>
    internal static bool IsBeforeKey(PathString path) => path == "/agent/v1/enrol" || path == "/agent/v1/package";

    public static void UseAgentGate(IApplicationBuilder app)
        => app.UseWhen(h => h.Request.Path.StartsWithSegments("/agent") && !IsBeforeKey(h.Request.Path), branch => branch.Use(async (HttpContext h, RequestDelegate next) =>
        {
            if (h.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                size.MaxRequestBodySize = h.Request.Path.Value?.EndsWith("/result", StringComparison.Ordinal) == true ? MaxResultBytes : MaxPollBytes;

            if (!await Authenticating.WaitAsync(TimeSpan.FromSeconds(5), h.RequestAborted).ConfigureAwait(false))
            {
                h.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            NetworkOpsStore.AgentCredential? who;
            try { who = await AuthenticateAsync(h, h.RequestServices.GetRequiredService<IAgentDirectory>(), h.RequestAborted).ConfigureAwait(false); }
            finally { Authenticating.Release(); }
            if (who is null) { h.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }

            if (!InFlight.Wait(0)) { h.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            try
            {
                h.Items[Caller] = who;
                await next(h).ConfigureAwait(false);
            }
            finally { InFlight.Release(); }
        }));

    private static NetworkOpsStore.AgentCredential Who(HttpContext h) => (NetworkOpsStore.AgentCredential)h.Items[Caller]!;

    private static string KeyOf(NetworkOpsStore.AgentCredential who) => Convert.ToHexString(who.SecretSha256);

    /// <summary>
    /// A work folder as the agent reports it: a plain local path, ASCII only. It is spliced into the script the agent
    /// runs, so anything that could close a PowerShell string -- including the typographic quotes PowerShell also
    /// accepts -- is refused here, not trusted (Codex audit 2026-09-30, work-directory finding).
    /// </summary>
    internal static bool IsSafeWorkDir(string? path)
        => path is { Length: > 3 and <= 200 } && System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\[A-Za-z0-9 _.()\\-]+$") && !path.Contains("..", StringComparison.Ordinal);

    public static void Map(IEndpointRouteBuilder app)
    {
        var agent = app.MapGroup("/agent/v1");

        // A PC outside the domain, before it has a key (AgentEnrolment; the agent side is Agent/Enrol.cs).
        agent.MapGet("/package", () => Results.File(AgentEnrolment.PackageZip(), "application/zip", "kor-agent.zip"));
        agent.MapPost("/enrol", async (EnrolBody body, HttpContext h, AgentEnrolment enrolment, NetworkOpsStore store, AgentHub hub, ILoggerFactory logs, CancellationToken ct) =>
        {
            var log = logs.CreateLogger("Kor.Operations.NetworkOps.Agents.Enrol");
            var from = h.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (body.Device is not { Length: > 0 and <= 64 } || body.Code is not { Length: > 0 and <= 40 }) return Results.BadRequest();
            if (enrolment.Redeem(body.Device, body.Code, DateTime.UtcNow) is not { } hit)
            {
                log.LogWarning("Enrolment refused for {Device} from {Address}: wrong, used or expired code", body.Device, from);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var key = NewKey();
            var hash = Hash(key);
            await store.SaveAgentAsync(hit.DeviceId, hash, AgentInstaller.PackageVersionOrNull() ?? "unknown", $"enrolled with a code from {hit.By}", ct).ConfigureAwait(false);
            hub.Revoke(body.Device, Convert.ToHexString(hash));   // from here only this key is accepted for the PC
            await store.AddNoteAsync(hit.DeviceId, "NetworkOps", $"Agent enrolled from {from} with a one-time code issued by {hit.By}.", ct).ConfigureAwait(false);
            log.LogInformation("Agent enrolled: {Device} from {Address}, code issued by {By}", body.Device, from, hit.By);
            return Results.Ok(new { key });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4096));

        agent.MapPost("/poll", async (AgentPoll body, HttpContext h, IAgentDirectory store, AgentHub hub, ILoggerFactory logs, CancellationToken ct) =>
        {
            var who = Who(h);
            if (!IsSafeWorkDir(body.WorkDir) || body.Version is not { Length: > 0 and <= 32 })
                return Results.BadRequest(new { error = "workDir must be a plain local path, version 1-32 characters" });
            var log = logs.CreateLogger("NetworkOps.Agents");

            var address = h.Connection.RemoteIpAddress?.ToString();
            var key = KeyOf(who);
            // Null: this key was replaced or removed after it authenticated (the hub, not SQL, has the last word now).
            if (hub.Seen(who.DeviceName, body.Version, address, key) is not { } seen) return Results.Unauthorized();
            if (seen.CameBack)
            {
                log.LogInformation("Agent {Device} {Version} connected from {Address}{Away}", who.DeviceName, body.Version, address,
                    seen.PreviousPollUtc is { } p ? $" (last heard {p:yyyy-MM-dd HH:mm} UTC)" : "");
                // A PC that missed the hourly sweep (off, or a laptop away) is checked the moment it is back.
                if (await store.QueueCheckIfStaleAsync(who.DeviceName, CatchUpAfter, "agent: back online", ct).ConfigureAwait(false) is { } trigger)
                    log.LogInformation("Agent {Device}: last health check older than {Min} min, check {Trigger} queued", who.DeviceName, CatchUpAfter.TotalMinutes, trigger);
            }
            else if (seen.Reconnected)
            {
                // Most often a restart: check it now, so the page (and its Windows Update search) reflects the restart within
                // a minute instead of at the next hourly sweep. At most one check per RecheckAfter, however often it blips.
                if (await store.QueueCheckIfStaleAsync(who.DeviceName, RecheckAfter, "agent: reconnected (restarted?)", ct).ConfigureAwait(false) is { } trigger)
                    log.LogInformation("Agent {Device} reconnected after {Gap:0} s, check {Trigger} queued", who.DeviceName,
                        (DateTime.UtcNow - seen.PreviousPollUtc!.Value).TotalSeconds, trigger);
            }
            var now = DateTime.UtcNow;
            if (seen.CameBack || !LastTouch.TryGetValue(who.DeviceId, out var last) || now - last > TouchEvery)
            {
                LastTouch[who.DeviceId] = now;
                await store.TouchAgentAsync(who.DeviceId, who.SecretSha256, body.Version, address, now, ct).ConfigureAwait(false);
            }

            AgentJobMessage? job;
            try { job = await hub.NextJobAsync(who.DeviceName, key, body.WorkDir, AgentHub.PollHold, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return Results.NoContent(); }   // the agent hung up, or the service is stopping
            if (job is null) return Results.NoContent();
            log.LogInformation("Agent {Device}: job {Job} handed over", who.DeviceName, job.JobId);
            return Results.Ok(job);
        });

        agent.MapPost("/jobs/{jobId}/result", (string jobId, AgentJobOutcome body, HttpContext h, AgentHub hub)
            => hub.Complete(Who(h).DeviceName, KeyOf(Who(h)), jobId, body) ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>The PC the request is from, or null. Refused: no or malformed headers, a PC with no agent row, a removed agent, a wrong key.</summary>
    internal static async Task<NetworkOpsStore.AgentCredential?> AuthenticateAsync(HttpContext h, IAgentDirectory store, CancellationToken ct)
    {
        var device = h.Request.Headers[DeviceHeader].ToString();
        var auth = h.Request.Headers.Authorization.ToString();
        if (!System.Text.RegularExpressions.Regex.IsMatch(device, "^[A-Za-z0-9-]{1,64}$") || !auth.StartsWith(Scheme + " ", StringComparison.Ordinal)) return null;
        var key = auth[(Scheme.Length + 1)..].Trim();
        if (key.Length is < 32 or > 256) return null;

        var row = await store.AgentCredentialAsync(device, ct).ConfigureAwait(false);
        if (row is null || row.Removed) return null;
        return Matches(key, row.SecretSha256) ? row : null;
    }

    internal static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    internal static bool Matches(string key, byte[] storedSha256) => CryptographicOperations.FixedTimeEquals(Hash(key), storedSha256);

    /// <summary>A new agent key: 256 random bits, as text that survives a header and a file unchanged.</summary>
    internal static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
