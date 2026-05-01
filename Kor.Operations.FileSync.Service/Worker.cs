#nullable enable
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.FileSync.Service;

internal sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<FileSyncOptions> _options;

    public Worker(ILogger<Worker> logger, IOptionsMonitor<FileSyncOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "Kor.Operations.FileSync.Service started. Mode={Mode}. Jobs registered: 0 (scaffold).",
            opts.Mode);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _logger.LogInformation("Kor.Operations.FileSync.Service stopping.");
    }
}
