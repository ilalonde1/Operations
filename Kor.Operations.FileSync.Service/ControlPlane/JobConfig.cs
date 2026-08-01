#nullable enable
namespace Kor.Operations.FileSync.Service.ControlPlane;

internal sealed record JobConfig(
    string JobName,
    string Mode,
    string? CronExpression,
    bool Enabled);
