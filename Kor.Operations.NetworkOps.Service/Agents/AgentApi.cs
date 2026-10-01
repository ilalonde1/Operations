#nullable enable
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
    Task TouchAgentAsync(int deviceId, string version, string? address, DateTime nowUtc, CancellationToken ct);
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

    public static void Map(IEndpointRouteBuilder app)
    {
        var agent = app.MapGroup("/agent/v1");

        agent.MapPost("/poll", async (AgentPoll body, HttpContext h, IAgentDirectory store, AgentHub hub, ILoggerFactory logs, CancellationToken ct) =>
        {
            var who = await AuthenticateAsync(h, store, ct).ConfigureAwait(false);
            if (who is null) return Results.Unauthorized();
            var log = logs.CreateLogger("NetworkOps.Agents");

            var address = h.Connection.RemoteIpAddress?.ToString();
            var seen = hub.Seen(who.DeviceName, body.Version, body.WorkDir, address);
            if (seen.CameBack)
            {
                log.LogInformation("Agent {Device} {Version} connected from {Address}{Away}", who.DeviceName, body.Version, address,
                    seen.PreviousPollUtc is { } p ? $" (last heard {p:yyyy-MM-dd HH:mm} UTC)" : "");
                // A PC that missed the hourly sweep (off, or a laptop away) is checked the moment it is back.
                if (await store.QueueCheckIfStaleAsync(who.DeviceName, CatchUpAfter, "agent: back online", ct).ConfigureAwait(false) is { } trigger)
                    log.LogInformation("Agent {Device}: last health check older than {Min} min, check {Trigger} queued", who.DeviceName, CatchUpAfter.TotalMinutes, trigger);
            }
            var now = DateTime.UtcNow;
            if (!LastTouch.TryGetValue(who.DeviceId, out var last) || now - last > TouchEvery)
            {
                LastTouch[who.DeviceId] = now;
                await store.TouchAgentAsync(who.DeviceId, body.Version, address, now, ct).ConfigureAwait(false);
            }

            AgentJobMessage? job;
            try { job = await hub.NextJobAsync(who.DeviceName, AgentHub.PollHold, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return Results.NoContent(); }   // the agent hung up, or the service is stopping
            if (job is null) return Results.NoContent();
            log.LogInformation("Agent {Device}: job {Job} handed over", who.DeviceName, job.JobId);
            return Results.Ok(job);
        });

        agent.MapPost("/jobs/{jobId}/result", async (string jobId, AgentJobOutcome body, HttpContext h, IAgentDirectory store, AgentHub hub, CancellationToken ct) =>
        {
            var who = await AuthenticateAsync(h, store, ct).ConfigureAwait(false);
            if (who is null) return Results.Unauthorized();
            return hub.Complete(who.DeviceName, jobId, body) ? Results.NoContent() : Results.NotFound();
        });
    }

    /// <summary>The PC the request is from, or null. Refused: no or malformed headers, a PC with no agent row, a removed agent, a wrong key.</summary>
    internal static async Task<NetworkOpsStore.AgentCredential?> AuthenticateAsync(HttpContext h, IAgentDirectory store, CancellationToken ct)
    {
        var device = h.Request.Headers[DeviceHeader].ToString();
        var auth = h.Request.Headers.Authorization.ToString();
        if (device.Length is 0 or > 64 || !auth.StartsWith(Scheme + " ", StringComparison.Ordinal)) return null;
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
