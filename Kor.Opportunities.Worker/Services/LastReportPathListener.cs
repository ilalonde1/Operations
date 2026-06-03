#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

internal sealed class LastReportPathListener : IJobListener
{
    private readonly ILogger<LastReportPathListener> _logger;
    private readonly IJobScheduleStore _store;

    public LastReportPathListener(
        ILogger<LastReportPathListener> logger,
        IJobScheduleStore store)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Name => nameof(LastReportPathListener);

    public Task JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task JobExecutionVetoed(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        var jobName = context.JobDetail.Key.Name;
        var summary = context.Result?.ToString() ?? string.Empty;
        var reportPath = ExtractReportPath(summary);

        try
        {
            await _store.UpdateLastReportPathAsync(jobName, reportPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Listener}: failed to update LastReportPath for {JobName}.",
                nameof(LastReportPathListener),
                jobName);
        }
    }

    private static string? ExtractReportPath(string summary)
    {
        var idx = summary.LastIndexOf("report=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var rest = summary[(idx + "report=".Length)..].Trim();
        var endIdx = rest.IndexOfAny(new[] { ';', ',', '\n', '\r' });
        if (endIdx >= 0)
        {
            rest = rest[..endIdx].Trim();
        }

        return string.IsNullOrWhiteSpace(rest) ? null : rest;
    }
}
