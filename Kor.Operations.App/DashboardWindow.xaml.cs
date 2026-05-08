#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Services;
using Serilog;

namespace Kor.Operations
{
    public partial class DashboardWindow : Window
    {
        // Existing collections
        private readonly ObservableCollection<DashboardRow> _rows = new();
        private readonly ObservableCollection<ActivityRow> _activity = new();
        private readonly IServiceProvider _services;
        private readonly ITransmittalsStore _transmittalsStore;

        // Debouncer you already have in MainWindow
        private readonly Debouncer _hintDebounce = new(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _hintCts;

        public DashboardWindow(IServiceProvider services, ITransmittalsStore transmittalsStore)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _transmittalsStore = transmittalsStore ?? throw new ArgumentNullException(nameof(transmittalsStore));
            InitializeComponent();

            // Keep your original header defaults. Deltek override is applied on Loaded.
            HeaderBar.UserDisplayName = Environment.UserName.Replace('.', ' ');
            HeaderBar.UserEmail = $"{Environment.UserName}@korstructural.com";

            ResultsGrid.ItemsSource = _rows;
            ActivityList.ItemsSource = _activity;

            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Non-blocking: improves header with Deltek data if available
            await InitHeaderIdentityAsync();
        }

        // ===== Header identity (Deltek full name + headshot) =====
        private async Task InitHeaderIdentityAsync()
        {
            try
            {
                var sam = Environment.UserName;
                var upnOverride = Kor.Operations.Services.AppServices.Get<UserOptions>().UserUpnOverride;
                var email = string.IsNullOrWhiteSpace(upnOverride)
                    ? $"{sam}@korstructural.com"
                    : upnOverride.Trim();
                await HeaderLoader.ApplyAsync(HeaderBar, email, sam);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DashboardWindow: header init failed.");
            }
        }

        // ===== Search / Clear / Create =====

        private async void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            try { await LoadTransmittalsAsync(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Email Filer — Search Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
            _rows.Clear();
            _activity.Clear();
            SearchPopup.IsOpen = false;
        }

        private void CreateTransmittalBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<MainWindow>();
            win.Owner = this;
            win.Show();
        }

        // ===== Results selection -> activity =====

        private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var sel = ResultsGrid.SelectedItem as DashboardRow;
                _activity.Clear();
                if (sel == null) return;
                await LoadActivityAsync(sel.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Email Filer — Load Activity Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== Open button in results grid =====

        private void OpenTransmittal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn)
                    return;

                var row = btn.Tag as DashboardRow;
                if (row == null)
                    return;

                OpenTransmittalPdf(row);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Email Filer — Open Transmittal Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTransmittalPdf(DashboardRow row)
        {
            var url = row.SharePointUrl?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("This transmittal does not have a SharePoint URL saved.",
                    "Email Filer — No SharePoint URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("This transmittal has an old-style SharePointUrl value.",
                    "Email Filer — Invalid SharePoint URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        // ===== Autocomplete for SearchBox =====

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = (SearchBox.Text ?? string.Empty).Trim();
            if (q.Length < 2)
            {
                SearchPopup.IsOpen = false;
                SearchHintsList.ItemsSource = null;
                return;
            }

            _hintDebounce.Run(async () =>
            {
                _hintCts?.Cancel();
                _hintCts = new CancellationTokenSource();
                try
                {
                    var hints = await _transmittalsStore.SearchHintsAsync(q, ct: _hintCts.Token);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SearchHintsList.ItemsSource = hints;
                        SearchHintsList.SelectedIndex = hints.Any() ? 0 : -1;
                        SearchPopup.IsOpen = hints.Any();
                    });
                }
                catch (OperationCanceledException) { /* ignore */ }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SearchPopup.IsOpen = false;
                        SearchHintsList.ItemsSource = null;
                    });
                }
            });
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && SearchHintsList != null && SearchHintsList.HasItems)
            {
                SearchPopup.IsOpen = true;
                SearchHintsList.Focus();
                if (SearchHintsList.SelectedIndex < 0) SearchHintsList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && SearchHintsList != null && SearchHintsList.HasItems)
            {
                SearchPopup.IsOpen = true;
                SearchHintsList.Focus();
                SearchHintsList.SelectedIndex = SearchHintsList.Items.Count - 1;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SearchBtn_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void SearchHintsList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var sel = SearchHintsList?.SelectedItem as string;
            if (sel != null) UseHint(sel);
        }

        private void SearchHintsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var sel = SearchHintsList?.SelectedItem as string;
                if (sel != null) { UseHint(sel); e.Handled = true; }
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                SearchBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && SearchHintsList.SelectedIndex <= 0)
            {
                SearchHintsList.SelectedIndex = -1;
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
            }
        }

        private void UseHint(string text)
        {
            SearchBox.Text = text;
            SearchPopup.IsOpen = false;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            SearchBox.Focus();
        }

        private async Task LoadTransmittalsAsync()
        {
            _rows.Clear();
            _activity.Clear();

            var q = (SearchBox.Text ?? string.Empty).Trim();
            DateTime? d1 = StartDatePicker.SelectedDate;
            DateTime? d2 = EndDatePicker.SelectedDate;
            var summaries = await _transmittalsStore.SearchSummaryAsync(
                q,
                d1,
                d2?.AddDays(1),
                typeFilter: null,
                includeSharePointUrlInSearch: true);

            foreach (var row in summaries)
            {
                _rows.Add(new DashboardRow
                {
                    Id = row.Id,
                    ProjectNo = row.ProjectNo,
                    Subject = row.Subject,
                    CreatedAt = row.CreatedAt,
                    SentAt = row.SentAt,
                    SharePointUrl = row.SharePointUrl,
                    Type = row.Type,
                    OpenCount = (int)row.OpenCount,
                    ClickCount = (int)row.ClickCount
                });
            }
        }

        private async Task LoadActivityAsync(Guid transmittalId)
        {
            _activity.Clear();
            var rows = await _transmittalsStore.LoadActivityAsync(transmittalId);
            foreach (var row in rows)
            {
                _activity.Add(new ActivityRow
                {
                    Kind = row.Kind,
                    RecipientEmail = row.RecipientEmail,
                    OccurredAt = row.OccurredAt,
                    ClientIp = row.ClientIp,
                    UserAgent = row.UserAgent,
                    Referer = row.Referer ?? string.Empty
                });
            }
        }
    }

    // POCOs for binding
    public sealed class DashboardRow
    {
        public Guid Id { get; set; }
        public string ProjectNo { get; set; } = "";
        public string Subject { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string SharePointUrl { get; set; } = "";
        public string Type { get; set; } = "Transmittal";  // NEW: used by Type column & row colouring
        public int OpenCount { get; set; }
        public int ClickCount { get; set; }
    }

    public sealed class ActivityRow
    {
        public string Kind { get; set; } = "";   // Open / Click
        public string RecipientEmail { get; set; } = "";
        public DateTime? OccurredAt { get; set; }
        public string ClientIp { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string Referer { get; set; } = "";
    }
}
