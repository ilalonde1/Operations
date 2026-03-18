namespace Kor.EmailCommon;

public static class PathHelpers
{
    public static string? TryGetProjectNumberFromPath(string fullPath)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        int first = Array.FindIndex(parts, p => p.Equals("Projects", StringComparison.OrdinalIgnoreCase));
        int second = (first >= 0) ? Array.FindIndex(parts, first + 1, p => p.Equals("Projects", StringComparison.OrdinalIgnoreCase)) : -1;

        int idx = (second >= 0 ? second : first) + 2;
        if (idx >= 0 && idx < parts.Length)
        {
            var projectFolder = parts[idx];
            if (projectFolder.Length >= 8) return projectFolder.Substring(0, 8);
        }

        return null;
    }
}
