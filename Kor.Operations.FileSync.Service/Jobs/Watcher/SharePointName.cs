#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// A file name that is perfectly legal on NTFS can be illegal in SharePoint /
// OneDrive. When one is uploaded, Microsoft Graph rejects the upload-session
// creation with "ODataError: Invalid request", the bucket records failed=1,
// and the run alerts as FAILED -- on every sync of that folder, forever,
// until the name is cleaned. That is exactly what happened to
// ' 31056-01 ... Issued for Draft IFC.pdf' (leading space) on 2026-09-08.
//
// This maps an on-disk name to the name SharePoint will actually store it
// under. The caller MUST compare local-vs-remote by this sanitized name and
// upload under it, otherwise the raw local name never matches the stored
// remote name and the file re-uploads then deletes on every pass.
//
// SCOPE -- what this normalises:
//   * leading and trailing whitespace   (SP forbids both)
//   * trailing dots                      (SP forbids a name ending in '.')
// What it deliberately does NOT touch, because Windows itself already forbids
// these characters in a file name, so they cannot occur on disk:
//   \ / : * ? " < > |
// It also does not rename SP-reserved stems (CON, PRN, .lock, leading '~$'):
// none of those produced the observed failure, and silently rewriting them
// would risk collisions. If one ever does, extend this one function and its
// tests -- do not scatter the logic into the sync loop.
internal static class SharePointName
{
    // Leading: whitespace only. A leading dot is legal in SharePoint (".gitignore"),
    // so stripping it would rename the file and churn it every pass.
    private static readonly char[] LeadingBad = { ' ', '\t', '\r', '\n', '\f', '\v' };
    // Trailing: whitespace AND dots (SharePoint forbids a name ending in either).
    private static readonly char[] TrailingBad = { ' ', '\t', '\r', '\n', '\f', '\v', '.' };

    /// <summary>
    /// The name SharePoint will store <paramref name="fileName"/> under.
    /// Returns an empty string if nothing legal remains (e.g. the name was
    /// only whitespace or dots) -- the caller treats that as "cannot sync".
    /// </summary>
    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return string.Empty;

        // Trailing '.' is stripped only at the very end, so it never eats the
        // extension separator of a normal "name.pdf".
        return fileName.TrimStart(LeadingBad).TrimEnd(TrailingBad);
    }

    /// <summary>True when the on-disk name is already SharePoint-legal.</summary>
    public static bool IsLegal(string fileName) =>
        string.Equals(Sanitize(fileName), fileName, System.StringComparison.Ordinal);
}
