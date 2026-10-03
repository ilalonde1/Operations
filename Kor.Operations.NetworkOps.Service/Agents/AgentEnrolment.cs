#nullable enable
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Service.Agents;

/// <summary>
/// One-time enrolment codes for PCs that are not in the domain (Ian, 2026-10-02: the Boardroom PC -- "It's not domain
/// joined"). The service cannot reach such a PC the domain way (its account is refused there), so the PC installs its own
/// agent: an administrator at it runs one command, which downloads the agent package from here and trades the code for the
/// PC's key (Agent/Enrol.cs). A code is for ONE named PC, good for an hour, spent on first use, and held only in memory --
/// a service restart voids it, which costs a re-issue and nothing else. Wrong guesses are limited per minute.
/// </summary>
public sealed record EnrolBody(string Device, string Code);

/// <summary>Add a PC that is not in the domain (by its Windows computer name).</summary>
public sealed record ManualPcRequest(string Name);

internal sealed class AgentEnrolment
{
    /// <summary>A Windows computer name: what the agent reports and the enrolment is held to.</summary>
    public static bool IsPcName(string? name) => name is { Length: >= 1 and <= 15 } && Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9-]*$");

    /// <summary>The server address the agent package carries (its .exe.config): the command downloads from the same place.</summary>
    public static string PackageServerUrl()
    {
        var config = Path.Combine(AgentInstaller.PackageDir, "Kor.Operations.NetworkOps.Agent.exe.config");
        var m = Regex.Match(File.ReadAllText(config), "key=\"ServerUrl\"\\s+value=\"(https://[^\"]+)\"");
        return m.Success ? m.Groups[1].Value : throw new InvalidOperationException("the agent package's config has no ServerUrl");
    }

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I: it may be read aloud or retyped
    private readonly ConcurrentDictionary<string, (int DeviceId, string Device, DateTime ExpiresUtc, string By)> _codes = new(StringComparer.Ordinal);
    // Wrong guesses are limited PER NAMED PC, not globally: an attacker spraying wrong codes at one PC must not freeze
    // every other PC's enrolment too (a global queue did). The 60-bit code is the real defence; this is the backstop.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _misses = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Issued(string Code, DateTime ExpiresUtc);

    public Issued Issue(int deviceId, string device, string by, DateTime nowUtc)
    {
        foreach (var (k, v) in _codes) if (v.ExpiresUtc < nowUtc || v.DeviceId == deviceId) _codes.TryRemove(k, out _);   // one live code per PC
        var code = string.Concat(RandomNumberGenerator.GetBytes(12).Select(b => Alphabet[b % Alphabet.Length]));
        var expires = nowUtc + Lifetime;
        _codes[code] = (deviceId, device, expires, by);
        return new Issued($"{code[..4]}-{code[4..8]}-{code[8..]}", expires);
    }

    /// <summary>The device a code was issued for, spending it -- or null: unknown, expired, for another name, or too many misses.</summary>
    public (int DeviceId, string By)? Redeem(string device, string code, DateTime nowUtc)
    {
        if (TooManyMisses(device, nowUtc)) return null;   // a burst of wrong guesses at THIS PC: refused for a minute
        var key = Regex.Replace(code.ToUpperInvariant(), "[^A-Z0-9]", "");
        // Peek before claiming: only a confirmed match (right code, right name, unexpired) is spent, and the spend is the
        // atomic TryRemove. A wrong name must NEVER remove-then-readd the code -- that transiently hid a valid code from a
        // racing correct redeem and failed it (and burned a miss) for no reason.
        if (_codes.TryGetValue(key, out var e) && e.ExpiresUtc >= nowUtc && e.Device.Equals(device, StringComparison.OrdinalIgnoreCase)
            && _codes.TryRemove(key, out e))
            return (e.DeviceId, e.By);
        Miss(device, nowUtc);
        return null;
    }

    private bool TooManyMisses(string device, DateTime nowUtc)
    {
        if (!_misses.TryGetValue(device, out var q)) return false;
        while (q.TryPeek(out var t) && nowUtc - t > TimeSpan.FromMinutes(1)) q.TryDequeue(out _);
        if (q.IsEmpty) { _misses.TryRemove(device, out _); return false; }   // prune, so the map does not keep dead names
        return q.Count >= 10;
    }

    // Track a wrong guess ONLY against a PC that actually has a live code to brute-force. A spray of random device names (no
    // code exists for them) achieves nothing and must not grow this map without bound (2026-10-03 re-audit).
    private void Miss(string device, DateTime nowUtc)
    {
        if (!_codes.Values.Any(v => v.ExpiresUtc > nowUtc && v.Device.Equals(device, StringComparison.OrdinalIgnoreCase))) return;
        _misses.GetOrAdd(device, _ => new ConcurrentQueue<DateTime>()).Enqueue(nowUtc);
    }

    private static readonly object _zipLock = new();
    private static byte[]? _zipCache;
    private static string? _zipSig;

    /// <summary>The agent package (the folder that travels with the service) as one zip, for the PC to download.
    /// Built once and cached: /agent/v1/package is unauthenticated (before-key), so recompressing the whole folder at
    /// Optimal on every request was a CPU/memory amplifier anyone on the LAN could loop. Rebuilt only when a deploy
    /// changes the package (file count or newest write time).</summary>
    public static byte[] PackageZip()
    {
        var sig = PackageSignature();
        lock (_zipLock)
        {
            if (_zipCache is not null && _zipSig == sig) return _zipCache;
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var f in Directory.GetFiles(AgentInstaller.PackageDir))
                    zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
            _zipCache = ms.ToArray();
            _zipSig = sig;
            return _zipCache;
        }
    }

    private static string PackageSignature()
    {
        var files = Directory.GetFiles(AgentInstaller.PackageDir);
        var newest = files.Length == 0 ? DateTime.MinValue : files.Max(File.GetLastWriteTimeUtc);
        return $"{files.Length}:{newest.Ticks}";
    }

    /// <summary>The certificate pin the agent package itself carries (its .exe.config): the download is checked against the
    /// same one, so there is one pin, not two.</summary>
    public static string PackagePin()
    {
        var config = Path.Combine(AgentInstaller.PackageDir, "Kor.Operations.NetworkOps.Agent.exe.config");
        var m = Regex.Match(File.ReadAllText(config), "key=\"ServerCertSha256\"\\s+value=\"([0-9A-Fa-f]{64})\"");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : throw new InvalidOperationException("the agent package's config has no ServerCertSha256");
    }

    /// <summary>
    /// The one command an administrator runs on the PC (Windows PowerShell 5.1, which every Windows 10/11 has): trust only
    /// APP01's pinned certificate, download the package, unpack it, enrol with the code.
    /// </summary>
    public static string Command(string serverUrl, string pin, string code)
    {
        var sb = new StringBuilder();
        sb.Append("$pin='").Append(pin).Append("'; ");
        sb.Append("[Net.ServicePointManager]::SecurityProtocol='Tls12'; ");
        sb.Append("[Net.ServicePointManager]::ServerCertificateValidationCallback={param($a,$c) ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($c.GetRawCertData())) -replace '-','') -eq $pin}; ");
        // A fresh, unpredictable staging folder: a non-admin process running as the same user cannot pre-plant files (an
        // sc.exe, an altered agent) in a known path for the elevated --enrol to pick up (2026-10-03 re-audit, finding 1).
        sb.Append("$d=Join-Path $env:TEMP ('kor-agent-'+[guid]::NewGuid().ToString('N')); $z=\"$d.zip\"; ");
        sb.Append("(New-Object Net.WebClient).DownloadFile('").Append(serverUrl.TrimEnd('/')).Append("/agent/v1/package', $z); ");
        sb.Append("Expand-Archive $z $d -Force; ");
        sb.Append("& (Join-Path $d 'Kor.Operations.NetworkOps.Agent.exe') --enrol ").Append(code);
        return sb.ToString();
    }
}
