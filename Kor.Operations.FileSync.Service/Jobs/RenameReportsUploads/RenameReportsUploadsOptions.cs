#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;

// Knob-driven config for the report-rename pass. Defaults match
// RenameReportsUploads.ps1 verbatim. Cron is NULL in the seed (manual
// fire only) but the runner is otherwise schedule-agnostic.
internal sealed record RenameReportsUploadsOptions(
    string ProjectFolderRegex,
    string ReportsSubfolderName,
    string ArchivedFolderName)
{
    public const string DefaultProjectFolderRegex = @"^[0-9]{5}-[0-9]{2}";
    public const string DefaultReportsSubfolderName = "Reports";
    public const string DefaultArchivedFolderName = "_Archived";

    public static RenameReportsUploadsOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string Get(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

        return new RenameReportsUploadsOptions(
            ProjectFolderRegex: Get("ProjectFolderRegex", DefaultProjectFolderRegex),
            ReportsSubfolderName: Get("ReportsSubfolderName", DefaultReportsSubfolderName),
            ArchivedFolderName: Get("ArchivedFolderName", DefaultArchivedFolderName));
    }
}
