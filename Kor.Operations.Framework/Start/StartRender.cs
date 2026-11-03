using System.Globalization;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// K6 - the record part of a session's start (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). Sections in priority order:
/// header (its first line: <see cref="NotReadLine"/>; module, HEAD per repo, freshness counts, when read); FIVE QUESTIONS
/// (the module's <c>_five-questions</c> record); STATE NOW (the handover's, at HEAD - <see cref="HandoverState"/>; cut
/// LAST, after every other section is at its floor); RESUME (cap 3,000) + the last session-end + the other recent resumes one line each; OPEN ISSUES; STALE with the recheck
/// commands; DECISIONS / DEAD ENDS / LESSONS / FINDINGS / OTHER (current only, each with repo:path@commit7); LAST EVENTS
/// (10); OFFICE LESSONS (cap 2,500); LEGACY MEMORY (the imported rows).
/// Over the hook budget it compacts FROM THE BOTTOM: legacy memory fills only the room left, then each section from the
/// bottom up steps down its own ladder (shorter, then ids only). The floors are fixed: every open-issue id, every stale id
/// and the resume's first 1,500 characters always survive. Superseded and resolved records never render (the views
/// exclude superseded rows; a resolution is history and is only counted).
/// THE HOOK SPLITS IT (board row 1198, 3 Nov): Claude Code caps each hook's additionalContext at 10,000 chars on its own,
/// and one 9,900 page cut KOR.Drafter's STATE NOW at 3,000 of 7,330 and four modules' legacy index to a fraction. So
/// the hook's record part (<see cref="Record(ModuleState, int?, bool)"/> split) carries everything but STATE NOW, OFFICE
/// LESSONS and LEGACY MEMORY, and names them in one line; <see cref="State"/> carries STATE NOW whole (to 9,900) and
/// <see cref="Memory"/> carries office + legacy whole across pages of 9,900. --text and --part all stay one page.
/// Not covered: whether a record is TRUE; a section's floor overflowing the budget on its own (over ~1,000 open issues);
/// memory past the pages wired in settings.json (cut on the last with a note); a part whose hook entry is not wired.
/// </summary>
public static class StartRender
{
    public const int HookBudget = 9900;
    public const int ResumeCap = 3000;
    public const int ResumeKeep = 1500;
    public const int OfficeMax = 2500;

    private sealed record Section(string Name, int MaxLevel, Func<int, string> Render);

    public static string Record(ModuleState s, int? budget) => Record(s, budget, split: false);

