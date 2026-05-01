#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

// Verbatim copy of the $PmEmail map in Send-Weekly-PM-Deadlines.ps1
// lines 22-35. Names must match Deltek's PM column exactly (post-trim).
internal static class PmDirectory
{
    public static IReadOnlyDictionary<string, string> ByDisplayName { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Andrea Neuviale"] = "andrean@korstructural.com",
            ["Conor Murtagh"] = "cmurtagh@korstructural.com",
            ["Griffin Dow"] = "gdow@korstructural.com",
            ["James DesRoches"] = "jdesroches@korstructural.com",
            ["Jason Stuart"] = "jstuart@korstructural.com",
            ["Jeremy Atkinson"] = "jatkinson@korstructural.com",
            ["John Bryson"] = "jbryson@korstructural.com",
            ["John Markulin"] = "jmarkulin@korstructural.com",
            ["Katherine Reid"] = "kreid@korstructural.com",
            ["Kevin Wurmlinger"] = "kevinw@korstructural.com",
            ["Omar Alcazar Pastrana"] = "omara@korstructural.com",
            ["Rory Beirne"] = "rbeirne@korstructural.com",
        };

    public static string? TryGetEmail(string pmName) =>
        ByDisplayName.TryGetValue(pmName, out var email) ? email : null;
}
