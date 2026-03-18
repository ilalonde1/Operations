#nullable enable
using System;
using System.Data.Odbc;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Kor.Operations.Data
{
    internal static class RetryPolicy
    {
        internal static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>()
                    .Handle<OdbcException>()
            })
            // Circuit breaker: opens when 100% of requests fail within a 1-minute sliding window
            // (minimum 5 requests required to evaluate). Stays open for 30s before allowing a probe.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>()
                    .Handle<OdbcException>()
            })
            .Build();
    }
}
