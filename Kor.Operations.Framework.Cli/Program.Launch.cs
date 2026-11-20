using System.Diagnostics;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Launch;
using Kor.Operations.Framework.Stall;
using Kor.Operations.Framework.Start;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// <c>kor launch [--root R] [--list] [--pick N --dry-run]</c> (board row 2188, Ian 19 Nov #134753): the repo menu a desktop
/// shortcut (runas /savecred /user:kor\kor-claude) opens; picking a number starts claude in that folder and, when claude
/// exits, the menu is read again. Refused in an elevated window. Rules and limits: <see cref="LaunchMenu"/>'s header.
/// --list prints the menu and exits; --pick N --dry-run prints what would be started (tests, and a look without starting).
/// </summary>
public static partial class Program
{
    internal static int LaunchVerb(Options o, TextWriter output, TextWriter error, BrainDb? board, string hookErrorLog,
                                  Func<bool>? elevated = null, TextReader? input = null, Func<string?>? pythonProblem = null,
                                  Func<string?>? lockProblem = null)
    {
        var who = LaunchMenu.WhoAmI();
        if ((elevated ?? LaunchMenu.IsElevated)())
        {
            error.WriteLine($"kor launch: REFUSED - this window is elevated (administrator) as {who}. Sessions run NOT elevated: " +
                            "an elevated session could unlock its own guards (Ian 19 Nov, row 2188). Open the KOR launch shortcut, not 'Run as administrator'.");
            try
            {
                if (board is not null) StallStore.LogGuardRefusal(board, "kor launch", "elevated window", null, null, who);
                else BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogGuardRefusal(b, "kor launch", "elevated window", null, null, who));
            }
            catch (Exception e) { LogHookError(hookErrorLog, o, e); }
            return 3;
        }

        var root = o.Get("--root") ?? LaunchMenu.DefaultRoot;
        output.WriteLine($"kor launch - running as {who} (not elevated)" +
                         (who.Equals(@"kor\kor-claude", StringComparison.OrdinalIgnoreCase) ? "" : "  ** NOT kor\\kor-claude: guards do not bind this account **"));

