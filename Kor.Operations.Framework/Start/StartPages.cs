using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Start;

/// <summary>One start's whole memory cut into hook-sized pages: what every <c>--part pages</c> hook of a start prints from.</summary>
public sealed record PagesSnapshot(string Module, DateTimeOffset ReadAt, IReadOnlyList<string> Pages);

/// <summary>
/// Board row 1569 (Ian, 4 and 5 Nov: "It needs to know EVERYTHING"; "SHOULD we not load the full memory before asking it to do
/// a task????"): THE PAGES. Today's parts fix a page count and fit the content to it - descriptions narrowed, the index cut at
/// page 4, the resume cut at 3,000. Here the page count follows the content: STATE NOW whole, the resume whole, OFFICE
/// LESSONS, the curated index whole and every live record with its full description, one stream cut at whole lines into
/// pages under the per-hook cap. A start wires more hook entries than it needs (a page past the content prints nothing);
/// when it wires too few, the last wired page, the record part and <see cref="StartGate"/> each say so plainly.
/// Compares: every live row (id, name, full description), the index, the resume and the section against the pages
/// (StartPagesTests, and live on the 6 lanes). Does NOT cover: a record's body (kor read and the gate); THE BOARD, whose own
/// COMPACT form still cuts titles; whether a record is true. Same-class miss: content in a table the start does not read.
/// </summary>
public static partial class StartRender
{
    /// <summary>Text per page; the rest of <see cref="HookBudget"/> is the head line and the short-wiring note (about 447 chars with the
    /// longest real exe path, session id and module: StartPagesTests holds the longest of them under the budget).</summary>
    public const int PageRoom = HookBudget - 700;

    /// <summary>What the pages name in their head: "KNOWN AT START - PAGES, page 3 of 20".</summary>
    public const string PagesHead = "KNOWN AT START - PAGES, page ";

    /// <summary>The whole stream, unmasked (<see cref="BuildPages"/> masks it once, before any cut can split a value).</summary>
    public static string PagesBody(StartSide side, ResumeChoice resume, StoredEvent? lastEnd)
    {
        var module = side.Module;
        var blocks = new List<string>();
        var state = StateNow(side.Handover, module, 0, whole: true);
        blocks.Add(state.Length > 0 ? state : "STATE NOW: not read - no handover state was read. Answer anything that needs the module's current state 'not checked'.");
        blocks.Add(ResumeText(resume, lastEnd, module, 0, whole: true, onPages: false));
        blocks.Add(WriteLine(module));
        blocks.Add($"OFFICE LESSONS (lane '{ModuleState.OfficeLane}', {side.Office.Count(r => r.Name != ModuleState.IndexName)} records) - every KOR lead gets these, " +
                   $"the same day they are learned. They bind here too. Body: kor read --module \"{module}\" --subject \"<words>\".");
        if (side.Office.FirstOrDefault(r => r.Name == ModuleState.IndexName)?.Body is { } officeIndex) blocks.Add(officeIndex.Trim());
        blocks.AddRange(side.Office.Where(r => r.Name != ModuleState.IndexName).Select(FullEntry));
        var rows = side.Records.Where(r => r.Name != ModuleState.FiveQuestionsName && r.Name != ModuleState.IndexName).ToList();
        foreach (var (title, group) in RecordGroups(rows.Where(r => !r.FromFileMemory).ToList()))
        {
            blocks.Add(group.Count == 0 ? $"{title}: none current." : $"{title} ({group.Count}, newest first):");
            blocks.AddRange(group.OrderByDescending(r => r.Id).Select(FullEntry));
        }
        var imported = rows.Where(r => r.FromFileMemory).OrderByDescending(r => r.Id).ToList();
        if (side.Records.FirstOrDefault(r => r.Name == ModuleState.IndexName) is { Body: { } body } index)
        {
            blocks.Add($"LEGACY MEMORY - the lane's curated index, record #{index.Id}:");
            blocks.Add(body.Trim());
        }
        blocks.Add($"LEGACY MEMORY ({imported.Count} rows imported from the file memories - weigh them below every sourced record above):");
        blocks.AddRange(imported.Select(FullEntry));
        return string.Join("\n", blocks);
    }

    /// <summary>A row's line on the pages: its description WHOLE. An imported row cites only the memory file it came from, so no source is printed for it.</summary>
    private static string FullEntry(StartRecord r) =>
        $"- record #{r.Id} [{r.Kind}] {r.Name} - {r.Description}" + (r.FromFileMemory ? "" : $" ({r.Where})") + (r.Status == "stale" ? " [STALE]" : "");

