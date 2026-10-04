#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// KOR Remote in the app's own window (Ian, 2026-10-02, a ScreenConnect screenshot: "can we create the control window with
/// controls and look like this?"). MeshCentral's own page draws the remote screen -- its header, menus, footer and desktop
/// bars hidden -- and the toolbar drives it through KorRemoteBridge.js, calling the same page functions its buttons call.
/// Signed in once per PC: the WebView2 profile lives in %LOCALAPPDATA%\KorOperations\KorRemote and keeps the session.
/// </summary>
public partial class KorRemoteViewerWindow : Window
{
    // One viewer per machine: a second Connect brings the open one forward.
    private static readonly Dictionary<string, KorRemoteViewerWindow> Open_ = new(StringComparer.OrdinalIgnoreCase);

    // One WebView2 profile for every viewer in the app (a profile folder may have only one set of options per process).
    private static Task<CoreWebView2Environment>? _environment;

    private static readonly Serilog.ILogger _log = Serilog.Log.ForContext<KorRemoteViewerWindow>();

    private readonly NetworkOpsDeviceWindow _device;
    private readonly KorRemoteViewerModel _vm;
    private string _url;
    private int _reroutes;
    private bool _gotBridge;
    private System.Windows.Threading.DispatcherTimer? _watchdog;

