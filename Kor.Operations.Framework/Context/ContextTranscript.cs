using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Context;

/// <summary>A session's context as its transcript shows it at one point (see <see cref="ContextGuard"/>'s header).</summary>
/// <param name="Tokens">the last real request's input + cache read + cache creation + output tokens; a compaction after it
/// gives its postTokens; 0 = no request yet.</param>
/// <param name="Model">that request's model id (null = none read).</param>
/// <param name="Line">its line: 1-based when the whole file was read, else counted back from the end (-1 = last).</param>
/// <param name="OverCliff">a "Prompt is too long" came after it.</param>
/// <param name="LastWarned">the tokens G5's last WARN named, read back off the transcript (null = none since the last compaction).</param>
/// <param name="HandoverWritten">a successful kor state AND a successful Edit/Write of a *HANDOVER*.md or *RESUME*.md, both after the
/// reading crossed STOP (main sessions); a successful SendMessage after it (subagents).</param>
public sealed record ContextReading(long Tokens, string? Model, DateTimeOffset? At, int Line, bool OverCliff = false,
                                    long? LastWarned = null, bool HandoverWritten = false, string Basis = "usage");

/// <summary>
/// G5 (board row 1474): the reading the hook takes, from the TAIL of the session's own transcript. It scans back from the
/// last line: the newest real assistant usage (or a compact_boundary, or a "Prompt is too long" after it), then, only as
/// far as the level needs, its own last WARN and the handover steps since the STOP crossing. The replay calls
/// <see cref="Read(IReadOnlyList{string}, int, bool, long?, CompactSettings)"/> on the lines up to each call, so it judges what the hook would.
/// </summary>
public static class ContextTranscript
{
    /// <summary>The live read starts with the last 2 MB and widens to <see cref="MaxScan"/> only when the level needs older
    /// lines (the STOP crossing, or the last warning) or when the tail held no usage at all.</summary>
    public const long FirstScan = 2L << 20;
    public const long MaxScan = 16L << 20;

    public static bool IsSubagentPath(string path) =>
        path.Replace('\\', '/').Contains("/subagents/", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).StartsWith("agent-", StringComparison.OrdinalIgnoreCase);

    /// <summary>The live reading of a transcript file (a missing file reads as nothing: Tokens 0).</summary>
    public static ContextReading ReadFile(string? path, CompactSettings cs)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new ContextReading(0, null, null, 0, Basis: "none");
        var sub = IsSubagentPath(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var len = fs.Length;
        for (var take = Math.Min(len, FirstScan); ; take = Math.Min(len, take * 8))
        {
            var lines = Tail(fs, len, take, out var whole);
            var r = Read(lines, lines.Count, sub, null, cs, out var complete, whole ? 1 : -1);
            if (complete || whole || take >= MaxScan) return r;
        }
    }

