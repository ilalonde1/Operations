#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.NetworkOps.Core.Network;

namespace Kor.Operations.App.NetworkOps;

/// <summary>The port map: the switches as panels of port tiles (a tile opens its device's page), or as a table and search.
/// A tab of the Command Center (folded from a standalone window 2026-10-04); reached the one way in, through the navigator.</summary>
public partial class NetworkOpsNetworkView : UserControl
{
    private readonly NetworkOpsClient? _client;
    private readonly NetworkOpsNavigator? _nav;
    private readonly CancellationTokenSource _cts = new();
    private NetworkOpsNetworkModel? _model;
    private PortCard? _chosen;
    private string? _pendingFocus;
    private int? _pendingPort;

    /// <param name="navigator">Opens a port's device page (the one device window, through the one navigator).</param>
    public NetworkOpsNetworkView(NetworkOpsClient client, NetworkOpsNavigator? navigator = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _nav = navigator;
        InitializeComponent();
        // Hosted in a visibility-toggled tab, so the view stays in the tree across tab switches: Unloaded fires only when
        // the console window closes. That is where the retry timer stops and the read is cancelled (was OnClosed).
        Unloaded += (_, _) => { _retry?.Stop(); _cts.Cancel(); };
    }

    /// <summary>
    /// Shows one switch's ports or one access point's wireless devices: how a UniFi tile on the network's own page gets here
    /// (Ian, 2026-10-02: duplicate ways INTO the same data, never a copy of it). Before the map has loaded, it is kept and
    /// applied when it does.
    /// </summary>
    /// <param name="mac">A switch or access point's MAC, or "core" (the core switch: an EdgeSwitch, UniFi gives it no MAC of its own here).</param>
    /// <param name="port">One of its ports: shown in the details beside the panels (a PC's page "on KOR-SW01 port 12" lands here).</param>
    public void FocusOn(string mac, int? port = null)
    {
        if (_model is null) { _pendingFocus = mac; _pendingPort = port; return; }
        _pendingFocus = null;
        _pendingPort = null;
        var map = _model.Response.Map;
        if ((mac == "core" ? map.Switches.FirstOrDefault(s => s.IsCore) : map.Switches.FirstOrDefault(s => s.Mac == mac)) is { } sw)
        {
            SearchBox.Text = "";
            PanelsViewBtn.IsChecked = true;
            Dispatcher.BeginInvoke(() => ScrollToSwitch(sw.Mac), System.Windows.Threading.DispatcherPriority.Loaded);
            if (port is { } n && _model.Panels.FirstOrDefault(p => p.Mac == sw.Mac)?.Ports.FirstOrDefault(c => c.Number == n) is { } card)
                ShowCard(card);
            else
            {
                PortWhere.Text = sw.Name;
                PortDetail.Text = "Its ports are on the left. Click one for what is on it.";
                OpenDeviceBtn.Visibility = Visibility.Collapsed;
            }
        }
        else if (_model.Places.FirstOrDefault(p => p.Key == mac) is { } place)
        {
            SearchBox.Text = "";
            TableViewBtn.IsChecked = true;
            PlaceList.SelectedItem = place;
            PlaceList.ScrollIntoView(place);
        }
    }

    /// <summary>For the render test: the view filled from a map, no service.</summary>
    internal NetworkOpsNetworkView(NetworkMapResponse map, bool table = false)
    {
        InitializeComponent();
        if (table) TableViewBtn.IsChecked = true;
        Apply(map);
    }

    private async void View_Loaded(object sender, RoutedEventArgs e) { if (_client is not null) await Guard(LoadAsync).ConfigureAwait(true); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private async Task LoadAsync(CancellationToken ct)
    {
        StatusText.Text = "Reading…";
        var map = await _client!.GetNetworkAsync(ct).ConfigureAwait(true);
        if (map is null)
        {
            // Just after the service starts there is no map until its first rack sweep (2026-10-02: a blank window, the
            // reason cut off at the foot). Say so where it is seen, and try again by itself.
            if (_model is null)
            {
                HeadlineText.Text = "The map is being built";
                SublineText.Text = "The service has just started: the map comes with its first rack sweep, within 5 minutes. This window checks again every 30 seconds.";
            }
            StatusText.Text = $"Waiting for the map (checked {DateTime.Now:HH:mm:ss}).";
            _retry ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(30), System.Windows.Threading.DispatcherPriority.Background,
                async (_, _) => await Guard(LoadAsync).ConfigureAwait(true), Dispatcher);
            _retry.Start();
            return;
        }
        _retry?.Stop();
        Apply(map);
        StatusText.Text = "";
    }

    private System.Windows.Threading.DispatcherTimer? _retry;

