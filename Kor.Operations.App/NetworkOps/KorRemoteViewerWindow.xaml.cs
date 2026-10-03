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

    private readonly NetworkOpsDeviceWindow _device;
    private readonly KorRemoteViewerModel _vm;
    private readonly string _url;
    private int _reroutes;

    public static string ProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KorOperations", "KorRemote");
    public static string ScreenshotFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "KOR Remote");

    /// <summary>Opens (or brings forward) the viewer for the device in <paramref name="device"/>.</summary>
    public static void Open(NetworkOpsDeviceWindow device)
    {
        var d = device.ViewModel;
        if (d.ViewerUrl is not { } url) return;
        if (Open_.TryGetValue(d.DeviceName, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var w = new KorRemoteViewerWindow(device, url);
        Open_[d.DeviceName] = w;
        w.Show();
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
            _environment ??= CoreWebView2Environment.CreateAsync(null, ProfileFolder);
            await View.EnsureCoreWebView2Async(await _environment.ConfigureAwait(true)).ConfigureAwait(true);
            var core = View.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(KorRemoteBridge.Script).ConfigureAwait(true);
            core.WebMessageReceived += (_, e) => OnPage(KorRemoteBridge.Parse(e.WebMessageAsJson));
            core.DownloadStarting += OnDownload;
            View.Source = new Uri(_url);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _vm.Apply(new BridgeState("desktop", Missing: ["the WebView2 runtime (" + ex.Message + ")"]));
        }
    }

    private void OnPage(BridgeState? s)
    {
        _vm.Apply(s);
        // Signing in lands on MeshCentral's home page, not the device: send it back to the device, a few times at most.
        if (s is { Page: "desktop" } && !Kor.Operations.NetworkOps.Core.Learning.MeshLinks.IsDeviceLink(View.Source) && _reroutes++ < 3)
            View.Source = new Uri(_url);
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
        if (View.CoreWebView2 is not null) _ = View.CoreWebView2.ExecuteScriptAsync(script);
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
