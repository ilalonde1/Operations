using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kor.Operations
{
    /// <summary>
    /// Keeps every window inside the screen it opens on. Several windows ask for more than a laptop has
    /// (BD Director is 1680 × 980; a 1920 × 1200 screen at 125% gives 1536 × 912 to work with), and
    /// Windows then centres them with the title bar above the top of the screen: no minimise,
    /// maximise or close button to reach. Registered once at startup for every <see cref="Window"/>,
    /// so a new window cannot reintroduce it.
    /// </summary>
    public static class WindowFit
    {
        private static bool _registered;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Fitted = new();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            // A Loaded class handler is never called for Window subclasses (WPF only broadcasts Loaded to elements
            // with instance handlers), so this hangs off the first SizeChanged instead, which every window raises
            // on its first layout. Once per window: a user's own resize is never fought. Queued so that
            // WindowStartupLocation has written Left/Top first.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((s, e) =>
            {
                if (s is not Window w || Fitted.TryGetValue(w, out var seen)) return;
                Fitted.Add(w, new object());
                w.Dispatcher.BeginInvoke(new Action(() => Fit(w)), System.Windows.Threading.DispatcherPriority.Loaded);
            }));
        }

        /// <summary>Shrinks the window to the work area of its monitor if it is larger, then moves it fully onto it.</summary>
        public static void Fit(Window w)
        {
            if (w.WindowState != WindowState.Normal) return;
            var area = WorkAreaOf(w);
            if (area.IsEmpty) return;
            Fit(w, area);
        }

        /// <summary>The arithmetic, separated so it can be tested without a monitor.</summary>
        public static void Fit(Window w, Rect area)
        {
            if (w.MinWidth > area.Width) w.MinWidth = area.Width;
            if (w.MinHeight > area.Height) w.MinHeight = area.Height;
            var width = double.IsNaN(w.Width) ? w.ActualWidth : w.Width;
            var height = double.IsNaN(w.Height) ? w.ActualHeight : w.Height;
            if (width > area.Width) { w.Width = area.Width; width = area.Width; }
            if (height > area.Height) { w.Height = area.Height; height = area.Height; }
            if (double.IsNaN(w.Left) || double.IsNaN(w.Top)) return;
            if (w.Left + width > area.Right) w.Left = area.Right - width;
            if (w.Top + height > area.Bottom) w.Top = area.Bottom - height;
            if (w.Left < area.Left) w.Left = area.Left;
            if (w.Top < area.Top) w.Top = area.Top;
        }

        /// <summary>The work area of the monitor the window is mostly on, in WPF units (DIPs).</summary>
        public static Rect WorkAreaOf(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                var source = PresentationSource.FromVisual(w);
                if (hwnd == IntPtr.Zero || source?.CompositionTarget is null) return SystemParameters.WorkArea;
                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
                var fromDevice = source.CompositionTarget.TransformFromDevice;
                var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
                return new Rect(topLeft, bottomRight);
            }
            catch (Exception)
            {
                return SystemParameters.WorkArea;
            }
        }

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
