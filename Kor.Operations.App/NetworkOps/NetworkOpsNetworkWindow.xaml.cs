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

/// <summary>The port map: the switches as panels of port tiles (a tile opens its device's page), or as a table and search.</summary>
public partial class NetworkOpsNetworkWindow : Window
{
    private readonly NetworkOpsClient? _client;
    private readonly NetworkOpsNavigator? _nav;
    private readonly CancellationTokenSource _cts = new();
    private NetworkOpsNetworkModel? _model;
    private PortCard? _chosen;
    private string? _pendingFocus;

    /// <param name="navigator">Opens a port's device page (the one device window, through the one navigator).</param>
    public NetworkOpsNetworkWindow(NetworkOpsClient client, NetworkOpsNavigator? navigator = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _nav = navigator;
        InitializeComponent();
    }

    /// <summary>
    /// Shows one switch's ports or one access point's wireless devices: how a UniFi tile on the network's own page gets here
    /// (Ian, 2026-10-02: duplicate ways INTO the same data, never a copy of it). Before the map has loaded, it is kept and
    /// applied when it does.
    /// </summary>
    public void FocusOn(string mac)
    {
        if (_model is null) { _pendingFocus = mac; return; }
        _pendingFocus = null;
        var map = _model.Response.Map;
        if (map.Switches.FirstOrDefault(s => s.Mac == mac) is { } sw)
        {
            SearchBox.Text = "";
            PanelsViewBtn.IsChecked = true;
            Dispatcher.BeginInvoke(() => ScrollToSwitch(mac), System.Windows.Threading.DispatcherPriority.Loaded);
            PortWhere.Text = sw.Name;
            PortDetail.Text = "Its ports are on the left. Click one for what is on it.";
            OpenDeviceBtn.Visibility = Visibility.Collapsed;
        }
        else if (_model.Places.FirstOrDefault(p => p.Key == mac) is { } place)
        {
            SearchBox.Text = "";
            TableViewBtn.IsChecked = true;
            PlaceList.SelectedItem = place;
            PlaceList.ScrollIntoView(place);
        }
    }

    /// <summary>For the render test: the window filled from a map, no service.</summary>
    internal NetworkOpsNetworkWindow(NetworkMapResponse map, bool table = false)
    {
        InitializeComponent();
        if (table) TableViewBtn.IsChecked = true;
        Apply(map);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) { if (_client is not null) await Guard(LoadAsync).ConfigureAwait(true); }

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
        _model = new NetworkOpsNetworkModel(map);
        HeadlineText.Text = _model.Headline;
        SublineText.Text = _model.Subline + (_model.Notes.Count > 0 ? " · " + string.Join(" · ", _model.Notes) : "");
        PanelList.ItemsSource = _model.Panels;
        PlaceList.ItemsSource = _model.Places;
        PlaceList.SelectedItem = _model.Places.FirstOrDefault(p => p.Key == was && was is { Length: > 0 }) ?? _model.Places.FirstOrDefault();
        if (SearchBox.Text.Length > 0) ShowSearch();
        if (_pendingFocus is { } f) FocusOn(f);
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
        _chosen = card;
        PortWhere.Text = card.Row.Where ?? $"Port {card.Number}";
        PortDetail.Text = string.Join("\n", card.Tip.Split('\n').Skip(1)) is { Length: > 0 } d ? d + (card.Row.Mac.Length > 0 ? "\nMAC: " + card.Row.Mac : "") + (card.Row.NamedBy.Length > 0 ? "\nNamed by: " + card.Row.NamedBy : "") : "Nothing is connected here.";
        var hasPage = card.OpenName is { } n && _nav?.HasPage(n) == true;
        OpenDeviceBtn.Visibility = hasPage ? Visibility.Visible : Visibility.Collapsed;
        if (hasPage) _nav!.OpenDevice(card.OpenName!);
        else if (card.GoToSwitch is { } mac) ScrollToSwitch(mac);
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

    // Every handler is async void: nothing may escape it. A cancel only ever means the window closed.
    private async Task Guard(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            StatusText.Text = $"Could not read the map: {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _retry?.Stop();
        _cts.Cancel();
        base.OnClosed(e);
    }
}
