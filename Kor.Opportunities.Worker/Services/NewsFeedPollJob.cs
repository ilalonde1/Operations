#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class NewsFeedPollJob : IJob
{
    private readonly NewsFeedPollService _service;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<NewsFeedPollJob> _logger;

    public NewsFeedPollJob(
        NewsFeedPollService service,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<NewsFeedPollJob> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        if (!_options.Value.NewsFeedPollEnabled)
        {
            return;
        }

        try
        {
            var r = await _service.PollAllAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "NewsFeedPoll: feeds={F} pulled={P} inserted={I} failed={X}.",
                r.FeedsPolled,
                r.ArticlesPulled,
                r.Inserted,
                r.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewsFeedPoll job failed.");
        }
    }
}
