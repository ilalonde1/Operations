using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// K6 - the module's CURRENT STATE at start: the STATE NOW section of its handover, read from HEAD through git (never
/// the working tree), cited as repo:path@commit7 blob7, read whole: the one-page record caps it at <see cref="Cap"/>
/// characters (whole lines), the hook's own STATE NOW part carries it whole up to the hook budget (board row 1198:
/// KOR.Drafter's was cut at 3,000 of 7,330 and its cold session answered half its questions "not checked"). Found 3 Nov
/// (done-when 1, first run): every cold session answered "not checked" wherever a question needed the handover's STATE
/// NOW, because the start view carried the five questions but not the module's current state.
/// The handover is the file the module's <c>_five-questions</c> record cites (KOR.Drafter: docs/LEAD-HANDOVER-2026-11-02.md),
/// else docs/HANDOVER.md in the repo named like the module. The section starts at the first heading (#, ## or ###) whose
/// text begins "STATE NOW" and ends at the next heading of level max(its own, 2) or higher: a # or ## STATE NOW ends at
/// the next # or ## (a ### inside it stays in it); a ### STATE NOW ends at the next #, ## or ### (review row 211). Seen 3 Nov in the
/// 6 KOR repos: five use "## STATE NOW - ...", KOR.Drafter uses "# STATE NOW - ..." with its "## 2 Nov log" next. KOR Website (added 9 Nov)
/// also uses "## STATE NOW - ...".
/// Not covered: whether the section is TRUE or recent; a heading inside a code fence ends the section early; a handover
/// that names its state otherwise ("## Where things stand") is reported as having no STATE NOW section.
/// </summary>
public sealed record HandoverState(string Repo, string Path, string? Commit, string? Blob, string? Section, bool Dirty, string? Why)
{
    public const int Cap = 3000;

    /// <summary>"repo:path@commit7 blob blob7".</summary>
    public string Where => $"{Repo}:{Path}@{Short(Commit)} blob {Short(Blob)}";

    private static string Short(string? sha) => sha is null ? "-" : sha[..Math.Min(7, sha.Length)];

    private static readonly Regex Start = new(@"^(#{1,3})[ \t]+STATE NOW\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Heading = new(@"^(#{1,6})[ \t]", RegexOptions.CultureInvariant);

    /// <summary>The handover at HEAD of its repo, beside <paramref name="fromDir"/>; never throws (Why says what failed).</summary>
    public static HandoverState Read(StartRecord? five, string module, string? fromDir)
    {
        var (repo, path) = five is { SourceKind: "git", SourceRepo: { } r, SourcePath: { } p } ? (r, p) : (module, "docs/HANDOVER.md");
        var dir = RepoLocator.Find(repo, fromDir);
        if (dir is null) return new(repo, path, null, null, null, false, $"repo {repo} is not on this machine beside this folder");
        var commit = Git.HeadFast(dir);
        if (commit is null) return new(repo, path, null, null, null, false, $"git could not read HEAD in {dir}");
        var t = TimeSpan.FromSeconds(3);
        var blob = Git.Run(dir, t, "rev-parse", "--verify", "--quiet", $"{commit}:{path}")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(blob)) return new(repo, path, commit, null, null, false, $"{path} is not in HEAD {Short(commit)}");
        var text = Git.Run(dir, t, "cat-file", "blob", blob);
        if (text is null) return new(repo, path, commit, blob, null, false, $"git could not read blob {Short(blob)}");
        var full = System.IO.Path.Combine(dir, path);
        var work = File.Exists(full) ? Git.Run(dir, t, "hash-object", "--", path)?.Trim().ToLowerInvariant() : null;
        var section = StateNow(text);
        // the WHOLE section (board row 1198): the record page caps it at Cap; the hook's own STATE NOW part carries it
        // whole up to the hook budget (StartRender.State)
        return new(repo, path, commit, blob, section, work != blob,
                   section is null ? $"{path} at HEAD has no heading starting \"STATE NOW\"" : null);
    }

    /// <summary>The STATE NOW section (its heading line included, trimmed), or null when there is none.</summary>
    public static string? StateNow(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var at = Array.FindIndex(lines, l => Start.IsMatch(l));
        if (at < 0) return null;
        var level = Math.Max(Start.Match(lines[at]).Groups[1].Length, 2);
        var end = at + 1;
        while (end < lines.Length && !(Heading.Match(lines[end]) is { Success: true } h && h.Groups[1].Length <= level)) end++;
        return string.Join("\n", lines[at..end]).Trim();
    }

    /// <summary>At most <paramref name="max"/> characters, cut at a whole line, with a note naming how to read the rest.</summary>
    public static string CapLines(string section, int max, string path)
    {
        if (section.Length <= max) return section;
        var note = $"... [STATE NOW cut at {max} of {section.Length} chars - the rest: git show HEAD:{path}]";
        var kept = new List<string>();
        var used = note.Length;
        foreach (var line in section.Split('\n'))
        {
            if (used + line.Length + 1 > max) break;
            kept.Add(line);
            used += line.Length + 1;
        }
        kept.Add(note);
        return string.Join("\n", kept);
    }
}
