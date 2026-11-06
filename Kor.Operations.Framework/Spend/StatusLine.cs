using System.Globalization;
using System.Text.Json;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Spend;

/// <summary>A real reading of the weekly limit, as Claude Code gave it to the status line.</summary>
public sealed record UsageReading(double Percent, DateTimeOffset At, DateTimeOffset? ResetsAt, int? EventId = null);

/// <summary>What the status line needs from Claude Code's JSON. A field it did not send is null.</summary>
public sealed record StatusInput(string? Model, double? ContextPercent, double? WeekPercent, long? WeekResetsAt, double? FiveHourPercent, long? FiveHourResetsAt, string? Session);

/// <summary>Where a reading lives in brain.Event. CK_Event_Kind has no kind for this and a new one would need a migration, so a reading is
/// Kind 'board', which no code reads as an event and no row used before this (read 6 Nov), in a Module of its own, so that it never
/// pushes a module's LAST EVENTS off its start page. Session stays NULL for the same reason: the turn capture reads a session's events.
/// The session is in Detail.</summary>
public static class StatusLineNames
{
    public const string Module = "Usage";
    public const string Kind = "board";
    public const string Ref = "usage-reading";
    public const string Actor = "kor statusline";
}

/// <summary>
/// Board row 1892: the real percent, which Claude Code hands its status line (docs, statusline: rate_limits.seven_day.used_percentage
/// and resets_at, for claude.ai subscribers, after the session's first API response). <c>kor statusline</c> prints one short line and
/// stores a reading at most once per <see cref="StoreEvery"/>, per PC. A status line runs often and must never slow or break a
/// session, so the verb prints first, touches the DB only for a reading that is due, and swallows every failure.
///
/// DOES NOT COVER: a session that has gone idle sends the percent of its own last response, so with several sessions open the newest
/// row can be older than its time says and read lower than the truth; other surfaces show only when a session next gets a response.
/// Same-class fault it would miss: Claude Code renaming the field. The line loses its week and nothing is stored, without an error.
/// </summary>
public static class StatusLine
{
    public static readonly TimeSpan StoreEvery = TimeSpan.FromMinutes(5);

    private static JsonElement? Child(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    private static double? Num(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var d) ? d : null;
    private static long? Long(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;
    private static string? Str(JsonElement? e) => e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    /// <summary>The fields, any of them absent. Throws on text that is not one JSON object.</summary>
    public static StatusInput Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("the status line got no JSON on stdin");
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        if (d.ValueKind != JsonValueKind.Object) throw new FormatException($"the status line's JSON is a {d.ValueKind}, not an object");
        JsonElement? limits = Child(d, "rate_limits"), week = limits is { } l ? Child(l, "seven_day") : null, five = limits is { } m ? Child(m, "five_hour") : null;
        return new StatusInput(
            d.TryGetProperty("model", out var model) ? Str(Child(model, "display_name")) ?? Str(Child(model, "id")) : null,
            d.TryGetProperty("context_window", out var ctx) ? Num(Child(ctx, "used_percentage")) : null,
            week is { } w ? Num(Child(w, "used_percentage")) : null, week is { } w2 ? Long(Child(w2, "resets_at")) : null,
            five is { } f ? Num(Child(f, "used_percentage")) : null, five is { } f2 ? Long(Child(f2, "resets_at")) : null,
            Str(Child(d, "session_id")));
    }

    private static string Pct(double d) => d.ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The one line the session shows.</summary>
    public static string Text(StatusInput s)
    {
        var parts = new List<string>();
        if (s.WeekPercent is { } w) parts.Add("week " + Pct(w));
        if (s.FiveHourPercent is { } f) parts.Add("5h " + Pct(f));
        if (s.ContextPercent is { } c) parts.Add("ctx " + Pct(c));
        var model = s.Model is { Length: > 0 } m ? $"[{(m.Length <= 30 ? m : m[..30])}]" : "[kor]";
        return parts.Count == 0 ? model : model + " " + string.Join(" | ", parts);
    }

    /// <summary>Has this input a reading worth keeping: a weekly percent, in a window that has not already reset.</summary>
    public static bool HasReading(StatusInput s, DateTimeOffset now) =>
        s.WeekPercent is >= 0 && (s.WeekResetsAt is not { } r || r > now.ToUnixTimeSeconds());

    /// <summary>Does this reading still speak for now? Past its reset it does not; with no reset named, this week's start decides.</summary>
    public static bool Current(UsageReading r, DateTimeOffset weekStart, DateTimeOffset now) =>
        r.ResetsAt is { } at ? at > now : r.At >= weekStart;

    /// <summary>Writes one reading; returns its brain.Event id.</summary>
    public static int Store(StatusInput s, BrainDb db)
    {
        var detail = JsonSerializer.Serialize(new
        {
            seven_day = new { used_percentage = s.WeekPercent, resets_at = s.WeekResetsAt },
            five_hour = new { used_percentage = s.FiveHourPercent, resets_at = s.FiveHourResetsAt },
            session = s.Session,
            model = s.Model,
        });
        return BrainStore.Insert(new BrainEvent(StatusLineNames.Module, StatusLineNames.Kind, null, null, StatusLineNames.Ref, null, detail, StatusLineNames.Actor), db);
    }

    /// <summary>The newest stored reading, or null when none is recorded or the newest cannot be read as one.</summary>
    public static UsageReading? Newest(BrainDb db)
    {
        using var c = db.Command("SELECT TOP (1) Id, AtUtc, Detail FROM brain.Event WHERE Module = @m AND Kind = @k AND Ref = @r ORDER BY Id DESC", 3);
        c.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = StatusLineNames.Module;
        c.Parameters.Add("@k", System.Data.SqlDbType.NVarChar, 16).Value = StatusLineNames.Kind;
        c.Parameters.Add("@r", System.Data.SqlDbType.NVarChar, 400).Value = StatusLineNames.Ref;
        using var r = c.ExecuteReader();
        if (!r.Read() || r.IsDBNull(2)) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.GetString(2));
            var week = Child(doc.RootElement, "seven_day");
            if (week is not { } w || Num(Child(w, "used_percentage")) is not { } percent) return null;
            var resets = Long(Child(w, "resets_at"));
            return new UsageReading(percent, r.GetDateTimeOffset(1), resets is { } u ? DateTimeOffset.FromUnixTimeSeconds(u) : null, r.GetInt32(0));
        }
        catch (Exception e) when (e is JsonException or ArgumentOutOfRangeException) { return null; }
    }
}
