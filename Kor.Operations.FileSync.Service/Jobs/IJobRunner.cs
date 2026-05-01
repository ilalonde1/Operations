#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;

namespace Kor.Operations.FileSync.Service.Jobs;

internal interface IJobRunner
{
    Task<JobRunResult> RunAsync(
        JobConfig config,
        string triggerSource,
        string? args,
        CancellationToken ct);
}

internal sealed record JobRunResult(bool Success, string Summary);
