using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Spend;

/// <summary>One screen reading of Ian's: the All-models percentage and when he read it.</summary>
public sealed record ScreenReading(double Percent, DateTimeOffset At);

/// <summary>What the meter holds now. <see cref="FilesLeft"/> above 0: the count is not yet whole.</summary>
public sealed record SpendCount(DateTimeOffset WeekStart, long Raw, long RawSinceReading, int Requests, int FilesLeft);

/// <summary>
/// G6, the spend line (Framework board row 1559; Ian, 4 Nov: "We MUST learn to be more efficient with token usage. That does
/// NOT mean we restrict functionality"). One line, seen by every session through <c>kor context</c>'s hooks (every prompt, and
/// at most once per <see cref="SayEvery"/> in a session's tool calls, so the line does not itself become the cost). WARN ONLY:
/// nothing is ever refused on a percentage (Ian, 4 Nov 09:3x, record #124879). So row 1559's refusal at a cap is not built.
///
/// WHAT IT READS: every *.jsonl under ~/.claude/projects, this PC only; message.usage's input, cache read, cache creation and
/// output tokens added raw, one count per message id (a message's lines repeat; the largest total is kept), from the week's
/// start, Monday 12:00 local (Ian's screen, 4 Nov: "resets 9 Nov 12:00"). Ported from OverLORD's spend_meter.py.
///
/// THE ESTIMATE IS A RANGE, ON PURPOSE. Ian's Usage screen read 65% at 4 Nov 00:13, 74% at about 09:25 and 77% at 14:49. That is
/// about 15M raw tokens a point overnight and about 68M after it (records #124962, #124963), so no one slope holds. The line
/// takes his newest reading this week (the live brain.Memory record <see cref="ReadingRecord"/> in lane Framework, written with
/// `kor record`: its says begins "77% at 2026-11-04T14:49:00-07:00") and adds the raw tokens since on each of the two slopes.
/// It says what it reads and that it is an estimate; his screen rules.
///
/// THE COUNT IS INCREMENTAL: a cache under %LOCALAPPDATA%\KOR\spend holds each file's offset and each message's total. A hook
/// reads only new bytes, at most once per <see cref="ScanEvery"/>, and for at most its deadline. A cold cache is filled over
/// several calls, and the line says it is partial until it is whole. Two sessions writing the cache at once: the last wins
/// and the other's bytes are read again next time, which the per-message count makes harmless.
///
/// DOES NOT COVER: other machines and claude.ai, which spend the same allowance; the provider's weighting (raw counts a cache
/// read like an output token, which the provider does not); usage in a transcript deleted before it was read; a screen reading
/// nobody recorded (the line then gives raw tokens and says so; a DB that cannot answer is said as that, not as none recorded).
/// In a hook the whole line gets 3 s; past that it is left out for that call, and G5's text still goes.
/// Same-class fault it would miss: the provider changes how it counts cache reads mid-week. The raw count goes on looking
/// right, and only the two slopes, measured on 4 Nov, go wrong. Only a new reading of Ian's screen shows it.
/// </summary>
public static class SpendMeter
{
    public const string ReadingRecord = "usage-screen-reading";
    public const long SteepRawPerPoint = 15_000_000;
    public const long FlatRawPerPoint = 68_000_000;
    public static readonly TimeSpan SayEvery = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ScanEvery = TimeSpan.FromMinutes(1);
    public const string Marker = "[kor spend] ";

    private static readonly Regex ReadingText = new(@"^\s*(?<p>\d+(?:\.\d+)?)%\s+at\s+(?<t>\S+)", RegexOptions.CultureInvariant);

