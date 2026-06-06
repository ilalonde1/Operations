#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.Controls;

public partial class ProjectSearchTypeahead : UserControl
{
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private bool _suppressTextChanged;

    public ProjectSearchTypeahead()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounceTimer.Tick += DebounceTimer_Tick;
        Unloaded += (_, _) => CancelSearch();
    }

    public event EventHandler<long>? ProjectSelected;

    public Func<string, CancellationToken, Task<IReadOnlyList<ProjectSearchRow>>>? Search { get; set; }

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
        if (string.IsNullOrWhiteSpace(q) || Search is null)
        {
            ResultsList.ItemsSource = null;
            ResultsPopup.IsOpen = false;
            return;
        }

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        try
        {
            var rows = await Search(q, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            ResultsList.ItemsSource = rows;
            ResultsList.SelectedIndex = rows.Count > 0 ? 0 : -1;
            ResultsPopup.IsOpen = rows.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ResultsList.ItemsSource = null;
            ResultsPopup.IsOpen = false;
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
            if (ResultsList.SelectedItem is not ProjectSearchRow)
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

    private void SelectCurrent()
    {
        if (ResultsList.SelectedItem is not ProjectSearchRow row)
        {
            return;
        }

        _debounceTimer.Stop();
        CancelSearch();
        _suppressTextChanged = true;
        SearchBox.Text = row.ProjectName;
        _suppressTextChanged = false;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        ResultsPopup.IsOpen = false;
        ProjectSelected?.Invoke(this, row.Id);
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

    private void CancelSearch()
    {
        var old = _searchCts;
        _searchCts = null;
        old?.Cancel();
        old?.Dispose();
    }
}
