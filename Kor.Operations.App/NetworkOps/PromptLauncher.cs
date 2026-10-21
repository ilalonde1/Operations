#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;

namespace Kor.Operations.App.NetworkOps;

// Where a prompt goes once the service has written it: a file Claude can read, and a terminal that starts Claude on it.
// The file lives in the person's own app-data folder (never the Desktop): it is a one-run artefact -- the prompt carries
// that run's one-time report-back token -- and the next time it is opened the service writes a fresh one.
public static class PromptLauncher
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KorOperations", "Prompts");

    public static string Save(RenderedPrompt prompt) => Save(prompt.FileName, prompt.Markdown);

    /// <summary>The same, for a prompt composed outside NetworkOps (BD Director's consigliere asks and syntheses).</summary>
    public static string Save(string fileName, string markdown)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, Path.GetFileName(fileName));
        File.WriteAllText(path, markdown);
        return path;
    }

    /// <summary>
    /// Opens Windows Terminal in the repo (or the prompt's folder when this PC has no repo) and starts Claude on the file.
    /// cmd /k keeps the tab open after Claude exits; claude is an npm .cmd shim, which only cmd resolves from PATH.
    /// </summary>
    public static void OpenInClaude(string promptPath)
    {
        var dir = Directory.Exists(PromptComposer.RepoPath) ? PromptComposer.RepoPath : Path.GetDirectoryName(promptPath)!;
        var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
        foreach (var a in new[] { "-d", dir, "cmd", "/k", "claude", $"Read {promptPath} and follow it." }) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>
    /// The same, for a job that needs nobody (Claude's RFP review): Claude Code runs the prompt once (-p), reports back and
    /// exits, and the tab closes with it. If it fails the tab stays open (pause) so the person sees why. Same Claude Code,
    /// same account; permissions as Ian runs it (--dangerously-skip-permissions), since nobody is there to answer a prompt.
    /// </summary>
    public static void RunInClaude(string promptPath, string title)
    {
        var dir = Directory.Exists(PromptComposer.RepoPath) ? PromptComposer.RepoPath : Path.GetDirectoryName(promptPath)!;
        var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
        foreach (var a in new[] { "--title", title, "-d", dir, "cmd", "/c", "claude", "-p", $"Read {promptPath} and follow it.",
                     "--dangerously-skip-permissions", "||", "pause" })
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
