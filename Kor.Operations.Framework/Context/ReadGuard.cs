using System.Globalization;
using System.Text.Json;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Context;

/// <summary>One Read call as the rule sees it.</summary>
public sealed record ReadCall(string Path, int? Offset, int? Limit, string? Pages);

/// <summary>
/// Board row 1936 (Ian, 6 Nov: "that should be a REQUIREMENT"). A main session's context is re-sent every turn, so it holds
/// findings, not raw material. Runs inside kor context on PreToolUse, which all 7 enrolled repos already run on every tool, so no
/// settings entry is added. Each refusal is one oversight.GuardRefusal row (Guard <see cref="GuardName"/>). The replay is
/// <c>kor context --read-replay</c>; its results and every name are in docs/READ-GUARD-REPLAY-2026-11-06.md.
///
/// REFUSED, in a main session: a Read of an image over <see cref="MaxImageBytes"/>; a Read of any other file over
/// <see cref="MaxFileBytes"/> with no line range, or with a range over <see cref="MaxRangeLines"/> lines; a PDF over that size with
/// no pages, or more than <see cref="MaxPdfPages"/> pages. The refusal names the routes: a helper that returns what it found; a
/// line range; a command that prints a summary; a smaller crop.
/// WARNED, once per crossing: a Bash or PowerShell output over <see cref="OutputWarnChars"/> characters, read off the transcript tail
/// at the next tool call or prompt, since it cannot be refused before it runs.
/// PASSES: a subagent's read (agent_id, as G5 tells them); an image or file under the size; a line range under the size; every
/// handover step (a *HANDOVER*.md or *RESUME*.md, CLAUDE.md, HOW-WE-BUILD.md, a state file in the scratchpad); a file whose size
/// cannot be read. FAILS OPEN on any read error or bad input: a guard fault must never stop the work.
///
/// WHY THESE SIZES (replay of 1,100 transcripts modified since 2 Nov, 991 main and 109 helper: 979 of 4,922 main-session Reads refused,
/// 938 image, 36 file, 5 pdf, in 34 of the 991 sessions): 40,000 characters is about 13k tokens; lowering it to 20,000 refuses over 100 more
/// reads, in 92 sessions rather than 34, and 33 read-then-edit reads rather than 6. A range of 300 lines is the 99th percentile of the ranges
/// sessions already read. Image tokens follow bytes only weakly (under 50k bytes, median 1,851; over 400k, median 4,187), so 50,000 bytes only
/// says "a crop". 10,000 characters is about the 99th percentile of command output (164 of 14,383 warned, in 29 sessions).
/// NO LOW-CONTEXT LINE: the replay shows none earns it. Reads at under 100k of context carry the longest (median 133 more requests): about 30
/// refusals, about 39M tokens re-sent in the main against about 10M through helpers. Requests still to come decide the cost, and a session cannot know them.
///
/// WHAT IT COSTS, HONESTLY: a helper starts near 46k (measured median 46,389 of 108), so a read late in a session can cost more through
/// a helper. Read off the replay: the 979 refused reads cost 576M tokens re-sent in the main. The only two purpose-built look helpers in the
/// data cost 28,071 and 51,392 per picture (net +495M and +472M). Counting a third helper that did other work, 398,338 per picture, the
/// net is +132M, and at that rate a 680-request session (006abbce) comes out 6M worse. Two of the 34 sessions (9,636 and 6,662 requests)
/// hold 706 of the 979. The numbers are the doc's, with the model behind them: a measured job is still owed.
///
/// DOES NOT COVER: a Read's size in tokens (bytes on disk stand in for it; the replay had only the result's own size); a
/// file read by a command (cat, Get-Content, type, sed), which is the warning's business and only if it is over its size; an
/// image or file brought in another way (pasted into a prompt, WebFetch, an MCP result); a session with no helper to send
/// (the Agent tool removed): an image over the size then has no route but a crop. A main session started as a one-shot review is
/// judged like a lead, though its few requests make a helper dearer. Whether the helper's finding is any good: that is the IQ
/// question, read on a real job. Ian's own word cannot lift it: the brief gave it no lift, and a file he names is refused like any other.
/// Same-class fault it would miss: a main session that runs `cat big.log` over and over at 9,000 characters each, every output under the
/// warning's size, or reads a 40,000-byte file in 300-line ranges from end to end: each call passes and the context holds the whole of it.
/// </summary>
public static class ReadGuard
{
    public const string GuardName = "kor-context-read";
    public const long MaxImageBytes = 50_000;
    public const long MaxFileBytes = 40_000;
    public const int MaxRangeLines = 300;
    public const int MaxPdfPages = 2;
    public const int OutputWarnChars = 10_000;
    public const string OutputMarker = "[kor context] OUTPUT of ";

    private static readonly string[] Images = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff" };
    private static readonly string[] StartDocs = { "CLAUDE.md", "HOW-WE-BUILD.md" };