    public static string ProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KorOperations", "KorRemote");
    public static string ScreenshotFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "KOR Remote");

    /// <summary>Opens (or brings forward) the clean viewer for the device at its remote desktop (Connect).</summary>
    public static void Open(NetworkOpsDeviceWindow device) => Open(device, device.ViewModel.ViewerUrl);

    /// <summary>Opens (or brings forward) the SAME clean viewer at the device's Web-RDP view -- one window, never the browser.</summary>
    public static void OpenRdp(NetworkOpsDeviceWindow device) => Open(device, device.ViewModel.RdpViewerUrl);

    private static void Open(NetworkOpsDeviceWindow device, string? url)
    {
        var d = device.ViewModel;
        if (url is null) { _log.Warning("KorRemote Connect: no viewer URL for {Device} (no mesh node id on record?)", d.DeviceName); return; }
        _log.Information("KorRemote Connect requested: {Device} -> {Url}", d.DeviceName, url);
        if (Open_.TryGetValue(d.DeviceName, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            open.NavigateTo(url);   // one viewer per machine: switch between Connect and Web-RDP in place
            return;
        }
        var w = new KorRemoteViewerWindow(device, url);
        Open_[d.DeviceName] = w;
        w.Show();
    }

    /// <summary>Point an already-open viewer at another view of the same machine (Connect &lt;-&gt; Web-RDP).</summary>
    public void NavigateTo(string url)
    {
        _log.Information("KorRemote NavigateTo {Device} -> {Url}", _vm.DeviceName, url);
        _url = url;
        _reroutes = 0;
        _gotBridge = false;
        if (View.CoreWebView2 is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri)) View.Source = uri;
    }

    private KorRemoteViewerWindow(NetworkOpsDeviceWindow device, string url)
    {
        _device = device;
        _url = url;
        _vm = new KorRemoteViewerModel(device.ViewModel.DeviceName, device.ViewModel.Device.Presence);
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => await StartAsync().ConfigureAwait(true);
        Closed += (_, _) => { Open_.Remove(_vm.DeviceName); View.Dispose(); };
    }

    /// <summary>For the render test only: the window with no page behind it.</summary>
    internal KorRemoteViewerWindow(KorRemoteViewerModel vm)
    {
        _device = null!;
        _url = "";
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
    }

    private async Task StartAsync()
    {
        try
        {
            var env = _environment ??= CoreWebView2Environment.CreateAsync(null, ProfileFolder);
            await View.EnsureCoreWebView2Async(await env.ConfigureAwait(true)).ConfigureAwait(true);
            var core = View.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(KorRemoteBridge.Script).ConfigureAwait(true);
            core.WebMessageReceived += (_, e) => OnPage(KorRemoteBridge.Parse(e.WebMessageAsJson));
            core.DownloadStarting += OnDownload;
            _log.Information("KorRemote WebView2 ready for {Device}; navigating to {Url}", _vm.DeviceName, _url);
            View.Source = new Uri(_url);
            StartWatchdog();
        }
        // Loaded is async void, so nothing may escape. The environment is a process-wide cached Task built with
        // ??=: a locked/busy profile folder faults CreateAsync with IOException/UnauthorizedAccessException, and a
        // faulted Task would be handed to every later Connect forever -- so on failure we drop it to let the next
        // attempt rebuild it. (The cert-not-found/runtime-missing cases keep a good env and just report.)
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_environment is { IsFaulted: true } or { IsCanceled: true }) _environment = null;
            _log.Error(ex, "KorRemote viewer failed to start for {Device}", _vm.DeviceName);
            _vm.Apply(new BridgeState("desktop", Missing: ["the remote viewer could not start (" + ex.Message + ")"]));
        }
    }

    // If the page never posts a bridge message, the injected script did not run (injection failed, or it is not a MeshCentral
    // page) -- which is invisible without this, and is exactly the "connected but no controls" case.
    private void StartWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _watchdog.Tick += (_, _) =>
        {
            _watchdog?.Stop();
            if (!_gotBridge)
                _log.Warning("KorRemote: no bridge message from {Device} within 8s at {Url} -- the toolbar has no controls because the page posted no state (script injection failed, or it is not the MeshCentral desktop page)", _vm.DeviceName, _url);
        };
        _watchdog.Start();
    }

    private void OnPage(BridgeState? s)
    {
        if (s is not null)
        {
            _gotBridge = true;
            _log.Information("KorRemote bridge [{Device}]: page={Page} state={State} node={Node} missing={Missing} displays={DisplayCount} {@Displays} files={Files}",
                _vm.DeviceName, s.Page, s.State, s.Node, s.Missing, s.Displays?.Count ?? 0, s.Displays, s.Files);
            if (s.Missing is { Count: > 0 })
                _log.Warning("KorRemote [{Device}]: MeshCentral page is MISSING functions the toolbar needs: {Missing} -- a MeshCentral update likely renamed them, so the toolbar buttons do nothing", _vm.DeviceName, s.Missing);
        }
        _vm.Apply(s);
        // Signing in lands on MeshCentral's home page, not the device: send it back to the device, a few times at most.
        if (s is { Page: "desktop" } && !Kor.Operations.NetworkOps.Core.Learning.MeshLinks.IsDeviceLink(View.Source) && _reroutes++ < 3)
        {
            _log.Information("KorRemote [{Device}]: landed off the device page, re-routing to {Url} (attempt {Attempt})", _vm.DeviceName, _url, _reroutes);
            View.Source = new Uri(_url);
        }
    }

    /// <summary>A screenshot (deskSaveImage downloads a PNG): straight to Pictures\KOR Remote, no download prompt.</summary>
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        Directory.CreateDirectory(ScreenshotFolder);
        var path = Path.Combine(ScreenshotFolder, Path.GetFileName(e.ResultFilePath));
        e.ResultFilePath = path;
        e.Handled = true;
        e.DownloadOperation.StateChanged += (_, _) =>
        {
            if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed) Toast.Text = $"Saved {Path.GetFileName(path)} to Pictures\\KOR Remote";
            else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted) Toast.Text = "The screenshot was not saved";
        };
    }

    private void Do(string script)
    {
        _log.Debug("KorRemote command [{Device}]: {Script}", _vm.DeviceName, script);
        if (View.CoreWebView2 is not null) _ = View.CoreWebView2.ExecuteScriptAsync(script);
        else _log.Warning("KorRemote command dropped [{Device}] (WebView not ready): {Script}", _vm.DeviceName, script);
    }

    private void Show(Button anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action click, bool enabled = true, bool isChecked = false, string? tip = null)
    {
        var m = new MenuItem { Header = header, IsEnabled = enabled, IsChecked = isChecked, ToolTip = tip };
        m.Click += (_, _) => click();
        return m;
    }

    private void Screen_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(_vm.IsConnected ? Item("Disconnect", () => Do(KorRemoteBridge.Command("disconnect"))) : Item("Connect", () => Do(KorRemoteBridge.Command("connect"))));
        if (_vm.ShowsDisplays)
        {
            menu.Items.Add(new Separator());
            foreach (var m in _vm.Monitors)
                menu.Items.Add(Item(m.Label == "All" ? "All monitors" : $"Monitor {m.Label}", () => Do(KorRemoteBridge.Command("display", m.Number)), _vm.IsConnected, m.Selected, m.Tip));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(WindowState == WindowState.Maximized ? "Restore window" : "Fill the screen", ToggleMaximised));
        Show((Button)sender, menu);
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is int n) Do(KorRemoteBridge.Command("display", n));
    }

    private void Keys_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Ctrl+Alt+Del", () => Do(KorRemoteBridge.Command("cad")), _vm.IsConnected));
        menu.Items.Add(Item("Lock this PC…", () => Do(KorRemoteBridge.Command("lock")), _vm.IsConnected, tip: "Locks the remote user's session (asks first)"));
        if (_vm.Keys.Count > 0) menu.Items.Add(new Separator());
        foreach (var k in _vm.Keys) menu.Items.Add(Item(k.Text, () => Do(KorRemoteBridge.Command("keys", k.Value)), _vm.IsConnected));
        Show((Button)sender, menu);
    }

    /// <summary>What NetworkOps has open on this machine; choosing one runs the device window's fix flow, owned by this window.</summary>
    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var open = _device.ViewModel.OpenFindings.ToList();
        if (open.Count == 0) menu.Items.Add(Item("Nothing open on this machine", () => { }, enabled: false));
        foreach (var row in open)
            menu.Items.Add(Item($"{row.Severity}: {row.Title}…", () => _ = _device.FixAsync(row, this)));
        Show((Button)sender, menu);
    }

    private void Files_Click(object sender, RoutedEventArgs e) => Do(KorRemoteBridge.Command("files", !_vm.FilesShowing));

    private void Screenshot_Click(object sender, RoutedEventArgs e) => Do(KorRemoteBridge.Command("screenshot"));

    private void People_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = _vm.PresenceText, IsEnabled = false });
        Show((Button)sender, menu);
    }

    private void Chat_Click(object sender, RoutedEventArgs e) => Do(KorRemoteBridge.Command("chat"));

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        if (_device.WindowState == WindowState.Minimized) _device.WindowState = WindowState.Normal;
        _device.Activate();
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreBtn.ContextMenu is not { } menu) return;
        Show(MoreBtn, menu);
    }

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e) => _device.OpenInBrowser(this);

    private void Reload_Click(object sender, RoutedEventArgs e) { _reroutes = 0; View.Source = new Uri(_url); }

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximise_Click(object sender, RoutedEventArgs e) => ToggleMaximised();

    private void ToggleMaximised() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Do(KorRemoteBridge.Command("disconnect"));
        Close();
    }

    // A maximised window with its own title bar hangs past the screen edge by the resize border: inset it by that much.
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximise";
    }
}
