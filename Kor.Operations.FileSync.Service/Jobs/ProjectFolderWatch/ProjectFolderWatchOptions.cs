#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.ProjectFolderWatch;

// Resolved per run from FileSync.JobKnobs. Defaults are what the 2026-09-30 sweep used by hand.
internal sealed record ProjectFolderWatchOptions(
    string ProjectsRoot,
    string EmailIndexDatabase,
    string AlertTo,
    bool IncludeSameJobNumber,
    string StateDir)
{
    public const string DefaultProjectsRoot = @"\\Kor-fs01\Projects\Projects";
    public const string DefaultEmailIndexDatabase = "KorEmailIndex";
    public const string DefaultAlertTo = "ilalonde@korstructural.com";

    public static string DefaultStateDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations",
        "FileSync",
        "ProjectFolderWatch");

    public static ProjectFolderWatchOptions FromKnobs(IReadOnlyDictionary<string, string?> knobs)
    {
        string S(string key, string fallback)
            => knobs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v!.Trim() : fallback;

        return new ProjectFolderWatchOptions(
            ProjectsRoot: S("ProjectsRoot", DefaultProjectsRoot).TrimEnd('\\'),
            EmailIndexDatabase: S("EmailIndexDatabase", DefaultEmailIndexDatabase),
            AlertTo: S("AlertTo", DefaultAlertTo),
            IncludeSameJobNumber: string.Equals(S("IncludeSameJobNumber", "false"), "true", StringComparison.OrdinalIgnoreCase),
            StateDir: S("StateDir", DefaultStateDir));
    }
}
