#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;

internal enum ToSendMoveStatus
{
    Moved,         // Live: file copied to project ToSend + deleted from SharePoint
    WouldMove,     // Shadow: file WOULD move
    SkippedName,   // Filename didn't match the project regex
    SkippedNoProj, // Filename matched but no project folder exists on the file server
    Failed,        // Move attempted but threw
}

internal sealed record ToSendMoveResult(
    ToSendMoveStatus Status,
    string EorFolderName,
    string FileName,
    string? ProjectNumber,
    string? DestinationPath,
    string? Note);
