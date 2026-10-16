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
/// only when KOR_NETWORKOPS_RENDER_LIVE=1 is set (it signs in, so never during an ordinary test run; it reads the installed
/// app's config, and only ever reads). Since 2026-10-16 also the LANDING screen (console on To clear), the Fleet table, the
/// connection-lost banner and an empty To clear, and on a live run the Updates and Network tabs from real data.
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
                if (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_RENDER_LIVE") == "1")
                {
                    // The test host has no App.config of its own, so FromAppConfig is unconfigured here: read the INSTALLED
                    // app's settings (or KOR_NETWORKOPS_APP_CONFIG). Same token cache as the app, so it signs in silently.
                    var real = NetworkOpsClient.FromAppConfig();
                    if (!real.IsConfigured) real = FromInstalledConfig() ?? real;
                    if (real.IsConfigured)
                        written += RenderFleet(real.GetFleetAsync(CancellationToken.None).GetAwaiter().GetResult(), real, outputDirectory, "live");
                }
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
        // The switch-port strip: the live map on a live run (a fixture map there drew another day's ports), else the 10-02 map.
        var liveMap = label == "live" ? reader.GetNetworkAsync(CancellationToken.None).GetAwaiter().GetResult() : null;
        device.SetNetwork((liveMap ?? NetworkOpsNetworkWindowTests.LiveMap()).Map);
        written += Render(new NetworkOpsDeviceWindow(device), Path.Combine(dir, $"{label}-pc-{worst.Name}.png"));

        // The LANDING screen as Ian opens it (the console on To clear), and the Fleet table -- never rendered before 10-16.
        written += RenderLanding(center, reader, snapshot, dir, label);
        // Its own view model: two console windows bound to one CardView ping-pong their Cards/Table radio buttons forever
        // (a test-only shape -- the app opens one console).
        var tableCenter = new NetworkOpsCommandCenterViewModel(reader);
        tableCenter.Apply(snapshot, DateTime.UtcNow);
        tableCenter.ApplyRack(rack, DateTime.UtcNow);
        tableCenter.CardView = false;
        written += Render(new NetworkOpsCommandCenterWindow(tableCenter), Path.Combine(dir, $"{label}-fleet-table.png"));
        if (label != "fixture")
        {
            // The other tabs on the live fleet: only real data shows whether they read well at 38 PCs and real text.
            var liveUpdates = new NetworkOpsUpdatesView(reader);
            liveUpdates.Apply(reader.GetUpdatesAsync(CancellationToken.None).GetAwaiter().GetResult(), DateTime.UtcNow);
            written += Render(liveUpdates, new Size(1320, 880), Path.Combine(dir, $"{label}-updates.png"));
            if (liveMap is not null)
                written += Render(new NetworkOpsNetworkView(liveMap, table: true), new Size(1500, 940), Path.Combine(dir, $"{label}-network.png"));
            return written;
        }
        written += RenderStates(snapshot, rack, dir, label);
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
        // The same panel with the reasons disclosed: the toggle state lives in the UI, so flip every "ReasonsToggle" in
        // the realised tree (the first Render laid it out) and render again -- this is the per-reason Fix/More layer.
        foreach (var tb in Descendants<System.Windows.Controls.Primitives.ToggleButton>(toClearOverlap).Where(t => t.Name == "ReasonsToggle"))
            tb.IsChecked = true;
        written += Render(toClearOverlap, new Size(1400, 1180), Path.Combine(dir, $"{label}-to-clear-overlap-expanded.png"));

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

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Measure/Arrange/Render the content rather than Show: showing raises Loaded, which starts the refresh timer.</summary>
    /// <summary>The console on its To clear tab, as Window_Loaded leaves it (Loaded never fires here, so select it the same
    /// way), filled from the service's worklist on a live run and from the fixture fleet otherwise.</summary>
    private static int RenderLanding(NetworkOpsCommandCenterViewModel center, NetworkOpsClient reader, FleetSnapshot snapshot, string dir, string label)
    {
        var data = reader.IsConfigured && label == "live"
            ? reader.GetToClearAsync(CancellationToken.None).GetAwaiter().GetResult()
            : ToClear.Build([snapshot], new System.Collections.Generic.HashSet<string>(), DateTime.UtcNow);
        var landing = new NetworkOpsCommandCenterWindow(center);
        landing.ToClearTab.IsChecked = true;
        ((NetworkOpsToClearView)landing.ToClearHost.Content).Apply(data);
        return Render(landing, Path.Combine(dir, $"{label}-landing.png"));
    }

    /// <summary>States nobody had looked at (audit 2026-10-16, tier 6): the service not answering, and nothing left to clear.</summary>
    private static int RenderStates(FleetSnapshot snapshot, FleetSnapshot rack, string dir, string label)
    {
        var lost = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("The NetworkOps service did not answer."));
        lost.Apply(snapshot, DateTime.UtcNow);
        lost.ApplyRack(rack, DateTime.UtcNow);
        lost.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();   // fails at once (no service): the connection-lost banner
        var written = Render(new NetworkOpsCommandCenterWindow(lost), Path.Combine(dir, $"{label}-connection-lost.png"));

        var empty = new NetworkOpsToClearView(NetworkOpsClient.Unconfigured("fixture"));
        empty.Apply(ToClear.Build([snapshot with { OpenFindings = [] }], new System.Collections.Generic.HashSet<string>(), DateTime.UtcNow));
        written += Render(empty, new Size(1400, 900), Path.Combine(dir, $"{label}-to-clear-empty.png"));
        return written;
    }

    /// <summary>A client from the installed app's config file, for a live render run from the test host.</summary>
    private static NetworkOpsClient? FromInstalledConfig()
    {
        var path = Environment.GetEnvironmentVariable("KOR_NETWORKOPS_APP_CONFIG") is { Length: > 0 } p ? p : @"C:\KOR-Operations\Kor.Operations.App.dll.config";
        if (!File.Exists(path)) return null;
        var settings = System.Xml.Linq.XDocument.Load(path).Descendants("appSettings").SelectMany(s => s.Elements("add"))
            .Where(a => a.Attribute("key") is not null)
            .GroupBy(a => (string)a.Attribute("key")!).ToDictionary(g => g.Key, g => ((string?)g.Last().Attribute("value") ?? "").Trim());
        string? S(string k) => settings.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
        return S("NetworkOps.ApiBaseUrl") is { } url && S("NetworkOps.ApiScope") is { } scope && S("NetworkOps.ApiCertSha256") is { } pin
               && S("Graph.TenantId") is { } tenant && S("Graph.ClientId") is { } client
            ? new NetworkOpsClient(url, scope, pin, tenant, client) : null;
    }

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
