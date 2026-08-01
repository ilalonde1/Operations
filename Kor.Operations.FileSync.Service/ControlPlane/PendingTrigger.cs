#nullable enable
namespace Kor.Operations.FileSync.Service.ControlPlane;

internal sealed record PendingTrigger(
    long TriggerId,
    string JobName,
    string RequestedBy,
    string? Args);
