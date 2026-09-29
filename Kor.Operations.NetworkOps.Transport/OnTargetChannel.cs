#nullable enable
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
        var reach = await SmbReachability.ProbeAsync(computer, ct: ct).ConfigureAwait(false);
        if (!reach.Reachable) return OnTargetRun.Failed(computer, OnTargetStatus.Offline, "port 445 not answering on any address");

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

        try
        {
            var launch = await Task.Run(() => Launch(computer, serviceName, localScript), ct).ConfigureAwait(false);
            if (launch is not null) return OnTargetRun.Failed(computer, OnTargetStatus.LaunchFailed, launch);

            var deadline = DateTime.UtcNow + _timeout;
            while (!File.Exists(uncResult))
            {
                if (DateTime.UtcNow >= deadline) return OnTargetRun.Failed(computer, OnTargetStatus.Timeout, $"no result after {_timeout.TotalSeconds:0}s");
                await Task.Delay(_poll, ct).ConfigureAwait(false);
            }
            var result = OnTargetPayload.ParseResult(await File.ReadAllTextAsync(uncResult, ct).ConfigureAwait(false));
            return result.Ok
                ? new OnTargetRun(computer, OnTargetStatus.Ok, result.OutputJson, null)
                : OnTargetRun.Failed(computer, OnTargetStatus.ScriptError, $"line {result.Line}: {result.Error}");
        }
        finally
        {
            TryDelete(uncScript);
            TryDelete(uncResult);
        }
    }

    /// <summary>Create, start, delete. Returns an error message, or null when the probe was launched.</summary>
    private static string? Launch(string computer, string serviceName, string localScript)
    {
        var binPath = $"cmd.exe /c start \"\" powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {localScript}";
        try
        {
            using var manager = ServiceControlManager.OpenManager(computer, forCreate: true);
            using var service = ServiceControlManager.Create(manager, serviceName, binPath);
            try
            {
                var err = ServiceControlManager.Start(service);
                // 1053: cmd is not a service, the SCM gives up waiting -- powershell is already running.
                if (err != 0 && err != ServiceControlManager.ErrorServiceRequestTimeout)
                    return $"StartService failed: {new Win32Exception(err).Message} ({err})";
                return null;
            }
            finally
            {
                ServiceControlManager.MarkForDelete(service);
            }
        }
        catch (Win32Exception ex)
        {
            return $"{ex.Message} ({ex.NativeErrorCode})";
        }
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
    public static OnTargetRun Failed(string computer, OnTargetStatus status, string error) => new(computer, status, null, error);
}
