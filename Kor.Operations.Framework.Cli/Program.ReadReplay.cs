using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Context;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// Board row 1936's measure: <c>kor context --read-replay &lt;transcript.jsonl | list.txt&gt;</c>. One JSON line per Read, per
/// shell output and per helper transcript, read by script, the file contents never printed. Reads only.
/// A call's tokens: the context of the next request less the context of the one that made it, shared by the calls of that
/// batch, so it also holds the next turn's own text. Size: the result's characters (an image: its base64 length x 3/4), since the
/// file on disk may have changed since; the live rule reads the file on disk. Remaining: requests left until the next compaction
/// or the transcript's end, each of which re-sends this result.
/// </summary>
public static partial class Program
{
    private sealed class ReplayRow
    {
        public string Kind = ""; public string File = ""; public int Line; public string? Path; public int? Offset; public int? Limit; public string? Pages;
        public long Before; public long Added; public int Batch; public long Chars; public long ImageBytes; public int Remaining; public string? Entry;
        public bool Edited; public bool UserNamed; public string? At; public int Request; public int Segment; public bool Error; public string? Refused;
    }

    private static int ReadReplay(string what, TextWriter output)
    {
        var files = what.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllLines(what).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string> { what };
        int reads = 0, refused = 0, warned = 0;
        foreach (var f in files)
        {
            try { ReplayOne(f, output, ref reads, ref refused, ref warned); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { output.WriteLine(JsonSerializer.Serialize(new { kind = "unreadable", file = f, error = e.Message })); }
        }
        output.WriteLine($"read-replay: {files.Count} transcript(s); main sessions: {refused} of {reads} Reads refused, {warned} command outputs over {ReadGuard.OutputWarnChars} characters warned");
        return 0;
    }

    private static void ReplayOne(string f, TextWriter output, ref int reads, ref int refused, ref int warned)
    {
        var sub = ContextTranscript.IsSubagentPath(f);
        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var rd = new StreamReader(fs, new UTF8Encoding(false));
        var rows = new List<ReplayRow>();
        var pending = new Dictionary<string, ReplayRow>(StringComparer.Ordinal);
        var batches = new Dictionary<int, List<ReplayRow>>();
        var edits = new List<(int Request, string Path)>();
        string? msgId = null, entry = null, lastPrompt = null, helperModel = null;
        int request = 0, segment = 0, no = 0, helperRequests = 0;
        long usage = 0, firstUsage = 0, lastUsage = 0, sends = 0;
        var segEnd = new Dictionary<int, int>();
        for (string? raw; (raw = rd.ReadLine()) is not null;)
        {
            no++;
            if (raw.Length == 0) continue;
            var isA = raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
            var isU = raw.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            var isC = raw.Contains("\"compact_boundary\"", StringComparison.Ordinal);
            if (!isA && !isU && !isC) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); } catch (JsonException) { continue; }
            using (doc)
            {
                var d = doc.RootElement;
                if (d.ValueKind != JsonValueKind.Object) continue;
                if (!sub && d.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                entry ??= Str(d, "entrypoint");
                var type = Str(d, "type");
                if (type == "system" && Str(d, "subtype") == "compact_boundary") { segEnd[segment] = request; segment++; usage = 0; continue; }
                if (type == "user" && d.TryGetProperty("message", out var um) && um.ValueKind == JsonValueKind.Object && um.TryGetProperty("content", out var uc))
                {
                    if (uc.ValueKind == JsonValueKind.String) { lastPrompt = uc.GetString(); continue; }
                    if (uc.ValueKind != JsonValueKind.Array) continue;
                    foreach (var b in uc.EnumerateArray())
                    {
                        if (b.ValueKind != JsonValueKind.Object) continue;
                        if (Str(b, "type") == "text") { lastPrompt = Str(b, "text") ?? lastPrompt; continue; }
                        if (Str(b, "type") != "tool_result" || Str(b, "tool_use_id") is not { } id || !pending.Remove(id, out var row)) continue;
                        row.Error = b.TryGetProperty("is_error", out var er) && er.ValueKind == JsonValueKind.True;
                        if (b.TryGetProperty("content", out var c))
                        {
                            if (c.ValueKind == JsonValueKind.String) row.Chars = (c.GetString() ?? "").Length;
                            else if (c.ValueKind == JsonValueKind.Array)
                                foreach (var x in c.EnumerateArray())
                                {
                                    if (Str(x, "type") == "text") row.Chars += (Str(x, "text") ?? "").Length;
                                    else if (Str(x, "type") == "image" && x.TryGetProperty("source", out var src) && Str(src, "data") is { } data) row.ImageBytes += data.Length * 3L / 4;
                                }
                        }
                    }
                    continue;
                }
                if (type != "assistant" || !d.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object || Str(m, "model") == "<synthetic>") continue;
                var mid = Str(m, "id");
                helperModel ??= Str(m, "model");
                var u = ContextTranscript.Usage(m);
                if (mid != msgId && u > 0)
                {
                    msgId = mid;
                    request++;
                    helperRequests++;
                    if (firstUsage == 0) firstUsage = u;
                    lastUsage = u;
                    sends += u;
                    // what the requests since the last one added, shared by the calls of the batch it answers
                    if (batches.TryGetValue(request - 1, out var answered))
                        foreach (var r in answered) { r.Added = Math.Max(0, u - r.Before) / answered.Count; r.Batch = answered.Count; }
                    usage = u;
                }
                if (!m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var b in content.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.Object || Str(b, "type") != "tool_use") continue;
                    var name = Str(b, "name") ?? "";
                    var input = b.TryGetProperty("input", out var inp) ? inp : default;
                    if (name is "Edit" or "Write" or "MultiEdit" && input.ValueKind == JsonValueKind.Object && Str(input, "file_path") is { } ep) edits.Add((request, ep));
                    ReplayRow? row = null;
                    if (name == "Read" && ReadGuard.Parse(input) is { } call)
                    {
                        row = new ReplayRow { Kind = "read", Path = call.Path, Offset = call.Offset, Limit = call.Limit, Pages = call.Pages };
                        var leaf = System.IO.Path.GetFileName(call.Path.Replace('\\', '/'));
                        row.UserNamed = lastPrompt is not null && leaf.Length > 0 && lastPrompt.Contains(leaf, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (name is "Bash" or "PowerShell") row = new ReplayRow { Kind = "output" };
                    if (row is null) continue;
                    row.File = f; row.Line = no; row.Before = usage; row.Request = request; row.Segment = segment; row.At = Str(d, "timestamp");
                    if (Str(b, "id") is { } tid) pending[tid] = row;
                    if (!batches.TryGetValue(request, out var list)) batches[request] = list = new List<ReplayRow>();
                    list.Add(row);
                    rows.Add(row);
                }
            }
        }
        segEnd[segment] = request;
        foreach (var r in rows)
        {
            r.Remaining = segEnd.GetValueOrDefault(r.Segment, request) - r.Request;
            r.Entry = entry;
            if (r.Kind == "read" && r.Path is not null)
            {
                r.Edited = edits.Any(e => e.Request >= r.Request && string.Equals(e.Path.Replace('\\', '/'), r.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                // what the rule would have said: an error result read nothing, a subagent's read passes
                if (!sub && !r.Error)
                {
                    reads++;
                    r.Refused = ReadGuard.Refuses(new ReadCall(r.Path, r.Offset, r.Limit, r.Pages), r.ImageBytes > 0 ? r.ImageBytes : r.Chars, null);
                    if (r.Refused is not null) refused++;
                }
            }
            else if (r.Kind == "output" && !sub && r.Chars > ReadGuard.OutputWarnChars) warned++;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                kind = r.Kind, sub, file = f, line = r.Line, at = r.At, path = r.Path, offset = r.Offset, limit = r.Limit, pages = r.Pages, before = r.Before, added = r.Added,
                batch = r.Batch, chars = r.Chars, image = r.ImageBytes, remaining = r.Remaining, entry = r.Entry, edited = r.Edited, named = r.UserNamed, error = r.Error, refused = r.Refused,
            }));
        }
        output.WriteLine(JsonSerializer.Serialize(new { kind = sub ? "helper" : "session", file = f, entry, first = firstUsage, last = lastUsage, requests = helperRequests, sends, model = helperModel }));
    }
}
