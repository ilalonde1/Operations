#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;

// One row from _FIELD REVIEWS TO INITIAL/EOR.csv. Headers per
// Move_Reports_To_EOR.ps1: ProjectNumber, EOR.
internal sealed record EorMappingRow(string ProjectNumber, string EorLastName);
