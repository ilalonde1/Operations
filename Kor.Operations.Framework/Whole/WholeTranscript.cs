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
public sealed record WholeEv(int Line, WholeKind Kind, string Tool, string Input, string? Output, string? FilePath, bool Partial)
{
    public bool Answered => Output is not null;
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
        var lines = Encoding.UTF8.GetString(buf).Split('\n').ToList();
        if (!whole && lines.Count > 0) lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return Parse(lines, whole ? 1 : -1);
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
                        evs.Add(new WholeEv(no, WholeKind.Call, name, InputOf(name, input), null, FileOf(input), partial));
                    }
                    else if (type == "user" && bt == "tool_result")
                    {
                        var id = S(b, "tool_use_id") ?? "";
                        if (!index.TryGetValue(id, out var k) || evs[k].Answered) continue;
                        evs[k] = evs[k] with { Output = ResultText(b.TryGetProperty("content", out var c) ? c : default) };
                    }
                }
            }
        }
        return evs;
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
