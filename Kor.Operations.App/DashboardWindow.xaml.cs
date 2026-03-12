#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using System.Windows.Media.Imaging;                 // added (for headshot)
using Kor.Operations.Services;               // added (DeltekHeadshotProvider)
using System.Diagnostics;                          // optional tracing

namespace Kor.Operations
{
    public partial class DashboardWindow : Window
    {
        // Existing collections
        private readonly ObservableCollection<DashboardRow> _rows = new();
        private readonly ObservableCollection<ActivityRow> _activity = new();

        private readonly string? _cs;

        // Debouncer you already have in MainWindow
        private readonly Debouncer _hintDebounce = new(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _hintCts;

        // Cache header identity to avoid repeated lookups
        private static BitmapImage? _cachedAvatar;
        private static string? _cachedDisplayName;

        public DashboardWindow()
        {
            InitializeComponent();

            // Keep your original header defaults. Deltek override is applied on Loaded.
            HeaderBar.UserDisplayName = Environment.UserName.Replace('.', ' ');
            HeaderBar.UserEmail = $"{Environment.UserName}@korstructural.com";

            ResultsGrid.ItemsSource = _rows;
            ActivityList.ItemsSource = _activity;

            _cs = ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString;

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
                var upnOverride = ConfigurationManager.AppSettings["UserUpnOverride"];
                var email = string.IsNullOrWhiteSpace(upnOverride)
                    ? $"{sam}@korstructural.com"
                    : upnOverride.Trim();

                // Display name via Deltek
                if (string.IsNullOrWhiteSpace(_cachedDisplayName))
                {
                    try
                    {
                        var provider = new DeltekHeadshotProvider();
                        var full = await provider.TryGetEmployeeDisplayNameAsync(email);
                        _cachedDisplayName = string.IsNullOrWhiteSpace(full)
                            ? sam.Replace('.', ' ').Replace('_', ' ')
                            : full;
                    }
                    catch
                    {
                        _cachedDisplayName = sam.Replace('.', ' ').Replace('_', ' ');
                    }
                }

                HeaderBar.UserDisplayName = _cachedDisplayName!;
                HeaderBar.UserEmail = email;

                // Headshot via Deltek
                if (_cachedAvatar == null)
                {
                    try
                    {
                        var provider = new DeltekHeadshotProvider();
                        var bmp = await provider.TryGetByEmailAsync(email);
                        if (bmp != null) _cachedAvatar = bmp;
                    }
                    catch
                    {
                        // initials fallback
                    }
                }

                if (_cachedAvatar != null)
                {
                    var prop = HeaderBar.GetType().GetProperty("AvatarImageSource");
                    if (prop?.CanWrite == true) prop.SetValue(HeaderBar, _cachedAvatar);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dashboard header init failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ===== Search / Clear / Create =====

        private async void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            try { await LoadTransmittalsAsync(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Error); }
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
            var win = new MainWindow { Owner = this };
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
                MessageBox.Show(this, ex.Message, "Load activity failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(this, ex.Message, "Open transmittal failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTransmittalPdf(DashboardRow row)
        {
            var url = row.SharePointUrl?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("This transmittal does not have a SharePoint URL saved.",
                    "No SharePoint URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("This transmittal has an old-style SharePointUrl value.",
                    "Invalid SharePoint URL", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    var hints = await FetchHintsAsync(q, _hintCts.Token);
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

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && SearchHintsList != null && SearchHintsList.HasItems)
            {
                SearchPopup.IsOpen = true;
                SearchHintsList.Focus();
                if (SearchHintsList.SelectedIndex < 0) SearchHintsList.SelectedIndex = 0;
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

        private void SearchHintsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var sel = SearchHintsList?.SelectedItem as string;
            if (sel == null) return;
            UseHint(sel);
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
        }

        private void UseHint(string text)
        {
            SearchBox.Text = text;
            SearchPopup.IsOpen = false;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            SearchBox.Focus();
        }

        private async Task<List<string>> FetchHintsAsync(string q, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_cs))
                return new List<string>();

            var like = $"%{q}%";

            const string sql = @"
;WITH P AS (
    SELECT DISTINCT TOP (30) ProjectNo
    FROM dbo.Transmittals
    WHERE
        -- Match on project number OR any text in the SharePoint URL
        (ProjectNo LIKE @like OR SharePointUrl LIKE @like)
    ORDER BY ProjectNo
),
S AS (
    SELECT DISTINCT TOP (20) Subject
    FROM dbo.Transmittals
    WHERE Subject LIKE @like
    ORDER BY Subject
)
SELECT ProjectNo AS Val, 1 AS Ord FROM P
UNION ALL
SELECT Subject   AS Val, 2 AS Ord FROM S
ORDER BY Ord, Val;";

            var result = new List<string>();

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@like", like);

            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result.Add(r.IsDBNull(0) ? "" : r.GetString(0));

            return result;
        }


        // ===== Data loading =====

        private async Task LoadTransmittalsAsync()
        {
            _rows.Clear();
            _activity.Clear();

            if (string.IsNullOrWhiteSpace(_cs))
                throw new InvalidOperationException("Missing connection string 'KorTransmittalsDb'.");

            var q = (SearchBox.Text ?? string.Empty).Trim();
            var like = $"%{q}%";
            DateTime? d1 = StartDatePicker.SelectedDate;
            DateTime? d2 = EndDatePicker.SelectedDate;

            const string sql = @"
SELECT
    t.Id,
    t.ProjectNo,
    t.Subject,
    t.CreatedAt,
    t.SentAt,
    t.SharePointUrl,
    ISNULL(t.[Type], 'Transmittal') AS [Type],
    (SELECT COUNT(1) FROM dbo.OpenEvents  oe WHERE oe.TransmittalId = t.Id)  AS OpenCount,
    (SELECT COUNT(1) FROM dbo.ClickEvents ce WHERE ce.TransmittalId = t.Id)  AS ClickCount
FROM dbo.Transmittals t
WHERE
    (
        @q = ''
        OR t.ProjectNo       LIKE @like
        OR t.Subject         LIKE @like
        OR CONVERT(nvarchar(36), t.Id) LIKE @like
        OR t.SharePointUrl   LIKE @like   -- NEW: match text in SP folder / file path
    )
    AND (@d1 IS NULL OR t.CreatedAt >= @d1)
    AND (@d2 IS NULL OR t.CreatedAt < DATEADD(day, 1, @d2))
ORDER BY COALESCE(t.SentAt, t.CreatedAt) DESC;";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@q", q);
            cmd.Parameters.AddWithValue("@like", like);
            cmd.Parameters.AddWithValue("@d1", (object?)d1 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d2", (object?)d2 ?? DBNull.Value);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                _rows.Add(new DashboardRow
                {
                    Id = r.GetGuid(0),
                    ProjectNo = r.IsDBNull(1) ? "" : r.GetString(1),
                    Subject = r.IsDBNull(2) ? "" : r.GetString(2),
                    CreatedAt = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3),
                    SentAt = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                    SharePointUrl = r.IsDBNull(5) ? "" : r.GetString(5),
                    Type = r.IsDBNull(6) ? "Transmittal" : r.GetString(6),
                    OpenCount = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    ClickCount = r.IsDBNull(8) ? 0 : r.GetInt32(8)
                });
            }
        }

        private async Task LoadActivityAsync(Guid transmittalId)
        {
            _activity.Clear();

            if (string.IsNullOrWhiteSpace(_cs))
                throw new InvalidOperationException("Missing connection string 'KorTransmittalsDb'.");

            const string sql = @"
SELECT 'Open' AS Kind, RecipientEmail, OpenedAt   AS OccurredAt, ClientIp, UserAgent, CAST(NULL AS nvarchar(1024)) AS Referer
FROM dbo.OpenEvents WHERE TransmittalId = @id
UNION ALL
SELECT 'Click'      , RecipientEmail, ClickedAt  , ClientIp, UserAgent, Referer
FROM dbo.ClickEvents WHERE TransmittalId = @id
ORDER BY OccurredAt DESC;";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@id", transmittalId);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                _activity.Add(new ActivityRow
                {
                    Kind = r.IsDBNull(0) ? "" : r.GetString(0),
                    RecipientEmail = r.IsDBNull(1) ? "" : r.GetString(1),
                    OccurredAt = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2),
                    ClientIp = r.IsDBNull(3) ? "" : r.GetString(3),
                    UserAgent = r.IsDBNull(4) ? "" : r.GetString(4),
                    Referer = r.IsDBNull(5) ? "" : r.GetString(5)
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
