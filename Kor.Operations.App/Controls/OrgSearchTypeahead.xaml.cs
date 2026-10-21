#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;

namespace Kor.Operations.App.Controls;

public partial class OrgSearchTypeahead : UserControl
{
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText),
            typeof(string),
            typeof(OrgSearchTypeahead),
            new PropertyMetadata("Search org"));

    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private bool _suppressTextChanged;

    public OrgSearchTypeahead()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounceTimer.Tick += DebounceTimer_Tick;
        Unloaded += (_, _) => CancelSearch();
    }

    public event EventHandler<long>? OrgSelected;

    public ICanonicalOrgStore? Store { get; set; }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        await SearchAsync().ConfigureAwait(true);
    }

    private async Task SearchAsync()
    {
        CancelSearch();

        var q = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            ResultsList.ItemsSource = null;
            ResultsPopup.IsOpen = false;
            return;
        }

        if (Store is null)
        {
            ResultsList.ItemsSource = null;
            ResultsPopup.IsOpen = false;
            return;
        }

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        try
        {
            var rows = await Store.SearchCanonicalOrgsWithRelationshipsAsync(q, kind: null, take: 20, ct)
                .ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            ResultsList.ItemsSource = rows;
            ResultsList.SelectedIndex = rows.Count > 0 ? 0 : -1;
            // 2026-10-20: an empty result used to show nothing at all, which read as a broken box.
            ShowMessage(rows.Count > 0 ? null : $"No organization matches \"{q}\". Try part of the name, e.g. \"Westland\".");
            ResultsPopup.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Organization search failed for {Query}.", q);
            ResultsList.ItemsSource = null;
            ShowMessage("Search failed: " + ex.Message);
            ResultsPopup.IsOpen = true;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && ResultsPopup.IsOpen && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex);
            ResultsList.Focus();
            FocusSelectedItem();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ResultsPopup.IsOpen && ResultsList.Items.Count > 0)
        {
            if (ResultsList.SelectedItem is not CanonicalOrgRow)
            {
                ResultsList.SelectedIndex = 0;
            }

            SelectCurrent();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            ResultsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up && ResultsList.SelectedIndex <= 0)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SelectCurrent();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCurrent();
        e.Handled = true;
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ListBoxItem)
            {
                SelectCurrent();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void SelectCurrent()
    {
        if (ResultsList.SelectedItem is not CanonicalOrgRow row)
        {
            return;
        }

        _debounceTimer.Stop();
        CancelSearch();
        _suppressTextChanged = true;
        SearchBox.Text = row.DisplayName;
        _suppressTextChanged = false;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        ResultsPopup.IsOpen = false;
        OrgSelected?.Invoke(this, row.Id);
    }

    private void FocusSelectedItem()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ResultsList.ItemContainerGenerator.ContainerFromItem(ResultsList.SelectedItem) is ListBoxItem item)
            {
                item.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void ShowMessage(string? text)
    {
        MessageText.Text = text ?? "";
        MessageText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CancelSearch()
    {
        var old = _searchCts;
        _searchCts = null;
        old?.Cancel();
        old?.Dispose();
    }
}

/// <summary>An organization's kind in words a lead knows ("GC" reads as "General contractor").</summary>
public sealed class OrgKindLabelConverter : System.Windows.Data.IValueConverter
{
    public static string Label(string? kind) => (kind ?? "").Trim() switch
    {
        "GC" => "General contractor",
        "KorClient" => "KOR client",
        "KorStructural" => "KOR Structural",
        "Buyer" => "Public buyer",
        "Competitor" => "Structural competitor",
        "Modular" => "Modular builder",
        "Unknown" or "" => "Organization",
        var k => k,
    };

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Label(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
