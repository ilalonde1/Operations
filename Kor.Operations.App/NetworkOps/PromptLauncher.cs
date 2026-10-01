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

    public static string Save(RenderedPrompt prompt)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, Path.GetFileName(prompt.FileName));
        File.WriteAllText(path, prompt.Markdown);
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
}
