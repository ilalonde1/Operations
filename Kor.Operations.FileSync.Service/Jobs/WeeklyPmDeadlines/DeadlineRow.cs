#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

internal sealed record DeadlineRow(
    string Pm,
    DateTime Date,
    string Project,
    string CustIssue,
    string CustRemarks);
