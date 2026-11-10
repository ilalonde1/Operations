using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Recall;

/// <summary>
/// Board row 1893, rolled out (BRAIN lane, 10 Nov): the record brought to the MOMENT of acting. <c>kor recall "text"</c> ranks, for one
/// piece of text (a prompt, a command, a brief, a message, a reply), the live records of EVERY lane (brain.Memory, retired and superseded
/// rows left out) together with the open board rows of EVERY owner (oversight.Commitment, State open/doing/blocked), and returns the top N
/// with each one's id and its first sentence. The pilot's <see cref="MemoryRecall"/> (one module, whole sections, Full-Text) is unchanged.
///
/// HOW IT RANKS. BM25 in this code (k1 1.2, b 0.75) over an <see cref="Index"/> of the whole searchable brain, on this code's own words:
/// lower case, letters and digits, a light suffix stem (<see cref="Stem"/>), stop words and path noise dropped (<see cref="Words"/>), a word
/// holding a digit weighted <see cref="DigitWeight"/>. A record is scored on Name + Description + the first <see cref="BodyChars"/> of its
/// Body; a board row on What + DoneWhen. Document frequency is over the whole index. Copies of one record across lanes (same Name and
/// Description; 49% of live rows, answer-memory.md) are indexed once, under the newest id. NOT SQL Full-Text: FREETEXTTABLE took 6.6 s on
/// one real 700-char brief (10 Nov, sqlcmd), too slow for a hook on every tool call; the reviewed design's ranking is BM25 either way.
///
/// WHAT IT DOES NOT DO. No synonyms (a record that needs a word the moment does not use is missed, the class the 5 Nov review measured:
/// ranked search found 40-57%); no link following; no knowledge of who the record is for. The hook's index file is up to about ten
/// minutes old (Program.Recall). A high score is not relevance: the hook shows only
/// results over <see cref="MinScore"/> matching at least <see cref="MinTermsHit"/> distinct words of the moment.
/// </summary>
public static class BrainRecall
{
    public const int MostTextChars = 4_000;
    public const int BodyChars = 20_000;
    public const int MostQueryWords = 120;
    public const double K1 = 1.2, B = 0.75;

    /// <summary>The hook's floor, calibrated 10 Nov on the 5 traps and on ordinary commands (BrainRecallTests states the calibration).</summary>
    public const double MinScore = 30.0;
    public const int MinTermsHit = 3;

    public sealed record Doc(string Ref, int Id, bool Board, string Kind, string Where, string Name, string Lead, string Text, DateTimeOffset? AsOf);
    public sealed record Ranked(Doc Doc, double Score, IReadOnlyList<string> Hit);

    // English function words, and words every command and path carries. Stems, as Words compares them.
    private static readonly HashSet<string> StopStems = new(new[]
    {
        "a","an","the","and","or","but","if","then","else","of","to","in","on","at","by","for","with","from","into","onto","as","is","are","was",
        "were","be","been","being","it","its","this","that","these","those","there","here","so","not","no","yes","do","doe","did","done","have",
        "ha","had","will","would","shall","should","can","could","may","might","must","i","me","my","we","our","us","you","your","he","she",
        "they","them","their","his","her","what","which","who","whom","whose","when","where","why","how","all","any","each","every","some",
        "one","two","than","too","very","just","only","also","about","after","before","up","down","out","over","under","again","once","more",
        "most","other","such","own","same","now","still","yet","get","got","go","going","let","make","made","use","used","via","per","vs",
        "nov","oct","dec","am","pm","min","sec","hour","hr","ok","re","etc","eg","ie","s","t","ll","ve","d","m",
        // commands and paths: the words that carry no topic
        "cd","ls","cat","grep","sed","head","tail","wc","echo","printf","tr","cut","sort","uniq","find","xarg","python","py","dotnet","git",
        "exe","dll","json","txt","md","sql","ps1","log","csv","cs","tmp","temp","user","ilalonde","appdata","local","claude","scratchpad",
        "visual","studio","c","dev","null","true","false","nocount","select","from","where","order","top","desc","asc","join","set",
        "utf8","utf","date","exit","stdout","stderr","http","https","www","com",
    }.Select(Stem));