        var pick = o.Int("--pick");
        var once = o.Get("--list") is not null || pick is not null;
        input ??= Console.In;
        List<LaunchEntry>? entries = null;
        while (true)
        {
            if (entries is null)   // read on start and after claude exits; a bad pick only asks again
            {
                entries = LaunchEntries(root, board);
                output.WriteLine($"Board read {DateTime.Now:yyyy-MM-dd HH:mm}");
                foreach (var l in LaunchMenu.Render(entries)) output.WriteLine(l);
                if (o.Get("--list") is not null) return 0;
            }

            int n;
            if (pick is not null) n = pick.Value;
            else
            {
                output.Write("Pick a number (q to quit): ");
                output.Flush();
                var line = input.ReadLine();
                if (line is null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) return 0;
                if (!int.TryParse(line.Trim(), out n)) { output.WriteLine($"'{line.Trim()}' is not a number on the menu."); continue; }
            }
            var chosen = entries.FirstOrDefault(e => e.Number == n);
            if (chosen is null)
            {
                output.WriteLine($"{n} is not on the menu (1-{entries.Count}).");
                if (once) return 2;
                continue;
            }
            if (!Directory.Exists(chosen.Path))
            {
                output.WriteLine($"{chosen.Path} does not exist on this PC.");
                if (once) return 2;
                continue;
            }
            var claude = LaunchMenu.FindClaude(Environment.GetEnvironmentVariable("PATH"),
                                               Environment.GetEnvironmentVariable("APPDATA"), File.Exists);
            if (claude is null)
            {
                output.WriteLine("claude was not found on PATH or in %APPDATA%\\npm - install Claude Code for this account first.");
                return 4;
            }
            // 19 Nov (row 2188 Evidence, 18:0x-18:26): Python was per-user for ilalonde only, so every guard hook in a kor-claude session failed
            // ("python: command not found") and a failed hook blocks nothing - the session ran unguarded (row 2188, #134757).
            var py = (pythonProblem ?? LaunchMenu.PythonProblem)();
            if (py is not null)
            {
                error.WriteLine($"kor launch: REFUSED - {py}. Every guard hook runs `python`; without it the session would run with " +
                                "NO guards. Put a Python 3 on this account's PATH first (row 2188).");
                try
                {
                    if (board is not null) StallStore.LogGuardRefusal(board, "kor launch", "python missing", null, null, $"{who}: {py}");
                    else BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogGuardRefusal(b, "kor launch", "python missing", null, null, $"{who}: {py}"));
                }
                catch (Exception e) { LogHookError(hookErrorLog, o, e); }
                return 5;
            }
            // Row 2195 (Ian #134886): a session must not be able to change its own guards. A lock is an ACL a file loses when
            // git or an editor replaces it, so it is checked here, before every session, not once.
            var locks = (lockProblem ?? LaunchMenu.GuardLockProblem)();
            if (locks is not null)
            {
                error.WriteLine($"kor launch: REFUSED - the guard files are not all locked: {locks}. A session could change its " +
                                "own guards. Ask Ian (or the Framework lead in an administrator window) to run: " +
                                $"python -I \"{LaunchMenu.GuardLocksScript}\" apply");
                try
                {
                    if (board is not null) StallStore.LogGuardRefusal(board, "kor launch", "guards unlocked", null, null, $"{who}: {locks}");
                    else BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogGuardRefusal(b, "kor launch", "guards unlocked", null, null, $"{who}: {locks}"));
                }
                catch (Exception e) { LogHookError(hookErrorLog, o, e); }
                return 6;
            }
            output.WriteLine($"Starting claude in {chosen.Path} ({chosen.Lead}) - {claude}");
            if (o.Get("--dry-run") is not null) return 0;

            // claude.exe starts directly; npm's claude.cmd needs cmd.exe (a .cmd is not an executable image)
            var isExe = claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo(isExe ? claude : "cmd.exe") { WorkingDirectory = chosen.Path, UseShellExecute = false };
            if (!isExe) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(claude); }
            using (var p = Process.Start(psi)!) p.WaitForExit();
            output.WriteLine($"claude in {chosen.Folder} ended. Reading the board again.");
            if (once) return 0;
            entries = null;
        }
    }

    /// <summary>The menu entries: <see cref="EnrollCheck.Repos"/> under <paramref name="root"/>, each repo's board read the way its
    /// session start reads it; one repo failing (no settings, no board hook, the DB down) is said on its line, never dropped.</summary>
    private static List<LaunchEntry> LaunchEntries(string root, BrainDb? board)
    {
        var list = new List<LaunchEntry>();
        var n = 0;
        foreach (var repo in EnrollCheck.Repos)
        {
            n++;
            var path = Path.Combine(root, repo.Folder);
            var desc = LaunchMenu.Descriptions.TryGetValue(repo.Folder, out var d) ? d : "(no description)";
            BoardSource? source = null;
            string? problem = null;
            try
            {
                var settings = Path.Combine(path, ".claude", "settings.json");
                if (!File.Exists(settings)) problem = $"no {settings}";
                else if ((source = LaunchMenu.BoardOf(File.ReadAllText(settings))) is null) problem = "its settings.json has no kor start --part board hook";
            }
            catch (Exception e) { problem = OneLine(e.Message); }

            IReadOnlyList<BoardRow>? rows = null;
            if (source is not null)
            {
                try
                {
                    rows = board is not null
                        ? LaunchMenu.Rows(board, source)
                        : BrainDb.Read(KorStandardsConnection.Login.Board, b => LaunchMenu.Rows(b, source));
                }
                catch (Exception e) { problem = OneLine(e.Message); }
            }
            list.Add(new LaunchEntry(n, repo.Folder, path, desc, source?.Lead ?? "lead unknown", rows, problem));
        }
        return list;
    }
}