    public static ReadCall? Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return null;
        if (!input.TryGetProperty("file_path", out var fp) || fp.ValueKind != JsonValueKind.String) return null;
        static int? I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : null;
        var pages = input.TryGetProperty("pages", out var pg) && pg.ValueKind == JsonValueKind.String ? pg.GetString() : null;
        return new ReadCall(fp.GetString() ?? "", I(input, "offset"), I(input, "limit"), pages);
    }

    private static string Leaf(string path) => Path.GetFileName(path.Replace('\\', '/'));

    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(Leaf(path)).ToLowerInvariant());

    /// <summary>The file's bytes on disk, or null when it cannot be read: a null size is never refused.</summary>
    public static long? SizeOf(ReadCall c, string? cwd)
    {
        try
        {
            if (cwd is null && !Path.IsPathRooted(c.Path)) return null;
            var full = CommandSegments.ResolvePath(c.Path, cwd ?? Path.GetTempPath());
            if (full is null) return null;
            var f = new FileInfo(full);
            return f.Exists ? f.Length : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>What is refused ("image", "file", "pdf"), or null when the call passes.</summary>
    public static string? Refuses(ReadCall c, long? size, string? scratchpad)
    {
        if (size is not { } bytes) return null;
        if (ContextGuard.IsHandoverFile(c.Path) || StartDocs.Contains(Leaf(c.Path), StringComparer.OrdinalIgnoreCase)) return null;
        if (ContextGuard.UnderScratchpad(c.Path, scratchpad) && ContextGuard.IsStateFile(c.Path)) return null;
        if (IsImage(c.Path)) return bytes > MaxImageBytes ? "image" : null;
        if (bytes <= MaxFileBytes) return null;
        if (c.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return PageCount(c.Pages) is { } n && n <= MaxPdfPages ? null : "pdf";
        return c.Limit is { } limit && limit <= MaxRangeLines ? null : "file";
    }

    /// <summary>How many pages "3", "3-4" or "1,4-5" names; null for none, or for a form this cannot read (read as no pages).</summary>
    private static int? PageCount(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages)) return null;
        var total = 0;
        foreach (var term in pages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = term.Split('-');
            if (ends.Length > 2 || !ends.All(e => int.TryParse(e, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return null;
            if (ends.Length == 2 && int.Parse(ends[1], CultureInfo.InvariantCulture) < int.Parse(ends[0], CultureInfo.InvariantCulture)) return null;
            total += ends.Length == 1 ? 1 : int.Parse(ends[1], CultureInfo.InvariantCulture) - int.Parse(ends[0], CultureInfo.InvariantCulture) + 1;
        }
        return total > 0 ? total : null;
    }

    public static string Says(ReadCall c, string kind, long size)
    {
        var what = $"{c.Path.Replace('\\', '/')} is {size:N0} bytes";
        var route = kind switch
        {
            "image" => $"Send a helper (the Agent tool) to read it and return what it found, or read a crop under {MaxImageBytes:N0} bytes. Ian's rule that every sheet is looked at stands: the look guard credits a helper's Read of a picture once the helper has finished.",
            "pdf" => $"Send a helper to read it and return what it found, or read at most {MaxPdfPages} pages (pages: \"3-4\").",
            _ => $"Read the lines you need (offset, and limit up to {MaxRangeLines}), run a command that prints a summary, or send a helper that returns what it found.",
        };
        return $"A main session holds findings, not raw material: its context is re-sent every turn (board row 1936; Ian, 6 Nov: \"that should be a REQUIREMENT\"). {what}. {route}";
    }

    /// <summary>
    /// The warning for a Bash or PowerShell output over the size since the last warning, within the last
    /// <see cref="RecentRequests"/> requests (so one from long ago is not raised when the guard first runs), or null.
    /// </summary>
    public static string? OutputWarning(IReadOnlyList<string> tail)
    {
        var requests = new HashSet<string>(StringComparer.Ordinal);
        var largest = 0;
        for (var i = tail.Count - 1; i >= 0; i--)
        {
            var raw = tail[i];
            if (raw.Contains("\"type\":\"attachment\"", StringComparison.Ordinal) && raw.Contains(OutputMarker, StringComparison.Ordinal)) break;
            if (raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal))
            {
                if (MessageId(raw) is { } id) requests.Add(id);
                if (requests.Count >= RecentRequests) break;
                continue;
            }
            if (raw.Length <= OutputWarnChars || !raw.Contains("\"type\":\"user\"", StringComparison.Ordinal) || !raw.Contains("\"tool_result\"", StringComparison.Ordinal)) continue;
            foreach (var (toolId, chars) in Results(raw))
                if (chars > OutputWarnChars && chars > largest && IsShellCall(tail, i, toolId)) largest = chars;
        }
        return largest == 0 ? null
            : $"{OutputMarker}{largest:N0} characters is now in this session's context and is re-sent every turn (board row 1936). Nothing is refused. " +
              "A main session holds findings: next time filter it or print a summary, or send a helper that returns what it found.";
    }

    /// <summary>How many requests back a big output is still news: the call before this one, and its parallel neighbours.</summary>
    private const int RecentRequests = 3;

    private static string? MessageId(string raw)
    {
        try
        {
            using var d = JsonDocument.Parse(raw);
            return d.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<(string Id, int Chars)> Results(string raw)
    {
        var found = new List<(string, int)>();
        try
        {
            using var d = JsonDocument.Parse(raw);
            if (!d.RootElement.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Array) return found;
            foreach (var b in c.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.Object || !b.TryGetProperty("type", out var t) || t.GetString() != "tool_result" || !b.TryGetProperty("tool_use_id", out var id)) continue;
                if (!b.TryGetProperty("content", out var r)) continue;
                var chars = r.ValueKind == JsonValueKind.String ? (r.GetString() ?? "").Length
                    : r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Sum(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String ? (tx.GetString() ?? "").Length : 0)
                    : 0;
                found.Add((id.GetString() ?? "", chars));
            }
        }
        catch (JsonException) { }
        return found;
    }

    private static bool IsShellCall(IReadOnlyList<string> tail, int before, string toolId)
    {
        for (var i = before - 1; i >= 0; i--)
        {
            var raw = tail[i];
            if (!raw.Contains(toolId, StringComparison.Ordinal) || !raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                using var d = JsonDocument.Parse(raw);
                if (!d.RootElement.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return false;
                return ContextTranscript.ToolUses(m).Any(u => u.Id == toolId && u.Tool is "Bash" or "PowerShell");
            }
            catch (JsonException) { return false; }
        }
        return false;
    }
}
