#nullable enable
using System.Collections.Concurrent;
using System.Threading.Channels;
using Kor.Operations.NetworkOps.Core.OnTarget;
using Kor.Operations.NetworkOps.Transport;

namespace Kor.Operations.NetworkOps.Service.Agents;

// The meeting point between whoever wants a script run on a PC (the health sweep, the fix runner) and that PC's
// agent, which is sitting in a held-open poll waiting for work. A job is offered to the agent's queue; the
// agent's poll takes it and is answered with the script; the agent posts the result, and the caller gets back
// exactly what the network route would have given it -- an OnTargetRun -- so nothing downstream knows or cares
// which route ran it.
//
// In memory on purpose: a job lives for minutes, the service is one process, and a restart loses nothing a
// restart does not already lose on the network route (a fix left running is marked failed at startup).
internal sealed class AgentHub(TimeProvider clock)
{
    /// <summary>How long a poll is held open waiting for work. The agent's own HTTP timeout is longer.</summary>
    public static readonly TimeSpan PollHold = TimeSpan.FromSeconds(25);

    /// <summary>An agent that has polled within this is connected (a poll is held 25 s, then the next starts at once).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(90);

    /// <summary>A connected agent takes a job within a second; one that has not in this long is treated as gone.</summary>
    public static readonly TimeSpan PickupWait = TimeSpan.FromSeconds(40);

    /// <summary>Time for the result to cross back after the script's own timeout.</summary>
    public static readonly TimeSpan ResultGrace = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Connection> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingJob> _jobs = new(StringComparer.Ordinal);

    /// <param name="KeyHash">SHA-256 (hex) of the key this agent last authenticated with: an install is confirmed only by
    /// a poll made with the NEW key, never by an old connection closing (Codex audit 2026-09-30, finding 5).</param>
    public sealed record AgentStatus(string Version, DateTime LastPollUtc, bool Connected, string? Address, string KeyHash);

    public bool IsConnected(string device)
        => _agents.TryGetValue(device, out var a) && a.LastPollUtc != default && clock.GetUtcNow().UtcDateTime - a.LastPollUtc < Fresh;

    public AgentStatus? Status(string device)
        => _agents.TryGetValue(device, out var a) ? new AgentStatus(a.Version, a.LastPollUtc, IsConnected(device), a.Address, a.KeyHash) : null;

    /// <summary>What a poll learned, for the caller to act on (record the contact, check a PC that was away).</summary>
    public sealed record PollSeen(bool CameBack, DateTime? PreviousPollUtc);