    /// <summary>Fills the window; the switch that was showing stays showing.</summary>
    internal void Apply(NetworkMapResponse map)
    {
        var was = (PlaceList.SelectedItem as NetworkPlace)?.Key;
        // The side panel's chosen port belongs to the old model; a rebuild makes it stale, so drop it rather than keep
        // showing a port from a map that no longer exists. The next tile click re-fills it from the new model.
        _chosen = null;
        PortWhere.Text = "";
        PortDetail.Text = "Click a port for what is on it.";
        OpenDeviceBtn.Visibility = Visibility.Collapsed;
        _model = new NetworkOpsNetworkModel(map);
        HeadlineText.Text = _model.Headline;
        SublineText.Text = _model.Subline + (_model.Notes.Count > 0 ? " · " + string.Join(" · ", _model.Notes) : "");
        PanelList.ItemsSource = _model.Panels;
        PlaceList.ItemsSource = _model.Places;
        PlaceList.SelectedItem = _model.Places.FirstOrDefault(p => p.Key == was && was is { Length: > 0 }) ?? _model.Places.FirstOrDefault();
        if (SearchBox.Text.Length > 0) ShowSearch();
        if (_pendingFocus is { } f) FocusOn(f, _pendingPort);
    }

    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelsView is null || TableView is null) return;
        var panels = PanelsViewBtn.IsChecked == true;
        PanelsView.Visibility = panels ? Visibility.Visible : Visibility.Collapsed;
        TableView.Visibility = panels ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A port tile: its details beside the panels; its device's page when it has one; a link scrolls to that switch.</summary>
    private void Port_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PortCard card) return;
        if (ShowCard(card)) _nav!.OpenDevice(card.OpenName!);
        else if (card.GoToSwitch is { } mac) ScrollToSwitch(mac);
    }

    /// <summary>A port's details beside the panels; true when its device has a page of its own.</summary>
    private bool ShowCard(PortCard card)
    {
        _chosen = card;
        PortWhere.Text = card.Row.Where ?? $"Port {card.Number}";
        PortDetail.Text = string.Join("\n", card.Tip.Split('\n').Skip(1)) is { Length: > 0 } d ? d + (card.Row.Mac.Length > 0 ? "\nMAC: " + card.Row.Mac : "") + (card.Row.NamedBy.Length > 0 ? "\nNamed by: " + card.Row.NamedBy : "") : "Nothing is connected here.";
        var hasPage = card.OpenName is { } n && _nav?.HasPage(n) == true;
        OpenDeviceBtn.Visibility = hasPage ? Visibility.Visible : Visibility.Collapsed;
        return hasPage;
    }

    private void OpenDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_chosen?.OpenName is { } n) _nav?.OpenDevice(n);
    }

    private void ScrollToSwitch(string mac)
    {
        if (_model is null) return;
        var i = _model.Panels.ToList().FindIndex(p => p.Mac == mac);
        if (i < 0) return;
        (PanelList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement)?.BringIntoView();
    }

    private void Place_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_model is null || PlaceList.SelectedItem is not NetworkPlace place) return;
        if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; return; }   // clearing the search shows the place
        ShowPlace(place);
    }

    private void ShowPlace(NetworkPlace place)
    {
        var s = _model!.Response.Map.Switches.FirstOrDefault(x => x.Mac == place.Key);
        PlaceTitle.Text = place.Name;
        PlaceSub.Text = s is null ? place.Sub
            : $"{s.Model}{(s.Ip is { } ip ? " · " + ip : "")} · MAC {s.Mac}" +
              (s.ParentName is { } pn ? $" · hangs from {pn}{(s.ParentPort is { } pp ? $" port {pp}" : "")}{(s.UplinkPort is { } up ? $" (its port {up})" : "")}" : "");
        WhereColumn.Visibility = Visibility.Collapsed;
        PortColumn.Visibility = Visibility.Visible;
        Rows.ItemsSource = _model.RowsOf(place);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_model is null) return;
        if (SearchBox.Text.Trim().Length == 0) { if (PlaceList.SelectedItem is NetworkPlace p) ShowPlace(p); return; }
        TableViewBtn.IsChecked = true;   // a search's answer is a list: where each match is
        ShowSearch();
    }

    private void ShowSearch()
    {
        var rows = _model!.Search(SearchBox.Text);
        PlaceTitle.Text = $"\"{SearchBox.Text.Trim()}\"";
        PlaceSub.Text = $"{rows.Count} of {_model.Response.Map.Everything().Count()} devices match";
        WhereColumn.Visibility = Visibility.Visible;
        PortColumn.Visibility = Visibility.Collapsed;   // "Where" already says the port
        Rows.ItemsSource = rows;
    }

    // Every handler is async void: nothing may escape it, so this catches everything but a cancel (which only ever
    // means the window closed). A narrower filter let a JsonException from a changed map DTO escape and take the whole
    // app down; the device and command-center view models already catch `is not OperationCanceledException` for this.
    private async Task Guard(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read the map: {ex.Message}";
        }
    }

}
