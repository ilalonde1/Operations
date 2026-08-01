#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;

// Knob-driven config for the EOR mover. Defaults match Move_Reports_To_EOR.ps1
// so a freshly-seeded job works without rows in FileSync.JobKnobs.
internal sealed record MoveReportsToEorOptions(
    string EorRootRelativePath,
    string EorCsvFileName,
    string ProjectFolderRegex,
    string ReportsSubfolderName,
    string CatchAllFolderName,
    string ArchivedFolderName,
    string AuditLogDir,
    string SenderAddress,
    string GlobalCc,
    string ShadowOutputDir)
{
    public const string DefaultEorRootRelativePath = "_FIELD REVIEWS TO INITIAL";
    public const string DefaultEorCsvFileName = "EOR.csv";
    public const string DefaultProjectFolderRegex = @"^[0-9]{5}-[0-9]{2}";
    public const string DefaultReportsSubfolderName = "Reports";
    public const string DefaultCatchAllFolderName = "CatchAll";
    public const string DefaultArchivedFolderName = "_Archived";
    public const string DefaultAuditLogDir = @"\\KOR-FS01\Projects\Reporting\Number Of Reports";
    public const string DefaultSenderAddress = "ilalonde@korstructural.com";
    public const string DefaultGlobalCc = "ilalonde@korstructural.com";

    public static string DefaultShadowOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations", "FileSync", "shadow", "MoveReportsToEor");

    public static MoveReportsToEorOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        return new MoveReportsToEorOptions(
            EorRootRelativePath: Get("EorRootRelativePath", DefaultEorRootRelativePath),
            EorCsvFileName: Get("EorCsvFileName", DefaultEorCsvFileName),
            ProjectFolderRegex: Get("ProjectFolderRegex", DefaultProjectFolderRegex),
            ReportsSubfolderName: Get("ReportsSubfolderName", DefaultReportsSubfolderName),
            CatchAllFolderName: Get("CatchAllFolderName", DefaultCatchAllFolderName),
            ArchivedFolderName: Get("ArchivedFolderName", DefaultArchivedFolderName),
            AuditLogDir: Get("AuditLogDir", DefaultAuditLogDir),
            SenderAddress: Get("SenderAddress", DefaultSenderAddress),
            GlobalCc: Get("GlobalCc", DefaultGlobalCc),
            ShadowOutputDir: Get("ShadowOutputDir", DefaultShadowOutputDir));
    }
}
