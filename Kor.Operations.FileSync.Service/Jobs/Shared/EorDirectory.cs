#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Shared;

// EOR notification map. Keys must match the SharePoint folder names under
// _FIELD REVIEWS TO INITIAL exactly (case-insensitive). Verbatim from
// Move_Reports_To_EOR.ps1 §19-28.
//
// Note this is a different set than PmDirectory: EORs are reviewers, not
// project managers. Some names overlap (Jeremy Atkinson, Conor Murtagh,
// Jim DesRoches, Kevin Wurmlinger, Omar Alcazar Pastrana, Rory Beirne)
// and some don't (John Zickmantel routes to admin@; John Markulin appears
// in both maps with the same address).
internal static class EorDirectory
{
    public static IReadOnlyDictionary<string, string> ByDisplayName { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jeremy Atkinson"] = "jatkinson@korstructural.com",
            ["Conor Murtagh"] = "cmurtagh@korstructural.com",
            ["Jim DesRoches"] = "jdesroches@korstructural.com",
            ["John Markulin"] = "jmarkulin@korstructural.com",
            ["John Zickmantel"] = "admin@korstructural.com",
            ["Kevin Wurmlinger"] = "kevinw@korstructural.com",
            ["Omar Alcazar Pastrana"] = "omara@korstructural.com",
            ["Rory Beirne"] = "rbeirne@korstructural.com",
        };

    public static string? TryGetEmail(string eorFolderName)
    {
        if (string.IsNullOrWhiteSpace(eorFolderName)) return null;
        return ByDisplayName.TryGetValue(eorFolderName.Trim(), out var email) ? email : null;
    }
}
