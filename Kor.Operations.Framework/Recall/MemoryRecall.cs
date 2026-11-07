using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Recall;

/// <summary>
/// Board row 1893, the ranked recall PILOT: <c>kor recall --module M "task"</c>. Read-only (BrainDb.Read: its Commit throws), not wired to any
/// hook. One module's live records ranked by SQL Server Full-Text (FREETEXTTABLE over migration 178's index on Name, Description, Body),
/// delivered in rank order as whole sections, verbatim but for SecretGuard's mask, up to a token budget. Each result names its id, score,
/// source and as-of date. A ranked search always returns something, so the head says which module was searched, which terms of the task
/// are found nowhere in it and which were ignored as stop words, and the tail names what the budget left out.
/// Scope: the module's rows in brain.vw_ModuleState (live, including stale ones, which carry their status), without issues, _index and
/// _five-questions, the rows kor read leaves out. Office and other lanes are NOT searched. The score is SQL Server's rank, whose term statistics
/// are over the WHOLE table, every lane and retired rows too: it orders one result list and means nothing across two.
/// A section is chosen inside a row by counting task terms from a word's start, cut to a rough stem: that choice is this code's, not SQL
/// Server's, so a row can rank on a word form this count misses, and then its first section is delivered.
/// Does NOT do: synonyms beyond FREETEXT's stemming (the thesaurus is empty), links between records, a record written in the last seconds
/// (the crawl is asynchronous), any log of what was delivered. Same-class miss: a record that needs a word the task does not use.
/// </summary>
public static class MemoryRecall
{
    /// <summary>Tokens per char, measured on the Operations pages (Framework docs/START-FULL-MEMORY.md: 79,667 tokens for 189,806 chars).</summary>
    public const double TokensPerChar = 0.42;

    /// <summary>The reviewer's about 20 sections (answer-memory.md: 12.6k tokens at 0.42).</summary>
    public const int DefaultBudgetTokens = 12_600;

    /// <summary>FREETEXTTABLE refuses nvarchar(max) (read 6 Nov), so the verb refuses a longer task rather than search part of it.</summary>
    public const int MostTaskChars = 4_000;

    public const int SectionChars = 1_500;
    public const int MostSectionsPerRecord = 2;
    public const int MostLeftOutNamed = 30;
    public const int MostPartlyNamed = 12;

    /// <summary>Kept back for the RESULTS line (under 200 chars) and the tail's two lines at their longest: 30 ids of up to 7 digits, 12 "#id section N".</summary>
    private const int TailReserveChars = 1_000;
    public const string NowhereHead = "FOUND NOWHERE IN THIS MODULE:";
    public const string LeftOutHead = "LEFT OUT BY THE BUDGET:";

    public sealed record Hit(int Id, int Score, string Kind, string Name, string Description, string Body, string Status, string Source, DateTimeOffset AsOf);
    public sealed record TermCount(string Term, int Records);
    public sealed record Found(int ModuleRecords, IReadOnlyList<Hit> Hits, IReadOnlyList<TermCount> Terms, IReadOnlyList<string> StopWords);

    public static int Tokens(int chars) => (int)Math.Ceiling(chars * TokensPerChar);

    /// <summary>The task's words, lower case, in order, once each: letters, digits and an inner apostrophe only ("don't" stays one word, so it is
    /// not reported as "don"), so each is safe inside a double-quoted CONTAINS term.</summary>
    public static List<string> Terms(string task) =>
        Regex.Split(task.Replace('’', '\''), @"[^\p{L}\p{N}']+").Select(t => t.Trim('\'').ToLowerInvariant()).Where(t => t.Length >= 2).Distinct().ToList();

