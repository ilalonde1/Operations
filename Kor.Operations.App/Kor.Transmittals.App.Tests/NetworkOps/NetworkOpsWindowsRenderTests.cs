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

    /// <summary>Every rack device's real readings of 2026-10-02 evening (the fixture RackComponentsTests reads).</summary>
    private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Kor.Operations.NetworkOps.Core.Rack.DeviceReading>> RealRackReadings()
        => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Kor.Operations.NetworkOps.Core.Rack.DeviceReading>>>(
               File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Operations.NetworkOps.Tests", "Fixtures", "rack", "readings-2026-10-02.json")),
               new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private static int RenderFleet(FleetSnapshot snapshot, NetworkOpsClient reader, string dir, string label)
    {
        var center = new NetworkOpsCommandCenterViewModel(reader);
        center.Apply(snapshot, DateTime.UtcNow);
        center.ApplyPower(reader.IsConfigured && label == "live"
            ? reader.GetPowerAsync(CancellationToken.None).GetAwaiter().GetResult()
            : NetworkOpsPowerCardTests.OneOnBattery(), DateTime.UtcNow);
        var rack = reader.IsConfigured && label == "live" ? reader.GetRackAsync(CancellationToken.None).GetAwaiter().GetResult() : NetworkOpsRackTests.Rack(DateTime.UtcNow);
        center.ApplyRack(rack, DateTime.UtcNow);
        var written = Render(new NetworkOpsCommandCenterWindow(center), Path.Combine(dir, $"{label}-fleet.png"));

        // A rack device's own window, on the worst one (the failed backup in the fixture).
        var worstRack = center.Rack.First();
        var rackDevice = new NetworkOpsDeviceViewModel(reader, rack, worstRack.Device);
        if (reader.IsConfigured) rackDevice.LoadHistoryAsync(CancellationToken.None).GetAwaiter().GetResult();
        else if (RealRackReadings().TryGetValue(worstRack.Name, out var readings)) rackDevice.SetReadings(readings);   // its tiles, from a real evening's readings
        written += Render(new NetworkOpsDeviceWindow(rackDevice), Path.Combine(dir, $"{label}-rack-device.png"));
        // And one of each other kind the rack has, so every kind of tile is looked at.
        foreach (var name in new[] { "ESXi host .10 (production)", "UC3200 SAN", "Core switch (EdgeSwitch 10G)", "UniFi network", "Internet (Netgate + Shaw)", "KOR-FS01 (file server)" })
        {
            if (label != "fixture" || !RealRackReadings().TryGetValue(name, out var r)) continue;
            var dev = new Kor.Operations.NetworkOps.Core.Learning.DeviceRow(9000 + name.Length, name, DateTime.UtcNow, DateTime.UtcNow, Kind: "Host");
            var vm = new NetworkOpsDeviceViewModel(reader, rack with { Devices = [.. rack.Devices, dev] }, dev);
            vm.SetReadings(r);
            written += Render(new NetworkOpsDeviceWindow(vm), Path.Combine(dir, $"{label}-rack-{new string(name.Where(char.IsLetterOrDigit).ToArray())}.png"));
            // "About this part" for the tile with the most to say: what clicking a tile shows.
            if (vm.Components.OrderByDescending(t => t.Info.Count).FirstOrDefault() is { } richest)
            {
                vm.SelectedPart = richest;
                written += Render(new NetworkOpsDeviceWindow(vm), Path.Combine(dir, $"{label}-rack-{new string(name.Where(char.IsLetterOrDigit).ToArray())}-part.png"));
            }
        }

        // The PC window on the worst PC: the one with the most to explain.
        var worst = center.Fleet.First();
        var device = new NetworkOpsDeviceViewModel(reader, snapshot, worst.Device);
        if (reader.IsConfigured) device.LoadHistoryAsync(CancellationToken.None).GetAwaiter().GetResult();
        else device.SetLastCheck(NetworkOpsComponentTilesTests.Kor208NCheck());   // the "This PC" strip, from a real v10 check
        device.SetNetwork(NetworkOpsNetworkWindowTests.LiveMap().Map);            // so the device page draws its switch-port strip
        written += Render(new NetworkOpsDeviceWindow(device), Path.Combine(dir, $"{label}-pc-{worst.Name}.png"));
        if (label != "fixture") return written;
        // The Network tab on the real map of 2026-10-02: BMZ-SW01's ports, then a search for one person. Folded from a
        // window into a UserControl (2026-10-04), so it renders at the window's old size.
        var netSize = new Size(1500, 940);
        written += Render(new NetworkOpsNetworkView(NetworkOpsNetworkWindowTests.LiveMap()), netSize, Path.Combine(dir, $"{label}-network-switches.png"));
        var network = new NetworkOpsNetworkView(NetworkOpsNetworkWindowTests.LiveMap(), table: true);
        written += Render(network, netSize, Path.Combine(dir, $"{label}-network.png"));
        network.SearchBox.Text = "SW02";
        written += Render(network, netSize, Path.Combine(dir, $"{label}-network-search.png"));

        // The "To clear" worklist with its category rollup strip, built from the fixture fleet's open issues.
        var toClear = new NetworkOpsToClearView(reader);
        toClear.Apply(ToClear.Build([snapshot], new System.Collections.Generic.HashSet<string>(), DateTime.UtcNow));
        written += Render(toClear, new Size(1400, 900), Path.Combine(dir, $"{label}-to-clear.png"));

        // "Clear in one action", built so the overlap line renders: three reboot reasons where two machines carry 2+ of
        // them (the fixture fleet has reboot reasons but no machine carries two). This is the worked case the unit test
        // asserts -- 5 machines, 8 findings, 2 overlapping -- so the panel can be LOOKED at, not just unit-proved.
        var rebootDevices = new System.Collections.Generic.List<DeviceRow>
        {
            new(1, "KOR-11", DateTime.UtcNow, DateTime.UtcNow), new(2, "KOR-12", DateTime.UtcNow, DateTime.UtcNow),
            new(3, "KOR-13", DateTime.UtcNow, DateTime.UtcNow), new(4, "KOR-14", DateTime.UtcNow, DateTime.UtcNow),
            new(5, "KOR-15", DateTime.UtcNow, DateTime.UtcNow),
        };
        FleetFinding RF(long id, string dev, string rule, string title)
            => new(id, dev, rule, Kor.Operations.NetworkOps.Core.Health.Severity.Warning, title, "evidence", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, null, null);
        var rebootOpen = new System.Collections.Generic.List<FleetFinding>
        {
            RF(1, "KOR-11", "not-restarted", "Not restarted in 21 days"),
            RF(2, "KOR-12", "not-restarted", "Not restarted in 21 days"),
            RF(3, "KOR-13", "not-restarted", "Not restarted in 21 days"),
            RF(4, "KOR-12", "reboot-overdue", "A reboot is overdue"),
            RF(5, "KOR-13", "reboot-overdue", "A reboot is overdue"),
            RF(6, "KOR-14", "reboot-overdue", "A reboot is overdue"),
            RF(7, "KOR-13", "crash-loop:opushutil.exe", "opushutil.exe keeps crashing"),
            RF(8, "KOR-15", "crash-loop:opushutil.exe", "opushutil.exe keeps crashing"),
        };
        var rebootSnap = new FleetSnapshot(rebootDevices,
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>>(), rebootOpen, [], null);
        var toClearOverlap = new NetworkOpsToClearView(reader);
        toClearOverlap.Apply(ToClear.Build([rebootSnap], new System.Collections.Generic.HashSet<string>(), DateTime.UtcNow));
        written += Render(toClearOverlap, new Size(1400, 900), Path.Combine(dir, $"{label}-to-clear-overlap.png"));

        // The KOR Remote viewer, connected to a PC with two monitors showing the second (the page itself is not drawn here).
        var viewer = new KorRemoteViewerModel(worst.Name, "kwurmlinger · active, idle 12 min");
        viewer.Apply(new BridgeState("desktop", [], worst.Name, 3, [new("655406", "Ctrl-Alt-Del")],
            [new(0, "All Displays", false), new(1, "Display 1", false), new(2, "Display 2", true)]));
        written += Render(new KorRemoteViewerWindow(viewer) { Width = 1400, Height = 360 }, Path.Combine(dir, $"{label}-kor-remote-viewer.png"));

        // The failing drive's "About this part": the case with the most on it.
        if ((device.Components.FirstOrDefault(t => t.Part.Kind == "drive" && !t.Part.IsSystem) ?? device.Components.FirstOrDefault()) is { } part)
        {
            device.SelectedPart = part;
            written += Render(new NetworkOpsDeviceWindow(device), Path.Combine(dir, $"{label}-pc-{worst.Name}-part.png"));
        }

        // The Fix dialog on a PC someone is actively using, with the restart chosen: the case with the most on it
        // (the warning, the relabelled button). It sizes to its content, so it is given a height to render into.
        FixOption[] fixes =
        [
            new("start-service", "Start a service", "Starts a stopped Windows service and sets it to start on its own.", false, "Service name", "Spooler"),
            new("restart-pc", "Restart the PC", "Restarts in 5 minutes, with a message on screen so whoever is on it can save.", true, null, null),
            new("run-command", "Run a command", "Runs PowerShell on the PC as SYSTEM. What ran and what it printed are recorded.", false, "PowerShell", null),
        ];
        var fix = new NetworkOpsFixWindow("Not restarted in 21 days", worst.Name, worst.Presence, someoneActive: true, fixes) { Height = 560 };
        fix.FixList.SelectedIndex = 1;
        written += Render(fix, Path.Combine(dir, $"{label}-fix-dialog.png"));

        // The Prompt Library, opened from a finding, with a prompt written and one session waiting for a decision.
        var finding = snapshot.OpenFindings.First(f => f.Device == worst.Name);
        var catalog = PromptLibraryTests.Catalog(snapshot);
        var library = new PromptLibraryWindow(reader, new PromptRequest("finding", null, worst.Device.DeviceId, finding.FindingId));
        library.Apply(catalog, PromptLibraryTests.Runs(),
            new RenderedPrompt(42, $"Claude: {finding.Title} on {worst.Name}", "claude-x.md", $"# Solve \"{finding.Title}\" on {worst.Name}\n\nWritten by NetworkOps at 2026-09-30 23:00 UTC from its live database..."));
        library.AskBox.Text = "Andrea's ETABS crashed around 2:40 today opening the Tower B model";
        written += Render(library, Path.Combine(dir, $"{label}-prompt-library.png"));

        // The Updates view (folded from a window into the console's Updates tab): overdue, due, held, failed, the special
        // servers, one that cannot be reached; two ticked, one selected.
        var updates = new NetworkOpsUpdatesView(reader);
        updates.Apply(NetworkOpsUpdatesTests.Rows(), new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc));
        updates.OnlyWaitingBox.IsChecked = false;
        var views = ((System.Collections.IEnumerable)updates.Grid.ItemsSource).Cast<UpdateRowView>().ToList();
        views[0].IsTicked = true;
        views[1].IsTicked = true;
        updates.Grid.SelectedItem = views[0];
        written += Render(updates, new Size(1320, 880), Path.Combine(dir, $"{label}-updates.png"));

        // The Deploy view (folded from a window into the console's Deploy tab): the fleet, one operation chosen.
        var deploy = new NetworkOpsDeployView(reader);
        deploy.Apply([new Kor.Operations.NetworkOps.Core.Learning.DeployOpView("migrate-korops", "Migrate to KOR-Operations",
            "Swaps the install and re-registers the add-in, closing Outlook briefly on each.", true)], snapshot);
        written += Render(deploy, new Size(1160, 820), Path.Combine(dir, $"{label}-deploy.png"));
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
        return Render(root, new Size(window.Width, window.Height), path);
    }

    // Render any element (a window's content, or a UserControl folded out of a window) at an explicit size.
    private static int Render(FrameworkElement root, Size size, string path)
    {
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
