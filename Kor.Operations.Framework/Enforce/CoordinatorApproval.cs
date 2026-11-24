using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kor.Operations.Framework.Enforce;

/// <summary>BudgetApproval's provenance/12-hour policy, using DrafterPythonGuard's recorder location. The recorder alone
/// has no timestamp/provenance: require matching typed/queued CLI user text in THIS session's transcript. Never grants on
/// read errors, tool results, files, peer relays, quoted/fenced tokens, future timestamps, or another session. The recorder
/// retains only 20 prompts of 4000 characters; an approval no longer present there cannot lift. Forging both files remains
/// outside this guard's boundary. Existing budget and Python guards are unchanged.</summary>
public static class CoordinatorApproval
{
    public sealed record Scan(string Transcript, long Length, DateTime LastWriteUtc, string RecorderHash,
        DateTimeOffset? ApprovedAtUtc);

    /// <summary>Rechecks the small recorder every time, but reads only appended transcript bytes when it is unchanged.
    /// Truncation, equal-size rewrites and recorder rotation reset the scan. Non-append replacements that grow, and
    /// same-size/same-timestamp forged files are outside
    /// this provenance boundary, as are forged transcript/recorder files generally. Expiry is checked on every call.</summary>
    public static DateTimeOffset? FindCached(string? session, string? promptsDir, string? transcript, DateTimeOffset now, string cacheDir)
    {
        if (!SessionRoles.ValidSession(session) || transcript is null) return null;
        try
        {
            var recorder = File.ReadAllText(Path.Combine(promptsDir ?? Path.Combine(Path.GetTempPath(), "claude-asked"), session + ".jsonl"));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recorder)));
            var prompts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in recorder.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                using var doc = JsonDocument.Parse(line);
                if (CoordinatorRoleGuard.S(doc.RootElement, "prompt") is { } p && HasToken(p)) prompts.Add(p.Trim());
            }
            if (prompts.Count == 0) return null;
            var state = CheckCache.LoadRole(cacheDir, session!) ?? new CheckCache.RoleSnapshot(DateTimeOffset.MinValue, null, null, null);
            var info = new FileInfo(transcript);
            var scan = state.ApprovalScan;
            var reuse = scan is not null && scan.Transcript == transcript && scan.RecorderHash == hash && info.Length >= scan.Length
                && (info.Length != scan.Length || info.LastWriteTimeUtc == scan.LastWriteUtc);
            var latest = reuse ? scan!.ApprovedAtUtc : null;
            using var stream = File.Open(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (reuse) stream.Seek(scan!.Length, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (TypedApproval(doc.RootElement, session!, now, out var at, out var prompt)
                    && prompts.Contains(prompt.Trim()[..Math.Min(prompt.Trim().Length, 4000)]) && (latest is null || at > latest)) latest = at;
            }
            CheckCache.SaveRole(cacheDir, session!, state with { ApprovalScan = new(transcript, stream.Position, info.LastWriteTimeUtc, hash, latest) });
            return latest <= now && now - latest < TimeSpan.FromHours(12) ? latest : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }

    public static DateTimeOffset? Find(string? session, string? promptsDir, string? transcript, DateTimeOffset now)
    {
        if (!SessionRoles.ValidSession(session) || transcript is null) return null;
        try
        {
            var prompts = new HashSet<string>(StringComparer.Ordinal);
            var path = Path.Combine(promptsDir ?? Path.Combine(Path.GetTempPath(), "claude-asked"), session + ".jsonl");
            foreach (var line in File.ReadLines(path))
            {
                using var doc = JsonDocument.Parse(line);
                if (CoordinatorRoleGuard.S(doc.RootElement, "prompt") is { } p && HasToken(p)) prompts.Add(p.Trim());
            }
            if (prompts.Count == 0) return null;
            DateTimeOffset? latest = null;
            foreach (var line in File.ReadLines(transcript))
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (TypedApproval(root, session!, now, out var at, out var prompt)
                    && prompts.Contains(prompt.Trim()[..Math.Min(prompt.Trim().Length, 4000)]) && (latest is null || at > latest)) latest = at;
            }
            return latest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }

    public static bool TypedApproval(JsonElement root, string session, DateTimeOffset now, out DateTimeOffset at, out string prompt)
    {
        at = default; prompt = "";
        if (CoordinatorRoleGuard.S(root, "type") != "user" || CoordinatorRoleGuard.S(root, "sessionId") != session
            || CoordinatorRoleGuard.S(root, "entrypoint") != "cli" || CoordinatorRoleGuard.S(root, "promptSource") is not ("typed" or "queued")
            || True(root, "isMeta") || True(root, "isSidechain") || root.TryGetProperty("toolUseResult", out var tr) && tr.ValueKind != JsonValueKind.Null) return false;
        if (root.TryGetProperty("origin", out var origin) && CoordinatorRoleGuard.S(origin, "kind") is { } kind && kind != "human") return false;
        if (!root.TryGetProperty("message", out var msg) || CoordinatorRoleGuard.S(msg, "role") != "user" || !msg.TryGetProperty("content", out var content)) return false;
        if (content.ValueKind == JsonValueKind.String) prompt = content.GetString()!;
        else if (content.ValueKind == JsonValueKind.Array && content.EnumerateArray().All(c => CoordinatorRoleGuard.S(c, "type") == "text"))
            prompt = string.Join("\n", content.EnumerateArray().Select(c => CoordinatorRoleGuard.S(c, "text")));
        else return false;
        return HasToken(prompt) && DateTimeOffset.TryParse(CoordinatorRoleGuard.S(root, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out at) && at <= now && now - at < TimeSpan.FromHours(12);
    }

    private static bool True(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static bool HasToken(string text)
    {
        var fenced = false;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var clean = line.Trim();
            if (clean.StartsWith("```", StringComparison.Ordinal) || clean.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (!fenced && clean == CoordinatorRoleGuard.Approval) return true;
        }
        return false;
    }
}
