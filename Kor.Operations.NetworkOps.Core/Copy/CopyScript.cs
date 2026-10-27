using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Copy;

/// <summary>
/// A file copy as a named, audited NetworkOps run: robocopy runs ON the machine nearest the files (a PC beside FS01 on
/// the LAN), never across the VPN from the session's PC. The copy itself runs as a one-off SYSTEM scheduled task so it
/// outlives the run's job object; the run waits a bounded time and returns the result or "running", and
/// <see cref="Status"/> reads it later. The proof comes back with the result: robocopy's own exit and summary rows, the
/// file count and bytes on BOTH sides, and a SHA-256 sample of up to 10 files hashed on both sides.
/// Never deletes anything: no /MIR, /PURGE or /MOV. Without overwrite, a file already at the destination is kept.
/// </summary>
public static class CopyScript
{
    public const int DefaultWaitSeconds = 120;
    public const int MaxWaitSeconds = 240;   // under the run's 300 s ceiling, with room to start the task and return

    /// <summary>Why this copy request may not run, or null. Paths are local (C:\...) or UNC (\\server\share\...), with
    /// nothing a quoted PowerShell string or robocopy's command line could read as more.</summary>
    public static string? Invalid(string? source, string? destination, string? files)
    {
        if (PathProblem(source) is { } s) return $"source: {s}";
        if (PathProblem(destination) is { } d) return $"destination: {d}";
        if (Norm(source!).Equals(Norm(destination!), StringComparison.OrdinalIgnoreCase)) return "the source and the destination are the same folder";
        if (files is not null && !Regex.IsMatch(files, @"^[^\\/:""<>|\r\n]{1,100}$")) return "files is one name or wildcard (e.g. *.rvt), with no folder in it";
        return null;
    }

    private static string? PathProblem(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "missing";
        if (p.Length > 400) return "longer than 400 characters";
        if (p.IndexOfAny(['"', '\r', '\n', '\t', '*', '?', '<', '>', '|']) >= 0) return "has a character a path cannot hold";
        if (!Regex.IsMatch(p, @"^([A-Za-z]:\\|\\\\[^\\]+\\[^\\]+)")) return "must be a full path: C:\\folder or \\\\server\\share\\folder";
        if (p.Contains(@"\..\") || p.EndsWith(@"\..")) return "may not climb with ..";
        return null;
    }

    /// <summary>The folder without its trailing backslash -- robocopy reads "C:\x\" + quote as an escaped quote -- except a
    /// drive root, which becomes "C:\." for the same reason.</summary>
    public static string Norm(string p)
    {
        var t = p.TrimEnd('\\');
        return Regex.IsMatch(t, @"^[A-Za-z]:$") ? t + @"\." : t;
    }

    /// <summary>A new copy id: sortable by time, unique enough for one machine's copies folder.</summary>
    public static string NewId() => $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    public static string Start(string copyId, string source, string destination, string? files, bool overwrite, int waitSeconds)
        => $"$CopyId = {Lit(copyId)}\n$Source = {Lit(Norm(source))}\n$Destination = {Lit(Norm(destination))}\n$Files = {Lit(files ?? "*.*")}\n" +
           $"$Overwrite = {(overwrite ? "$true" : "$false")}\n$WaitSeconds = {Math.Clamp(waitSeconds, 0, MaxWaitSeconds)}\n" + Body("copy-start");

    public static string Status(string copyId)
    {
        if (!IsId(copyId)) throw new ArgumentException("not a copy id", nameof(copyId));
        return $"$CopyId = {Lit(copyId)}\n" + Body("copy-status");
    }

    public static bool IsId(string? id) => id is not null && Regex.IsMatch(id, @"^\d{8}-\d{6}-[0-9a-f]{6}$");

    private static string Lit(string s) => "'" + s.Replace("'", "''") + "'";

    private static string Body(string name)
    {
        using var s = typeof(CopyScript).Assembly.GetManifestResourceStream($"Copy.{name}.ps1")
            ?? throw new InvalidOperationException($"no embedded script {name}");
        return new StreamReader(s).ReadToEnd();
    }
}
