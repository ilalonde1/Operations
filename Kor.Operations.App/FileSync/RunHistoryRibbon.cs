#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kor.Operations.App.FileSync;

// Horizontal 24-hour timeline of every JobRun across every job. Each dot is
// a real Rectangle child (not a draw call) so we get free hit testing and
// tooltips. Clicks bubble out via the RunClicked event so the host window
// can decide what to open. Custom-rolled instead of pulling in a chart lib.
public sealed class RunHistoryRibbon : Canvas
{
    public static readonly DependencyProperty RunsProperty =
        DependencyProperty.Register(
            nameof(Runs),
            typeof(IReadOnlyList<JobRunRow>),
            typeof(RunHistoryRibbon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRunsChanged));

    public IReadOnlyList<JobRunRow>? Runs
    {
        get => (IReadOnlyList<JobRunRow>?)GetValue(RunsProperty);
        set => SetValue(RunsProperty, value);
    }

    public event Action<JobRunRow>? RunClicked;

    private static readonly SolidColorBrush SuccessBrush;
    private static readonly SolidColorBrush FailedBrush;
    private static readonly SolidColorBrush RunningBrush;
    private static readonly SolidColorBrush CancelledBrush;
    private static readonly SolidColorBrush UnknownBrush;
    private static readonly SolidColorBrush GridBrush;
    private static readonly SolidColorBrush AxisLabelBrush;
    private static readonly Pen GridPen;
    private static readonly Typeface AxisTypeface = new("Segoe UI");

    static RunHistoryRibbon()
    {
        SuccessBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
        FailedBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));
        RunningBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));
        CancelledBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
        UnknownBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));
        GridBrush      = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)));
        AxisLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
        GridPen        = FreezePen(new Pen(GridBrush, 1));
    }

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public RunHistoryRibbon()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        SizeChanged += (_, _) => Rebuild();
    }

    private static void OnRunsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RunHistoryRibbon ribbon) return;
        if (e.OldValue is INotifyCollectionChanged oldNcc)
            oldNcc.CollectionChanged -= ribbon.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNcc)
            newNcc.CollectionChanged += ribbon.OnCollectionChanged;
        ribbon.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Rebuild();
        else Dispatcher.Invoke(Rebuild);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawGridAndLabels(dc);
    }

    private void DrawGridAndLabels(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // 6 vertical gridlines at every 4h, plus one at right edge for "now".
        for (int i = 0; i <= 6; i++)
        {
            var x = w * i / 6.0;
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
        }

        // Labels along the bottom: -24h, -20h, ..., -4h, now.
        var labels = new[] { "-24h", "-20h", "-16h", "-12h", "-8h", "-4h", "now" };
        for (int i = 0; i < labels.Length; i++)
        {
            var x = w * i / 6.0;
            var ft = new FormattedText(
                labels[i],
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                AxisTypeface,
                10,
                AxisLabelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            // Centre the label under the gridline; shift the right edge so
            // "now" doesn't get clipped.
            var lx = i == labels.Length - 1 ? x - ft.Width : x - (ft.Width / 2);
            if (i == 0) lx = x;
            dc.DrawText(ft, new Point(lx, h - ft.Height - 1));
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        InvalidateVisual();

        var runs = Runs;
        if (runs is null || runs.Count == 0) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var now = DateTimeOffset.Now;
        var windowStart = now.AddHours(-24);
        const double dotSize = 8;
        // Lane is the top portion; bottom ~14px is reserved for axis labels.
        var laneHeight = Math.Max(8, h - 16);
        var laneCenter = laneHeight / 2.0;

        foreach (var run in runs)
        {
            if (run.StartedAt < windowStart) continue;
            var fraction = (run.StartedAt - windowStart).TotalHours / 24.0;
            var x = fraction * w - (dotSize / 2.0);
            var y = laneCenter - (dotSize / 2.0);

            var rect = new Rectangle
            {
                Width = dotSize,
                Height = dotSize,
                RadiusX = dotSize / 2,
                RadiusY = dotSize / 2,
                Fill = BrushFor(run.Status),
                Cursor = Cursors.Hand,
                Tag = run,
                ToolTip = BuildTooltip(run),
            };
            rect.MouseLeftButtonUp += OnRectClicked;
            SetLeft(rect, x);
            SetTop(rect, y);
            Children.Add(rect);
        }
    }

    private void OnRectClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is Rectangle r && r.Tag is JobRunRow run)
            RunClicked?.Invoke(run);
    }

    private static Brush BrushFor(string status) => status switch
    {
        "Success"   => SuccessBrush,
        "Failed"    => FailedBrush,
        "TimedOut"  => FailedBrush,
        "Running"   => RunningBrush,
        "Cancelled" => CancelledBrush,
        _           => UnknownBrush,
    };

    private static string BuildTooltip(JobRunRow run)
    {
        var dur = run.Duration.HasValue
            ? (run.Duration.Value.TotalSeconds < 60
                ? $"{run.Duration.Value.TotalSeconds:0.0}s"
                : $"{(int)run.Duration.Value.TotalMinutes}m {run.Duration.Value.Seconds}s")
            : "running";
        var summary = string.IsNullOrEmpty(run.Summary) ? string.Empty : $"\n{run.Summary}";
        return $"{run.JobName} [{run.Mode}]\n{run.StartedAt:HH:mm:ss} on {run.HostName}\n{run.Status} · {dur}{summary}";
    }
}
