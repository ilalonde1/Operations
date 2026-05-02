#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Kor.Operations.App.FileSync;

// Per-minute stacked-bar volume chart for the Log Viewer. Custom-rendered
// (FrameworkElement.OnRender + DrawingContext) so we don't pull in a chart
// lib for one widget.
//
// Bins the bound log lines into 60 fixed buckets covering [min..max] of the
// observed timestamps; each bucket is a stacked bar (DBG -> INF -> WRN -> ERR
// -> FTL bottom-up) sized to the max observed bucket count. Re-renders on
// CollectionChanged so it tracks the live tail.
public sealed class LogVolumeSparkline : FrameworkElement
{
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines),
        typeof(IEnumerable<FileSyncLogLine>),
        typeof(LogVolumeSparkline),
        new PropertyMetadata(null, OnLinesChanged));

    private const int BucketCount = 60;
    private const double BarGapPx = 1.0;
    private const double AxisStripPx = 14.0;

    private static readonly SolidColorBrush BackgroundFill = new(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
    private static readonly SolidColorBrush AxisInk = new(Color.FromArgb(0x88, 0x60, 0x60, 0x60));

    private static readonly Brush[] LevelBrushes =
    {
        LogLevelToBrushConverter.Verbose, // VRB (sev 0)
        LogLevelToBrushConverter.Debug,   // DBG (sev 1)
        LogLevelToBrushConverter.Info,    // INF (sev 2)
        LogLevelToBrushConverter.Warning, // WRN (sev 3)
        LogLevelToBrushConverter.Error,   // ERR (sev 4)
        LogLevelToBrushConverter.Fatal,   // FTL (sev 5)
    };

    static LogVolumeSparkline()
    {
        BackgroundFill.Freeze();
        AxisInk.Freeze();
        foreach (Brush b in LevelBrushes) b.Freeze();
    }

    public IEnumerable<FileSyncLogLine>? Lines
    {
        get => (IEnumerable<FileSyncLogLine>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public string Subtitle { get; set; } = string.Empty;

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LogVolumeSparkline self) return;
        if (e.OldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= self.OnSourceChanged;
        if (e.NewValue is INotifyCollectionChanged newNotify)
            newNotify.CollectionChanged += self.OnSourceChanged;
        self.InvalidateVisual();
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // CollectionChanged can fire from a worker thread (the tailer runs
        // on Task.Run). Marshal back to the UI thread before invalidating.
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke((Action)InvalidateVisual);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var chartH = Math.Max(0, h - AxisStripPx);
        dc.DrawRectangle(BackgroundFill, null, new Rect(0, 0, w, chartH));

        var lines = Lines?.ToList();
        if (lines is null || lines.Count == 0)
        {
            dc.DrawText(BuildAxisLabel("(no data)"), new Point(4, chartH));
            return;
        }

        DateTimeOffset min = lines[0].Timestamp;
        DateTimeOffset max = lines[0].Timestamp;
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Timestamp < min) min = lines[i].Timestamp;
            if (lines[i].Timestamp > max) max = lines[i].Timestamp;
        }
        var span = max - min;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromMinutes(1);

        var buckets = new int[BucketCount, LevelBrushes.Length];
        var spanTicks = span.Ticks;
        foreach (var line in lines)
        {
            var offsetTicks = (line.Timestamp - min).Ticks;
            var idx = (int)Math.Min(BucketCount - 1, offsetTicks * BucketCount / Math.Max(1L, spanTicks));
            var sev = Math.Clamp(line.Severity, 0, LevelBrushes.Length - 1);
            buckets[idx, sev]++;
        }

        int peak = 1;
        for (int i = 0; i < BucketCount; i++)
        {
            int total = 0;
            for (int s = 0; s < LevelBrushes.Length; s++) total += buckets[i, s];
            if (total > peak) peak = total;
        }

        var barW = Math.Max(1.0, (w - BarGapPx * (BucketCount - 1)) / BucketCount);

        for (int i = 0; i < BucketCount; i++)
        {
            var x = i * (barW + BarGapPx);
            double yBottom = chartH;

            for (int s = 0; s < LevelBrushes.Length; s++)
            {
                var c = buckets[i, s];
                if (c == 0) continue;
                var segH = (c / (double)peak) * chartH;
                yBottom -= segH;
                dc.DrawRectangle(LevelBrushes[s], null, new Rect(x, yBottom, barW, segH));
            }
        }

        // Axis labels: leftmost = oldest, rightmost = newest, center = span.
        var leftLabel = BuildAxisLabel(min.LocalDateTime.ToString("HH:mm:ss"));
        var rightLabel = BuildAxisLabel(max.LocalDateTime.ToString("HH:mm:ss"));
        var spanLabel = BuildAxisLabel(FormatSpan(span));
        dc.DrawText(leftLabel, new Point(2, chartH));
        dc.DrawText(rightLabel, new Point(w - rightLabel.Width - 2, chartH));
        dc.DrawText(spanLabel, new Point((w - spanLabel.Width) / 2, chartH));

        // Faint peak guide line so users have context for the bar height.
        dc.DrawLine(new Pen(AxisInk, 0.5), new Point(0, 0), new Point(w, 0));
    }

    private static FormattedText BuildAxisLabel(string text) => new(
        text,
        System.Globalization.CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface("Cascadia Mono, Consolas"),
        10.5,
        AxisInk,
        VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip);

    private static string FormatSpan(TimeSpan ts) =>
        ts.TotalSeconds < 60 ? $"{(int)ts.TotalSeconds}s span"
        : ts.TotalMinutes < 60 ? $"{ts.TotalMinutes:F1}m span"
        : ts.TotalHours < 24 ? $"{ts.TotalHours:F1}h span"
        : $"{ts.TotalDays:F1}d span";
}