    /// <summary>
    /// Records that the agent is here, authenticated with the key hashing to <paramref name="keyHash"/>. Returns whether
    /// it had been away (never seen by this process, revoked since, or silent 10+ minutes).
    /// </summary>
    public PollSeen Seen(string device, string version, string? address, string keyHash)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var a = _agents.GetOrAdd(device, _ => new Connection());
        lock (a)
        {
            var previous = a.LastPollUtc == default ? (DateTime?)null : a.LastPollUtc;
            a.LastPollUtc = now;
            a.Version = version;
            a.Address = address;
            a.KeyHash = keyHash;
            return new PollSeen(previous is null || now - previous.Value > TimeSpan.FromMinutes(10), previous);
        }
    }

    /// <summary>
    /// The PC's key has just been replaced or removed: every poll held open with the old one is cut at once (it can no
    /// longer be handed a job), and the PC counts as not connected until a poll with a valid key arrives
    /// (Codex audit 2026-09-30, finding 6). Jobs already queued wait for that poll, or fall back to the network route.
    /// </summary>
    public void Revoke(string device)
    {
        if (!_agents.TryGetValue(device, out var a)) return;
        CancellationTokenSource old;
        lock (a)
        {
            old = a.Revoked;
            a.Revoked = new CancellationTokenSource();
            a.LastPollUtc = default;
            a.KeyHash = "";
        }
        old.Cancel();
        old.Dispose();
    }

    /// <summary>
    /// The agent's held-open poll: the next job for it, or null once <paramref name="hold"/> passes with nothing to do
    /// (or the PC's key is revoked meanwhile). The job comes back as the complete script, wrapped to publish its result
    /// into <paramref name="workDir"/> -- the folder THIS poll reported, never one left behind by another request.
    /// </summary>
    public async Task<AgentJobMessage?> NextJobAsync(string device, string workDir, TimeSpan hold, CancellationToken ct)
    {
        if (!_agents.TryGetValue(device, out var a)) return null;
        CancellationToken revoked;
        lock (a) revoked = a.Revoked.Token;
        using var held = CancellationTokenSource.CreateLinkedTokenSource(ct, revoked);
        held.CancelAfter(hold);
        try
        {
            while (true)
            {
                var job = await a.Queue.Reader.ReadAsync(held.Token).ConfigureAwait(false);
                // A job its caller already gave up on is skipped, never run late.
                if (Interlocked.Exchange(ref job.Claimed, 1) != 0) continue;
                string script;
                try { script = OnTargetPayload.Build(job.Body, Path.Combine(workDir, job.Id + ".json")); }
                catch (ArgumentException ex)
                {
                    job.Done.TrySetResult(new AgentJobOutcome("error", null, $"cannot build the job for this agent: {ex.Message}", 0));
                    job.PickedUp.TrySetResult();
                    continue;
                }
                job.PickedUp.TrySetResult();
                return new AgentJobMessage(job.Id, script, (int)job.Timeout.TotalSeconds, job.WantsIdle);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;   // the hold ran out (or the key was revoked): "nothing to do", and the agent polls again
        }
        finally
        {
            // A poll that ended normally is evidence the agent is alive; one cut by a revocation is not.
            if (!revoked.IsCancellationRequested) lock (a) a.LastPollUtc = clock.GetUtcNow().UtcDateTime;
        }
    }

    /// <summary>The agent's result. False when nobody is waiting for that job (unknown, someone else's, or given up).</summary>
    public bool Complete(string device, string jobId, AgentJobOutcome outcome)
        => _jobs.TryGetValue(jobId, out var job) && job.Device.Equals(device, StringComparison.OrdinalIgnoreCase) && job.Done.TrySetResult(outcome);

    /// <summary>
    /// Runs <paramref name="body"/> on <paramref name="device"/> through its agent. Null when the agent is not
    /// connected or did not take the job in time -- the caller then uses the network route instead.
    /// </summary>
    public async Task<OnTargetRun?> RunAsync(string device, string body, TimeSpan timeout, bool wantsIdle, CancellationToken ct)
    {
        if (!IsConnected(device) || !_agents.TryGetValue(device, out var a)) return null;
        var started = clock.GetTimestamp();
        var job = new PendingJob(Guid.NewGuid().ToString("N"), device, body, timeout, wantsIdle);
        _jobs[job.Id] = job;
        try
        {
            a.Queue.Writer.TryWrite(job);
            if (!await WithinAsync(job.PickedUp.Task, PickupWait, ct).ConfigureAwait(false)
                && Interlocked.Exchange(ref job.Claimed, 1) == 0)
                return null;   // not taken: withdrawn, so the agent can never run it late as well

            if (!await WithinAsync(job.Done.Task, timeout + ResultGrace, ct).ConfigureAwait(false))
                return OnTargetRun.Failed(device, OnTargetStatus.Timeout, $"the agent took the job but sent no result within {(timeout + ResultGrace).TotalSeconds:0} s");
            return ToRun(device, await job.Done.Task.ConfigureAwait(false), clock.GetElapsedTime(started));
        }
        finally
        {
            // Whatever ended the wait -- a result, a timeout, or the caller cancelling -- a job nobody has taken yet is
            // withdrawn, so a poll that comes later can never run what its caller abandoned (Codex audit, finding 2).
            Interlocked.Exchange(ref job.Claimed, 1);
            _jobs.TryRemove(job.Id, out _);
        }
    }

    /// <summary>What the agent sent, as the network route would have reported it.</summary>
    internal static OnTargetRun ToRun(string device, AgentJobOutcome o, TimeSpan total)
    {
        var stages = $"agent: ran {o.ElapsedMs} ms, {total.TotalMilliseconds:0} ms in all";
        OnTargetRun run;
        switch (o.Status)
        {
            case "ok" when o.Result is not null:
                OnTargetResult result;
                try { result = OnTargetPayload.ParseResult(o.Result); }
                catch (System.Text.Json.JsonException ex) { run = OnTargetRun.Failed(device, OnTargetStatus.ScriptError, $"the agent's result is not JSON: {ex.Message}"); break; }
                run = result.Ok
                    ? new OnTargetRun(device, OnTargetStatus.Ok, result.OutputJson, null)
                    : OnTargetRun.Failed(device, OnTargetStatus.ScriptError, $"line {result.Line}: {result.Error}");
                break;
            case "timeout":
                run = OnTargetRun.Failed(device, OnTargetStatus.Timeout, o.Error ?? "timed out on the PC");
                break;
            default:
                run = OnTargetRun.Failed(device, OnTargetStatus.ScriptError, o.Error ?? $"the agent reported '{o.Status}'");
                break;
        }
        return run with { Stages = stages, TotalMs = (int)total.TotalMilliseconds };
    }

    private async Task<bool> WithinAsync(Task task, TimeSpan limit, CancellationToken ct)
    {
        if (task.IsCompleted) return true;
        var winner = await Task.WhenAny(task, Task.Delay(limit, clock, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return winner == task;
    }

    private sealed class Connection
    {
        public DateTime LastPollUtc;
        public string Version = "";
        public string? Address;
        public string KeyHash = "";
        /// <summary>Cancelled (and replaced) when the PC's key is replaced or removed: cuts polls held with the old key.</summary>
        public CancellationTokenSource Revoked = new();
        public readonly Channel<PendingJob> Queue = Channel.CreateUnbounded<PendingJob>();
    }

    private sealed class PendingJob(string id, string device, string body, TimeSpan timeout, bool wantsIdle)
    {
        public string Id { get; } = id;
        public string Device { get; } = device;
        public string Body { get; } = body;
        public TimeSpan Timeout { get; } = timeout;
        public bool WantsIdle { get; } = wantsIdle;
        public int Claimed;
        public TaskCompletionSource PickedUp { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AgentJobOutcome> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>What the agent's poll is answered with (Agent/Protocol.cs, Job).</summary>
internal sealed record AgentJobMessage(string JobId, string Script, int TimeoutSeconds, bool WantsIdle);

/// <summary>What the agent posts back (Agent/Protocol.cs, JobResult).</summary>
internal sealed record AgentJobOutcome(string Status, string? Result, string? Error, long ElapsedMs);

/// <summary>What the agent's poll carries (Agent/Protocol.cs, PollRequest). WorkDir is checked by AgentApi.IsSafeWorkDir.</summary>
internal sealed record AgentPoll(string Version, string WorkDir);
