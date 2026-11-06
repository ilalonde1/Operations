using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// Board row 1569, done-when 5: the no-tools cold test. Questions come from content on the pages cut today: a row whose full
/// description the old 4+4 pages do not carry, an index line past old page 4, the resume's tail when the old cap cuts it. The key is
/// what the session was actually given, from its own transcript (lesson #114269); the first request's tokens come from there too.
/// Compares: last-words quotes and the session's page count against the delivered hook context. Does NOT cover: bodies (kor read is
/// tool-read, so a no-tools session cannot show it - StartGateTests hold that path); understanding beyond quoting. Same-class miss: a
/// session that holds a page and misreads it fails here as if the page were absent. The grader is trusted only because it fails the
/// control (lesson #114267).
/// </summary>
public static class ColdProof
{
    public sealed record Question(string Key, string Ask, string Expect);
    public sealed record Line(string Key, bool Delivered, bool Pass, bool Invented, string Detail);
    public sealed record Graded(IReadOnlyList<Line> Lines, int PagesHeld, int PagesTotal, bool PagesRight)
    {
        public int Passed => Lines.Count(l => l.Pass) + (PagesRight ? 1 : 0);
        public int Of => Lines.Count + 1;
        public int Invented => Lines.Count(l => l.Invented);
    }

    public const string NotInContext = "NOT IN CONTEXT";
    public const int OldPages = 4;
    public const int OldResumeCap = StartRender.ResumeCap;