    /// <summary>
    /// A body cut at paragraph ends into pieces of at most <paramref name="size"/> chars; a paragraph over the size is cut at line ends; a line
    /// over the size at sentence ends; a sentence over the size stays whole. string.Concat of the result is the body, byte for byte.
    /// </summary>
    public static List<string> Sections(string body, int size)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0) pieces.Add(current.ToString());
            current.Clear();
        }
        void Add(string piece)
        {
            if (current.Length > 0 && current.Length + piece.Length > size) Flush();
            current.Append(piece);
        }
        foreach (var paragraph in Regex.Split(body, @"(?<=\n[ \t]*\r?\n)"))
        {
            if (paragraph.Length <= size) { Add(paragraph); continue; }
            foreach (var line in Regex.Split(paragraph, @"(?<=\n)"))
            {
                if (line.Length <= size) { Add(line); continue; }
                foreach (var sentence in Regex.Split(line, @"(?<=[.!?;:]\s)")) Add(sentence);
            }
        }
        Flush();
        return pieces;
    }

    private static string Stem(string term) => term.Length <= 4 ? term : term[..Math.Max(4, term.Length - 2)];

    /// <summary>Indexes of the sections holding the most distinct terms, best first, at most <paramref name="most"/>; empty when none holds one.</summary>
    public static List<int> BestSections(IReadOnlyList<string> sections, IReadOnlyList<string> terms, int most)
    {
        var starts = terms.Select(t => new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(Stem(t)), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)).ToList();
        return sections.Select((s, i) => (Index: i, Count: starts.Count(r => r.IsMatch(s))))
            .Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Index).Take(most).Select(x => x.Index).ToList();
    }

    /// <summary>The database's part, on a read-only unit of work.</summary>
    public static Found Search(BrainDb db, string module, string task)
    {
        int moduleRecords;
        using (var c = db.Command("SELECT COUNT(*) FROM brain.vw_ModuleState WHERE Module = @m AND Kind <> N'issue' AND Name NOT IN (N'_five-questions', N'_index');", 60))
        {
            c.Parameters.Add("@m", SqlDbType.NVarChar, 60).Value = module;
            moduleRecords = (int)c.ExecuteScalar();
        }
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var c = db.Command("SELECT stopword FROM sys.fulltext_system_stopwords WHERE language_id = 1033;", 60))
        using (var r = c.ExecuteReader())
            while (r.Read()) stop.Add(r.GetString(0));
        var terms = Terms(task);
        var counts = new List<TermCount>();
        foreach (var t in terms.Where(t => !stop.Contains(t)))
        {
            using var c = db.Command("SELECT COUNT(*) FROM CONTAINSTABLE(brain.Memory, (Name, Description, Body), @q) k " +
                                     "JOIN brain.vw_ModuleState m ON m.Id = k.[KEY] WHERE m.Module = @m AND m.Kind <> N'issue' AND m.Name NOT IN (N'_five-questions', N'_index');", 60);
            c.Parameters.Add("@q", SqlDbType.NVarChar, 400).Value = $"FORMSOF(INFLECTIONAL, \"{t}\")";
            c.Parameters.Add("@m", SqlDbType.NVarChar, 60).Value = module;
            counts.Add(new TermCount(t, (int)c.ExecuteScalar()));
        }
        var hits = new List<Hit>();
        if (counts.Any(t => t.Records > 0))
        {
            using var c = db.Command(
                "SELECT m.Id, k.[RANK], m.Kind, m.Name, m.Description, m.Body, m.Status, m.SourceKind, m.SourceRepo, m.SourceCommit, m.SourcePath, m.SourceRef, b.Source, " +
                "       COALESCE(b.UpdatedAtUtc, b.CreatedAtUtc) " +
                "FROM FREETEXTTABLE(brain.Memory, (Name, Description, Body), @task, LANGUAGE 1033) k " +
                "JOIN brain.vw_ModuleState m ON m.Id = k.[KEY] JOIN brain.Memory b ON b.Id = m.Id " +
                "WHERE m.Module = @m AND m.Kind <> N'issue' AND m.Name NOT IN (N'_five-questions', N'_index') " +
                "ORDER BY k.[RANK] DESC, m.Id DESC;", 60);
            c.Parameters.Add("@task", SqlDbType.NVarChar, MostTaskChars).Value = task;
            c.Parameters.Add("@m", SqlDbType.NVarChar, 60).Value = module;
            using var r = c.ExecuteReader();
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (r.Read())
                hits.Add(new Hit(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), S(4) ?? "", S(5) ?? "", r.GetString(6),
                                 SourceLabel(S(7), S(8), S(9), S(10), S(11), S(12)), r.GetDateTimeOffset(13)));
        }
        return new Found(moduleRecords, hits, counts, terms.Where(stop.Contains).ToList());
    }

    /// <summary>Where a record says it came from, or that it says nothing: 895 imported rows have no path (answer-memory.md), so "current" is not "true".</summary>
    public static string SourceLabel(string? kind, string? repo, string? commit, string? path, string? sourceRef, string? source)
    {
        if (kind == "git" && path is not null) return $"{repo}:{path}{(commit is null ? "" : "@" + commit[..Math.Min(7, commit.Length)])}";
        if (kind == "run") return $"run {sourceRef}";
        if (!string.IsNullOrWhiteSpace(path)) return path;
        if (kind == "file" && !string.IsNullOrWhiteSpace(sourceRef)) return $"file: {sourceRef} (imported file, history only)";   // the ref cites the file and its SHA-256 (ModuleState.Where)
        if (kind is not null && !string.IsNullOrWhiteSpace(sourceRef)) return $"{kind} {sourceRef}";   // query, session: their ref is their source
        return string.IsNullOrWhiteSpace(source) ? "none recorded, history only" : $"{source} (no path, history only)";
    }

    /// <summary>What the verb prints, whole, within <paramref name="budgetTokens"/>; never a cut section.</summary>
    public static string Render(string module, string task, Found found, int budgetTokens, string readAt)
    {
        var nowhere = found.Terms.Where(t => t.Records == 0).Select(t => t.Term).ToList();
        var known = found.Terms.Count - nowhere.Count;
        var head = new StringBuilder();
        head.Append($"KOR RECALL - module '{module}' searched ({found.ModuleRecords} live records; Office and other lanes NOT searched); SQL Server Full-Text; read {readAt}.\n");
        head.Append($"TASK: {task.ReplaceLineEndings(" ")}\n");
        head.Append($"TERMS: {known} of {found.Terms.Count} are in some record of this module.\n");
        head.Append($"{NowhereHead} {(nowhere.Count == 0 ? "none" : string.Join(", ", nowhere))}\n");
        if (found.StopWords.Count > 0) head.Append($"IGNORED AS STOP WORDS: {string.Join(", ", found.StopWords)}\n");
        head.Append("A ranked search always returns something: a result may have nothing to do with the task. Absence here is only absence of these words.\n");
        var room = (int)Math.Floor(budgetTokens / TokensPerChar) - TailReserveChars;
        if (head.Length > room)
            throw new ArgumentException($"the task's head alone is {head.Length} chars and the budget of {budgetTokens} tokens leaves {room} for it: give a shorter task or a larger --budget");
        if (found.Hits.Count == 0)
        {
            head.Append("NOTHING FOUND: no live record of this module matches. Nothing here says the record does not exist under other words.\n");
            return head.ToString();
        }
        var terms = found.Terms.Where(t => t.Records > 0).Select(t => t.Term).ToList();
        var spent = head.Length;
        var body = new StringBuilder();
        var delivered = 0;
        var left = new List<Hit>();
        var partly = new List<string>();
        foreach (var hit in found.Hits)
        {
            var sections = Sections(SecretGuard.Mask(hit.Body) ?? "", SectionChars);
            if (sections.Count == 0) sections.Add("");   // an empty body: the record's head still says what it is
            var pick = BestSections(sections, terms, MostSectionsPerRecord);
            if (pick.Count == 0) pick.Add(0);
            var any = false;
            var dropped = new List<int>();
            foreach (var i in pick)
            {
                var piece = $"=== record #{hit.Id} [{hit.Kind}] {SecretGuard.Mask(hit.Name)} - score {hit.Score} - source {SecretGuard.Mask(hit.Source)} - as of " +
                            $"{hit.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - status {hit.Status} - section {i + 1} of {sections.Count} - {SecretGuard.Mask(hit.Description)}\n" +
                            $"{sections[i]}\n=== end #{hit.Id}\n";
                if (spent + piece.Length > room) { dropped.Add(i + 1); continue; }
                body.Append(piece);
                spent += piece.Length;
                any = true;
            }
            if (any)
            {
                delivered++;
                partly.AddRange(dropped.Select(d => $"#{hit.Id} section {d}"));
            }
            else left.Add(hit);
        }
        var named = string.Join(", ", left.Take(MostLeftOutNamed).Select(h => $"#{h.Id}"));
        head.Append($"RESULTS: {found.Hits.Count} records matched; {delivered} delivered, in rank order, up to {MostSectionsPerRecord} whole sections each, within {budgetTokens.ToString("N0", CultureInfo.InvariantCulture)} tokens.\n");
        var tail = left.Count == 0 ? $"{LeftOutHead} none\n"
            : $"{LeftOutHead} {left.Count} records, highest rank first: {named}{(left.Count > MostLeftOutNamed ? $" and {left.Count - MostLeftOutNamed} more" : "")}\n";
        if (partly.Count > 0)
            tail += $"SECTIONS LEFT OUT OF RECORDS DELIVERED: {string.Join(", ", partly.Take(MostPartlyNamed))}{(partly.Count > MostPartlyNamed ? $" and {partly.Count - MostPartlyNamed} more" : "")}\n";
        return head.ToString() + body + tail;
    }
}
