#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Operations.FileSync.Service.Jobs.ProjectFolderWatch;

internal sealed record NestedProjectFolder(string ParentFolder, string NestedFolder);

// The decisions behind the nightly project-folder check, kept free of I/O so they can be pinned.
//
// On 2026-09-30 eleven project folders were found DRAGGED INTO a neighbouring project folder
// (30459-02 inside 30427-05, 30217-03 three folders deep, ...). Nothing was deleted, but to
// everyone looking in the right place the project was gone, and nobody knew for months. The
// Deltek webhook then made an empty folder under the old name, which hid it further.
//
// Two symptoms, one class: a project folder is not where the firm expects it. This finds both:
//   - a project-numbered folder sitting directly inside another project's folder, and
//   - a project folder the email index files into that no longer exists.
internal static class ProjectFolderAudit
{
    private static readonly Regex ProjectFolderName = new(@"^(\d{5})-(\d{2})\s*\(", RegexOptions.Compiled);

    public static bool TryGetProjectNumber(string folderName, out string jobBase, out string number)
    {
        var m = ProjectFolderName.Match(folderName ?? string.Empty);
        jobBase = m.Success ? m.Groups[1].Value : string.Empty;
        number = m.Success ? m.Groups[1].Value + "-" + m.Groups[2].Value : string.Empty;
        return m.Success;
    }

    // A child folder named like a project, inside a project folder, is a misplaced project --
    // unless it is another phase of the SAME job (30993-02 inside 30993-01), which people do on
    // purpose, and which includeSameJob turns back on.
    public static IReadOnlyList<NestedProjectFolder> FindNested(
        IEnumerable<(string ProjectPath, IEnumerable<string> ChildFolderNames)> projects,
        bool includeSameJob)
    {
        var found = new List<NestedProjectFolder>();
        foreach (var (projectPath, children) in projects)
        {
            var projectName = Path.GetFileName(projectPath.TrimEnd('\\', '/'));
            if (!TryGetProjectNumber(projectName, out var parentBase, out var parentNumber))
                continue;

            foreach (var child in children)
            {
                if (!TryGetProjectNumber(child, out var childBase, out var childNumber))
                    continue;
                if (string.Equals(childNumber, parentNumber, StringComparison.Ordinal))
                    continue;
                if (!includeSameJob && string.Equals(childBase, parentBase, StringComparison.Ordinal))
                    continue;
                found.Add(new NestedProjectFolder(projectPath, Path.Combine(projectPath, child)));
            }
        }

        return found;
    }

    // The project folder an indexed email lives in: everything before "\Newforma\".
    public static string? ProjectFolderOf(string filePath)
    {
        var i = (filePath ?? string.Empty).IndexOf(@"\Newforma\", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? filePath![..i] : null;
    }

    // What to tell a person about: only what was not already reported. Anything reported before and
    // still true stays quiet (counted, not re-listed); anything fixed drops out of the known set.
    public static (IReadOnlyList<string> New, int StillOpen) Diff(IEnumerable<string> current, IEnumerable<string> known)
    {
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var fresh = new List<string>();
        var stillOpen = 0;
        foreach (var item in current.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownSet.Contains(item)) stillOpen++;
            else fresh.Add(item);
        }

        fresh.Sort(StringComparer.OrdinalIgnoreCase);
        return (fresh, stillOpen);
    }
}
