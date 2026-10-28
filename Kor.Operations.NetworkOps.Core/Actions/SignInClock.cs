using System.Text.Json;

namespace Kor.Operations.NetworkOps.Core.Actions;

/// <summary>
/// When the NetworkOps sign-in will need MFA again, so a session warns hours ahead instead of losing the fleet mid-run
/// (28 Oct ~13:00 it lapsed mid-afternoon and blocked every session's netops calls; Ian chose this early warning).
///
/// WHY A FILE: Conditional Access "NetworkOps API - require MFA" (this app only) asks for MFA again every 12 h -- Entra
/// refused a silent refresh with AADSTS70043 "sign-in frequency ... 43200" on 29 Sep -- but no token says when the last
/// MFA was: the access token and the cached ID token carry no auth_time (read 28 Oct 13:5x: ID token claims aud, exp,
/// iat, iss, name, nbf, oid, preferred_username, rh, sid, sub, tid, uti, ver). So each road that completes an
/// INTERACTIVE NetworkOps sign-in records the moment here: %LOCALAPPDATA%\KOR\netops\last-mfa.json, {"at","via"}. No
/// token, no account id, no secret. It never changes any sign-in or MFA setting.
///
/// COVERS: interactive sign-ins through the netops CLI (AppServer) and the KOR Operations app (NetworkOpsClient), on
/// this PC, for this Windows user; the lapse = last MFA + 12 h; a warning from 2 h before.
/// DOES NOT COVER: an MFA done anywhere else (a browser, another PC, an app build older than this change) -- the file
/// then holds an OLDER time, so the warning comes EARLY, never late; the policy's frequency being changed in Entra (12 h
/// is a constant here, KOR_NETOPS_SIGNIN_HOURS overrides it); a revoked session or a password change, which ends the
/// sign-in at once whatever the clock says.
/// SAME-CLASS FAULT IT WOULD MISS: Entra ending the session early for a reason of its own (risk policy, token
/// revocation) -- the clock says hours left while every call already exits 4.
/// </summary>
public static class SignInClock
{
    public static readonly TimeSpan DefaultFrequency = TimeSpan.FromHours(12);
    public static readonly TimeSpan DefaultWarnBefore = TimeSpan.FromHours(2);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "netops", "last-mfa.json");

    public sealed record Mark(DateTimeOffset At, string Via);

    public enum State { Unknown, Ok, Soon, Lapsed }

    public sealed record Reading(State State, string Line, DateTimeOffset? LapsesAt);

    public static TimeSpan Frequency =>
        double.TryParse(Environment.GetEnvironmentVariable("KOR_NETOPS_SIGNIN_HOURS"), out var h) && h > 0 ? TimeSpan.FromHours(h) : DefaultFrequency;

    /// <summary>Best effort: a failure to write never breaks the sign-in that called it.</summary>
    public static void Record(string via, DateTimeOffset? at = null, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { at = (at ?? DateTimeOffset.Now).ToString("o"), via }));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static Mark? Read(string? path = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path ?? DefaultPath));
            return DateTimeOffset.TryParse(doc.RootElement.GetProperty("at").GetString(), out var at)
                ? new Mark(at, doc.RootElement.TryGetProperty("via", out var v) ? v.GetString() ?? "?" : "?")
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string At(DateTimeOffset t) => $"{Health.Dates.Day(t.LocalDateTime)} {Health.Dates.Clock(t.LocalDateTime)}";

    /// <summary>Pure: the state and the one line to show, judged at <paramref name="now"/>.</summary>
    public static Reading Judge(Mark? mark, DateTimeOffset now, TimeSpan? frequency = null, TimeSpan? warnBefore = null)
    {
        if (mark is null)
            return new(State.Unknown, "NetworkOps sign-in: last MFA not known on this PC (nothing recorded yet); the next interactive sign-in starts the clock.", null);
        var lapses = mark.At + (frequency ?? Frequency);
        var left = lapses - now;
        var when = $"MFA {At(mark.At)} via {mark.Via}";
        const string renew = "renew when it suits you: `netops token` in a terminal on this PC, or open the KOR Operations app";
        if (left <= TimeSpan.Zero)
            return new(State.Lapsed, $"NetworkOps sign-in lapsed at {At(lapses)} ({when}): every netops call exits 4 until Ian signs in. To {renew}.", lapses);
        var text = $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return left <= (warnBefore ?? DefaultWarnBefore)
            ? new(State.Soon, $"NetworkOps sign-in lapses at {At(lapses)}, in {text} ({when}). A run that crosses it loses the fleet: {renew}.", lapses)
            : new(State.Ok, $"NetworkOps sign-in good until {At(lapses)}, {text} left ({when}).", lapses);
    }
}
