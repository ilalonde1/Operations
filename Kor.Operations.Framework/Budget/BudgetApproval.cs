using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Budget;

/// <summary>
/// Reads the same claude-asked/session.jsonl prompt record as DrafterPythonGuard. That recorder has NO timestamp or
/// provenance, so corroborates its text against this session's typed/queued CLI user entries in the Claude transcript.
/// Nothing read from a tool/file/peer/SDK message grants approval. Read errors fail closed. The token must occupy a
/// whole line, not a quote or prose mention. Files are not a security boundary against a session that forges both records.
/// </summary>
public static class BudgetApproval
{
    public static DateTimeOffset? Find(int row, string token, string? session, string? promptsDir, string? projectsDir, DateTimeOffset now)
    {
        if (row <= 0 || string.IsNullOrWhiteSpace(session) || session == "unknown" || !Regex.IsMatch(session, @"\A[A-Za-z0-9_-]{1,64}\z")) return null;
        if (token is not ("IAN-APPROVED-OVER-BUDGET" or "IAN-APPROVED-BUDGET")) return null;
        try
        {
            var asked = Path.Combine(promptsDir ?? Path.Combine(Path.GetTempPath(), "claude-asked"), session + ".jsonl");
            var wanted = token + " " + row.ToString(CultureInfo.InvariantCulture);
            var prompts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(asked))
            {
                using var doc = JsonDocument.Parse(line);
                if (String(doc.RootElement, "prompt") is { } prompt && HasToken(prompt, wanted)) prompts.Add(prompt.Trim());
            }
            if (prompts.Count == 0) return null;
            var projects = projectsDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            DateTimeOffset? latest = null;
            // Only the requested session, one level under projects. Never search other sessions for a lift.
            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                var transcript = Path.Combine(dir, session + ".jsonl");
                if (!File.Exists(transcript)) continue;
                foreach (var line in File.ReadLines(transcript))
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (String(root, "type") != "user" || String(root, "entrypoint") != "cli"
                        || String(root, "promptSource") is not ("typed" or "queued")
                        || True(root, "isMeta") || True(root, "isSidechain") || NonNull(root, "toolUseResult")
                        || String(root, "sessionId") is { } id && id != session) continue;
                    if (root.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object
                        && String(origin, "kind") is { } kind && kind != "human") continue;
                    if (!root.TryGetProperty("message", out var message) || String(message, "role") != "user"
                        || !message.TryGetProperty("content", out var content)) continue;
                    string text;
                    if (content.ValueKind == JsonValueKind.String) text = content.GetString()!;
                    else if (content.ValueKind == JsonValueKind.Array && content.EnumerateArray().All(c => String(c, "type") == "text"))
                        text = string.Join("\n", content.EnumerateArray().Select(c => String(c, "text")));
                    else continue;
                    // The recorder strips and caps the prompt at 4000 chars. The approving line itself must be in it.
                    if (!HasToken(text, wanted) || !prompts.Contains(text.Trim()[..Math.Min(text.Trim().Length, 4000)])) continue;
                    if (!DateTimeOffset.TryParse(String(root, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                        || at > now || now - at >= TimeSpan.FromHours(12)) continue;
                    if (latest is null || at > latest) latest = at;
                }
            }
            return latest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { return null; }
    }

    private static bool HasToken(string text, string wanted)
    {
        var fenced = false;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var clean = line.Trim();
            if (clean.StartsWith("```", StringComparison.Ordinal) || clean.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (!fenced && clean == wanted) return true;
        }
        return false;
    }

    private static string? String(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool True(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;
    private static bool NonNull(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null;
}
