#nullable enable
using System.Collections.Concurrent;
using System.ComponentModel;
using Kor.Operations.NetworkOps.Core.OnTarget;

namespace Kor.Operations.NetworkOps.Transport;

// Runs a PowerShell probe ON a workstation as SYSTEM, using only svcctl and c$:
//   1. the payload is written to C:\Windows\Temp over c$ (UTF-8 with BOM -- the targets run
//      Windows PowerShell 5.1);
//   2. a one-shot service whose binary is `cmd /c start "" powershell -File <payload>` is
//      created, started and deleted. cmd detaches powershell and exits, so the SCM reports
//      1053 "did not respond" -- the expected outcome, not a failure;
//   3. the payload publishes its JSON result atomically; it is read back as one small file;
//   4. the staged script and result are deleted, and the service is deleted in a finally.
// Only one service call and one file cross the VPN, whatever the probe reads locally.
public sealed class OnTargetChannel
{
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _poll;

    public OnTargetChannel(TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
        _poll = poll ?? TimeSpan.FromMilliseconds(1500);
    }

    public async Task<OnTargetRun> RunAsync(string computer, string script, CancellationToken ct = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = await RunCoreAsync(computer, script, clock, ct).ConfigureAwait(false);
        return run with { TotalMs = (int)clock.ElapsedMilliseconds };
    }

