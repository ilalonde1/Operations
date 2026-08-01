#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;

// Knob-driven config for the EOR-acknowledged pickup job. Defaults match
// Move_Reports_To_ToSend.ps1 verbatim so a freshly-seeded job works without
// rows in FileSync.JobKnobs. Cadence is monthly @ 5th 08:00 PT (set in
// QuartzInstaller and reflected in the FileSync.Jobs seed/backfill).
internal sealed record MoveReportsToToSendOptions(
    string EorRootRelativePath,
    string ProjectsRootPath,
    string CategoryFolderExcludePrefix,
    string ToSendRelativePath,
    string ProjectFilenameRegex,
    int ProjectNumberLength,
    string AuditLogDir,
    string SenderAddress,
    string SummaryTo,
    string GlobalCc,
    string ShadowOutputDir)
{
    public const string DefaultEorRootRelativePath = "_FIELD REVIEWS TO INITIAL";
    public const string DefaultProjectsRootPath = @"\\KOR-FS01\Projects\Projects";
    public const string DefaultCategoryFolderExcludePrefix = "00";
    public const string DefaultToSendRelativePath = @"04 Construction Admin\01 Inspection Reports\To Send";
    public const string DefaultProjectFilenameRegex = @"^[0-9]{5}-[0-9]{2}";
    public const int DefaultProjectNumberLength = 8;
    public const string DefaultAuditLogDir = @"\\KOR-FS01\Projects\Reporting\Number Of Reports";
    public const string DefaultSenderAddress = "ilalonde@korstructural.com";
    public const string DefaultSummaryTo = "admin@korstructural.com";
    public const string DefaultGlobalCc = "ilalonde@korstructural.com";

    public static string DefaultShadowOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations", "FileSync", "shadow", "MoveReportsToToSend");

    public static MoveReportsToToSendOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        int GetInt(string key, int fallback)
        {
            if (knobs.TryGetValue(key, out var v) && int.TryParse(v, out var n)) return n;
            return fallback;
        }

        return new MoveReportsToToSendOptions(
            EorRootRelativePath: Get("EorRootRelativePath", DefaultEorRootRelativePath),
            ProjectsRootPath: Get("ProjectsRootPath", DefaultProjectsRootPath),
            CategoryFolderExcludePrefix: Get("CategoryFolderExcludePrefix", DefaultCategoryFolderExcludePrefix),
            ToSendRelativePath: Get("ToSendRelativePath", DefaultToSendRelativePath),
            ProjectFilenameRegex: Get("ProjectFilenameRegex", DefaultProjectFilenameRegex),
            ProjectNumberLength: GetInt("ProjectNumberLength", DefaultProjectNumberLength),
            AuditLogDir: Get("AuditLogDir", DefaultAuditLogDir),
            SenderAddress: Get("SenderAddress", DefaultSenderAddress),
            SummaryTo: Get("SummaryTo", DefaultSummaryTo),
            GlobalCc: Get("GlobalCc", DefaultGlobalCc),
            ShadowOutputDir: Get("ShadowOutputDir", DefaultShadowOutputDir));
    }
}
