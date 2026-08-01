#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Nightly BD research-queue refresh (2026-06-12 port of the dev-box
/// "KOR BD Nightly Refresh" scheduled task — hard rule: all automation runs
/// on KOR-APP01, never on a workstation). Executes the deterministic trigger
/// script Scripts\Nightly-Refresh.ps1 (deployed alongside the Worker, single
/// source = tools\BdQueueDrainBatchGenerate) against the server-local
/// QueueDrain root: m127 auto-revival, per-kind housekeeping + race-guard,
/// Generate-Batch selection, run cards, and the QueueRefreshReport DB row
/// the morning email embeds. Interactive drain sessions stay on the dev box
/// and reach the queues via the \\KOR-APP01\QueueDrain share.
/// (Replaces the original CSV gap-queue builder, which fed nothing once the
/// QueueDrain campaigns became the research pipeline.)
/// </summary>
[DisallowConcurrentExecution]
public sealed class BdResearchQueueBuilderJob : IJob
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(20);

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BdResearchQueueBuilderJob> _logger;

    public BdResearchQueueBuilderJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BdResearchQueueBuilderJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BdResearchQueueEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(BdResearchQueueBuilderJob),
                nameof(opt.BdResearchQueueEnabled));
            return;
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "Nightly-Refresh.ps1");
        if (!File.Exists(scriptPath))
        {
            _logger.LogError(
                "{Job}: trigger script not found at {Path} — deploy is missing the Scripts folder.",
                nameof(BdResearchQueueBuilderJob), scriptPath);
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: script missing at {scriptPath}.";
            return;
        }

        if (!File.Exists(opt.BdResearchQueuePwshPath))
        {
            _logger.LogError(
                "{Job}: pwsh not found at {Path} (option {Option}).",
                nameof(BdResearchQueueBuilderJob), opt.BdResearchQueuePwshPath, nameof(opt.BdResearchQueuePwshPath));
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: pwsh missing at {opt.BdResearchQueuePwshPath}.";
            return;
        }

        var ct = context.CancellationToken;
        try
        {
            _logger.LogInformation(
                "{Job}: running {Script} queueRoot={QueueRoot}.",
                nameof(BdResearchQueueBuilderJob), scriptPath, opt.BdResearchQueueDrainRoot);

            var psi = new ProcessStartInfo
            {
                FileName = opt.BdResearchQueuePwshPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-QueueRoot");
            psi.ArgumentList.Add(opt.BdResearchQueueDrainRoot);
            psi.ArgumentList.Add("-ConnectionString");
            psi.ArgumentList.Add(opt.OpportunitiesDb);

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ScriptTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                var why = ct.IsCancellationRequested ? "job canceled" : $"timed out after {ScriptTimeout.TotalMinutes:N0} min";
                _logger.LogError("{Job}: trigger script {Why}; killed.", nameof(BdResearchQueueBuilderJob), why);
                context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: {why}.";
                return;
            }

            var output = stdout.ToString();
            var errors = stderr.ToString();
            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "{Job}: trigger script exited {Code}. stderr: {Stderr} stdout tail: {Stdout}",
                    nameof(BdResearchQueueBuilderJob), process.ExitCode, Tail(errors, 2000), Tail(output, 2000));
                context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: exit {process.ExitCode}. {Tail(errors, 400)}";
                return;
            }

            if (errors.Length > 0)
            {
                // $ErrorActionPreference='Stop' makes real failures non-zero;
                // anything left on stderr is warning-grade (e.g. the
                // QueueRefreshReport bridge warning). Surface, don't fail.
                _logger.LogWarning(
                    "{Job}: trigger script wrote warnings: {Stderr}",
                    nameof(BdResearchQueueBuilderJob), Tail(errors, 2000));
            }

            _logger.LogInformation(
                "{Job}: trigger script completed. Report:\n{Report}",
                nameof(BdResearchQueueBuilderJob), Tail(output, 4000));
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} ok. {Tail(output, 800)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Job} failed.", nameof(BdResearchQueueBuilderJob));
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string Tail(string text, int maxChars)
    {
        text = text.Trim();
        return text.Length <= maxChars ? text : "…" + text[^maxChars..];
    }
}
