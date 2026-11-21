using System.Text;
using System.Text.Json;

namespace Kor.Operations.Framework.Whole;

public enum WholeKind { Prompt, Text, Call }

/// <summary>
/// One moment of a session, in transcript order: a human or peer PROMPT, an assistant TEXT block, or a tool CALL with its input
/// text (a shell command, a Write's content, an Edit's new text, a message, a helper's prompt) and its result.
/// </summary>
/// <param name="Input">what the call SAYS: Bash/PowerShell command; Write content; Edit/MultiEdit new text; SendMessage message; Agent
/// prompt; Read/Grep path and pattern; else the input JSON. For a Text, the text.</param>
/// <param name="Partial">a Read with offset or limit (part of the file was read).</param>
public sealed record WholeEv(int Line, WholeKind Kind, string Tool, string Input, string? Output, string? FilePath, bool Partial, string? Cwd = null)
{
    public bool Answered => Output is not null;
    /// <summary>The command reads stored prose through a SQL or transcript-reader script written earlier in this transcript.
    /// This says where its output came from; it does not assert that the prose is true or complete.</summary>
    public bool ReadsStoredText { get; init; }
}

/// <summary>
/// Board row 2139: the session's moments for <see cref="WholeGate"/>. It walks the transcript the way
/// <see cref="Stall.StallTranscript.Parse"/> does (only the transcript's own chain - a main file's sidechain lines are skipped; a
/// prompt's origin read by <see cref="Capture.TurnCapture.PromptOrigin"/>; a tool_use id kept once) and keeps, beside the calls, the
/// assistant text blocks and each call's whole input - the two things StallTranscript does not hold and this gate judges.
/// </summary>
public static class WholeTranscript
{
    public const long TailBytes = Stall.StallTranscript.TailBytes;
    public const long MostCountedBytes = 200L << 20;