    /// <summary>The last lines of a transcript, up to <see cref="FirstScan"/> (row 1936's output warning reads these).</summary>
    public static List<string> TailLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var len = fs.Length;
        return Tail(fs, len, Math.Min(len, FirstScan), out _);
    }

    private static List<string> Tail(FileStream fs, long len, long take, out bool whole)
    {
        whole = take >= len;
        fs.Seek(len - take, SeekOrigin.Begin);
        var buf = new byte[take];
        fs.ReadExactly(buf, 0, (int)take);
        var lines = Encoding.UTF8.GetString(buf).Split('\n').ToList();
        if (!whole && lines.Count > 0) lines.RemoveAt(0);   // a partial first line
        if (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>The reading at <paramref name="end"/> (exclusive) of <paramref name="lines"/>. The replay passes
    /// <paramref name="lastWarned"/> itself (no hook ever wrote a warning into an old transcript); live, it is read back.</summary>
    /// <param name="handover">false = do not look for the handover steps (the replay: allow or deny never depends on them).</param>
    public static ContextReading Read(IReadOnlyList<string> lines, int end, bool subagent, long? lastWarned, CompactSettings cs, bool handover = true) =>
        Read(lines, end, subagent, lastWarned, cs, out _, 1, handover);

    private static ContextReading Read(IReadOnlyList<string> lines, int end, bool subagent, long? lastWarned, CompactSettings cs, out bool complete, int firstLineNo, bool handover = true)
    {
        long? tokens = null; string? model = null; DateTimeOffset? at = null; var line = 0; var over = false; var basis = "none";
        long? warned = lastWarned;
        var wantWarn = lastWarned is null;
        var results = new Dictionary<string, (bool Ok, string Text)>(StringComparer.Ordinal);
        bool edited = false, recorded = false, reported = false, crossed = false;
        long stopAt = 0;
        complete = false;
        ContextLevel level = ContextLevel.Ok;
        for (var i = end - 1; i >= 0; i--)
        {
            var raw = lines[i];
            if (raw.Length == 0) continue;
            var no = firstLineNo > 0 ? firstLineNo + i : i - lines.Count;
            var isSystem = raw.Contains("\"compact_boundary\"", StringComparison.Ordinal);
            var isAssistant = raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
            var isUser = raw.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            // the hook's own warning comes back as an attachment line (hook_additional_context); a tool result or a reply
            // that merely quotes the marker is not one
            var hasMarker = wantWarn && warned is null && raw.Contains(ContextGuard.WarnMarker, StringComparison.Ordinal)
                            && raw.Contains("\"type\":\"attachment\"", StringComparison.Ordinal);
            if (hasMarker) warned = MarkerTokens(raw);
            if (!isSystem && !isAssistant && !isUser) continue;
            if (tokens is not null && level == ContextLevel.Warn)
            {
                if (warned is not null) { complete = true; break; }
                if (!isSystem) continue;   // in WARN only the marker and a compaction (the end of this context) matter
            }

            JsonElement d;
            JsonDocument? doc = null;
            // a tool result is read while the reading is not yet found (it may answer the newest message's calls) or at STOP
            var wantResult = isUser && raw.Contains("\"tool_result\"", StringComparison.Ordinal) && (tokens is null || level == ContextLevel.Stop);
            if (isSystem || isAssistant || wantResult)
            {
                try { doc = JsonDocument.Parse(raw); } catch (JsonException) { continue; }
            }
            if (doc is null) continue;
            using (doc)
            {
                d = doc.RootElement;
                if (d.ValueKind != JsonValueKind.Object) continue;
                var type = S(d, "type");
                if (!subagent && d.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;

                if (type == "system" && S(d, "subtype") == "compact_boundary")
                {
                    if (tokens is null)
                    {
                        long post = 0;
                        if (d.TryGetProperty("compactMetadata", out var cm) && cm.ValueKind == JsonValueKind.Object
                            && cm.TryGetProperty("postTokens", out var pt) && pt.ValueKind == JsonValueKind.Number) post = pt.GetInt64();
                        return Done(new ContextReading(post, model, Ts(d), no, over, null, false, "compact"), out complete);
                    }
                    // older than the compaction is another context: nothing before it counts
                    complete = true;
                    break;
                }

                if (type == "user")
                {
                    if (d.TryGetProperty("message", out var um) && um.ValueKind == JsonValueKind.Object
                        && um.TryGetProperty("content", out var uc) && uc.ValueKind == JsonValueKind.Array)
                        foreach (var b in uc.EnumerateArray())
                            if (b.ValueKind == JsonValueKind.Object && S(b, "type") == "tool_result" && S(b, "tool_use_id") is { } id && !results.ContainsKey(id))
                            {
                                var err = b.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                                results[id] = (!err, ResultText(b));
                            }
                    continue;
                }

                if (type != "assistant" || !d.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) continue;
                var mod = S(m, "model");
                if (mod == "<synthetic>")
                {
                    if (tokens is null && Text(m).StartsWith("Prompt is too long", StringComparison.OrdinalIgnoreCase)) over = true;
                    continue;
                }
                var usage = Usage(m);
                if (tokens is null)
                {
                    if (usage <= 0) continue;
                    tokens = usage; model = mod; at = Ts(d); line = no; basis = "usage";
                    var probe = new ContextReading(usage, mod, at, no, over);
                    level = ContextGuard.LevelOf(probe, cs);
                    stopAt = ContextGuard.StopAt(ContextGuard.CeilingOf(mod, cs));
                    if (level == ContextLevel.Ok || (level == ContextLevel.Stop && !handover)) return Done(probe with { LastWarned = warned }, out complete);
                }
                else if (level == ContextLevel.Stop && usage > 0 && usage < stopAt && !over)
                {
                    crossed = true;   // the request before the crossing: what came after it is "since STOP"
                }
                if (level == ContextLevel.Stop && !crossed)
                    foreach (var (tool, text, id) in ToolUses(m))
                    {
                        if (!results.TryGetValue(id, out var res) || !res.Ok) continue;
                        if (tool is "Edit" or "Write" or "MultiEdit" && ContextGuard.IsHandoverFile(text)) edited = true;
                        if (tool is "Bash" or "PowerShell" && KorState.IsMatch(text) && res.Text.Contains("brain.LaneState", StringComparison.Ordinal)) recorded = true;
                        if (tool == "SendMessage") reported = true;
                    }
                if (level == ContextLevel.Warn && (!wantWarn || warned is not null)) { complete = true; break; }
                if (level == ContextLevel.Stop && crossed) { complete = true; break; }
            }
        }
        // no usage in what was read: not complete, so ReadFile widens a tail that is not the whole file. A partial tail with no
        // usage (one huge tool result) must not read as a fresh session (review row 514)
        if (tokens is null) return new ContextReading(0, null, null, 0, over, Basis: "none");
        return new ContextReading(tokens.Value, model, at, line, over, warned, subagent ? reported : edited && recorded, basis);
    }

    private static ContextReading Done(ContextReading r, out bool complete) { complete = true; return r; }

    private static readonly Regex KorState = new(@"(?:^|[\s""'/\\&;(|])kor(?:\.exe)?[""']?\s+state\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MarkerNumber = new(Regex.Escape(ContextGuard.WarnMarker) + @"([\d,]+)", RegexOptions.CultureInvariant);

    private static long? MarkerTokens(string raw)
    {
        var m = MarkerNumber.Match(raw);
        return m.Success && long.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>input + cache read + cache creation + output tokens of an assistant message (0 = none).</summary>
    public static long Usage(JsonElement message)
    {
        if (!message.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return 0;
        long N(string k) => u.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
        var input = N("input_tokens") + N("cache_read_input_tokens") + N("cache_creation_input_tokens");
        return input == 0 ? 0 : input + N("output_tokens");
    }

    public static IEnumerable<(string Tool, string Text, string Id)> ToolUses(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Array) yield break;
        foreach (var b in c.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object || S(b, "type") != "tool_use") continue;
            var name = S(b, "name") ?? "";
            var input = b.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp : default;
            yield return (name, ContextGuard.TextOf(name, input), S(b, "id") ?? "");
        }
    }

    private static string Text(JsonElement m) =>
        m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array
            ? string.Concat(c.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object && S(b, "type") == "text").Select(b => S(b, "text") ?? ""))
            : "";

    private static string ResultText(JsonElement b)
    {
        if (!b.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && S(x, "type") == "text").Select(x => S(x, "text") ?? ""));
    }

    private static DateTimeOffset? Ts(JsonElement d) =>
        DateTimeOffset.TryParse(S(d, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
