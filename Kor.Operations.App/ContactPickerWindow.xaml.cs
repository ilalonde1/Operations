#nullable enable
using Kor.Operations.Data;
using Kor.Operations.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Kor.Operations
{
    public partial class ContactPickerWindow : Window
    {
        private readonly VantagepointRepository _repo;

        // paging state
        private int _page = 1;
        private int _pageSize = 200;
        private int _totalRows = 0;
        private string? _lastQuery;

        // hard upper limit from source (we still request only when searching)
        private const int MaxRowsFromRepo = 25000;

        public ObservableCollection<ContactView> Items { get; } = new();
        public List<string> SelectedEmails { get; } = new();

        // cache so we don’t re-fetch headshots/display name while app is open
        private static BitmapImage? _cachedAvatar;
        private static string? _cachedDisplayName;

        public Task LoadAsync()
        {
            // Do NOT pre-populate; just ensure UI hints/pager are correct.
            CountTextBlock.Text = "Enter 2+ characters and press Search.";
            UpdatePagerState();
            return Task.CompletedTask;
        }
        public sealed class ContactView
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Company { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;

            public VantagepointRepository.ContactRow? Source { get; init; }
        }

        public ContactPickerWindow(VantagepointRepository repo)
        {
            InitializeComponent();
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // header only; DO NOT auto-load contacts
            CountTextBlock.Text = "Enter 2+ characters and press Search.";
            try
            {
                var sam = Environment.UserName;
                var upnOverride = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.UserUpnOverride];
                var email = string.IsNullOrWhiteSpace(upnOverride) ? $"{sam}@korstructural.com" : upnOverride.Trim();

                string display = _cachedDisplayName ?? await GetDisplayNameFromDeltekOrFallbackAsync(email, sam);
                _cachedDisplayName = display;

                HeaderBar.UserDisplayName = display;
                HeaderBar.UserEmail = email;

                await EnsureAvatarFromDeltekAsync(email);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ContactPicker header init failed: {ex.GetType().Name}: {ex.Message}");
            }
            UpdatePagerState();
        }

        private async Task<string> GetDisplayNameFromDeltekOrFallbackAsync(string email, string sam)
        {
            try
            {
                var provider = new DeltekHeadshotProvider();
                var full = await provider.TryGetEmployeeDisplayNameAsync(email);
                if (!string.IsNullOrWhiteSpace(full)) return full;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Deltek name lookup failed: {ex.Message}");
            }
            return sam.Replace('.', ' ').Replace('_', ' ');
        }

        private async Task EnsureAvatarFromDeltekAsync(string email)
        {
            try
            {
                if (_cachedAvatar != null) { SetHeaderAvatar(_cachedAvatar); return; }

                var provider = new DeltekHeadshotProvider();
                var bmp = await provider.TryGetByEmailAsync(email);
                if (bmp != null)
                {
                    _cachedAvatar = bmp;
                    SetHeaderAvatar(bmp);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Avatar load skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void SetHeaderAvatar(BitmapImage bmp)
        {
            var prop = HeaderBar.GetType().GetProperty("AvatarImageSource");
            if (prop != null && prop.CanWrite) prop.SetValue(HeaderBar, bmp);
        }

        // ---------------- Search + Paging ----------------

        private static readonly Regex TrailingEmailInParens =
            new(@"\s*\(([^)]+@[^)]+)\)\s*$", RegexOptions.Compiled);

        private static string CleanName(string? name)
            => string.IsNullOrWhiteSpace(name) ? string.Empty : TrailingEmailInParens.Replace(name, string.Empty);

        private async Task RunSearchAsync(bool resetPage)
        {
            // read inputs
            var q = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(q) || q.Length < 2)
            {
                Items.Clear();
                _totalRows = 0;
                _lastQuery = null;
                CountTextBlock.Text = "Enter 2+ characters and press Search.";
                UpdatePagerState();
                return;
            }

            if (!int.TryParse(PageBox.Text, out _page) || _page < 1) _page = 1;
            if (!int.TryParse(PageSizeBox.Text, out _pageSize) || _pageSize < 1) _pageSize = 200;
            if (resetPage) _page = 1;

            // fetch from repo (bounded)
            var rows = await _repo.SearchPeopleAsync(q, MaxRowsFromRepo)
                       ?? Enumerable.Empty<VantagepointRepository.ContactRow>();

            // transform + distinct by email
            var all = rows
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Email))
                .Select(r => new ContactView
                {
                    Name = CleanName(r.Name),
                    Email = r.Email ?? string.Empty,
                    Company = r.Company ?? string.Empty,
                    Title = r.Title ?? string.Empty,
                    Source = r
                })
                .GroupBy(v => v.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(v => !string.IsNullOrWhiteSpace(v.Title))
                    .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _totalRows = all.Count;
            _lastQuery = q;

            // page slice
            var skip = (_page - 1) * _pageSize;
            var page = all.Skip(skip).Take(_pageSize).ToList();

            Items.Clear();
            foreach (var v in page) Items.Add(v);

            CountTextBlock.Text = $"{_totalRows:n0} result(s). Showing {page.Count:n0}.";
            TotalText.Text = $"{_totalRows:n0} result(s) — Page {_page} of {Math.Max(1, (int)Math.Ceiling((double)_totalRows / _pageSize))}";
            UpdatePagerState();
        }

        private void UpdatePagerState()
        {
            var maxPage = Math.Max(1, (int)Math.Ceiling(_totalRows / (double)Math.Max(1, _pageSize)));
            PrevBtn.IsEnabled = _page > 1 && _totalRows > 0;
            NextBtn.IsEnabled = _page < maxPage && _totalRows > 0;
        }

        private async void SearchBtn_Click(object sender, RoutedEventArgs e)
            => await RunSearchAsync(resetPage: true);

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            Items.Clear();
            _totalRows = 0;
            _page = 1;
            _lastQuery = null;
            CountTextBlock.Text = "Enter 2+ characters and press Search.";
            TotalText.Text = string.Empty;
            UpdatePagerState();
            SearchBox.Focus();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await RunSearchAsync(resetPage: true);
            }
        }

        private async void OnPrev(object sender, RoutedEventArgs e)
        {
            if (_totalRows == 0) return;
            if (!int.TryParse(PageBox.Text, out _page)) _page = 1;
            if (_page <= 1) return;
            PageBox.Text = (--_page).ToString();
            await RunSearchAsync(resetPage: false);
        }

        private async void OnNext(object sender, RoutedEventArgs e)
        {
            if (_totalRows == 0) return;
            if (!int.TryParse(PageBox.Text, out _page)) _page = 1;
            if (!int.TryParse(PageSizeBox.Text, out _pageSize)) _pageSize = 200;
            var maxPage = Math.Max(1, (int)Math.Ceiling(_totalRows / (double)_pageSize));
            if (_page >= maxPage) return;
            PageBox.Text = (++_page).ToString();
            await RunSearchAsync(resetPage: false);
        }

        // -------------- Selection --------------

        private void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is ContactView vm && !string.IsNullOrWhiteSpace(vm.Email))
            {
                SelectedEmails.Clear();
                SelectedEmails.Add(vm.Email);
                DialogResult = true;
                Close();
            }
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            var emails = ResultsDataGrid.SelectedItems
                .OfType<ContactView>()
                .Select(v => v.Email)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            SelectedEmails.Clear();
            SelectedEmails.AddRange(emails);
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