    /// <summary>A light suffix stem, the same for the moment and the records: plurals, -ing, -ed, a final e (measure, measured,
    /// measuring all become measur). Not Porter; short words are left alone.</summary>
    public static string Stem(string w)
    {
        w = w.ToLowerInvariant();
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) w = w[..^3] + "y";
        else if (w.Length > 4 && w.EndsWith("sses", StringComparison.Ordinal)) w = w[..^2];
        else if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal)) w = w[..^1];
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) w = w[..^3];
        else if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) w = w[..^2];
        if (w.Length > 4 && w.EndsWith('e')) w = w[..^1];
        return w;
    }

    private static readonly Regex PathNoise = new(@"(?i)(?:[a-z]:|/[a-z])[/\\]+users[/\\]+[^\s""'`]*|visual studio projects|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    /// <summary>The stems of a text, in order, repeats kept: letters and digits only, 2+ chars, user paths and GUIDs removed, a bare
    /// number under 3 digits dropped (times, counts), a long letter-digit run dropped (hashes), stop words dropped.</summary>
    public static List<string> Words(string text)
    {
        var clean = PathNoise.Replace(text, " ");
        var list = new List<string>();
        foreach (Match m in Word.Matches(clean))
        {
            var w = m.Value.ToLowerInvariant();
            if (w.Length < 2) continue;
            var digits = w.Count(char.IsDigit);
            if (digits == w.Length && w.Length < 3) continue;
            if (w.Length >= 7 && digits > 0 && digits < w.Length && !Regex.IsMatch(w, @"^\d+[a-z]{1,2}$")) continue;
            var s = Stem(w);
            if (s.Length < 2 || StopStems.Contains(s)) continue;
            list.Add(s);
        }
        return list;
    }

    /// <summary>The text a hook moment carries, by tool: a command and its description, a brief, a message, a written file's path and
    /// content, a prompt. Null for a tool recall does not read.</summary>
    public static string? MomentText(string? tool, System.Text.Json.JsonElement input)
    {
        string? S(string n) => input.ValueKind == System.Text.Json.JsonValueKind.Object && input.TryGetProperty(n, out var v)
                                && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        string Join(params string?[] parts) => string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return tool switch
        {
            "Bash" or "PowerShell" => Join(S("description"), S("command")),
            "Agent" or "Task" => Join(S("description"), S("prompt")),
            "SendMessage" => Join(S("summary"), S("message")),
            "Write" => Join(S("file_path"), S("content")),
            _ => null,
        };
    }

    /// <summary>The first sentence of a text: up to the first . ! or ? followed by a space or the end, once at least 40 chars are in
    /// (so "e.g." or "5 Nov." early does not end it); whole when there is none; then cut at <paramref name="most"/> chars with "...".</summary>
    public static string FirstSentence(string text, int most)
    {
        var t = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        var m = Regex.Match(t.Length > 40 ? t[40..] : "", @"[.!?](\s|$)");
        var s = m.Success ? t[..(40 + m.Index + 1)] : t;
        return s.Length <= most ? s : s[..(most - 3)].TrimEnd() + "...";
    }

    /// <summary>The whole searchable brain as BM25 postings: every live record of every lane (copies across lanes kept once, the highest id)
    /// and every open board row. Built from the database (<see cref="Build"/>); the hook reads it from a local file (<see cref="Save"/>,
    /// <see cref="Load"/>) so a tool call costs no database round trip.</summary>
    public sealed class Index
    {
        public DateTime BuiltUtc { get; init; }
        public List<Doc> Docs { get; init; } = new();
        public int[] Len { get; init; } = Array.Empty<int>();
        public double AvgLen { get; init; }
        /// <summary>Term to (doc, tf) postings; after a partial <see cref="Load"/>, only the terms asked for.</summary>
        public Dictionary<string, List<(int Doc, int Tf)>> Postings { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>Kept for the push-back's quote of Ian's words: decisions and dead ends only.</summary>
    public const int DecisionTextChars = 3_000;

    /// <summary>Reads both stores and indexes them. Memory through the Brain login, the board through the Board login (korbrain_app cannot read
    /// oversight.Commitment, 10 Nov). <paramref name="asOfUtc"/>: only records and board rows written by then (a replay of a past moment must
    /// not be shown what was written after it); a board row's state is today's, so a row closed since is not in it.</summary>
    public static Index Build(BrainDb brain, BrainDb board, DateTime? asOfUtc = null, int commandSeconds = 60)
    {
        var docs = new List<Doc>();
        var texts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using (var c = brain.Command(
                   "SELECT Id, Kind, Lane, Name, Description, LEFT(Body, @bodyChars), COALESCE(UpdatedAtUtc, CreatedAtUtc) FROM brain.Memory " +
                   "WHERE RetiredAtUtc IS NULL AND Status <> N'superseded' AND (@asOf IS NULL OR CreatedAtUtc <= @asOf) ORDER BY Id DESC;", commandSeconds))
        {
            c.Parameters.Add("@bodyChars", SqlDbType.Int).Value = BodyChars;
            c.Parameters.Add("@asOf", SqlDbType.DateTime2).Value = (object?)asOfUtc ?? DBNull.Value;
            using var r = c.ExecuteReader();
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            while (r.Read())
            {
                var (id, kind, name, desc, body) = (r.GetInt32(0), S(1), S(3), S(4), S(5));
                if (!seen.Add(name + "\u0001" + desc)) continue;   // a copy in another lane: the newest id stands for it
                var decided = kind is "decision" or "dead-end";
                var whole = $"{desc}\n{body}";
                docs.Add(new Doc($"#{id}", id, false, kind, S(2), name, FirstSentence(desc.Length > 0 ? desc : body, 600),
                                 decided ? whole[..Math.Min(whole.Length, DecisionTextChars)] : "", r.IsDBNull(6) ? null : r.GetDateTimeOffset(6)));
                texts.Add($"{name.Replace('-', ' ').Replace('_', ' ')}\n{whole}");
            }
        }
        using (var c = board.Command(
                   "SELECT Id, Owner, State, What, DoneWhen FROM oversight.Commitment WHERE State IN (N'open', N'doing', N'blocked') " +
                   "AND (@asOf IS NULL OR RecordedAtUtc <= @asOf);", commandSeconds))
        {
            c.Parameters.Add("@asOf", SqlDbType.DateTime2).Value = (object?)asOfUtc ?? DBNull.Value;
            using var r = c.ExecuteReader();
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            while (r.Read())
            {
                docs.Add(new Doc($"board row {r.GetInt32(0)}", r.GetInt32(0), true, S(2), S(1), "", FirstSentence(S(3), 600), "", null));
                texts.Add($"{S(3)}\n{S(4)}");
            }
        }
        var len = new int[docs.Count];
        var postings = new Dictionary<string, List<(int, int)>>(StringComparer.Ordinal);
        for (var i = 0; i < docs.Count; i++)
        {
            var words = Words(texts[i]);
            len[i] = words.Count;
            foreach (var g in words.GroupBy(w => w, StringComparer.Ordinal))
            {
                if (!postings.TryGetValue(g.Key, out var list)) postings[g.Key] = list = new List<(int, int)>();
                list.Add((i, g.Count()));
            }
        }
        return new Index { BuiltUtc = DateTime.UtcNow, Docs = docs, Len = len, AvgLen = Math.Max(1.0, len.DefaultIfEmpty(0).Average()), Postings = postings };
    }

    private const string FileHead = "KOR-RECALL-INDEX 1";
    private static readonly JsonSerializerOptions FileJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>One text file: a head line, one JSON line per doc with its length, a TERMS line, one line per term ("term TAB doc:tf,doc:tf").
    /// Written to a temporary name and moved into place, so a reader never sees half a file.</summary>
    public static void Save(Index ix, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
        {
            w.WriteLine(string.Join('\t', FileHead, ix.BuiltUtc.ToString("O", CultureInfo.InvariantCulture), ix.Docs.Count.ToString(CultureInfo.InvariantCulture),
                                    ix.AvgLen.ToString("R", CultureInfo.InvariantCulture)));
            for (var i = 0; i < ix.Docs.Count; i++) w.WriteLine(ix.Len[i].ToString(CultureInfo.InvariantCulture) + '\t' + JsonSerializer.Serialize(ix.Docs[i], FileJson));
            w.WriteLine("TERMS");
            foreach (var (term, list) in ix.Postings)
                w.WriteLine(term + '\t' + string.Join(',', list.Select(p => p.Item1.ToString(CultureInfo.InvariantCulture) + ':' + p.Item2.ToString(CultureInfo.InvariantCulture))));
        }
        File.Move(tmp, path, true);
    }

    /// <summary>The file back, with the postings of <paramref name="terms"/> only (null: all). Null when the file is missing or not this format.</summary>
    public static Index? Load(string path, IReadOnlySet<string>? terms)
    {
        if (!File.Exists(path)) return null;
        using var rd = new StreamReader(path, Encoding.UTF8);
        var head = rd.ReadLine()?.Split('\t');
        if (head is null || head.Length != 4 || head[0] != FileHead) return null;
        var n = int.Parse(head[2], CultureInfo.InvariantCulture);
        var docs = new List<Doc>(n);
        var len = new int[n];
        for (var i = 0; i < n; i++)
        {
            var line = rd.ReadLine() ?? throw new InvalidDataException($"{path}: {i} of {n} docs");
            var tab = line.IndexOf('\t');
            len[i] = int.Parse(line.AsSpan(0, tab), CultureInfo.InvariantCulture);
            docs.Add(JsonSerializer.Deserialize<Doc>(line.AsSpan(tab + 1), FileJson)!);
        }
        if (rd.ReadLine() != "TERMS") throw new InvalidDataException($"{path}: no TERMS line");
        var postings = new Dictionary<string, List<(int, int)>>(StringComparer.Ordinal);
        string? l;
        while ((l = rd.ReadLine()) is not null)
        {
            var tab = l.IndexOf('\t');
            if (tab <= 0) continue;
            var term = l[..tab];
            if (terms is not null && !terms.Contains(term)) continue;
            var list = new List<(int, int)>();
            foreach (var p in l[(tab + 1)..].Split(','))
            {
                var colon = p.IndexOf(':');
                list.Add((int.Parse(p.AsSpan(0, colon), CultureInfo.InvariantCulture), int.Parse(p.AsSpan(colon + 1), CultureInfo.InvariantCulture)));
            }
            postings[term] = list;
        }
        return new Index
        {
            BuiltUtc = DateTime.Parse(head[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Docs = docs, Len = len,
            AvgLen = double.Parse(head[3], CultureInfo.InvariantCulture), Postings = postings,
        };
    }

    /// <summary>A word holding a digit (150k, 31218, 1647, g5) counts this many times: in this record such words name the one thing a
    /// record is about - a threshold, a job, a board row - where plain words are shared by hundreds.</summary>
    public const double DigitWeight = 2.0;

    /// <summary>The moment's distinct words (at most <see cref="MostQueryWords"/>), as <see cref="Index"/> needs them loaded.</summary>
    public static HashSet<string> QueryWords(string text) =>
        Words(text.Length > MostTextChars ? text[..MostTextChars] : text).Distinct().Take(MostQueryWords).ToHashSet(StringComparer.Ordinal);

    /// <summary>BM25 over the whole index (document frequency over every record and open row), best first.</summary>
    /// <summary>A word on the moment's first line counts this many times when there is more than one line: <see cref="MomentText"/> puts the
    /// tool's own statement of intent there (a command's description, a brief's description, a message's summary), and the lines after it -
    /// the command, the brief - carry most of the noise. Trap A (10 Nov): the description names the subject, the sed/printf line does not.</summary>
    public const double FirstLineWeight = 2.0;

    /// <summary>A word that names one thing: letters with digits (150k, g5, l22) or a number of 4+ digits (a row, a job, a record). A plain
    /// number under 1000 is a count or a percent (trap A's "100*v/tot") and is not one.</summary>
    public static bool Names(string w) =>
        w.Any(char.IsDigit) && (w.Any(char.IsLetter) || w.Length >= 4);

    public static List<Ranked> Rank(string text, Index ix)
    {
        var q = QueryWords(text);
        var cut = text.IndexOf('\n');
        var intent = cut > 0 && cut < text.Length - 1 ? Words(text[..cut]).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
        var n = ix.Docs.Count;
        var score = new Dictionary<int, double>();
        var hits = new Dictionary<int, List<string>>();
        foreach (var w in q)
        {
            if (!ix.Postings.TryGetValue(w, out var list)) continue;
            var idf = Math.Log(1 + (n - list.Count + 0.5) / (list.Count + 0.5));
            var weight = (Names(w) ? DigitWeight : 1.0) * (intent.Contains(w) ? FirstLineWeight : 1.0);
            foreach (var (d, f) in list)
            {
                score[d] = score.GetValueOrDefault(d) + idf * f * (K1 + 1) / (f + K1 * (1 - B + B * ix.Len[d] / ix.AvgLen)) * weight;
                if (!hits.TryGetValue(d, out var h)) hits[d] = h = new List<string>();
                h.Add(w);
            }
        }
        return score.OrderByDescending(kv => kv.Value).ThenByDescending(kv => ix.Docs[kv.Key].Id)
                    .Select(kv => new Ranked(ix.Docs[kv.Key], kv.Value, hits[kv.Key])).ToList();
    }


    /// <summary>One result as one line: id, kind, where, date, name, first sentence (masked by SecretGuard).</summary>
    public static string Line(Ranked r, int leadChars)
    {
        var d = r.Doc;
        var lead = SecretGuard.Mask(FirstSentence(d.Lead, leadChars)) ?? "";
        return d.Board
            ? $"- {d.Ref} [{d.Kind}, owner {d.Where}] {lead}"
            : $"- record {d.Ref} [{d.Kind}, {d.Where}{(d.AsOf is { } a ? ", " + a.ToString("d MMM", CultureInfo.InvariantCulture) : "")}] {SecretGuard.Mask(d.Name)}: {lead}";
    }

    public const string HookHead = "BRAIN (kor recall) - records and open board rows nearest this moment, ranked by shared words (check before you repeat or contradict one):";

    /// <summary>The hook's text: the results over the floor, not delivered earlier in this session, at most <paramref name="top"/>; null when none.</summary>
    public static string? HookText(IReadOnlyList<Ranked> ranked, ISet<string> delivered, int top, int leadChars, out List<string> refs, double floor = MinScore)
    {
        refs = ranked.Where(r => r.Score >= floor && r.Hit.Count >= MinTermsHit && !delivered.Contains(r.Doc.Ref)).Take(top).Select(r => r.Doc.Ref).ToList();
        if (refs.Count == 0) return null;
        var pick = refs.ToHashSet();
        var sb = new StringBuilder(HookHead).Append('\n');
        foreach (var r in ranked.Where(r => pick.Contains(r.Doc.Ref))) sb.Append(Line(r, leadChars)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}