    /// <summary>
    /// The record part. <paramref name="split"/> (the SessionStart hook, board row 1198): STATE NOW and LEGACY MEMORY are
    /// NOT on it - each is its own hook entry with its own 9,900 (<see cref="State"/>, <see cref="Memory"/>; Claude Code caps
    /// each hook's additionalContext separately) - and one line in their place names those parts. Without it, one page
    /// with every section (--text, --part all).
    /// </summary>
    public static string Record(ModuleState s, int? budget, bool split)
    {
        // mistake 138: every credential value in what was read is masked BEFORE any section clips it (a clipped value
        // could fall under the classifier's length rule), and each rendered section is masked again below
        s = Masked(s);
        var m = s.Module;
        var five = s.Records.FirstOrDefault(r => r.Name == ModuleState.FiveQuestionsName);
        var stale = s.Fresh.Rows.Where(r => r.Verdict == Verdict.Stale).ToList();
        var unverifiable = s.Fresh.Rows.Where(r => r.Verdict == Verdict.Unverifiable).ToList();
        var staleIds = stale.Select(r => r.Id).ToHashSet();
        var sourced = s.Records.Where(r => !r.Legacy && r.Name != ModuleState.FiveQuestionsName && r.Kind != "resolution"
                                            && r.Status == "current" && !staleIds.Contains(r.Id)).ToList();
        var legacy = split ? new List<StartRecord>() : s.Records.Where(r => r.Legacy).ToList();
        var sections = new List<Section>
        {
            new("header", 0, _ => Header(s)),
            new("five", 2, l => FiveQuestions(five, staleIds, l)),
            split ? new("state", 0, _ => PartsLine(m))
                  : new("state", 2, l => StateNow(s.Handover, m, l, whole: budget is null)),
            new("resume", 2, l => Resume(s, l)),
            new("issues", 3, l => Issues(s.OpenIssues, l)),
            new("stale", 3, l => Stale(stale, unverifiable, l)),
            new("records", 4, l => Records(m, sourced, l)),
            new("events", 3, l => Events(m, s.Events, l)),
        };
        if (!split) sections.Add(new("office", 2, l => Office(s.Office, l)));
        var levels = new int[sections.Count];
        // cut from the bottom up, except STATE NOW: it is cut last, after every other section is at its floor (only the
        // issue and stale ids and the resume's first 1,500 chars outlive it)
        var cutOrder = Enumerable.Range(0, sections.Count).Reverse().OrderBy(i => sections[i].Name == "state" ? 1 : 0).ToList();
        string Compose()
        {
            var parts = sections.Select((x, i) => SecretGuard.Mask(x.Render(levels[i]))!).Where(t => t.Length > 0).ToList();
            var head = string.Join("\n", parts);
            if (legacy.Count == 0) return head;
            // Legacy is cut FIRST: it fills the room left only while no section above it has been cut. Once one has, legacy
            // is its one line - it never refills the room a cut section gave up (seen live 3 Nov 02:24: Office cut to its
            // header while legacy rows still rendered below it).
            var room = budget is null ? (int?)null : levels.Any(l => l > 0) ? 0 : budget.Value - head.Length - 1;
            return head + "\n" + SecretGuard.Mask(Legacy(m, legacy, room));
        }
        var text = Compose();
        while (budget is not null && text.Length > budget)
        {
            var pick = cutOrder.FirstOrDefault(i => levels[i] < sections[i].MaxLevel, -1);
            if (pick < 0) break;
            levels[pick]++;
            text = Compose();
        }
        if (budget is not null && text.Length > budget)
        {
            const string cut = "\n... [cut at the session-start limit: every section is at its floor - kor start --text for all]";
            text = text[..(budget.Value - cut.Length)] + cut;
        }
        return text;
    }

    /// <summary>The state with SecretGuard.Mask applied to every text read from the brain (records, issues, office,
    /// resumes, events, the handover section). Ids, kinds and times are not text a value hides in.</summary>
    internal static ModuleState Masked(ModuleState s)
    {
        static StartRecord R(StartRecord r) => r with
        {
            Description = SecretGuard.Mask(r.Description)!, Body = SecretGuard.Mask(r.Body), CheckRef = SecretGuard.Mask(r.CheckRef),
            SourceRef = SecretGuard.Mask(r.SourceRef), Name = SecretGuard.Mask(r.Name)!,
        };
        static ResumeRow Q(ResumeRow r) => r with { ResumeBlock = SecretGuard.Mask(r.ResumeBlock)!, WrittenBy = SecretGuard.Mask(r.WrittenBy)! };
        static StoredEvent E(StoredEvent e) => e with { Ref = SecretGuard.Mask(e.Ref), Detail = SecretGuard.Mask(e.Detail), Actor = SecretGuard.Mask(e.Actor)! };
        return s with
        {
            Records = s.Records.Select(R).ToList(),
            OpenIssues = s.OpenIssues.Select(R).ToList(),
            Office = s.Office.Select(R).ToList(),
            Resume = s.Resume with
            {
                Chosen = s.Resume.Chosen is null ? null : Q(s.Resume.Chosen), Others = s.Resume.Others.Select(Q).ToList(),
                Why = SecretGuard.Mask(s.Resume.Why)!,
            },
            LastSessionEnd = s.LastSessionEnd is null ? null : E(s.LastSessionEnd),
            Events = s.Events.Select(E).ToList(),
            CheckLines = s.CheckLines.Select(l => SecretGuard.Mask(l)!).ToList(),
            Handover = s.Handover is null ? null : s.Handover with { Section = SecretGuard.Mask(s.Handover.Section) },
        };
    }

