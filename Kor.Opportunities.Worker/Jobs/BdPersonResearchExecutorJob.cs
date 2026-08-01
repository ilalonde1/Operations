#nullable enable
using System.Diagnostics;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Jobs;

[DisallowConcurrentExecution]
public sealed class BdPersonResearchExecutorJob : IJob
{
    private readonly BdPersonResearchExecutorService _svc;
    private readonly ILogger<BdPersonResearchExecutorJob> _logger;

    public BdPersonResearchExecutorJob(
        BdPersonResearchExecutorService svc,
        ILogger<BdPersonResearchExecutorJob> logger)
    {
        _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var sw = Stopwatch.StartNew();
        var result = await _svc.RunBatchAsync(ct).ConfigureAwait(false);
        sw.Stop();
        // JobRunLoggingListener persists context.Result into JobRuns.Summary —
        // this is the durable per-run cost record (2026-06-10 audit C3).
        context.Result =
            $"considered={result.PeopleConsidered}; executed={result.PeopleExecuted}; ok={result.Successes}; failed={result.Failures}; inputTok={result.TotalInputTokens}; outputTok={result.TotalOutputTokens}";
        _logger.LogInformation(
            "{Job}: completed. considered={Considered}; executed={Executed}; ok={Successes}; failed={Failures}; inputTok={InputTok}; outputTok={OutputTok}; elapsedMs={ElapsedMs}.",
            nameof(BdPersonResearchExecutorJob),
            result.PeopleConsidered,
            result.PeopleExecuted,
            result.Successes,
            result.Failures,
            result.TotalInputTokens,
            result.TotalOutputTokens,
            sw.ElapsedMilliseconds);
    }
}
