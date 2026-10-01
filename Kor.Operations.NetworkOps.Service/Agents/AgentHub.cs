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
    /// it had been away (never seen by this process, revoked since, or silent 10+ minutes) -- or NULL when that key is no
    /// longer the PC's: it authenticated against SQL before a revocation and reached here after it (Codex re-check).
    /// </summary>
    public PollSeen? Seen(string device, string version, string? address, string keyHash)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var a = _agents.GetOrAdd(device, _ => new Connection());
        lock (a)
        {
            if (!a.Accepts(keyHash)) return null;
            var previous = a.LastPollUtc == default ? (DateTime?)null : a.LastPollUtc;
            a.LastPollUtc = now;
            a.Version = version;
            a.Address = address;
            a.KeyHash = keyHash;
            return new PollSeen(previous is null || now - previous.Value > TimeSpan.FromMinutes(10), previous);
        }
    }

    /// <summary>
    /// The PC's key has just been replaced (<paramref name="currentKeyHash"/> = the new key's hash) or removed (null).
    /// From here only that key is accepted by THIS process, whatever a request authenticated with earlier; every poll held
    /// open with another key is cut at once; and the PC counts as not connected until a poll with the current key
    /// arrives (Codex audit 2026-09-30, finding 6, and its re-check). Queued jobs wait for that poll or fall back.
    /// </summary>
    public void Revoke(string device, string? currentKeyHash)
    {
        var a = _agents.GetOrAdd(device, _ => new Connection());
        CancellationTokenSource old;
        lock (a)
        {
            old = a.Revoked;
            a.Revoked = new CancellationTokenSource();
            a.Authorized = currentKeyHash ?? "";
            a.LastPollUtc = default;
            a.KeyHash = "";
        }
        old.Cancel();
        old.Dispose();
    }

    /// <summary>
    /// The agent's held-open poll, made with the key hashing to <paramref name="keyHash"/>: the next job for it, or null
    /// once <paramref name="hold"/> passes with nothing to do, or that key is revoked meanwhile. The job comes back as the
    /// complete script, wrapped to publish its result into <paramref name="workDir"/> -- the folder THIS poll reported.
    /// The key is checked again, under the lock revocation takes, after a job is dequeued and before it is handed over.
    /// </summary>
    public async Task<AgentJobMessage?> NextJobAsync(string device, string keyHash, string workDir, TimeSpan hold, CancellationToken ct)
    {
        if (!_agents.TryGetValue(device, out var a)) return null;
        CancellationToken revoked;
        lock (a)
        {
            if (!a.Accepts(keyHash)) return null;
            revoked = a.Revoked.Token;
        }
        using var held = CancellationTokenSource.CreateLinkedTokenSource(ct, revoked);
        held.CancelAfter(hold);
        try
        {
            while (true)
            {
                var job = await a.Queue.Reader.ReadAsync(held.Token).ConfigureAwait(false);
                bool stale;
                lock (a) stale = revoked.IsCancellationRequested || !a.Accepts(keyHash);
                if (stale)
                {
                    a.Queue.Writer.TryWrite(job);   // back in the queue, unclaimed, for a poll with the current key
                    return null;
                }
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
                job.HandedToKey = keyHash;
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

    /// <summary>
    /// The agent's result. False when nobody is waiting for that job (unknown, given up), or it was not handed to a poll
    /// made with this same key on this same PC.
    /// </summary>
    public bool Complete(string device, string keyHash, string jobId, AgentJobOutcome outcome)
        => _jobs.TryGetValue(jobId, out var job) && job.Device.Equals(device, StringComparison.OrdinalIgnoreCase)
           && job.HandedToKey == keyHash && job.Done.TrySetResult(outcome);

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
        /// <summary>
        /// The one key hash this process accepts for the PC once an install or removal has run here (null: nothing has
        /// since the service started, so SQL's answer at authentication is the authority; "": removed, accept none).
        /// </summary>
        public string? Authorized;
        /// <summary>Cancelled (and replaced) when the PC's key is replaced or removed: cuts polls held with the old key.</summary>
        public CancellationTokenSource Revoked = new();

        /// <summary>Call under lock(this).</summary>
        public bool Accepts(string keyHash) => Authorized is null || (Authorized.Length > 0 && Authorized == keyHash);
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
        /// <summary>The key hash of the poll it was handed to: only a result sent with that key completes it.</summary>
        public volatile string? HandedToKey;
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
