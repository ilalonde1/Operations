using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kor.Operations.Framework.Stall;

/// <summary>
/// One tool call of a session, as its transcript holds it: the line of its tool_use, when it was made, the tool, the
/// text G1 reads (a shell command; WebFetch's url; an MCP tool's input JSON; a file tool's path), the result's text and
/// when the result was written (null = no result on disk yet).
/// </summary>
/// <param name="Content">a Write's content (a diagnosis body file is read from it in a replay); null otherwise.</param>
public sealed record StallCall(int Line, DateTimeOffset? At, string Id, string Tool, string Text, string? Output, bool IsError,
                               DateTimeOffset? ResultAt, string? Content = null)
{
    public bool Answered => Output is not null;
}

/// <summary>
/// G1 (board row 1308): the tool calls of ONE transcript, in order, each with its result. The live hook reads the tail
/// (<see cref="TailBytes"/>) of the session's own transcript - the subagent's file when the hook input carries agent_id -
/// and the replay reads whole files; both go through <see cref="Parse"/>, so the replay judges what the hook would.
/// Only user/assistant lines of the transcript's own chain are read (a main transcript's sidechain lines are skipped,
/// as TurnCapture does); a tool_use id seen twice is kept once (its first line).
/// </summary>
public static class StallTranscript
{
    /// <summary>The live hook reads the last 8 MB: more than the failures inside <see cref="StallGuard.Window"/> in every
    /// transcript of the dry run (the replay reports the largest span it needed).</summary>
    public const long TailBytes = 8L << 20;