    private static string LastWords(string s, int n = 6) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).TakeLast(n));

    private static int WordCount(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>What today's wiring (4 memory pages, 4 records pages) carries of this side, in one text.</summary>
    public static string OldPagesText(StartSide side) =>
        string.Join("\n", Enumerable.Range(1, OldPages).SelectMany(p => new[]
        {
            StartRender.Memory(side, p, OldPages, StartRender.HookBudget), StartRender.RecordsPage(side, p, OldPages, StartRender.HookBudget),
        }));

    /// <summary>Six questions, spread over the content the old pages leave out.</summary>
    public static List<Question> Make(StartSide side, ResumeChoice resume)
    {
        var old = OldPagesText(side);
        var rows = StartRender.PageRows(side)
            .Select(r => r with { Description = SecretGuard.Mask(r.Description)!, Name = SecretGuard.Mask(r.Name)! })
            .Where(r => WordCount(r.Description) >= 12 && !old.Contains(r.Description, StringComparison.Ordinal))
            .OrderBy(r => r.Id).ToList();
        var questions = new List<Question>();
        var index = side.Records.FirstOrDefault(r => r.Name == ModuleState.IndexName)?.Body ?? "";
        var indexLine = index.Split('\n').Select(l => SecretGuard.Mask(l.Trim())!)
            .Where(l => WordCount(l) >= 12 && !old.Contains(l, StringComparison.Ordinal)).ToList();
        if (indexLine.Count > 0)
        {
            var l = indexLine[indexLine.Count / 2];
            questions.Add(new Question("index", $"The memory index line that begins \"{string.Join(" ", l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))}\": quote its last six words.", LastWords(l)));
        }
        var block = SecretGuard.Mask(resume.Chosen?.ResumeBlock.Trim() ?? "")!;
        if (block.Length > OldResumeCap && WordCount(block) >= 12)
            questions.Add(new Question("resume", "The RESUME block chosen for this session: quote the last six words of the block.", LastWords(block)));
        var need = 5 - questions.Count;
        for (var i = 0; i < need && rows.Count > 0; i++)
        {
            var r = rows[(int)((i + 0.5) * rows.Count / need)];
            questions.Add(new Question($"record{r.Id}", $"Record #{r.Id} ({r.Name}): quote the last six words of its description (the description, not any source in brackets after it).", LastWords(r.Description)));
        }
        return questions;
    }

    public static string Prompt(IReadOnlyList<Question> questions)
    {
        var fields = string.Join(", ", questions.Select(q => $"\"{q.Key}\": \"<answer>\""));
        return "This is a context-loading test. Answer ONLY from what is already in your context (the session-start blocks and CLAUDE.md files). You have no tools; do not guess or reconstruct.\n" +
               string.Join("\n", questions.Select(q => $"{q.Key}: {q.Ask}")) + "\n" +
               $"pages_held: how many distinct blocks headed '{StartRender.PagesHead}N of M' do you hold? pages_total: the M in their heads.\n" +
               $"Reply with ONE JSON object and nothing else: {{{fields}, \"pages_held\": <number>, \"pages_total\": <number>}}. For anything not in your context use exactly \"{NotInContext}\" (0 for a number).";
    }

    /// <summary>Everything the SessionStart hooks gave the session, from its transcript (attachment hook_additional_context).</summary>
    public static string Delivered(IEnumerable<string> transcript)
    {
        var parts = new List<string>();
        foreach (var line in transcript)
        {
            if (!line.Contains("hook_additional_context", StringComparison.Ordinal) || !line.Contains("SessionStart", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("attachment", out var a) || a.ValueKind != JsonValueKind.Object) continue;
                if (a.GetProperty("type").GetString() != "hook_additional_context" || !a.TryGetProperty("content", out var c)) continue;
                if (c.ValueKind == JsonValueKind.String) parts.Add(c.GetString()!);
                else if (c.ValueKind == JsonValueKind.Array) parts.AddRange(c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* a torn line */ }
        }
        return string.Join("\n", parts);
    }

    /// <summary>The first assistant request's whole context: input + cache written + cache read.</summary>
    public static long? TokensAtRequestOne(IEnumerable<string> transcript)
    {
        foreach (var line in transcript)
        {
            if (!line.Contains("\"usage\"", StringComparison.Ordinal) || !line.Contains("\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant") continue;
                var u = root.GetProperty("message").GetProperty("usage");
                long N(string name) => u.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                return N("input_tokens") + N("cache_creation_input_tokens") + N("cache_read_input_tokens");
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* a torn line */ }
        }
        return null;
    }

    private static string Norm(string? s) =>
        Regex.Replace(Regex.Replace(s ?? "", @"[`*""“”‘’]", ""), @"\s+", " ").Trim().TrimEnd('.', ',', ';', ':').ToLowerInvariant();

    public static Graded Grade(IReadOnlyList<Question> questions, string? answer, string delivered)
    {
        Dictionary<string, JsonElement>? parsed = null;
        var text = answer ?? "";
        int i = text.IndexOf('{'), j = text.LastIndexOf('}');
        JsonDocument? doc = null;
        try
        {
            if (i >= 0 && j > i)
            {
                doc = JsonDocument.Parse(text[i..(j + 1)]);
                parsed = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
        }
        catch (JsonException) { parsed = null; }
        finally { doc?.Dispose(); }
        string? Got(string key) => parsed is not null && parsed.TryGetValue(key, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
        var given = Norm(delivered);
        var lines = new List<Line>();
        foreach (var q in questions)
        {
            var got = Got(q.Key);
            var was = given.Contains(Norm(q.Expect), StringComparison.Ordinal);
            var said = got is not null && !Norm(got).StartsWith(Norm(NotInContext), StringComparison.Ordinal);
            var right = said && Norm(got).Contains(Norm(q.Expect), StringComparison.Ordinal);
            lines.Add(new Line(q.Key, was, was && right, !was && said,
                $"{(was ? "given" : "NOT given")}; key '{q.Expect}'; answered '{got ?? "(nothing)"}'"));
        }
        var heads = Regex.Matches(delivered, Regex.Escape(StartRender.PagesHead) + @"(\d+) of (\d+)\b");
        var held = heads.Select(m => int.Parse(m.Groups[1].Value)).Distinct().Count();
        var total = heads.Select(m => int.Parse(m.Groups[2].Value)).DefaultIfEmpty(0).Max();
        var pagesRight = total > 0 && held == total && Got("pages_held") == held.ToString() && Got("pages_total") == total.ToString();
        return new Graded(lines, held, total, pagesRight);
    }

    /// <summary>
    /// The SessionStart commands a repo needs for THE PAGES, from its own settings.json: the old start parts (record, state, memory,
    /// records) become the record part with --pages-wired and <paramref name="wire"/> page entries; every other SessionStart command
    /// is kept as it is. <paramref name="exe"/> runs the start parts ("C:/KOR-Operations/kor/kor.exe" when live, the built Debug kor in a proof).
    /// </summary>
    public static List<string> Wiring(string settingsJson, string module, string exe, int wire)
    {
        using var doc = JsonDocument.Parse(settingsJson);
        var kept = new List<string>();
        foreach (var group in doc.RootElement.GetProperty("hooks").GetProperty("SessionStart").EnumerateArray())
            foreach (var h in group.GetProperty("hooks").EnumerateArray())
                if (h.GetProperty("command").GetString() is { } c && !Regex.IsMatch(c, @"\bstart\b.*--part (record|records|state|memory)\b"))
                    kept.Add(c);
        var run = exe.Contains(' ') ? $"\"{exe}\"" : exe;
        var lane = $"\"{module}\"";
        var lines = new List<string> { $"{run} start --module {lane} --part record --pages-wired {wire} --hook" };
        lines.AddRange(Enumerable.Range(1, wire).Select(p => $"{run} start --module {lane} --part pages --page {p} --pages {wire} --hook"));
        lines.AddRange(kept);
        return lines;
    }

    /// <summary>The repo's SessionStart commands as they stand (kor start-proof --baseline).</summary>
    public static List<string> CurrentSessionStart(string settingsJson)
    {
        using var doc = JsonDocument.Parse(settingsJson);
        return doc.RootElement.GetProperty("hooks").GetProperty("SessionStart").EnumerateArray()
            .SelectMany(g => g.GetProperty("hooks").EnumerateArray()).Select(h => h.GetProperty("command").GetString()!).ToList();
    }

    public static string GateCommand(string module, string exe)
    {
        var run = exe.Contains(' ') ? $"\"{exe}\"" : exe;
        return $"{run} start-gate --module \"{module}\" --hook";
    }
}
