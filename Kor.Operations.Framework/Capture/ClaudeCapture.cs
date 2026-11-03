using System.Text.Json;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K3: the Claude Code hooks' side of capture - PostToolUse / PostToolUseFailure (Bash, PowerShell), PostCompact,
/// SessionEnd. Input shapes are the official hooks reference (code.claude.com/docs/en/hooks.md, read 2 Nov 2026):
/// common fields session_id, transcript_path, cwd, hook_event_name; PostToolUse adds tool_name, tool_input,
/// tool_response; PostToolUseFailure adds error ("Exit code N\n..." for a Bash command that exited non-zero - a failing
/// test run arrives HERE, not in PostToolUse); PostCompact adds trigger, compact_summary; SessionEnd adds reason.
/// Module (lane) = the session's start folder: CLAUDE_PROJECT_DIR, else the input's cwd, by leaf name.
/// </summary>
public static class ClaudeCapture
{
    public sealed record HookInput(string? SessionId, string? TranscriptPath, string? Cwd, string? EventName, string? ToolName,
                                   string? Command, string? Output, string? Error, string? CompactSummary, string? Trigger,
                                   string? Reason);

    /// <summary>Parses the hook's stdin. Throws on anything that is not a JSON object (hook mode swallows it).</summary>
    public static HookInput Parse(string stdin)
    {
        using var doc = JsonDocument.Parse(stdin);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("hook input is not a JSON object");
        string? command = null, output = null;
        if (root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object)
            command = Str(ti, "command");
        if (root.TryGetProperty("tool_response", out var tr))
        {
            if (tr.ValueKind == JsonValueKind.String) output = tr.GetString();
            else if (tr.ValueKind == JsonValueKind.Object)
            {
                // Bash: stdout + stderr. Other shells' shapes are not documented: take whichever text fields exist.
                var parts = new[] { Str(tr, "stdout"), Str(tr, "stderr"), Str(tr, "output") }.Where(p => !string.IsNullOrEmpty(p));
                output = string.Join("\n", parts);
            }
        }
        var error = Str(root, "error");
        return new HookInput(Str(root, "session_id"), Str(root, "transcript_path"), Str(root, "cwd"), Str(root, "hook_event_name"),
                             Str(root, "tool_name"), command, output, error, Str(root, "compact_summary"), Str(root, "trigger"),
                             Str(root, "reason"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static string ModuleOf(HookInput h, string? moduleOverride) =>
        moduleOverride
        ?? Git.Leaf(Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR"))
        ?? Git.Leaf(h.Cwd)
        ?? "unknown";

    public static string ActorOf(HookInput h)
    {
        var bridge = Environment.GetEnvironmentVariable("CLAUDE_CODE_BRIDGE_SESSION_ID");
        var who = $"claude session {h.SessionId ?? "?"}";
        return string.IsNullOrWhiteSpace(bridge) || bridge.Trim() == h.SessionId ? who : $"{who} (bridge {bridge.Trim()})";
    }

    /// <summary>The event for a tool call, or null when the command is not one K3 records.</summary>
    public static BrainEvent? Tool(HookInput h, string? moduleOverride)
    {
        var output = h.Error is null ? h.Output : string.IsNullOrEmpty(h.Output) ? h.Error : h.Output + "\n" + h.Error;
        var exit = h.Error is not null ? ToolClassifier.ExitCodeOf(h.Error) ?? 1 : (int?)null;   // a failure event is a failure
        var t = ToolClassifier.Classify(h.Command, output, exit);
        if (t is null) return null;
        string? repo = null, commit = null;
        if (!string.IsNullOrEmpty(h.Cwd) && Directory.Exists(h.Cwd))
        {
            // One git call: the repo the command ran in, and its HEAD - the source the result belongs to.
            var lines = Git.Run(h.Cwd, TimeSpan.FromSeconds(3), "rev-parse", "--show-toplevel", "HEAD")?.Split('\n');
            if (lines is { Length: >= 2 }) { repo = Git.Leaf(lines[0].Trim()); commit = lines[1].Trim(); }
        }
        return new BrainEvent(ModuleOf(h, moduleOverride), t.Kind, repo, commit, t.Ref, t.Outcome, t.Detail, ActorOf(h));
    }

    /// <summary>PostCompact: the summary becomes the module's resume record (brain.LaneState), plus a 'compact' event.</summary>
    public static (int? LaneStateId, int EventId) Compact(HookInput h, string? moduleOverride)
    {
        var module = ModuleOf(h, moduleOverride);
        int? laneId = null;
        if (!string.IsNullOrWhiteSpace(h.CompactSummary))
            laneId = BrainStore.InsertLaneState(module, h.CompactSummary!, $"compaction {h.SessionId ?? "?"}");
        var detail = JsonSerializer.Serialize(new
        {
            trigger = h.Trigger,
            lane_state_id = laneId,
            summary_chars = h.CompactSummary?.Length ?? 0,
            session_id = h.SessionId,
        }, GitCapture.Json);
        var id = BrainStore.Insert(new BrainEvent(module, "compact", null, null, h.TranscriptPath,
                                                  laneId is null ? "empty" : "stored", detail, ActorOf(h)));
        return (laneId, id);
    }

    /// <summary>SessionEnd: the end and where the transcript is. No git, one insert - it must finish inside the budget.</summary>
    public static BrainEvent End(HookInput h, string? moduleOverride) =>
        new(ModuleOf(h, moduleOverride), "session-end", null, null, h.TranscriptPath, h.Reason, h.Reason, ActorOf(h));
}