    /// <summary>Absolute paths of the files current git-sourced records cite (the FileChanged watch list), at most 200.</summary>
    public static IReadOnlyList<string> WatchPaths(FreshnessReport fresh, int max = 200)
    {
        var dirs = fresh.RepoDirs ?? new Dictionary<string, string>();
        return fresh.Rows
            .Where(r => r.Verdict == Verdict.Current && r.SourceKind == "git" && r.Repo is not null && r.Path is not null && dirs.ContainsKey(r.Repo))
            .OrderByDescending(r => r.Id)
            .Select(r => Path.GetFullPath(Path.Combine(dirs[r.Repo!], r.Path!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    // ---- sections ---------------------------------------------------------------------------------------------------
    private static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Header(ModuleState s)
    {
        var f = s.Fresh;
        var heads = f.RepoHeads.Count == 0 ? "no source-cited record read a repo"
            : string.Join(", ", f.RepoHeads.Select(h => $"{h.Key} {(h.Value is null ? "UNREADABLE" : h.Value[..Math.Min(7, h.Value.Length)])}"));
        var resolutions = s.Records.Count(r => r.Kind == "resolution") + s.ResolvedAtStart;
        var lines = new List<string>
        {
            NotReadLine,
            $"KNOWN AT START - module '{s.Module}' (KorStandards brain, read {Stamp(s.ReadAt)}). Ids: record #N = brain.Memory, " +
            "resume LaneState #N = brain.LaneState, event #N = brain.Event. A record's body: kor memory --module \"" + s.Module +
            "\"; this page uncut: kor start --module \"" + s.Module + "\" --text.",
            $"HEAD: {heads} | freshness: {f.Count(Verdict.Current)} current, {f.Count(Verdict.Stale)} stale, " +
            $"{f.Count(Verdict.Unverifiable)} unverifiable{(f.Partial ? $" (PARTIAL - cap hit after {Freshness.Secs(f.Elapsed)} s)" : "")}" +
            $" | open issues: {s.OpenIssues.Count} | resolutions in history: {resolutions}",
        };
        if (s.CheckLines.Count > 0) lines.Add("QUERY CHECKS AT START: " + string.Join(" | ", s.CheckLines));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The record part's FIRST line (found 3 Nov, done-when 1: a cold NetworkOps session wrote tool calls as text and
    /// reported invented results, "netops fleet lists 41 agents"; on the re-run a cold KOR.RevitTools session wrote
    /// &lt;invoke name="Bash"&gt; as text and then reported "I ran date, git status ... four times, and no output came back").
    /// After that line, a cold Operations session still wrote &lt;invoke&gt; AND &lt;function_results&gt; as text, with invented
    /// output ("Tue Nov  3 04:29:12 PST 2026", "OpsRows 0", trailers no commit carries) and answered from them.
    /// One line, so no compaction can separate it from the page.
    /// </summary>
    public const string NotReadLine =
        "Everything a session started here knows at start is on this page and its KNOWN AT START parts (STATE NOW, MEMORY) and THE BOARD. " +
        "Anything not on them was NOT read - answer 'not checked', never a value. " +
        "A command written as text runs nothing and returns nothing: never write a tool call or its result yourself - a result you did not " +
        "receive from a real tool call is invented.";

    /// <summary>The record part's line, in the split hook, naming the start parts beside it (board row 1198).</summary>
    public static string PartsLine(string module) =>
        "STATE NOW, OFFICE LESSONS and LEGACY MEMORY are not on this part: each is a session-start part of its own beside it " +
        "(blocks headed 'KNOWN AT START - STATE NOW' and 'KNOWN AT START - MEMORY, page N of M'). A part not in your context was " +
        $"not read - answer what it would hold 'not checked'; all of it: kor start --module \"{module}\" --text.";

    /// <summary>The first line of a side part (STATE NOW, LEGACY MEMORY pages): what it is and that it is part of the start.</summary>
    private static string SideHead(string module, string what, DateTimeOffset readAt) =>
        $"KNOWN AT START - {what} - module '{module}' (a session-start part beside the record part 'KNOWN AT START' and THE BOARD; " +
        $"read {Stamp(readAt)}).";

    /// <summary>
    /// The STATE NOW part (the SessionStart hook, board row 1198): the handover's STATE NOW section WHOLE when it fits
    /// <paramref name="budget"/>, else cut at a whole line with a note naming the rest (git show HEAD:path). Measured 3 Nov:
    /// the longest of the 6 KOR modules' is KOR.Drafter's, 7,330 chars, which fits whole.
    /// </summary>
    public static string State(StartSide side, int? budget)
    {
        var h = side.Handover is { } x ? x with { Section = SecretGuard.Mask(x.Section) } : null;
        var head = SideHead(side.Module, "STATE NOW", side.ReadAt);
        var body = StateNow(h, side.Module, 0, whole: true);
        if (body.Length == 0) body = "STATE NOW: not read - no handover state was read. Answer anything that needs the module's current state 'not checked'.";
        var text = head + "\n" + body;
        if (budget is null || text.Length <= budget || h?.Section is null) return SecretGuard.Mask(text)!;
        var room = budget.Value - (text.Length - h.Section.Length);
        return SecretGuard.Mask(head + "\n" + StateNow(h with { Section = HandoverState.CapLines(h.Section, room, h.Path) }, side.Module, 0, whole: true))!;
    }

    /// <summary>
    /// One page of the MEMORY part (the SessionStart hook, board row 1198): OFFICE LESSONS whole, then the lane's imported
    /// file memory (LEGACY MEMORY - its curated index whole, the MEMORY.md it was imported from, else its rows), then the
    /// line saying where new memories go - split at whole lines into pages of at most <paramref name="budget"/> each.
    /// Page <paramref name="page"/> of the <paramref name="pages"/> wired; a page past the content is empty (nothing to
    /// print); content past the last wired page is cut there with a note. Measured 3 Nov: the longest is NetworkOps',
    /// 1,866 office + 21,617 legacy chars - 3 pages of the 4 wired.
    /// </summary>
    public static string Memory(StartSide side, int page, int pages, int? budget)
    {
        if (page < 1 || pages < page) throw new ArgumentException($"--page {page} is not within 1..{pages}");
        static StartRecord M(StartRecord r) => r with
        {
            Description = SecretGuard.Mask(r.Description)!, Body = SecretGuard.Mask(r.Body), Name = SecretGuard.Mask(r.Name)!,
        };
        var legacy = side.Records.Where(r => r.Legacy).Select(M).ToList();
        var office = side.Office.Select(M).ToList();
        var blocks = new List<string> { WriteLine(side.Module) };
        if (office.Count > 0) blocks.Add(Office(office, 0, capOverride: int.MaxValue));
        blocks.Add(legacy.Count > 0 ? Legacy(side.Module, legacy, null) : "LEGACY MEMORY: none - this lane has no imported file memory.");
        var body = string.Join("\n", blocks);
        if (budget is null) return SecretGuard.Mask(SideHead(side.Module, "MEMORY", side.ReadAt) + "\n" + body)!;
        // the head names "page N of M"; M is the count of pages the content needs, at most the wired ones
        const int headRoom = 400;
        var chunks = Chunk(body, budget.Value - headRoom);
        if (page > chunks.Count) return "";
        var shown = Math.Min(chunks.Count, pages);
        var head = SideHead(side.Module, $"MEMORY, page {page} of {shown}", side.ReadAt);
        var text = chunks[page - 1];
        if (page == pages && chunks.Count > pages)
        {
            var note = $"\n... [memory continues past page {pages} of the session start ({chunks.Count - pages} more page(s)) - " +
                       $"kor start --module \"{side.Module}\" --part memory --text for all]";
            text = text[..Math.Max(0, Math.Min(text.Length, budget.Value - headRoom - note.Length))].TrimEnd() + note;
        }
        return SecretGuard.Mask(head + "\n" + text)!;
    }

    private static string WriteLine(string module) =>
        "This is the module's memory, from the DB. A '[title](file.md)' line below names the record whose name is that file's stem: " +
        $"python -I \"C:/VIsual Studio Projects/Framework/tools/kor_brain.py\" show --lane \"{module}\" --name <stem> (Office lines: --lane Office). " +
        $"New memories go into the DB (kor record --module \"{module}\" ..., or as this repo's CLAUDE.md says), never into a memory file.";

    /// <summary>Whole lines into chunks of at most <paramref name="max"/> chars (a single longer line is split hard).</summary>
    internal static List<string> Chunk(string text, int max)
    {
        var chunks = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            while (line.Length > max) { Flush(); chunks.Add(line[..max]); line = line[max..]; }
            if (cur.Length > 0 && cur.Length + 1 + line.Length > max) Flush();
            if (cur.Length > 0) cur.Append('\n');
            cur.Append(line);
        }
        Flush();
        return chunks;
        void Flush() { if (cur.Length > 0) { chunks.Add(cur.ToString()); cur.Clear(); } }
    }

    private static string StateNow(HandoverState? h, string module, int level, bool whole)
    {
        if (h is null) return "";
        if (h.Section is null)
            return $"STATE NOW: not read - {h.Why}. Answer anything that needs the module's current state 'not checked'.";
        var head = $"STATE NOW - the module's current state, from its handover at HEAD ({h.Where}):";
        var lines = new List<string> { head };
        if (h.Dirty) lines.Add("the handover has uncommitted changes - commit them to show them here");
        lines.Add(level switch
        {
            0 => whole ? h.Section : HandoverState.CapLines(h.Section, HandoverState.Cap, h.Path),
            1 => HandoverState.CapLines(h.Section, 1500, h.Path),
            _ => $"[STATE NOW cut at the session-start limit - kor start --module \"{module}\" --text, or git show HEAD:{h.Path}]",
        });
        return string.Join("\n", lines);
    }

    private static string FiveQuestions(StartRecord? five, HashSet<int> staleIds, int level)
    {
        if (five is null)
            return "FIVE QUESTIONS: no _five-questions record in this module - read its handover (docs/HANDOVER.md) for them.";
        var state = staleIds.Contains(five.Id) || five.Status == "stale"
            ? "STALE - the handover changed since it was recorded: read the handover, then kor recheck " + five.Id
            : "current";
        var head = $"FIVE QUESTIONS (record #{five.Id}, {five.Where}, {state}) - answer them before any work:";
        var body = (five.Body ?? five.Description).Trim();
        return level switch
        {
            0 => head + "\n" + body,
            1 => head + "\n" + Clip(body, 1500, $"[cut - kor memory --module \"{five.Lane}\"]"),
            _ => head + " [body cut at the session-start limit - kor start --text]",
        };
    }

    private static string Resume(ModuleState s, int level)
    {
        var c = s.Resume;
        var lines = new List<string>();
        if (c.Chosen is { } r)
        {
            lines.Add((r.Id == 0 ? $"RESUME - not yet a LaneState: {r.WrittenBy}, written {Stamp(r.WrittenAtUtc)}"
                                 : $"RESUME - LaneState #{r.Id}, written {Stamp(r.WrittenAtUtc)} by {r.WrittenBy}") +
                      (r.Session is null ? "" : $" (session {r.Session})") + $"; chosen: {c.Why}:");
            // the first ResumeKeep characters are a floor: kept whole, the note goes AFTER them
            var block = r.ResumeBlock.Trim();
            var keep = level == 0 ? ResumeCap : ResumeKeep;
            lines.Add(block.Length <= keep ? block
                : block[..keep] + $" ... [resume cut at {keep} of {block.Length} chars - kor start --module \"{s.Module}\" --text]");
        }
        else lines.Add($"RESUME: {c.Why}.");
        if (s.LastSessionEnd is { } e)
            lines.Add($"LAST SESSION END: event #{e.Id} {Stamp(e.AtUtc)} ({e.Outcome ?? "no reason"})" + (e.Session is null ? "" : $" session {e.Session}"));
        if (level < 2 && c.Others.Count > 0)
        {
            lines.Add("Other recent resumes (NOT the resume - another session's, listed so none is lost):");
            foreach (var o in c.Others)
                lines.Add($"- LaneState #{o.Id} {Stamp(o.WrittenAtUtc)} by {o.WrittenBy}" + (o.Session is null ? "" : $", session {o.Session}") +
                          (o.Eligible ? "" : " - that session did no work in this module"));
        }
        return string.Join("\n", lines);
    }

    private static string Issues(IReadOnlyList<StartRecord> open, int level)
    {
        if (open.Count == 0) return "OPEN ISSUES: none.";
        var lines = new List<string>
        {
            $"OPEN ISSUES ({open.Count}) - each closes only by evidence: kor check --issue N | kor resolve --issue N (a source check) | kor issue --restates N:",
        };
        if (level >= 3)
        {
            lines.Add("ids: " + string.Join(", ", open.Select(i => $"#{i.Id}")));
            return string.Join("\n", lines);
        }
        foreach (var i in open)
        {
            var reg = i.RegressionOf is { } x ? $" (REGRESSION of #{x})" : "";
            lines.Add(level switch
            {
                0 => $"- record #{i.Id} {i.Name} [{i.Status}]{reg} - {i.Description}\n    check {i.CheckKind}: {Clip(i.CheckRef ?? "-", 300, "...")}",
                1 => $"- record #{i.Id} {i.Name} [{i.Status}]{reg} - {Clip(i.Description, 80, "...")}",
                _ => $"- #{i.Id} {i.Name}{reg}",
            });
        }
        return string.Join("\n", lines);
    }

    private static string Stale(List<Checked> stale, List<Checked> unverifiable, int level)
    {
        var lines = new List<string>();
        if (stale.Count == 0) lines.Add("STALE: none - every source-cited record read matches HEAD.");
        else
        {
            lines.Add($"STALE ({stale.Count}) - its source changed; re-check each: kor recheck <id> confirm --because \"...\" | " +
                      "supersede --because \"...\" --body-file F | withdraw --because \"...\" (a source-checked issue: kor resolve --issue <id>):");
            if (level >= 3) lines.Add("ids: " + string.Join(", ", stale.Select(r => $"#{r.Id}")));
            else
                foreach (var r in stale)
                    lines.Add(level switch
                    {
                        0 => $"- record #{r.Id} [{r.Kind}] {r.Name}  {r.Repo ?? r.SourceKind}:{r.Path} - {r.Why}",
                        1 => $"- record #{r.Id} [{r.Kind}] {r.Name}",
                        _ => $"- #{r.Id} {r.Name}",
                    });
        }
        if (unverifiable.Count > 0)
            lines.Add($"UNVERIFIABLE ({unverifiable.Count}) - not checked, neither current nor stale: " +
                      string.Join(", ", unverifiable.Select(r => level == 0 ? $"#{r.Id} {r.Name} ({r.Why})" : $"#{r.Id}")));
        return string.Join("\n", lines);
    }

    private static readonly (string Kind, string Title)[] Groups =
    {
        ("decision", "DECISIONS"), ("dead-end", "DEAD ENDS (tried, failed - do not repeat)"), ("lesson", "LESSONS"), ("finding", "FINDINGS"),
    };

    private static string Records(string module, List<StartRecord> sourced, int level)
    {
        var groups = Groups.Select(g => (g.Title, Rows: sourced.Where(r => r.Kind == g.Kind).ToList())).ToList();
        groups.Add(("OTHER SOURCED RECORDS", sourced.Where(r => Groups.All(g => g.Kind != r.Kind)).ToList()));
        if (level >= 4)
            return "RECORDS (current): " + string.Join(", ", groups.Select(g => $"{g.Rows.Count} {g.Title.Split(' ')[0].ToLowerInvariant()}")) +
                   $" - cut at the session-start limit: kor start --module \"{module}\" --text";
        var lines = new List<string>();
        foreach (var (title, rows) in groups)
        {
            if (rows.Count == 0) { if (level == 0) lines.Add($"{title}: none current."); continue; }
            if (level == 3) { lines.Add($"{title}: " + string.Join(", ", rows.Select(r => $"#{r.Id} {r.Name}"))); continue; }
            lines.Add($"{title} ({rows.Count}, current):");
            foreach (var r in rows)
                lines.Add(level switch
                {
                    0 => $"- record #{r.Id} [{r.Kind}] {r.Name} - {r.Description} ({r.Where})",
                    1 => $"- record #{r.Id} [{r.Kind}] {r.Name} - {Clip(r.Description, 80, "...")} ({r.Where})",
                    _ => $"- #{r.Id} {r.Name} ({r.Where})",
                });
        }
        return string.Join("\n", lines);
    }

    private static string Events(string module, IReadOnlyList<StoredEvent> events, int level)
    {
        if (events.Count == 0) return "LAST EVENTS: none recorded for this module.";
        if (level >= 3) return $"LAST EVENTS: cut at the session-start limit - kor events --module \"{module}\" --last 10";
        var take = level >= 2 ? 5 : 10;
        var lines = new List<string> { $"LAST EVENTS ({Math.Min(take, events.Count)}, newest first):" };
        foreach (var e in events.Take(take))
        {
            var at = e.Repo is null ? "" : $" {e.Repo}" + (e.Commit is { Length: >= 7 } c ? $"@{c[..7]}" : "");
            var line = $"- event #{e.Id} {Stamp(e.AtUtc)} {e.Kind} {e.Outcome ?? "-"}{at}";
            if (level == 0 && e.Ref is not null) line += " " + Clip(e.Ref.ReplaceLineEndings(" "), 100, "...");
            if (e.Session is not null) line += $" (session {e.Session[..Math.Min(8, e.Session.Length)]})";
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static string Office(IReadOnlyList<StartRecord> office, int level, int? capOverride = null)
    {
        if (office.Count == 0) return "";
        var cap = capOverride ?? (level == 0 ? OfficeMax : 1200);
        var head = $"OFFICE LESSONS (lane '{ModuleState.OfficeLane}') - every KOR lead gets these, the same day they are learned. They bind here too. " +
                   $"Body: kor memory --module {ModuleState.OfficeLane}.";
        if (level >= 2) return head + " [cut at the session-start limit - kor start --text]";
        var index = office.FirstOrDefault(r => r.Name == ModuleState.IndexName)?.Body;
        var text = index is not null ? head + "\n" + index.Trim()
            : string.Join("\n", new[] { head }.Concat(office.Where(r => r.Name != ModuleState.IndexName)
                                                           .Select(r => $"- record #{r.Id} [{r.Kind}] {r.Name} - {r.Description}")));
        if (text.Length <= cap) return text;
        var note = $"\n... [Office cut at {cap} chars of {text.Length} - kor memory --module {ModuleState.OfficeLane}]";
        return text[..(cap - note.Length)].TrimEnd() + note;
    }

    private static string Legacy(string module, List<StartRecord> legacy, int? room)
    {
        var index = legacy.FirstOrDefault(r => r.Name == ModuleState.IndexName);
        var rows = legacy.Where(r => r.Name != ModuleState.IndexName).ToList();
        var head = $"LEGACY MEMORY ({rows.Count} rows imported from the file memories: no source, so they never go stale - weigh them " +
                   "below every sourced record above)" + (index is not null ? $"; the lane's curated index, record #{index.Id}:" : ":");
        var full = index?.Body is { } b ? head + "\n" + b.Trim()
            : string.Join("\n", new[] { head }.Concat(rows.Select(r => $"- record #{r.Id} [{r.Kind}] {r.Name} - {r.Description}")));
        if (room is null || full.Length <= room) return full;
        var note = $"\n... [legacy memory cut at the session-start limit - kor start --module \"{module}\" --text for all]";
        var minimal = $"LEGACY MEMORY: {rows.Count} imported rows cut at the session-start limit - kor start --module \"{module}\" --text";
        if (room < head.Length + note.Length + 200) return minimal;
        return full[..(room.Value - note.Length)].TrimEnd() + note;
    }

    private static string Clip(string s, int max, string note) =>
        s.Length <= max ? s : s[..Math.Max(0, max - note.Length - 1)].TrimEnd() + " " + note;
}
