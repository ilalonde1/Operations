using System;
using System.Threading;
using System.Windows;
using Kor.Operations;
using Xunit;

namespace Kor.Operations.App.Tests;

/// <summary>
/// A window larger than the screen used to open with its title bar above the top edge (BD Director, 1680 × 980,
/// on a 1536 × 912 work area). Covers: shrinking to the work area, lowering a MinWidth/MinHeight that would
/// otherwise block the shrink, moving fully on screen, and leaving a window that already fits on a second
/// monitor where it is. Does NOT cover: the per-monitor DPI lookup itself (needs a real monitor) or windows
/// that are maximised on open (left alone by design).
/// </summary>
public sealed class WindowFitTests
{
    [Fact]
    public void AWindowBiggerThanTheScreenShrinksAndItsTitleBarComesBackOnScreen()
    {
        RunSta(() =>
        {
            var w = new Window { Width = 1680, Height = 980, MinWidth = 1100, MinHeight = 720, Left = -72, Top = -34 };
            WindowFit.Fit(w, new Rect(0, 0, 1536, 912));
            Assert.Equal(1536, w.Width);
            Assert.Equal(912, w.Height);
            Assert.Equal(0, w.Left);
            Assert.Equal(0, w.Top);
        });
    }

    [Fact]
    public void AMinimumSizeLargerThanTheScreenIsLoweredSoTheShrinkTakes()
    {
        RunSta(() =>
        {
            var w = new Window { Width = 1680, Height = 940, MinWidth = 1600, MinHeight = 820, Left = 0, Top = 0 };
            WindowFit.Fit(w, new Rect(0, 0, 1366, 728));
            Assert.Equal(1366, w.Width);
            Assert.Equal(728, w.Height);
            Assert.True(w.MinHeight <= 728);
        });
    }

    [Fact]
    public void AWindowThatFitsOnASecondMonitorIsLeftWhereItIs()
    {
        RunSta(() =>
        {
            var w = new Window { Width = 1200, Height = 800, Left = 2000, Top = 100 };
            WindowFit.Fit(w, new Rect(1920, 0, 1920, 1040));
            Assert.Equal(1200, w.Width);
            Assert.Equal(800, w.Height);
            Assert.Equal(2000, w.Left);
            Assert.Equal(100, w.Top);
        });
    }

    [Fact]
    public void ARegisteredOversizedWindowOpensWithinThisMachinesWorkArea()
    {
        RunSta(() =>
        {
            WindowFit.Register();
            var w = new Window
            {
                Width = SystemParameters.WorkArea.Width + 300,
                Height = SystemParameters.WorkArea.Height + 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            try
            {
                w.Show();
                w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                // The monitor the window landed on, which on a multi-monitor desk need not be the primary.
                var area = WindowFit.WorkAreaOf(w);
                var bounds = new Rect(w.Left, w.Top, w.Width, w.Height);
                Assert.True(area.Contains(new Rect(bounds.X + 0.5, bounds.Y + 0.5, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1))),
                    $"window {bounds} not inside its monitor's work area {area}");
            }
            finally
            {
                w.Close();
            }
        });
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
