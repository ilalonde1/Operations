#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;

// One row from the CTR sheet of "Concrete Test Report Data.xlsx".
// Names mirror the PS1 PSCustomObject fields so a side-by-side review
// against File-ConcreteTestReports.ps1 reads cleanly.
internal sealed record MappingRow(
    string ProjectNo,
    string ProjectMgr,   // already PmDirectory.NormalizePmName'd
    string ProjectName,
    string Agency,
    string AgencyNo,
    string AgencyName,
    string TargetRoot);  // CTR Folder on Projects Drive (UNC, trust as-is)
