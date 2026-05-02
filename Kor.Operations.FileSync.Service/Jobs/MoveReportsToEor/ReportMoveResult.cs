#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;

internal enum ReportMoveStatus
{
    Moved,        // Live: file moved to EOR folder (or CatchAll)
    WouldMove,    // Shadow: file WOULD move
    Failed,       // Move attempted but threw
}

internal sealed record ReportMoveResult(
    ReportMoveStatus Status,
    string ProjectFolderName,
    string FileName,
    string EorFolderName,    // EOR display name OR "CatchAll"
    string DestinationPath,  // "_FIELD REVIEWS TO INITIAL/<EOR>/<file>"
    string? Note);
