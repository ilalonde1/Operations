#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Serilog;

namespace Kor.Operations.Services;

/// <summary>
/// Shared retry policy for outbound AI HTTP calls (Anthropic API).
/// Retries on transient 5xx + 429 (rate limit) + network exceptions
/// with exponential backoff. Logs each retry so transient hiccups
/// are visible in production logs without surfacing to the user.
/// </summary>
internal static class HttpRetryPolicy
{
    // 3 retries with 500ms / 2s / 8s backoff. Total retry budget ~10s
    // before giving up: long enough to ride through Anthropic blips,
    // short enough that the user isn't left waiting on a dead endpoint.
    private static readonly AsyncRetryPolicy<HttpResponseMessage> Policy =
        Polly.Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>(ex => ex.InnerException is not OperationCanceledException)
            .OrResult<HttpResponseMessage>(r =>
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromMilliseconds(500 * Math.Pow(4, attempt - 1)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    var status = outcome.Result?.StatusCode.ToString()
                                 ?? outcome.Exception?.GetType().Name
                                 ?? "unknown";
                    Log.Warning(
                        "Anthropic API call retry {Attempt}/3 after {Status}, waiting {Delay}ms.",
                        attempt, status, delay.TotalMilliseconds);
                });

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
        => Policy.ExecuteAsync(async _ =>
        {
            using var request = requestFactory();
            return await client.SendAsync(request, ct).ConfigureAwait(false);
        }, ct);
}
