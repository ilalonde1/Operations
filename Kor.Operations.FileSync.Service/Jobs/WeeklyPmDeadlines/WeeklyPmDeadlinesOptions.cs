#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

// Resolved per-run from FileSync.JobKnobs with PS1 defaults baked in
// (Send-Weekly-PM-Deadlines.ps1 lines 13-19, 8-9). Anything missing in
// the DB falls back to these so first-run-after-port still works without
// a separate seed migration for the knobs.
internal sealed record WeeklyPmDeadlinesOptions(
    string ExcelPath,
    string SheetName,
    string PmColumn,
    string DateColumn,
    string ProjectColumn,
    string CustIssueColumn,
    string CustRemarksColumn,
    string SenderAddress,
    string GlobalCc,
    string ShadowOutputDir,
    IReadOnlyList<string> IgnorePms)
{
    public const string DefaultExcelPath = @"C:\Users\app-admin\KOR - Structured Engineering\Kor Hub - Deltek Connection\Project Deadlines.xlsx";
    public const string DefaultSheetName = "Projects_Deadlines";
    public const string DefaultPmColumn = "PM";
    public const string DefaultDateColumn = "CustDateExpected";
    public const string DefaultProjectColumn = "WBS1";
    public const string DefaultCustIssueColumn = "CustIssue";
    public const string DefaultCustRemarksColumn = "CustRemarks";
    public const string DefaultSenderAddress = "ilalonde@korstructural.com";
    public const string DefaultGlobalCc = "ilalonde@korstructural.com";
    public const string DefaultIgnorePms = "Adrian Crowder;John Zickmantel";

    public static string DefaultShadowOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations",
        "FileSync",
        "shadow",
        "WeeklyPmDeadlines");

    public static WeeklyPmDeadlinesOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
        {
            return knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;
        }

        var ignoreRaw = Get("IgnorePMs", DefaultIgnorePms);
        var ignore = ignoreRaw
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return new WeeklyPmDeadlinesOptions(
            ExcelPath: Get("ExcelPath", DefaultExcelPath),
            SheetName: Get("SheetName", DefaultSheetName),
            PmColumn: Get("PmColumn", DefaultPmColumn),
            DateColumn: Get("DateColumn", DefaultDateColumn),
            ProjectColumn: Get("ProjectColumn", DefaultProjectColumn),
            CustIssueColumn: Get("CustIssueColumn", DefaultCustIssueColumn),
            CustRemarksColumn: Get("CustRemarksColumn", DefaultCustRemarksColumn),
            SenderAddress: Get("SenderAddress", DefaultSenderAddress),
            GlobalCc: Get("GlobalCc", DefaultGlobalCc),
            ShadowOutputDir: Get("ShadowOutputDir", DefaultShadowOutputDir),
            IgnorePms: ignore);
    }
}
