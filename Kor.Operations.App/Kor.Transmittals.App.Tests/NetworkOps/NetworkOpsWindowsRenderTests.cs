#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// Builds both NetworkOps windows on a fleet and renders them to PNGs a person can open and look at
/// (%TEMP%\kor-networkops-screens). The pattern is IntakeScreensRenderTests'.
///
/// WHAT IT COVERS: that each window constructs, that every StaticResource it names resolves, and that
/// it lays out non-blank with data bound -- the fixture fleet always, and the real one through the API
/// only when KOR_NETWORKOPS_RENDER_LIVE=1 is set (it signs in, so never during an ordinary test run).
/// WHAT IT DOES NOT COVER: whether it looks GOOD (that is what the PNGs are for), anything behind
/// Loaded (the windows are never shown, so no timer starts and no check is queued), or interaction.
/// A same-class fault it would NOT catch: text painted in the colour of its background renders,
/// is non-blank, and is invisible.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class NetworkOpsWindowsRenderTests
{
    [Fact]
    public void Both_windows_build_and_render()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "kor-networkops-screens");
        Directory.CreateDirectory(outputDirectory);
        Exception? failure = null;
        var written = 0;

        // WPF demands STA and xUnit runs MTA, so the whole thing goes on its own thread.
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });

                written += RenderFleet(NetworkOpsViewModelTests.Fleet(), NetworkOpsClient.Unconfigured("fixture"), outputDirectory, "fixture");

                // The real fleet, when this machine can read it: the fixture shows the layout works,
                // only real data shows whether it reads well at 38 PCs and real finding text.
                var real = NetworkOpsClient.FromAppConfig();
                if (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_RENDER_LIVE") == "1" && real.IsConfigured)
                    written += RenderFleet(real.GetFleetAsync(CancellationToken.None).GetAwaiter().GetResult(), real, outputDirectory, "live");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Rendering the NetworkOps windows did not finish within two minutes.");
        Assert.True(failure is null, $"A NetworkOps window failed to build or render: {failure}");
        Assert.True(written >= 2, $"Expected at least 2 renders, got {written}.");
    }

    private static int RenderFleet(FleetSnapshot snapshot, NetworkOpsClient reader, string dir, string label)
    {
        var center = new NetworkOpsCommandCenterViewModel(reader);
        center.Apply(snapshot, DateTime.UtcNow);
        var written = Render(new NetworkOpsCommandCenterWindow(center), Path.Combine(dir, $"{label}-fleet.png"));

        // The PC window on the worst PC: the one with the most to explain.
        var worst = center.Fleet.First();
        var device = new NetworkOpsDeviceViewModel(reader, snapshot, worst.Device);
        if (reader.IsConfigured) device.LoadHistoryAsync(CancellationToken.None).GetAwaiter().GetResult();
        written += Render(new NetworkOpsDeviceWindow(device), Path.Combine(dir, $"{label}-pc-{worst.Name}.png"));
        return written;
    }

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Measure/Arrange/Render the content rather than Show: showing raises Loaded, which starts the refresh timer.</summary>
    private static int Render(Window window, string path)
    {
        var root = window.Content as FrameworkElement
                   ?? throw new InvalidOperationException($"{window.GetType().Name} has no FrameworkElement content to render.");
        var size = new Size(window.Width, window.Height);
        // A DataGrid sizes its star columns in work it QUEUES on the dispatcher once it knows its
        // viewport; a never-shown window has no running dispatcher, so without pumping the queue those
        // columns render collapsed -- which a shown window never does. Lay out, drain, lay out again.
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            DrainDispatcher();
        }

        var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Assert.True(new FileInfo(path).Length > 10_000, $"{path} is suspiciously small: the window may have rendered blank.");
        return 1;
    }
}
