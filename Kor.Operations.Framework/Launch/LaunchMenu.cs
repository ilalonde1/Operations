using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Start;

namespace Kor.Operations.Framework.Launch;

/// <summary>Where a repo's board comes from: the owners (and their helpers) or, behind the RevitTools wall, a tag only.</summary>
public sealed record BoardSource(IReadOnlyList<string> Owners, string? Tag)
{
    /// <summary>The lead as Ian names it: "Framework lead", "Virtual Drafter lead", "Revit Tools lead".</summary>
    public string Lead => (Tag ?? string.Join(", ", Owners)) + " lead";
}

/// <summary>One menu line: the repo, what it is, its lead and its open board rows as read now (null rows = not read, Problem says why).</summary>
public sealed record LaunchEntry(int Number, string Folder, string Path, string Description, string Lead,
                                 IReadOnlyList<BoardRow>? Rows, string? Problem);

/// <summary>
/// <c>kor launch</c> (Framework board row 2188, gap 1; Ian 19 Nov #134753): the menu a kor\kor-claude window opens on - each
/// KOR repo's one-line description, its lead and its open board rows read live - from which claude is started in the chosen
/// folder. The repos are <see cref="EnrollCheck.Repos"/> (one list, never a second); each repo's board is read the way its own
/// session start reads it, from the <c>kor start --part board</c> hook in its <c>.claude/settings.json</c> (--owner, or --tag
/// for the wall), so the menu and the session can never disagree about whose rows they are.
/// <para>What it checks: the window is not elevated (an elevated kor-claude could unlock its own guards - Ian 19 Nov), and
/// says who it runs as. What it does NOT check: that the repo's settings are complete (enroll-check does that), that claude
/// is the version the self-test passed, or that the user picked the right repo. Same-class fault it would miss: a window
/// started with a token that is not elevated but holds a privilege such as SeBackup through some other group.</para>
/// </summary>
public static class LaunchMenu
{
    public const string DefaultRoot = @"C:\VIsual Studio Projects";

    /// <summary>Rows listed per repo before "and N more".</summary>
    public const int RowsShown = 5;

    /// <summary>One line per enrolled repo, from BUILD-PLAN.md's enrollment table (19 Nov). A repo added to
    /// <see cref="EnrollCheck.Repos"/> without a line here shows "(no description)" and fails LaunchMenuTests.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Framework"] = "THE framework: how every repo is developed - the board, guards, review, the kor CLI's plan",
        ["Operations"] = "BD, Finance, the Ops app, the MCP server, contracts; home of the kor CLI code",
        ["KOR.Drafter"] = "the Virtual Drafter: drawings to ETABS/SAFE models and drafted sheets",
        ["KOR.RevitTools"] = "the Revit add-ins and tools drafters use",
        ["NetworkOps"] = "NetworkOps and IT: the office network, servers, backups",
        ["KOR Inspections Bookings"] = "the site inspections booking app",
        ["KOR Website"] = "korstructural.com (WordPress)",
    };

    /// <summary>
    /// The board a repo's session start reads: the first hook command in settings.json holding <c>--part board</c>, its
    /// --owner values in order, or its --tag. Null when no such hook exists (the repo is not enrolled for the board).
    /// </summary>
    public static BoardSource? BoardOf(string settingsJson)
    {
        using var doc = JsonDocument.Parse(settingsJson);
        foreach (var command in Commands(doc.RootElement))
        {
            var t = Tokens(command);
            var i = t.IndexOf("--part");
            if (i < 0 || i + 1 >= t.Count || t[i + 1] != "board") continue;
            var owners = new List<string>();
            string? tag = null;
            for (var k = 0; k + 1 < t.Count; k++)
            {
                if (t[k] == "--owner") owners.Add(t[k + 1]);
                if (t[k] == "--tag") tag = t[k + 1];
            }
            if (tag is null && owners.Count == 0)
            {
                var m = t.IndexOf("--module");
                if (m >= 0 && m + 1 < t.Count) owners.Add(t[m + 1]);   // kor start: no --owner means owner = module
            }
            return new BoardSource(owners, tag);
        }
        return null;
    }

    private static IEnumerable<string> Commands(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals("command") && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString()!;
                    else foreach (var c in Commands(p.Value)) yield return c;
                }
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) foreach (var c in Commands(x)) yield return c;
                break;
        }
    }

    /// <summary>A command line split on spaces, double quotes grouping (as the hooks write "Virtual Drafter").</summary>
    public static List<string> Tokens(string command)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false, any = false;
        foreach (var ch in command)
        {
            if (ch == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (any || cur.Length > 0) list.Add(cur.ToString());
                cur.Clear();
                any = false;
                continue;
            }
            cur.Append(ch);
        }
        if (any || cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    /// <summary>The open rows for a board source on an open Board-login reader - the same reads kor start makes.</summary>
    public static IReadOnlyList<BoardRow> Rows(BrainDb db, BoardSource source) =>
        source.Tag is not null
            ? BoardState.LoadTagged(db, source.Tag).Rows
            : BoardState.Load(db, source.Owners, withHelpers: true).Rows;

    /// <summary>The menu text. A tagged (walled) repo's rows are listed without owner names, as its own session sees them.</summary>
    public static IEnumerable<string> Render(IReadOnlyList<LaunchEntry> entries)
    {
        foreach (var e in entries)
        {
            yield return $"{e.Number,2}. {e.Folder} - {e.Description}";
            if (e.Problem is not null)
            {
                yield return $"    {e.Lead} | board NOT READ: {e.Problem}";
                continue;
            }
            var rows = e.Rows ?? Array.Empty<BoardRow>();
            yield return $"    {e.Lead} | {rows.Count} open row(s)";
            foreach (var r in rows.Take(RowsShown)) yield return $"      row {r.Id} [{r.State}] {Cut(r.What, 100)}";
            if (rows.Count > RowsShown) yield return $"      ... and {rows.Count - RowsShown} more";
        }
    }

    public static string Cut(string? s, int max)
    {
        var t = (s ?? "").ReplaceLineEndings(" ").Trim();
        return t.Length <= max ? t : t[..(max - 3)] + "...";
    }

    /// <summary>True when this process runs with an elevated (administrator) token.</summary>
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string WhoAmI()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.Name;
    }

    /// <summary>
    /// The claude to start: claude.exe or claude.cmd on PATH, else npm's global folder under %APPDATA%. Null if none - the
    /// launcher says so rather than starting something else.
    /// </summary>
    public static string? FindClaude(string? path, string? appData, Func<string, bool> exists)
    {
        foreach (var dir in (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                var f = System.IO.Path.Combine(dir, name);
                if (exists(f)) return f;
            }
        if (!string.IsNullOrEmpty(appData))
        {
            var npm = System.IO.Path.Combine(appData, "npm", "claude.cmd");
            if (exists(npm)) return npm;
        }
        return null;
    }
}