    public static List<WholeEv> ReadTail(string? path, long bytes = TailBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<WholeEv>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var len = fs.Length;
        var take = Math.Min(len, bytes);
        var whole = take >= len;
        fs.Seek(len - take, SeekOrigin.Begin);
        var buf = new byte[take];
        fs.ReadExactly(buf, 0, (int)take);
        // review 2583: number the tail's lines as the FILE numbers them (a refusal citing "line -523" cannot be found), counting the
        // newlines before the tail - a byte scan, no JSON parse
        // measured 20 Nov: 290-552 ms for a whole Stop judgement on a 15.6 MB transcript; transcripts reach 789 MB (#122190), so past
        // MostCountedBytes the count is skipped and lines are numbered back from the end (-1 = the last), as StallTranscript does
        var counted = whole || len - take <= MostCountedBytes;
        var before = whole ? 0 : counted ? NewlinesBefore(fs, len - take) : 0;
        var lines = Encoding.UTF8.GetString(buf).Split('\n').ToList();
        var first = before + 1;
        if (!whole && lines.Count > 0) { lines.RemoveAt(0); first++; }
        if (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return Parse(lines, counted ? first : -1);
    }

    private static int NewlinesBefore(FileStream fs, long end)
    {
        fs.Seek(0, SeekOrigin.Begin);
        var buf = new byte[1 << 20];
        long read = 0;
        var n = 0;
        while (read < end)
        {
            var got = fs.Read(buf, 0, (int)Math.Min(buf.Length, end - read));
            if (got <= 0) break;
            n += buf.AsSpan(0, got).Count((byte)'\n');
            read += got;
        }
        return n;
    }

    public static List<WholeEv> ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, new UTF8Encoding(false));
        var lines = new List<string>();
        for (string? l; (l = r.ReadLine()) is not null;) lines.Add(l);
        return Parse(lines, 1);
    }

    /// <param name="firstLineNo">the 1-based number of lines[0]; negative = unknown, lines are numbered -N from the end.</param>
    public static List<WholeEv> Parse(IReadOnlyList<string> lines, int firstLineNo)
    {
        var evs = new List<WholeEv>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var edits = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        bool? sidechainMode = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            if (raw.Length == 0) continue;
            if (!raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal) && !raw.Contains("\"type\":\"user\"", StringComparison.Ordinal)) continue;
            var no = firstLineNo > 0 ? firstLineNo + i : i - lines.Count;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); } catch (JsonException) { continue; }
            using (doc)
            {
                var d = doc.RootElement;
                if (d.ValueKind != JsonValueKind.Object) continue;
                var type = S(d, "type");
                if (type is not ("user" or "assistant")) continue;
                var side = d.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True;
                sidechainMode ??= side;
                if (side != sidechainMode) continue;
                if (!d.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                if (!msg.TryGetProperty("content", out var content)) continue;
                if (type == "user" && !HasToolResult(content))
                {
                    var origin = d.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object ? S(o, "kind") : null;
                    var meta = d.TryGetProperty("isMeta", out var im) && im.ValueKind == JsonValueKind.True;
                    var compact = d.TryGetProperty("isCompactSummary", out var ic) && ic.ValueKind == JsonValueKind.True;
                    var said = PromptText(content).TrimStart();
                    var kind = Capture.TurnCapture.PromptOrigin(origin, meta, compact, said);
                    var own = said.Length == 0 || said.StartsWith("Stop hook feedback", StringComparison.Ordinal)
                              || said.StartsWith("This session is being continued", StringComparison.Ordinal)
                              || said.StartsWith("<system-reminder>", StringComparison.Ordinal) || said.StartsWith("Caveat:", StringComparison.Ordinal);
                    if (kind is "human" or "peer" && !own && sidechainMode != true)
                        evs.Add(new WholeEv(no, WholeKind.Prompt, kind, said, null, null, false));
                    continue;
                }
                if (content.ValueKind != JsonValueKind.Array) continue;
                foreach (var b in content.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.Object) continue;
                    var bt = S(b, "type");
                    if (type == "assistant" && bt == "text")
                    {
                        var t = S(b, "text") ?? "";
                        if (t.Trim().Length > 0) evs.Add(new WholeEv(no, WholeKind.Text, "", t, null, null, false));
                    }
                    else if (type == "assistant" && bt == "tool_use")
                    {
                        var id = S(b, "id") ?? "";
                        if (id.Length == 0 || index.ContainsKey(id)) continue;
                        var name = S(b, "name") ?? "";
                        var input = b.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp : default;
                        index[id] = evs.Count;
                        var partial = name == "Read" && input.ValueKind == JsonValueKind.Object && (input.TryGetProperty("offset", out _) || input.TryGetProperty("limit", out _));
                        var text = InputOf(name, input);
                        var cwd = S(d, "cwd");
                        evs.Add(new WholeEv(no, WholeKind.Call, name, text, null, FileOf(input), partial, cwd)
                        {
                            ReadsStoredText = WholeGate.ReadsStoredText(name, text, cwd, p => written.GetValueOrDefault(p)),
                        });
                        if (name is "Edit" or "MultiEdit" && input.ValueKind == JsonValueKind.Object) edits[id] = input.Clone();
                    }
                    else if (type == "user" && bt == "tool_result")
                    {
                        var id = S(b, "tool_use_id") ?? "";
                        if (!index.TryGetValue(id, out var k) || evs[k].Answered) continue;
                        var result = ResultText(b.TryGetProperty("content", out var c) ? c : default);
                        evs[k] = evs[k] with { Output = result };
                        var call = evs[k];
                        var failed = b.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;
                        failed |= result.StartsWith("PreToolUse:", StringComparison.Ordinal) || result.StartsWith("PostToolUse:", StringComparison.Ordinal);
                        if (call.Tool is "Bash" or "PowerShell")
                        {
                            foreach (var (path, body) in WholeGate.WrittenHeredocs(call.Input, call.Cwd))
                            {
                                if (failed) written.Remove(path);
                                else written[path] = body;
                            }
                            foreach (var (path, sql) in WholeGate.PrintedSql(call.Input, call.Cwd))
                            {
                                if (failed || sql is null) written.Remove(path);
                                else written[path] = sql;
                            }
                        }
                        if (call.FilePath is not null && call.Tool is "Write" or "Edit" or "MultiEdit")
                        {
                            var key = PathKey(call.FilePath, call.Cwd);
                            if (failed) written.Remove(key); // do not infer the contents after an unsuccessful mutation
                            else if (call.Tool == "Write") written[key] = call.Input;
                            else if (edits.TryGetValue(id, out var edit) && written.TryGetValue(key, out var previous))
                            {
                                var next = ApplyEdits(previous, edit, call.Tool == "MultiEdit");
                                if (next is null) written.Remove(key);
                                else written[key] = next;
                            }
                        }
                        edits.Remove(id);
                    }
                }
            }
        }
        return evs;
    }

    internal static string PathKey(string path, string? cwd)
    {
        var p = path.Replace('\\', '/');
        // Git Bash's /c/... and PowerShell's C:\... name the same local file. Resolve . and .. lexically;
        // never consult the filesystem or the replay process's cwd for historical source identity.
        if (p.Length >= 3 && p[0] == '/' && char.IsAsciiLetter(p[1]) && p[2] == '/') p = p[1] + ":" + p[2..];
        if (!Path.IsPathRooted(p) && cwd is not null) p = cwd.Replace('\\', '/') + "/" + p;
        var rooted = p.StartsWith('/');
        var unc = p.StartsWith("//", StringComparison.Ordinal);
        var parts = new List<string>();
        foreach (var part in p.Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != ".." && !parts[^1].EndsWith(':')) parts.RemoveAt(parts.Count - 1);
            else if (part != ".." || !rooted) parts.Add(part);
        }
        return (unc ? "//" : rooted ? "/" : "") + string.Join('/', parts);
    }

    private static string? ApplyEdits(string previous, JsonElement input, bool multi)
    {
        var changes = multi && input.TryGetProperty("edits", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToArray() : new[] { input };
        foreach (var change in changes)
        {
            var old = S(change, "old_string");
            var next = S(change, "new_string");
            if (string.IsNullOrEmpty(old) || next is null) return null;
            var at = previous.IndexOf(old, StringComparison.Ordinal);
            if (at < 0) return null;
            var all = change.TryGetProperty("replace_all", out var replace) && replace.ValueKind == JsonValueKind.True;
            if (!all && previous.IndexOf(old, at + old.Length, StringComparison.Ordinal) >= 0) return null;
            previous = all ? previous.Replace(old, next, StringComparison.Ordinal) : previous[..at] + next + previous[(at + old.Length)..];
        }
        return previous;
    }

    /// <summary>What a call SAYS (see <see cref="WholeEv.Input"/>).</summary>
    public static string InputOf(string tool, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        switch (tool)
        {
            case "Bash" or "PowerShell": return S(input, "command") ?? "";
            case "Write": return S(input, "content") ?? "";
            case "Edit": return S(input, "new_string") ?? "";
            case "MultiEdit":
                return input.TryGetProperty("edits", out var ed) && ed.ValueKind == JsonValueKind.Array
                    ? string.Join("\n", ed.EnumerateArray().Select(e => S(e, "new_string") ?? "")) : "";
            case "SendMessage": return S(input, "message") ?? S(input, "content") ?? S(input, "summary") ?? input.GetRawText();
            case "Agent" or "Task": return S(input, "prompt") ?? "";
            case "Read": return S(input, "file_path") ?? "";
            case "Grep": return (S(input, "pattern") ?? "") + " " + (S(input, "path") ?? "");
            default: return input.GetRawText();
        }
    }

    private static string? FileOf(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object ? S(input, "file_path") ?? S(input, "notebook_path") : null;

    private static string ResultText(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var x in c.EnumerateArray())
            if (x.ValueKind == JsonValueKind.Object && S(x, "type") == "text") sb.Append(S(x, "text")).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    private static bool HasToolResult(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(b => b.ValueKind == JsonValueKind.Object && S(b, "type") == "tool_result");

    private static string PromptText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object && S(b, "type") == "text").Select(b => S(b, "text") ?? ""));
    }

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