    /// <summary>The rows the pages must carry (each with its full description): the test's Y.</summary>
    public static IEnumerable<StartRecord> PageRows(StartSide side) =>
        side.Records.Concat(side.Office).Where(r => r.Name != ModuleState.FiveQuestionsName && r.Name != ModuleState.IndexName);

    public static PagesSnapshot BuildPages(StartSide side, ResumeChoice resume, StoredEvent? lastEnd) =>
        new(side.Module, side.ReadAt, Chunk(SecretGuard.Mask(PagesBody(side, resume, lastEnd))!, PageRoom));

    /// <summary>Page <paramref name="page"/> of the <paramref name="wired"/> hook entries: "" past the content. When the content needs
    /// more pages than are wired, the last wired one says which are not loaded and how to read them.</summary>
    public static string Page(PagesSnapshot s, int page, int wired, string exe, string? session)
    {
        if (page < 1 || page > wired) throw new ArgumentException($"--page {page} is not within 1..{wired}");
        if (page > s.Pages.Count) return "";
        var text = SideHead(s.Module, $"PAGES, page {page} of {s.Pages.Count}", s.ReadAt) + "\n" + s.Pages[page - 1];
        return page == wired && s.Pages.Count > wired ? text + "\n" + ShortNote(s.Module, wired, s.Pages.Count, exe, session) : text;
    }

    /// <summary>The plain words when the wired hook entries fall short of the content.</summary>
    public static string ShortNote(string module, int wired, int needed, string exe, string? session) =>
        $"!! MEMORY LEFT OUT: {needed} pages, {wired} wired - pages {wired + 1} to {needed} are NOT in your context. Read them: " +
        ReadPageCommand(module, exe, session, wired + 1, needed) + " (each page to " + needed + ").";

    internal static string ReadPageCommand(string module, string exe, string? session, int page, int pages) =>
        $"\"{exe}\" start --module \"{module}\" --part pages --page {page} --pages {pages} --text" + (session is null ? "" : $" --session {session}");

    /// <summary>Every page, one after the other (--text with no --page).</summary>
    public static string WholePages(PagesSnapshot s) =>
        string.Join("\n", s.Pages.Select((t, i) => SideHead(s.Module, $"PAGES, page {i + 1} of {s.Pages.Count}", s.ReadAt) + "\n" + t));

    /// <summary>The record part's line, when the pages are wired: where the memory is, how to state it, how to read the bodies.</summary>
    public static string PagesLine(string module, PagesWiring w, string exe, string? session) =>
        $"THE PAGES: this module's whole memory (STATE NOW, the resume whole, OFFICE LESSONS, the index, every record with its full description) is on " +
        $"{w.Needed} session-start page(s) headed '{PagesHead}N of {w.Needed}'; {w.Wired} hook entries are wired. Count the pages you hold: a part not in your context was not read - " +
        $"answer what it would hold 'not checked'. The bodies are not on the pages: \"{exe}\" read --module \"{module}\" --subject \"<words for your task>\" prints them whole, X of Y. " +
        "Your first work tool is refused until every page is held and that read is done. Your first words before any work: " +
        "'pages X of Y held, bodies A of B read for subject S'. When the work moves to a new subject, read its bodies too." +
        (w.Needed > w.Wired ? "\n" + ShortNote(module, w.Wired, w.Needed, exe, session) : "");
}

/// <summary>The record part's view of the pages: how many hook entries the settings wire, how many the content needs.</summary>
public sealed record PagesWiring(int Wired, int Needed);