    private async Task<OnTargetRun> RunCoreAsync(string computer, string script, System.Diagnostics.Stopwatch clock, CancellationToken ct)
    {
        var reach = await SmbReachability.ProbeAsync(computer, ct: ct).ConfigureAwait(false);
        if (!reach.Reachable) return OnTargetRun.Failed(computer, OnTargetStatus.Offline, "port 445 not answering on any address");
        var reachMs = (int)clock.ElapsedMilliseconds;

        var id = Guid.NewGuid().ToString("N")[..8];
        var serviceName = "KorRun" + id;
        var localScript = $@"C:\Windows\Temp\korrun-{id}.ps1";
        var localResult = $@"C:\Windows\Temp\korrun-{id}.json";
        var uncScript = $@"\\{computer}\c$\Windows\Temp\korrun-{id}.ps1";
        var uncResult = $@"\\{computer}\c$\Windows\Temp\korrun-{id}.json";

        try
        {
            await File.WriteAllTextAsync(uncScript, OnTargetPayload.Build(script, localResult), OnTargetPayload.ScriptEncoding, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OnTargetRun.Failed(computer, OnTargetStatus.NoAdminShare, ex.Message);
        }

        var stagedMs = (int)clock.ElapsedMilliseconds;
        try
        {
            var launch = await LaunchAsync(computer, serviceName, localScript).ConfigureAwait(false);
            if (launch is not null) return OnTargetRun.Failed(computer, OnTargetStatus.LaunchFailed, launch);
            var launchedMs = (int)clock.ElapsedMilliseconds;

            var deadline = DateTime.UtcNow + _timeout;
            while (!File.Exists(uncResult))
            {
                if (DateTime.UtcNow >= deadline) return OnTargetRun.Failed(computer, OnTargetStatus.Timeout, $"no result after {_timeout.TotalSeconds:0}s");
                await Task.Delay(_poll, ct).ConfigureAwait(false);
            }
            // Second line of defence behind the payload's own limit (a target running an older payload,
            // or one whose script was altered): never pull an oversized file across the VPN.
            var size = new FileInfo(uncResult).Length;
            if (size > (long)OnTargetPayload.MaxResultChars * 4)   // UTF-8: at most 4 bytes a character
                return OnTargetRun.Failed(computer, OnTargetStatus.ScriptError, $"result file is {size / 1048576.0:N1} MB; refused without reading it");
            var result = OnTargetPayload.ParseResult(await File.ReadAllTextAsync(uncResult, ct).ConfigureAwait(false));
            var stages = $"reach {reachMs} ms, staged {stagedMs - reachMs} ms, launch {launchedMs - stagedMs} ms, result after {(int)clock.ElapsedMilliseconds - launchedMs} ms";
            return result.Ok
                ? new OnTargetRun(computer, OnTargetStatus.Ok, result.OutputJson, null) { Stages = stages }
                : OnTargetRun.Failed(computer, OnTargetStatus.ScriptError, $"line {result.Line}: {result.Error}") with { Stages = stages };
        }
        finally
        {
            TryDelete(uncScript);
            TryDelete(uncResult);
        }
    }

    // StartService on a one-shot blocks until the SCM gives up with 1053 -- while the probe itself
    // started in the first moment. So the start runs in the
    // background, the result file is read as soon as it lands, and the service is deleted when the
    // SCM lets go. That is the difference between a "check now" that answers in ~10 s and ~45 s.
    // Pending cleanups are tracked so a process can wait for them before it exits (DrainAsync).
    private static readonly ConcurrentDictionary<Task, byte> PendingCleanups = new();

    /// <summary>Waits for every one-shot service still being cleaned up. Call before the process exits.
    /// Loops, re-snapshotting: a cleanup is registered only once its remote service has started, so a one-shot
    /// Task.WhenAll could take its snapshot before a launch-in-flight registered and strand that KorRun service on exit.
    /// The caller bounds this with its own timeout.</summary>
    public static async Task DrainAsync()
    {
        while (!PendingCleanups.IsEmpty)
            await Task.WhenAll(PendingCleanups.Keys.ToArray()).ConfigureAwait(false);
    }

    /// <summary>The host wires this to its log. It is called when a one-shot KorRun service could not be marked for
    /// deletion, which leaves it installed on the target -- a leak that was previously discarded unlogged.</summary>
    public static Action<string>? CleanupProblem;

    /// <summary>Creates and starts the one-shot service. Returns an error message, or null when the probe is running.</summary>
    private static async Task<string?> LaunchAsync(string computer, string serviceName, string localScript)
    {
        var binPath = $"cmd.exe /c start \"\" powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {localScript}";
        ServiceControlManager.ScmHandle service;
        try
        {
            // The manager handle is pooled (see ServiceControlManager): the connection is the expensive part.
            service = await Task.Run(() => ServiceControlManager.WithPooledManager(computer, m => ServiceControlManager.Create(m, serviceName, binPath))).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return $"{ex.Message} ({ex.NativeErrorCode})";
        }

        var svc = service;
        var start = Task.Run(() => ServiceControlManager.Start(svc));
        var cleanup = start.ContinueWith(_ =>
        {
            try
            {
                var err = ServiceControlManager.MarkForDelete(svc);
                // A failed delete (not "already marked") leaves KorRun<id> installed on the target, invisibly: surface it.
                if (err is not 0 and not ServiceControlManager.ErrorServiceMarkedForDelete)
                    CleanupProblem?.Invoke($"one-shot service '{serviceName}' on {computer} was not deleted: {new Win32Exception(err).Message} ({err}) -- it is left installed");
            }
            finally { svc.Dispose(); }
        }, TaskScheduler.Default);
        PendingCleanups.TryAdd(cleanup, 0);
        _ = cleanup.ContinueWith(t => PendingCleanups.TryRemove(t, out byte _), TaskScheduler.Default);

        // A start that fails outright (access denied, bad path) says so at once; 1053 comes ~21 s later
        // and only means "cmd is not a service" -- powershell is already running by then.
        if (await Task.WhenAny(start, Task.Delay(1500)).ConfigureAwait(false) == start)
        {
            var err = start.Result;
            if (err != 0 && err != ServiceControlManager.ErrorServiceRequestTimeout)
                return $"StartService failed: {new Win32Exception(err).Message} ({err})";
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort; the next census reports leftovers */ }
    }
}

public enum OnTargetStatus { Ok, Offline, NoAdminShare, LaunchFailed, Timeout, ScriptError }

/// <param name="OutputJson">The probe's output as a JSON array, when <see cref="Status"/> is Ok.</param>
public sealed record OnTargetRun(string Computer, OnTargetStatus Status, string? OutputJson, string? Error)
{
    /// <summary>Wall time of the whole run, and where it went -- "check now" has to answer in seconds.</summary>
    public int TotalMs { get; init; }
    public string? Stages { get; init; }

    public static OnTargetRun Failed(string computer, OnTargetStatus status, string error) => new(computer, status, null, error);
}
