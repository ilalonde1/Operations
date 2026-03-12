#nullable enable
using System;
using System.Data.Odbc;
using Microsoft.Data.SqlClient;
using Polly;
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
            .Build();
    }
}
