#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Kor.Operations.NetworkOps.Core.OnTarget;

// Do not stack payloads on a host that is still running earlier ones (12 Nov 2026, brain #133338): a caller that
// timed out and called again 64 times left 64 powershell.exe spinning on KOR-210, ~120 GB of commit, until ETABS
// starved. The caller giving up stops nothing on the target, so the caller must look before it sends.
//
// How "alive" is seen: the payload opens C:\Windows\Temp\korrun-<id>-<kind>.alive with DeleteOnClose and holds it
// for its whole life (OnTargetPayload.Build, alivePath). The kernel deletes it when the last handle closes -- at a
// normal end, a crash, or the watchdog's kill. While it is held, an exclusive open fails with a sharing violation:
// that, not the file merely existing, is the proof of life. A marker nobody holds (a power cut mid-run) is stale
// and is removed here, so it can never block a host forever.
public static class OnTargetAlive
{
    /// <summary>Above this many live payloads of ANY kind on one host, nothing more is sent (a caller whose script
    /// changes a little each call -- a timestamp in it -- has a new kind each time).</summary>
    public const int MaxAlivePerHost = 4;

    /// <summary>The kind of a payload: the same script body is the same kind.</summary>
    public static string KindOf(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..12].ToLowerInvariant();

    public static string FileName(string id, string kind) => $"korrun-{id}-{kind}.alive";

    /// <summary>The payloads alive in <paramref name="tempDir"/> (the target's C:\Windows\Temp, over c$). Stale
    /// markers are deleted as they are found.</summary>
    public static IReadOnlyList<AlivePayload> Find(string tempDir)
    {
        var alive = new List<AlivePayload>();
        foreach (var path in Directory.EnumerateFiles(tempDir, "korrun-*.alive"))   // the OS filters (rule 4)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var kind = name[(name.LastIndexOf('-') + 1)..];
            DateTime since;
            try { since = File.GetCreationTimeUtc(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                // Opened exclusively: nobody holds it, so its payload is gone. Remove the leftover.
                try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                alive.Add(new AlivePayload(Path.GetFileName(path), kind, since));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone between the listing and the open, or unreadable: not proof of life.
            }
        }
        return alive;
    }

    /// <summary>Why a new payload of <paramref name="kind"/> must not be sent, or null when it may.</summary>
    public static string? Refusal(IReadOnlyList<AlivePayload> alive, string kind, string computer, DateTime nowUtc)
    {
        var same = alive.Where(a => a.Kind == kind).OrderBy(a => a.SinceUtc).ToList();
        if (same.Count > 0)
            return $"not sent: {same.Count} earlier run(s) of this same script are still alive on {computer} " +
                   $"(oldest {Age(same[0].SinceUtc, nowUtc)} old, {same[0].File}); each kills itself at its own limit. " +
                   "Wait for it, or read why it is slow, before sending it again.";
        if (alive.Count >= MaxAlivePerHost)
            return $"not sent: {alive.Count} payloads are already alive on {computer} (the limit is {MaxAlivePerHost}; " +
                   $"oldest {Age(alive.Min(a => a.SinceUtc), nowUtc)} old). Each kills itself at its own limit.";
        return null;
    }

    private static string Age(DateTime sinceUtc, DateTime nowUtc)
    {
        var age = nowUtc - sinceUtc;
        return age.TotalMinutes >= 1 ? $"{age.TotalMinutes:0} min" : $"{Math.Max(0, age.TotalSeconds):0} s";
    }

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33), as an HRESULT 0x80070020 / 0x80070021.
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;
}

public sealed record AlivePayload(string File, string Kind, DateTime SinceUtc);
