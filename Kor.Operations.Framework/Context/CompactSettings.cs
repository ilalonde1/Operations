using System.Text.Json;

namespace Kor.Operations.Framework.Context;

/// <summary>
/// G5 (board row 1474): Claude Code's auto-compact settings as the session sees them, read from the files Claude Code reads,
/// in ITS precedence order (code.claude.com/docs/en/settings, "Settings precedence", read 3 Nov 2026: managed, command
/// line, project local .claude/settings.local.json, shared project .claude/settings.json, user ~/.claude/settings.json -
/// "a key at a higher level overrides the same key anywhere below it"). G5 reads the three files it can see: project local
/// and shared project (from the hook's cwd; on Windows the docs keep the local file beside .claude/settings.json) and user
/// (%CLAUDE_CONFIG_DIR% or ~/.claude).
/// <list type="bullet">
/// <item>autoCompactEnabled: the highest file that sets it; unset everywhere = on (Claude Code compacts by default).</item>
/// <item>The window for a model: CLAUDE_CODE_AUTO_COMPACT_WINDOW in the environment first (env-vars: it "takes precedence
/// over /autocompact, --autocompact and autoCompactWindow"); else the highest file that sets modelSettings[model]
/// .autoCompactWindow or a top-level autoCompactWindow, the model's own entry first within that file (settings-reference,
/// modelSettings: "for that model, the value takes precedence over a top-level autoCompactWindow in the same settings
/// file"; across files G5 applies the rule the same entry states for effortLevel). "auto" = the model's own window.
/// The model key is matched as the docs say Claude Code does: its canonical name, with a date suffix or "[1m]" removed.</item>
/// </list>
/// Not read: managed settings, a --settings or --autocompact flag at launch, and a /cd that moved the session away from the
/// hook's cwd. Same-class miss: a session started with --settings '{"autoCompactWindow":250000}' compacts at about 217k
/// while G5 still judges it on the files' 900k (WARN 300k), so it compacts before G5 says anything.
/// </summary>
/// <param name="Cap">a ceiling given on the hook's command line (--ceiling N): it can only LOWER the ceiling, never raise
/// it (the live demonstration runs a small session against a small ceiling).</param>
public sealed record CompactSettings(bool Enabled, long? EnvWindow, IReadOnlyList<CompactSettings.Layer> Layers, long? Cap = null)
{
    /// <summary>One settings file's auto-compact keys (null = the file does not set it).</summary>
    public sealed record Layer(string File, bool? Enabled, long? Window, bool WindowAuto, IReadOnlyDictionary<string, long?> PerModel);

    /// <summary>Auto-compact off: the thresholds stand on the model's window alone.</summary>
    public static readonly CompactSettings Off = new(false, null, Array.Empty<Layer>());

    /// <summary>The auto-compact window for this model, or null (off, or no file sets one: the model's own window).</summary>
    public long? WindowFor(string? model)
    {
        if (!Enabled) return null;
        if (EnvWindow is { } e) return e;
        var key = Canonical(model);
        foreach (var l in Layers)
        {
            if (key is not null && l.PerModel.TryGetValue(key, out var w)) return w;   // null = "auto"
            if (l.Window is not null || l.WindowAuto) return l.Window;
        }
        return null;
    }

    public static string? Canonical(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var m = model.Trim().ToLowerInvariant();
        var b = m.IndexOf('[');
        if (b > 0) m = m[..b];
        m = System.Text.RegularExpressions.Regex.Replace(m, @"-\d{8}$", "");
        return m;
    }

    /// <summary>The settings as a session in <paramref name="cwd"/> sees them (files that are missing or not JSON are skipped).</summary>
    public static CompactSettings Read(string? cwd, string? userDir = null, string? envWindow = null)
    {
        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            files.Add(Path.Combine(cwd, ".claude", "settings.local.json"));
            files.Add(Path.Combine(cwd, ".claude", "settings.json"));
        }
        var user = userDir ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        files.Add(Path.Combine(user, "settings.json"));
        var layers = files.Select(Parse).Where(l => l is not null).Select(l => l!).ToList();
        var enabled = layers.FirstOrDefault(l => l.Enabled is not null)?.Enabled ?? true;
        long? env = long.TryParse((envWindow ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_AUTO_COMPACT_WINDOW"))?.Trim(), out var n) && n > 0 ? n : null;
        return new CompactSettings(enabled, env, layers);
    }

    public static Layer? Parse(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return ParseText(file, File.ReadAllText(file));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static Layer? ParseText(string file, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            bool? enabled = r.TryGetProperty("autoCompactEnabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;
            var (window, auto) = WindowOf(r);
            var per = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            if (r.TryGetProperty("modelSettings", out var ms) && ms.ValueKind == JsonValueKind.Object)
                foreach (var p in ms.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object)
                    {
                        var (w, a) = WindowOf(p.Value);
                        if ((w is not null || a) && Canonical(p.Name) is { } k) per[k] = w;
                    }
            return new Layer(file, enabled, window, auto, per);
        }
    }

    private static (long? Window, bool Auto) WindowOf(JsonElement o)
    {
        if (!o.TryGetProperty("autoCompactWindow", out var w)) return (null, false);
        if (w.ValueKind == JsonValueKind.Number && w.TryGetInt64(out var n) && n > 0) return (n, false);
        if (w.ValueKind == JsonValueKind.String && string.Equals(w.GetString(), "auto", StringComparison.OrdinalIgnoreCase)) return (null, true);
        return (null, false);
    }

    /// <summary>One line naming what was read (for the replay summary and the hook's plain mode).</summary>
    public string Describe(string? model) =>
        !Enabled ? "auto-compact off" : $"auto-compact on, window for {model ?? "-"}: {(WindowFor(model) is { } w ? w.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "the model's own")}" +
                                       (EnvWindow is not null ? " (env)" : "");
}
