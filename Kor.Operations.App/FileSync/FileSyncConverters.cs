#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kor.Operations.App.FileSync;

// Status (string) -> SolidColorBrush. Used for the colored chip in the Jobs grid.
public sealed class JobStatusToBrushConverter : IValueConverter
{
    public static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22));
    public static readonly Brush Failed = new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E));
    public static readonly Brush Running = new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00));
    public static readonly Brush Cancelled = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    public static readonly Brush Unknown = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "Success" => Success,
            "Failed" => Failed,
            "TimedOut" => Failed,
            "Running" => Running,
            "Cancelled" => Cancelled,
            _ => Unknown,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Friendly status label. "(never)" when null so the cell is never blank.
public sealed class JobStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value as string ?? "(never)";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// TimeSpan? -> "4h ago" / "in 3d 14h" / "(never)". Negative = elapsed, positive = future.
// Source value is the SinceLastRun / UntilNextFire computed property on JobRow.
public sealed class TimeSpanToHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "(never)";
        if (value is not TimeSpan ts) return string.Empty;

        var future = parameter as string == "future";
        var abs = ts.Duration();
        var label = abs.TotalSeconds < 60 ? "just now"
            : abs.TotalMinutes < 60 ? $"{(int)abs.TotalMinutes}m"
            : abs.TotalHours < 24 ? $"{(int)abs.TotalHours}h {abs.Minutes}m"
            : abs.TotalDays < 7 ? $"{(int)abs.TotalDays}d {abs.Hours}h"
            : $"{(int)(abs.TotalDays / 7)}w {(int)(abs.TotalDays % 7)}d";

        if (abs.TotalSeconds < 60) return label;
        return future ? $"in {label}" : $"{label} ago";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// int (collection count) -> Visibility. Used to hide the Pending-Triggers
// panel when the queue is empty. WPF's built-in BooleanToVisibilityConverter
// can't take an int, so we ship a tiny one.
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0,
        };
        return count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// bool -> "On" / "Off" so the Enabled column is a glance-readable word, not "True"/"False".
public sealed class BoolToOnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "On" : "Off";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// HeartbeatRow.HealthStatus ("Live"/"Stale"/"Down") -> SolidColorBrush for
// the heartbeat panel's status chip. Three-tier signal beats a boolean
// "stale: True/False" cell at a glance.
public sealed class HeartbeatHealthToBrushConverter : IValueConverter
{
    public static readonly Brush Live  = new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22));
    public static readonly Brush Stale = new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00));
    public static readonly Brush Down  = new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "Live"  => Live,
            "Stale" => Stale,
            "Down"  => Down,
            _       => Down,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Quartz cron string ("0 30 23 * * ?") -> human-readable schedule
// ("Daily at 11:30 PM"). Handles the patterns we actually use today;
// anything more exotic falls back to the raw cron so it stays inspectable.
public sealed class CronToHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var cron = value as string;
        if (string.IsNullOrWhiteSpace(cron)) return "(manual)";
        return CronToHuman(cron);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string CronToHuman(string cron)
    {
        // Quartz 6-field: sec min hour day-of-month month day-of-week
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6) return cron;

        if (!int.TryParse(parts[0], out var sec)) return cron;
        if (!int.TryParse(parts[1], out var min)) return cron;
        if (!int.TryParse(parts[2], out var hr)) return cron;
        var dom = parts[3];
        var mon = parts[4];
        var dow = parts[5];

        var time = FormatTime(hr, min, sec);

        bool everyDay = (dom is "*" or "?") && mon == "*" && (dow is "*" or "?");
        if (everyDay) return $"Daily at {time}";

        bool weekly = (dom is "?" or "*") && mon == "*" && dow != "*" && dow != "?";
        if (weekly)
        {
            var dayLabel = DayOfWeekLabel(dow);
            return dayLabel is null ? cron : $"{dayLabel} at {time}";
        }

        bool monthly = int.TryParse(dom, out var domDay) && mon == "*" && (dow is "?" or "*");
        if (monthly) return $"Monthly on the {Ordinal(domDay)} at {time}";

        return cron;
    }

    private static string FormatTime(int hr, int min, int sec)
    {
        var suffix = hr < 12 ? "AM" : "PM";
        var h12 = hr % 12 == 0 ? 12 : hr % 12;
        var s = sec > 0 ? $":{sec:D2}" : string.Empty;
        return $"{h12}:{min:D2}{s} {suffix}";
    }

    private static string? DayOfWeekLabel(string dow)
    {
        // Quartz accepts SUN/MON/TUE/WED/THU/FRI/SAT or 1-7 (1=SUN). We
        // only see name forms in the seed; numeric falls through to raw.
        return dow.ToUpperInvariant() switch
        {
            "SUN" => "Sundays",
            "MON" => "Mondays",
            "TUE" => "Tuesdays",
            "WED" => "Wednesdays",
            "THU" => "Thursdays",
            "FRI" => "Fridays",
            "SAT" => "Saturdays",
            "MON-FRI" => "Weekdays",
            _ => null,
        };
    }

    private static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        },
    };
}

// Serilog 3-letter level (INF/WRN/ERR/FTL/DBG/VRB) -> SolidColorBrush for the
// Log Viewer's level chip. Mirrors the JobStatus brush palette so colours are
// consistent across the Command Center.
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public static readonly Brush Verbose = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
    public static readonly Brush Debug   = new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1));
    public static readonly Brush Info    = new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22));
    public static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00));
    public static readonly Brush Error   = new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E));
    public static readonly Brush Fatal   = new SolidColorBrush(Color.FromRgb(0x60, 0x10, 0x10));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "VRB" => Verbose,
            "DBG" => Debug,
            "INF" => Info,
            "WRN" => Warning,
            "ERR" => Error,
            "FTL" => Fatal,
            _ => Verbose,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