    /// <summary>The reading at the head of a record's says, or null when it holds none.</summary>
    public static ScreenReading? ParseReading(string? says)
    {
        var m = ReadingText.Match(says ?? "");
        return m.Success
               && double.TryParse(m.Groups["p"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
               && DateTimeOffset.TryParse(m.Groups["t"].Value.TrimEnd(':', ',', ';', '.'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? new ScreenReading(p, at) : null;
    }

    /// <summary>The newest Monday 12:00 in the machine's local time at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var noon = new DateTime(local.Year, local.Month, local.Day, 12, 0, 0, DateTimeKind.Local);
        var back = ((int)local.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var start = noon.AddDays(-back);
        if (start > local) start = start.AddDays(-7);
        return new DateTimeOffset(start);
    }

    private static string M(long raw) => (raw / 1_000_000).ToString("N0", CultureInfo.InvariantCulture) + "M";

    /// <summary>The one line. A reading from before this week's start is not used.</summary>
    public static string Line(SpendCount c, ScreenReading? reading, bool readingFailed = false)
    {
        var head = $"{Marker}this PC since {c.WeekStart.LocalDateTime.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture)}: {M(c.Raw)} raw tokens";
        if (c.FilesLeft > 0) head += $" so far ({c.FilesLeft} transcript(s) still to count)";
        const string Rules = "An ESTIMATE from this PC's transcripts, not the provider's meter: Ian's Usage screen rules.";
        if (readingFailed) return $"{head}; Ian's screen reading could not be read just now. {Rules}";
        if (reading is null || reading.At < c.WeekStart) return $"{head}; no reading of Ian's screen recorded this week. {Rules}";
        var low = reading.Percent + (double)c.RawSinceReading / FlatRawPerPoint;
        var high = reading.Percent + (double)c.RawSinceReading / SteepRawPerPoint;
        string F(double d) => d.ToString("0", CultureInfo.InvariantCulture);
        var at = reading.At.LocalDateTime.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        return $"{head}; Ian's screen read {F(reading.Percent)}% at {at}, {M(c.RawSinceReading)} since, so about {F(low)}-{F(high)}% " +
               $"(no one slope held: {M(FlatRawPerPoint)} to {M(SteepRawPerPoint)} a point). {Rules}";
    }

    /// <summary>The total of one usage object, or 0.</summary>
    private static long RawOf(JsonElement usage)
    {
        long t = 0;
        foreach (var k in new[] { "input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "output_tokens" })
            if (usage.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) t += n;
        return t;
    }

    // a stable 64-bit key: string.GetHashCode changes per process, and the cache outlives one
    private static ulong Key(string id)
    {
        var h = 14695981039346656037UL;
        foreach (var c in id) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    private sealed class Cache
    {
        public long WeekTicks;
        public DateTime ScannedUtc;
        public int FilesLeft;
        public Dictionary<string, long> Offsets = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<ulong, (long Raw, long Ticks)> Seen = new();
    }

    private static string CachePath(string dir) => Path.Combine(dir, "spend-cache.bin");

    private static Cache Load(string dir, long weekTicks)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(CachePath(dir)));
            if (r.ReadInt32() != 1) return new Cache { WeekTicks = weekTicks };
            var c = new Cache { WeekTicks = r.ReadInt64(), ScannedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc), FilesLeft = r.ReadInt32() };
            if (c.WeekTicks != weekTicks) return new Cache { WeekTicks = weekTicks };   // a new week starts from nothing
            for (var n = r.ReadInt32(); n > 0; n--) { var p = r.ReadString(); c.Offsets[p] = r.ReadInt64(); }
            for (var n = r.ReadInt32(); n > 0; n--) { var k = r.ReadUInt64(); c.Seen[k] = (r.ReadInt64(), r.ReadInt64()); }
            return c;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException or FormatException)
        {
            return new Cache { WeekTicks = weekTicks };
        }
    }

    private static void Save(string dir, Cache c)
    {
        Directory.CreateDirectory(dir);
        var tmp = CachePath(dir) + "." + Environment.ProcessId + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(1); w.Write(c.WeekTicks); w.Write(c.ScannedUtc.Ticks); w.Write(c.FilesLeft);
            w.Write(c.Offsets.Count);
            foreach (var (p, o) in c.Offsets) { w.Write(p); w.Write(o); }
            w.Write(c.Seen.Count);
            foreach (var (k, v) in c.Seen) { w.Write(k); w.Write(v.Raw); w.Write(v.Ticks); }
        }
        try { File.Move(tmp, CachePath(dir), overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { File.Delete(tmp); }   // another session's write wins
    }

    /// <summary>Reads a file's complete new lines into the cache. Returns false when the deadline stopped it.</summary>
    private static bool ReadNew(string path, long length, Cache c, DateTimeOffset weekStart, DateTime deadlineUtc)
    {
        var offset = c.Offsets.GetValueOrDefault(path);
        if (offset > length) offset = 0;   // the file was replaced: the per-message count keeps this honest
        if (offset == length) return true;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[1 << 20];
        var pending = new MemoryStream();
        var consumed = offset;
        var whole = true;
        for (int n; (n = fs.Read(buffer, 0, buffer.Length)) > 0;)
        {
            var from = 0;
            for (var i = Array.IndexOf(buffer, (byte)'\n', 0, n); i >= 0; i = i + 1 < n ? Array.IndexOf(buffer, (byte)'\n', i + 1, n - i - 1) : -1)
            {
                pending.Write(buffer, from, i - from);
                consumed += pending.Length + 1;
                OneLine(pending.GetBuffer().AsSpan(0, (int)pending.Length), c, weekStart);
                pending.SetLength(0);
                from = i + 1;
            }
            pending.Write(buffer, from, n - from);   // a line not yet ended stays unread until it is
            if (DateTime.UtcNow > deadlineUtc) { whole = false; break; }
        }
        c.Offsets[path] = consumed;
        return whole;
    }

    private static ReadOnlySpan<byte> UsageKey => "\"usage\""u8;

    private static void OneLine(ReadOnlySpan<byte> line, Cache c, DateTimeOffset weekStart)
    {
        if (line.IndexOf(UsageKey) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(line));
            var d = doc.RootElement;
            if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
            if (!m.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
            if (!m.TryGetProperty("id", out var idEl) || idEl.GetString() is not { Length: > 0 } id) return;
            if (!d.TryGetProperty("timestamp", out var ts) || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return;
            if (at < weekStart) return;
            var raw = RawOf(u);
            var key = Key(id);
            if (!c.Seen.TryGetValue(key, out var was) || raw > was.Raw) c.Seen[key] = (raw, was.Ticks > 0 ? was.Ticks : at.UtcTicks);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }   // a broken line counts nothing
    }

    /// <summary>The count now: the cache as it stands, brought up to date when it is older than <see cref="ScanEvery"/>
    /// (or still partial), reading for at most <paramref name="budget"/>. A file not read to its end counts in FilesLeft, so
    /// a cold start's line says it is partial.</summary>
    public static SpendCount Count(string projectsRoot, string cacheDir, DateTimeOffset now, DateTimeOffset? readingAt, TimeSpan budget)
    {
        var weekStart = WeekStart(now);
        var c = Load(cacheDir, weekStart.UtcTicks);
        if (now.UtcDateTime - c.ScannedUtc >= ScanEvery || c.FilesLeft > 0)
        {
            var deadline = DateTime.UtcNow + budget;
            var left = 0;
            if (Directory.Exists(projectsRoot))
                foreach (var f in new DirectoryInfo(projectsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                {
                    if (f.LastWriteTimeUtc < weekStart.UtcDateTime) continue;
                    if (DateTime.UtcNow > deadline) { if (c.Offsets.GetValueOrDefault(f.FullName) != f.Length) left++; continue; }
                    try { if (!ReadNew(f.FullName, f.Length, c, weekStart, deadline)) left++; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (File.Exists(f.FullName)) left++; }   // held: not whole, read next time; gone: nothing to count
                }
            c.FilesLeft = left;
            c.ScannedUtc = now.UtcDateTime;
            try { Save(cacheDir, c); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the line still stands, from memory
        }
        long raw = 0, since = 0;
        var fromTicks = readingAt?.UtcTicks ?? long.MaxValue;
        foreach (var v in c.Seen.Values) { raw += v.Raw; if (v.Ticks > fromTicks) since += v.Raw; }
        return new SpendCount(weekStart, raw, since, c.Seen.Count, c.FilesLeft);
    }

    /// <summary>Should a session's tool call say the line now? True at most once per <see cref="SayEvery"/> per session (a marker
    /// file's time; on a marker that cannot be written, false, so a failing disk never makes it say on every call).</summary>
    public static bool ShouldSay(string cacheDir, string? session, DateTime nowUtc)
    {
        try
        {
            var marker = Marker_(cacheDir, session);
            if (File.Exists(marker) && nowUtc - File.GetLastWriteTimeUtc(marker) < SayEvery) return false;
            Stamp(cacheDir, session, nowUtc);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The session's line was said now: the next tool call waits <see cref="SayEvery"/> from here.</summary>
    public static void Stamp(string cacheDir, string? session, DateTime nowUtc)
    {
        Directory.CreateDirectory(cacheDir);
        var marker = Marker_(cacheDir, session);
        File.WriteAllBytes(marker, Array.Empty<byte>());
        File.SetLastWriteTimeUtc(marker, nowUtc);
    }

    private static string Marker_(string cacheDir, string? session) =>
        Path.Combine(cacheDir, "said-" + Regex.Replace(session ?? "none", "[^A-Za-z0-9-]", "_"));

    public static string DefaultProjectsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    public static string DefaultCacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "spend");
}
