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
    private readonly Func<string, bool>? _hasPage;
    private readonly Func<string, bool>? _openPage;
    private readonly CancellationTokenSource _cts = new();
    private NetworkOpsNetworkModel? _model;
    private PortCard? _chosen;

    /// <param name="hasPage">Whether a device (by name) has a NetworkOps page: a fleet PC or a rack device.</param>
    /// <param name="openPage">Opens that page (the Command Center's own device window); false if it has none.</param>
    public NetworkOpsNetworkWindow(NetworkOpsClient client, Func<string, bool>? hasPage = null, Func<string, bool>? openPage = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _hasPage = hasPage;
        _openPage = openPage;
        InitializeComponent();
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
        if (map is null) { StatusText.Text = "The map is built after the service's next rack sweep (every 5 minutes). Try again shortly."; return; }
        Apply(map);
        StatusText.Text = "";
    }

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
        var hasPage = card.OpenName is { } n && _hasPage?.Invoke(n) == true;
        OpenDeviceBtn.Visibility = hasPage ? Visibility.Visible : Visibility.Collapsed;
        if (hasPage) _openPage!(card.OpenName!);
        else if (card.GoToSwitch is { } mac) ScrollToSwitch(mac);
    }

    private void OpenDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_chosen?.OpenName is { } n) _openPage?.Invoke(n);
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
        _cts.Cancel();
        base.OnClosed(e);
    }
}
