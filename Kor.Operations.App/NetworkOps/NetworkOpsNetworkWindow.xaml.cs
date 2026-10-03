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

/// <summary>The port map: every switch, its ports and what is on each, by name and person; and a search across it all.</summary>
public partial class NetworkOpsNetworkWindow : Window
{
    private readonly NetworkOpsClient? _client;
    private readonly CancellationTokenSource _cts = new();
    private NetworkOpsNetworkModel? _model;

    public NetworkOpsNetworkWindow(NetworkOpsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        InitializeComponent();
    }

    /// <summary>For the render test: the window filled from a map, no service.</summary>
    internal NetworkOpsNetworkWindow(NetworkMapResponse map)
    {
        InitializeComponent();
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
        PlaceList.ItemsSource = _model.Places;
        PlaceList.SelectedItem = _model.Places.FirstOrDefault(p => p.Key == was && was is { Length: > 0 }) ?? _model.Places.FirstOrDefault();
        if (SearchBox.Text.Length > 0) ShowSearch();
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
