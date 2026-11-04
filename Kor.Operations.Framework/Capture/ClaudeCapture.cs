using System.Text.Json;
using System.Text.Json.Nodes;

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
                                   string? Reason, string? Source = null, bool StopHookActive = false,
                                   string? BackgroundTaskId = null, string? LastAssistantMessage = null, string? Prompt = null);

    /// <summary>Parses the hook's stdin. Throws on anything that is not a JSON object (hook mode swallows it).</summary>
    public static HookInput Parse(string stdin)
    {
        using var doc = JsonDocument.Parse(stdin);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("hook input is not a JSON object");
        string? command = null, output = null, background = null;
        if (root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object)
        {
            command = Str(ti, "command");
            // row 1393: a launch with run_in_background - its result arrives later as a task-notification
            if (ti.TryGetProperty("run_in_background", out var bg) && bg.ValueKind == JsonValueKind.True) background = "run_in_background";
        }
        if (root.TryGetProperty("tool_response", out var tr))
        {
            if (tr.ValueKind == JsonValueKind.String) output = tr.GetString();
            else if (tr.ValueKind == JsonValueKind.Object)
            {
                // Bash: stdout + stderr. Other shells' shapes are not documented: take whichever text fields exist.
                var parts = new[] { Str(tr, "stdout"), Str(tr, "stderr"), Str(tr, "output") }.Where(p => !string.IsNullOrEmpty(p));
                output = string.Join("\n", parts);
                // a command moved to the background (run_in_background, or after running past its wait) names its task
                background = Str(tr, "backgroundTaskId") ?? background;
            }
        }
        var error = Str(root, "error");
        return new HookInput(Str(root, "session_id"), Str(root, "transcript_path"), Str(root, "cwd"), Str(root, "hook_event_name"),
                             Str(root, "tool_name"), command, output, error, Str(root, "compact_summary"), Str(root, "trigger"),
                             Str(root, "reason"), Str(root, "source"),
                             root.TryGetProperty("stop_hook_active", out var sa) && sa.ValueKind == JsonValueKind.True,
                             background, Str(root, "last_assistant_message"), Str(root, "prompt"));
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

    /// <summary>
    /// The event for a tool call, or null when the command is not one K3 records. It is cited to the path
    /// <see cref="SourceResolver"/> PROVES the tool ran on (repo + HEAD of that path); MODULE = that repo (#125, 2 Nov: an
    /// Operations build was filed under Framework), unless --module is given. When the source is UNKNOWN there is no
    /// repo or commit, and the module falls back to the session's lane (CLAUDE_PROJECT_DIR / cwd) - the only module
    /// known - with Detail saying so (source null, source_why, module_from).
    /// </summary>
    public static BrainEvent? Tool(HookInput h, string? moduleOverride, SourceResolver.Probe? probe = null)
    {
        var output = h.Error is null ? h.Output : string.IsNullOrEmpty(h.Output) ? h.Error : h.Output + "\n" + h.Error;
        // row 1393: a background launch's response is "Command running in background" - nothing has run yet. Its result is
        // recorded at Stop from the turn's task-notification (TurnCapture), never here as 'unknown'.
        if (h.BackgroundTaskId is not null && h.Error is null) return null;
        // a command a hook refused never ran: no event (row 1393 - it was recorded as a failed build or test)
        if (ToolClassifier.IsHookRefusal(h.Error) || ToolClassifier.IsHookRefusal(h.Output)) return null;
        var exit = h.Error is not null ? ToolClassifier.ExitCodeOf(h.Error) ?? 1 : (int?)null;   // a failure event is a failure
        var t = ToolClassifier.Classify(h.Command, output, exit);
        if (t is null) return null;
        var shell = string.Equals(h.ToolName, "PowerShell", StringComparison.OrdinalIgnoreCase) ? "powershell" : "bash";
        var source = SourceResolver.Resolve(h.Command!, shell, h.Cwd, t, probe ?? SourceResolver.Probe.Real);
        string? repo = null, commit = null, why = source.Why;
        if (source.Path is not null)
        {
            var dir = File.Exists(source.Path) ? Path.GetDirectoryName(source.Path)! : source.Path;
            // One git call: the repo holding what the tool ran on, and its HEAD.
            var lines = Git.Run(dir, TimeSpan.FromSeconds(3), "rev-parse", "--show-toplevel", "HEAD")?.Split('\n');
            if (lines is { Length: >= 2 }) { repo = Git.Leaf(lines[0].Trim()); commit = lines[1].Trim(); }
            else why += " - but it is not inside a git work tree";
        }
        var (module, from) = moduleOverride is not null ? (moduleOverride, "--module")
                           : repo is not null ? (repo, "the repo it ran on")
                           : (ModuleOf(h, null), "the session's lane (the source is unknown)");
        var detail = JsonNode.Parse(t.Detail)!.AsObject();
        detail["source"] = source.Path;
        detail["source_why"] = why;
        detail["module_from"] = from;
        detail["shell"] = shell;
        return new BrainEvent(module, t.Kind, repo, commit, t.Ref, t.Outcome, detail.ToJsonString(GitCapture.Json), ActorOf(h), h.SessionId);
    }

    /// <summary>
    /// PostCompact: the summary becomes the module's resume record (brain.LaneState), plus a 'compact' event. A credential
    /// value in the summary is MASKED and the rest stored (mistake 138; a lost resume costs more than a masked token): the
    /// hits come back so the caller logs one line naming the mask, and the event's Detail counts them. Never the value.
    /// </summary>
    public static (int? LaneStateId, int EventId, IReadOnlyList<SecretGuard.Hit> Masked) Compact(HookInput h, string? moduleOverride, BrainDb? db = null)
    {
        var module = ModuleOf(h, moduleOverride);
        int? laneId = null;
        IReadOnlyList<SecretGuard.Hit> masked = Array.Empty<SecretGuard.Hit>();
        if (!string.IsNullOrWhiteSpace(h.CompactSummary))
            (laneId, masked) = BrainStore.InsertLaneStateMasked(module, h.CompactSummary!, $"compaction {h.SessionId ?? "?"}", db, h.SessionId);
        var detail = JsonSerializer.Serialize(new
        {
            trigger = h.Trigger,
            lane_state_id = laneId,
            summary_chars = h.CompactSummary?.Length ?? 0,
            masked_values = masked.Count,
            session_id = h.SessionId,
        }, GitCapture.Json);
        var id = BrainStore.Insert(new BrainEvent(module, "compact", null, null, h.TranscriptPath,
                                                  laneId is null ? "empty" : "stored", detail, ActorOf(h), h.SessionId), db);
        return (laneId, id, masked);
    }

    /// <summary>SessionEnd: the end and where the transcript is. No git, one insert - it must finish inside the budget.</summary>
    public static BrainEvent End(HookInput h, string? moduleOverride) =>
        new(ModuleOf(h, moduleOverride), "session-end", null, null, h.TranscriptPath, h.Reason, h.Reason, ActorOf(h), h.SessionId);
}