/// <summary>
/// Every hook of one start prints from ONE read (row 1569): the hooks run in parallel, and pages cut from separate reads could
/// lose or repeat a line when a record changes between them. The first hook in reads and keeps the snapshot; the rest wait on
/// a mutex and take it. A hook takes it only while young (a later compaction reads again); --text with the session's id takes it
/// at any age, so a page read by hand is the same page.
/// </summary>
public static class StartPages
{
    public static readonly TimeSpan HookReuse = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    public static PagesSnapshot Shared(string cacheDir, string module, string? session, string? source, bool hook, TimeSpan wait,
                                       Func<PagesSnapshot> read, Func<DateTime> now)
    {
        if (string.IsNullOrEmpty(session)) return read();
        var dir = Path.Combine(cacheDir, "pages");
        var path = Path.Combine(dir, Safe($"{module}_{session}_{(hook ? source ?? "none" : "any")}") + ".json");
        // --text has no source: it finds the session's newest snapshot, whichever start wrote it
        using var mutex = new Mutex(false, "Local\\kor-start-pages-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(module + "|" + session)))[..24]);
        var held = false;
        try
        {
            try { held = mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { held = true; }
            if (Load(dir, module, session, hook ? path : null, hook ? HookReuse : null, now) is { } kept) return kept;
            var s = read();
            Save(dir, path, s, now);
            return s;
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    private static PagesSnapshot? Load(string dir, string module, string session, string? exact, TimeSpan? young, Func<DateTime> now)
    {
        try
        {
            var path = exact ?? (Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, Safe($"{module}_{session}_") + "*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null);
            if (path is null || !File.Exists(path)) return null;
            if (young is { } y && now() - File.GetLastWriteTime(path) > y) return null;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path, Encoding.UTF8));
            return s is null ? null : new PagesSnapshot(s.Module, s.ReadAt, s.Pages);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void Save(string dir, string path, PagesSnapshot s, Func<DateTime> now)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(new Stored(s.Module, s.ReadAt, s.Pages.ToList())), new UTF8Encoding(false));
            foreach (var old in Directory.EnumerateFiles(dir, "*.json").Where(f => now() - File.GetLastWriteTime(f) > KeepFor)) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* each page then reads for itself */ }
    }

    private sealed record Stored(string Module, DateTimeOffset ReadAt, List<string> Pages);
}

/// <summary>
/// <c>kor read</c> (row 1569): the bodies for the session's subject, whole. A row is the subject's when every word of the subject is
/// in its name or description, else (none so) in its body. Pages fit a tool result; each names how many it has read of how many.
/// Over <see cref="MaxChars"/> it asks for a narrower subject or --all first: the cost is shown, nothing is cut. Does not judge
/// whether the subject is the session's real one: "zzz" reads 0 of 0 and opens the gate.
/// </summary>
public static class BodyRead
{
    /// <summary>A tool result is cut at 30,000 chars (Claude Code's Bash default); a page stays under it.</summary>
    public const int PageChars = 25_000;
    public const int MaxChars = 400_000;
    public const string Head = "KOR BODIES - subject '";

    public sealed record Row(int Id, string Lane, string Name, string Kind, string Description, string? Body);

    public static List<string> Terms(string subject) =>
        subject.Split(new[] { ' ', '\t', ',', ';', ':', '/', '\'', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('.').ToLowerInvariant())
               .Where(t => t.Length >= 2).Distinct().ToList();

    public static List<Row> Match(IEnumerable<Row> rows, string subject)
    {
        var terms = Terms(subject);
        if (terms.Count == 0) return new List<Row>();
        var all = rows.ToList();
        // each word from a word's start ("river" is not "driver"; "pursuit" is "pursuits")
        var starts = terms.Select(t => new System.Text.RegularExpressions.Regex(@"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(t),
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        bool Every(string text) => starts.All(s => s.IsMatch(text));
        var named = all.Where(r => Every(r.Name + " " + r.Description)).ToList();
        return (named.Count > 0 ? named : all.Where(r => Every(r.Body ?? "")).ToList()).OrderByDescending(r => r.Id).ToList();
    }

    public static string Pages(List<Row> matched, string subject, string module, string exe, int page, bool all, out bool complete)
    {
        complete = false;
        var clean = string.Join(" ", Terms(subject));
        var text = matched.Select(r => $"=== record #{r.Id} [{r.Kind}] {r.Name} (lane {r.Lane}) - {r.Description}\n{(r.Body ?? "").Trim()}").ToList();
        var chars = text.Sum(t => t.Length);
        if (chars > MaxChars && !all)
            return $"NOT READ: subject '{clean}' matches {matched.Count} bodies, {chars.ToString("N0", CultureInfo.InvariantCulture)} chars (about " +
                   $"{(chars / 4).ToString("N0", CultureInfo.InvariantCulture)} tokens, paid on every later turn). Give narrower words, or add --all to read them all. " +
                   "Names: " + string.Join(", ", matched.Select(r => $"#{r.Id} {r.Name}"));
        var chunks = StartRender.Chunk(SecretGuard.Mask(string.Join("\n", text))!, PageChars);
        if (chunks.Count == 0) chunks.Add("");
        if (page < 1 || page > chunks.Count) throw new ArgumentException($"--page {page} is not within 1..{chunks.Count}");
        complete = page == chunks.Count;
        var through = chunks.Take(page).Sum(c => c.Split("\n=== record #").Length - (c.StartsWith("=== record #", StringComparison.Ordinal) ? 0 : 1));
        var head = $"{Head}{clean}' - page {page} of {chunks.Count} - {through} of {matched.Count} bodies read through this page (lanes {module} and {ModuleState.OfficeLane}).";
        var next = complete ? $"ALL {matched.Count} of {matched.Count} bodies for '{clean}' are read."
            : $"NEXT: \"{exe}\" read --module \"{module}\" --subject \"{clean}\" --page {page + 1}{(all ? " --all" : "")}";
        return head + "\n" + chunks[page - 1] + "\n" + next;
    }
}