    public static List<StallCall> ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, new UTF8Encoding(false));
        var lines = new List<string>();
        for (string? l; (l = r.ReadLine()) is not null;) lines.Add(l);
        return Parse(lines, 1);
    }

    /// <summary>The calls in the last <paramref name="bytes"/> of a transcript (line numbers counted back from the end,
    /// -1 = the last line, unless the whole file was read). A missing file is no calls.</summary>
    public static List<StallCall> ReadTail(string? path, long bytes = TailBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<StallCall>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var len = fs.Length;
        var take = Math.Min(len, bytes);
        var whole = take >= len;
        fs.Seek(len - take, SeekOrigin.Begin);
        var buf = new byte[take];
        fs.ReadExactly(buf, 0, (int)take);
        var lines = Encoding.UTF8.GetString(buf).Split('\n').ToList();
        if (!whole && lines.Count > 0) lines.RemoveAt(0);   // a partial first line
        if (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return Parse(lines, whole ? 1 : -1);
    }

    /// <summary>
    /// The transcript a PreToolUse hook should read: with agent_id (a subagent's call) the subagent's own file,
    /// &lt;dir&gt;/&lt;session&gt;/subagents/agent-&lt;id&gt;.jsonl, when it exists; else transcript_path. A subagent whose own
    /// file is not found is judged on nothing (null): its calls are not in the parent's transcript.
    /// </summary>
    public static string? PathFor(string? transcriptPath, string? sessionId, string? agentId)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath)) return null;
        if (string.IsNullOrWhiteSpace(agentId)) return transcriptPath;
        var name = $"agent-{agentId}.jsonl";
        if (Path.GetFileName(transcriptPath).Equals(name, StringComparison.OrdinalIgnoreCase)) return transcriptPath;
        var dir = Path.GetDirectoryName(transcriptPath);
        var session = sessionId ?? Path.GetFileNameWithoutExtension(transcriptPath);
        if (dir is null) return null;
        var own = Path.Combine(dir, session, "subagents", name);
        return File.Exists(own) ? own : null;
    }

    /// <param name="firstLineNo">the 1-based number of lines[0]; negative = unknown, lines are numbered -N from the end.</param>
    public static List<StallCall> Parse(List<string> lines, int firstLineNo)
    {
        var calls = new List<StallCall>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        bool? sidechainMode = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            if (raw.Length == 0) continue;
            var isAssistant = raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
            var isUser = raw.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            if (!isAssistant && !isUser) continue;
            if (isAssistant && !raw.Contains("\"tool_use\"", StringComparison.Ordinal) && sidechainMode is not null) continue;
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
                DateTimeOffset? at = DateTimeOffset.TryParse(S(d, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts) ? ts : null;
                if (type == "user" && !HasToolResult(content))
                {
                    // a PROMPT from Ian or a peer session: the world outside the session may have changed (a grant, a
                    // sign-in renewed, "retry") - kept as a pseudo-call G1 never judges (Tool = Prompt)
                    var origin = d.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object ? S(o, "kind") : null;
                    var meta = d.TryGetProperty("isMeta", out var im) && im.ValueKind == JsonValueKind.True;
                    var compact = d.TryGetProperty("isCompactSummary", out var ic) && ic.ValueKind == JsonValueKind.True;
                    var said = PromptText(content).TrimStart();
                    var kind = Capture.TurnCapture.PromptOrigin(origin, meta, compact, said);
                    // Claude Code's own text is no one's message: a Stop hook's feedback, a continuation summary, an empty line
                    var own = said.Length == 0 || said.StartsWith("Stop hook feedback", StringComparison.Ordinal)
                              || said.StartsWith("This session is being continued", StringComparison.Ordinal)
                              || said.StartsWith("<system-reminder>", StringComparison.Ordinal) || said.StartsWith("Caveat:", StringComparison.Ordinal);
                    if (kind is "human" or "peer" && !own && sidechainMode != true)
                        calls.Add(new StallCall(no, at, $"prompt@{no}", Prompt, kind, "", false, at));
                    continue;
                }
                if (content.ValueKind != JsonValueKind.Array) continue;
                foreach (var b in content.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.Object) continue;
                    var bt = S(b, "type");
                    if (type == "assistant" && bt == "tool_use")
                    {
                        var id = S(b, "id") ?? "";
                        if (id.Length == 0 || index.ContainsKey(id)) continue;
                        var name = S(b, "name") ?? "";
                        var input = b.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp : default;
                        index[id] = calls.Count;
                        calls.Add(new StallCall(no, at, id, name, TextOf(name, input), null, false, null,
                                                name == "Write" && input.ValueKind == JsonValueKind.Object ? Clip(S(input, "content"), 20000) : null));
                    }
                    else if (type == "user" && bt == "tool_result")
                    {
                        var id = S(b, "tool_use_id") ?? "";
                        if (!index.TryGetValue(id, out var k) || calls[k].Answered) continue;
                        calls[k] = calls[k] with
                        {
                            Output = ResultText(b.TryGetProperty("content", out var c) ? c : default),
                            IsError = b.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True,
                            ResultAt = at,
                        };
                    }
                }
            }
        }
        return calls;
    }

    /// <summary>What G1 reads of a call's input: the command (Bash, PowerShell), the url (WebFetch), the file path (file
    /// tools), else the input as JSON (an MCP tool's host, device, script ...).</summary>
    public static string TextOf(string tool, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        return tool switch
        {
            "Bash" or "PowerShell" => S(input, "command") ?? "",
            "WebFetch" => S(input, "url") ?? "",
            "Write" or "Edit" or "MultiEdit" => S(input, "file_path") ?? "",
            "NotebookEdit" => S(input, "notebook_path") ?? "",
            _ => input.GetRawText(),
        };
    }

    private static string ResultText(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var x in c.EnumerateArray())
            if (x.ValueKind == JsonValueKind.Object && S(x, "type") == "text") sb.Append(S(x, "text")).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The Tool of a prompt's pseudo-call (Ian's message, or a peer session's).</summary>
    public const string Prompt = "<prompt>";

    private static bool HasToolResult(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(b => b.ValueKind == JsonValueKind.Object && S(b, "type") == "tool_result");

    private static string PromptText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object && S(b, "type") == "text").Select(b => S(b, "text") ?? ""));
    }

    private static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
