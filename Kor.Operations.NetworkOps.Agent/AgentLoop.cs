using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// Ask APP01 for work, forever. The server holds each poll open for up to ~25 s and answers the moment it has
/// something, so a "check now" or a fix reaches the PC in about a second without the PC listening on anything.
/// Jobs run alongside the polling: a 40-minute repair on this PC never stops a health check from arriving.
/// </summary>
internal sealed class AgentLoop
{
    /// <summary>The largest result ever sent back, in bytes: the same limit the network route applies (8 M characters, UTF-8).</summary>
    public const long MaxResultBytes = 8L * 1024 * 1024 * 4;

    private readonly AgentSettings _s;
    private readonly AgentLog _log;
    private readonly HttpClient _http;

    public AgentLoop(AgentSettings settings, AgentLog log)
    {
        _s = settings;
        _log = log;
        var handler = new HttpClientHandler
        {
            // The server's certificate is self-signed; it is trusted by its hash and by nothing else, so a
            // machine that only claims to be APP01 never hands this PC a script.
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && Sha256Hex(cert.RawData) == _s.ServerCertSha256,
            UseProxy = false,
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(_s.ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(75) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("KorNetworkOpsAgent/" + AgentSettings.Version);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /* Tls13 */;
        Directory.CreateDirectory(_s.WorkDir);
        ClearLeftovers();
        var key = File.ReadAllText(_s.KeyPath).Trim();
        _http.DefaultRequestHeaders.Add("X-Kor-Agent", _s.Device);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("KorAgent", key);
        _log.Info($"agent {AgentSettings.Version} started as {Environment.UserName} on {_s.Device}; server {_s.ServerUrl}");

        var backoff = 5;
        string? lastProblem = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await PollAsync(ct).ConfigureAwait(false);
                if (lastProblem is not null) _log.Info("connected to the server again");
                lastProblem = null;
                backoff = 5;
                if (job is not null) _ = Task.Run(() => RunJobAsync(job, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Off the network, APP01 down, or refused: wait, longer each time, and say so once, not every minute.
                var problem = Describe(ex);
                if (problem != lastProblem) _log.Warn($"cannot reach the server: {problem} (retrying, up to every 2 min)");
                lastProblem = problem;
                try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 120);
            }
        }
    }

    private async Task<Job?> PollAsync(CancellationToken ct)
    {
        var body = Json.Write(new PollRequest { Version = AgentSettings.Version, WorkDir = _s.WorkDir });
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _http.PostAsync("agent/v1/poll", content, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
        return Json.Read<Job>(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }

    private async Task RunJobAsync(Job job, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        JobResult result;
        try
        {
            int? idle = job.WantsIdle ? ConsoleIdle.ForConsoleUser(_log) : null;
            result = await JobRunner.RunAsync(job, _s.WorkDir, idle, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new JobResult { Status = "error", Error = ex.Message };
        }
        result.ElapsedMs = clock.ElapsedMilliseconds;
        _log.Info($"job {job.JobId}: {result.Status} in {result.ElapsedMs} ms{(result.Error is null ? "" : " -- " + result.Error)}");

        // The result is the point of the job: try a few times before giving up on it.
        for (var attempt = 1; attempt <= 4 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var content = new ByteArrayContent(Json.Write(result));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await _http.PostAsync($"agent/v1/jobs/{Uri.EscapeDataString(job.JobId)}/result", content, ct).ConfigureAwait(false);
                // 404: the server no longer waits for it (it restarted, or gave up and used the network route).
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
                _log.Warn($"job {job.JobId}: result refused, {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.Warn($"job {job.JobId}: sending the result failed: {Describe(ex)}"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>A job's files are deleted when it ends; any still here are from an agent that was stopped mid-job.</summary>
    private void ClearLeftovers()
    {
        foreach (var f in Directory.GetFiles(_s.WorkDir))
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        return inner == ex ? ex.Message : ex.Message + " / " + inner.Message;
    }

    internal static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("X2")));
    }
}

/// <summary>Runs one job's script with the PC's own Windows PowerShell 5.1, as this process's account (SYSTEM as a service).</summary>
internal static class JobRunner
{
    private static readonly Encoding ScriptEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static async Task<JobResult> RunAsync(Job job, string workDir, int? consoleIdleSeconds, CancellationToken ct)
    {
        if (job.JobId.Length == 0 || job.JobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || job.JobId.Contains(".."))
            return new JobResult { Status = "error", Error = "bad job id" };
        var script = Path.Combine(workDir, job.JobId + ".ps1");
        var result = Path.Combine(workDir, job.JobId + ".json");
        try
        {
            // PowerShell 5.1 reads a BOM-less script as ANSI: one em-dash and nothing runs.
            File.WriteAllText(script, job.Script, ScriptEncoding);
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };
            if (consoleIdleSeconds is { } idle) psi.EnvironmentVariables["KOR_CONSOLE_IDLE_SECONDS"] = idle.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell did not start");
            var exited = await WaitForExitAsync(p, TimeSpan.FromSeconds(Math.Max(10, job.TimeoutSeconds)), ct).ConfigureAwait(false);
            if (!exited)
            {
                KillTree(p.Id);
                return new JobResult { Status = "timeout", Error = $"no result after {job.TimeoutSeconds} s" };
            }
            if (!File.Exists(result)) return new JobResult { Status = "error", Error = $"powershell exited {p.ExitCode} without publishing a result" };
            var size = new FileInfo(result).Length;
            if (size > AgentLoop.MaxResultBytes) return new JobResult { Status = "error", Error = $"result file is {size / 1048576.0:N1} MB; refused without reading it" };
            return new JobResult { Status = "ok", Result = File.ReadAllText(result, Encoding.UTF8) };
        }
        finally
        {
            TryDelete(script);
            TryDelete(result);
            TryDelete(result + ".tmp");
        }
    }

    private static async Task<bool> WaitForExitAsync(Process p, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!p.WaitForExit(0))
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>A fix may have started sfc or DISM: stop those with it, not only powershell.</summary>
    private static void KillTree(int pid)
    {
        try
        {
            using var k = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), $"/T /F /PID {pid}")
            { UseShellExecute = false, CreateNoWindow = true });
            k?.WaitForExit(15000);
        }
        catch (System.ComponentModel.Win32Exception) { /* already gone */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
