#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;

internal enum FileMoveStatus
{
    Moved,        // Live: file was moved to TargetPath
    WouldMove,    // Shadow: file WOULD move
    Unmatched,    // No mapping row matched filename
    Ambiguous,    // Multiple mapping candidates after disambiguation -- skip
    NoTarget,     // Mapping row matched but TargetRoot was blank
    Failed,       // Move was attempted and threw
}

internal sealed record FileMoveResult(
    FileMoveStatus Status,
    string SourceFile,
    string? ProjectNo,
    string? ProjectName,
    string? ProjectMgr,
    string? TargetFolder,
    string? TargetPath,
    string? Note);
