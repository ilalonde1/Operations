#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class NewsMentionClassifyJob : IJob
{
    private readonly NewsMentionClassifier _classifier;
    private readonly INewsStore _store;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<NewsMentionClassifyJob> _logger;

    public NewsMentionClassifyJob(
        NewsMentionClassifier classifier,
        INewsStore store,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<NewsMentionClassifyJob> logger)
    {
        _classifier = classifier;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        if (!opt.NewsClassificationEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(NewsMentionClassifyJob),
                nameof(opt.NewsClassificationEnabled));
            return;
        }

        var key = !string.IsNullOrWhiteSpace(opt.AnthropicApiKey)
            ? opt.AnthropicApiKey
            : Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogDebug("{Job} skipped: no Anthropic API key configured.", nameof(NewsMentionClassifyJob));
            return;
        }

        var batch = opt.NewsClassificationBatchSize > 0 ? opt.NewsClassificationBatchSize : 5;
        if (opt.NewsClassificationTotalCap > 0)
        {
            var done = await _store.CountClassifiedAsync(ct).ConfigureAwait(false);
            if (done >= opt.NewsClassificationTotalCap)
            {
                _logger.LogInformation(
                    "NewsClassification paused: cap reached ({Done} >= {Cap}).",
                    done,
                    opt.NewsClassificationTotalCap);
                return;
            }

            batch = Math.Min(batch, opt.NewsClassificationTotalCap - done);
        }

        if (batch <= 0) return;

        try
        {
            var r = await _classifier.ClassifyBatchAsync(batch, ct).ConfigureAwait(false);
            if (r.Attempted == 0)
            {
                _logger.LogDebug(
                    "NewsClassify: attempted={A} ok={O} failed={F} mentions={M}.",
                    r.Attempted,
                    r.Ok,
                    r.Failed,
                    r.MentionsFound);
            }
            else
            {
                _logger.LogInformation(
                    "NewsClassify: attempted={A} ok={O} failed={F} mentions={M}.",
                    r.Attempted,
                    r.Ok,
                    r.Failed,
                    r.MentionsFound);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewsClassify job failed.");
        }
    }
}
