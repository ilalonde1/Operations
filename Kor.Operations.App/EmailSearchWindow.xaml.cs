#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kor.EmailSearch.Core;   // EmailSearchService, SearchResult
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations
{
    public partial class EmailSearchWindow : Window
    {
        private readonly IEmailSearchService _svc;
        private int _page = 1;
        private int _pageSize = 50;
        private int _totalRows = 0;

        // ------------------- Project list for autocomplete -------------------

        private sealed class ProjectInfo
        {
            public string Code { get; set; } = string.Empty;        // e.g. "00171-04"
            public string DisplayName { get; set; } = string.Empty; // e.g. "00171-04 (Some Project Name)"
        }

        private readonly List<ProjectInfo> _projects = new();
        private readonly string _projectsRoot;

        public EmailSearchWindow(IEmailSearchService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            InitializeComponent();

            // Wire a sensible default header immediately; HeaderLoader refines it on load
            HeaderBar.UserDisplayName = Environment.UserName.Replace('.', ' ').Replace('_', ' ').Trim();
            HeaderBar.UserEmail = $"{Environment.UserName}@korstructural.com";
            _projectsRoot = GetRequiredProjectsRoot();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Standard header (cached Deltek name + headshot)
            await HeaderLoader.ApplyAsync(HeaderBar);

            // Load projects from disk for the project autocomplete
            LoadProjects();

            // Disable paging buttons until first search
            PrevBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
            UpdatePagerText();
        }

        // ------------------- Project loading (from disk) ---------------------

        private void LoadProjects()
        {
            _projects.Clear();
            try
            {
                if (!Directory.Exists(_projectsRoot))
                    return;

                var categories = Directory.GetDirectories(_projectsRoot);

                foreach (var category in categories)
                {
                    string[] subfolders;
                    try
                    {
                        subfolders = Directory.GetDirectories(category);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var folder in subfolders)
                    {
                        var name = Path.GetFileName(folder);
                        if (string.IsNullOrEmpty(name) || name.Length < 8)
                            continue;

                        // Skip template/training projects where the number prefix uses x-placeholders,
                        // e.g. 000xx-01, 30xxx-01, etc.
                        var prefix = name.Substring(0, Math.Min(5, name.Length));
                        if (prefix.IndexOf('x', StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        var codePart = name.Substring(0, 8);
                        if (!codePart.Contains("-"))
                            continue;

                        _projects.Add(new ProjectInfo
                        {
                            Code = codePart,
                            DisplayName = name
                        });
                    }
                }
            }
            catch
            {
                // If this fails for any reason, we just won't have project autocomplete.
                // The FTS search will still work normally.
            }
        }

        private static string GetRequiredProjectsRoot()
        {
            var projectsRoot = Kor.Operations.Services.AppServices.Get<StorageOptions>().ProjectsRoot.Trim();
            return !string.IsNullOrWhiteSpace(projectsRoot)
                ? projectsRoot
                : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");
        }

        // --------------------- Search flow ---------------------

        private async Task QueryAsync()
        {
            // parse paging safely
            _ = int.TryParse(PageBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _page);
            if (_page <= 0) _page = 1;

            _ = int.TryParse(PageSizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _pageSize);
            if (_pageSize <= 0) _pageSize = 50;

            // FTS text (subject, body, addresses, filenames etc.)
            string? fts = string.IsNullOrWhiteSpace(FtsBox.Text) ? null : FtsBox.Text.Trim();

            // Project text -> project code (first 8 chars like 00171-04)
            string? project = null;
            if (!string.IsNullOrWhiteSpace(ProjectBox.Text))
            {
                var text = ProjectBox.Text.Trim();

                // 1. User picked an item from the list — DisplayName matches exactly
                var match = _projects.Find(p =>
                    p.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    project = match.Code;
                }
                else if (text.Length >= 8 && text[5] == '-')
                {
                    // 2. User typed a code directly (00171-04 or 00171-04 Something ...)
                    project = text.Substring(0, 8);
                }
                else
                {
                    // 3. Try a forgiving Code match (covers partials like "00171" or trailing whitespace)
                    var codeMatch = _projects.Find(p =>
                        p.Code.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (codeMatch != null)
                        project = codeMatch.Code;
                }

                // If we still couldn't resolve, don't silently fall back to a full-table
                // scan — tell the user. Clearing the box is the explicit way to search all.
                if (project == null)
                {
                    MessageBox.Show(
                        this,
                        $"Project filter \"{text}\" didn't match any project.\n\n" +
                        "Pick a project from the autocomplete list, type a full code (e.g. 00171-04), " +
                        "or clear the Project box to search across all projects.",
                        "Project not recognized",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            DateTime? fromUtc = ToUtc(FromDate.SelectedDate);
            DateTime? toUtc = ToUtcEndOfDay(ToDate.SelectedDate);

            bool? hasAttach = HasAttachBox.IsChecked == true ? true : (bool?)null;

            try
            {
                LoadingOverlay.Show("Searching...");

                var result = await _svc.SearchAsync(
                    query: fts,
                    project: project,
                    fromUtc: fromUtc,
                    toUtc: toUtc,
                    hasAttach: hasAttach,
                    page: _page,
                    pageSize: _pageSize);

                _totalRows = result.TotalRows;
                Grid.ItemsSource = result.Rows;

                UpdatePagerButtons();
                UpdatePagerText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Hide();
            }
        }

        private static DateTime? ToUtc(DateTime? local)
        {
            if (local == null) return null;
            var dt = DateTime.SpecifyKind(local.Value, DateTimeKind.Local);
            return dt.ToUniversalTime();
        }

        private static DateTime? ToUtcEndOfDay(DateTime? local)
        {
            if (local == null) return null;
            var endLocal = DateTime.SpecifyKind(local.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local);
            return endLocal.ToUniversalTime();
        }

        // --------------------- UI events ---------------------

        private async void OnSearch(object sender, RoutedEventArgs e)
        {
            PageBox.Text = "1";
            await QueryAsync();
        }

        private async void OnClear(object sender, RoutedEventArgs e)
        {
            ProjectBox.Text = string.Empty;
            FtsBox.Clear();
            FromDate.SelectedDate = null;
            ToDate.SelectedDate = null;
            HasAttachBox.IsChecked = false;

            Grid.ItemsSource = null;
            _totalRows = 0;
            _page = 1;
            PageBox.Text = "1";
            UpdatePagerButtons();
            UpdatePagerText();

            await Task.CompletedTask;
        }

        private async void OnPrev(object sender, RoutedEventArgs e)
        {
            if (_page > 1)
            {
                _page--;
                PageBox.Text = _page.ToString(CultureInfo.InvariantCulture);
                await QueryAsync();
            }
        }

        private async void OnNext(object sender, RoutedEventArgs e)
        {
            var maxPage = MaxPage();
            if (_page < maxPage)
            {
                _page++;
                PageBox.Text = _page.ToString(CultureInfo.InvariantCulture);
                await QueryAsync();
            }
        }

        private async void FtsBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PageBox.Text = "1";
                await QueryAsync();
                e.Handled = true;
            }
        }

        private bool _suppressProjectFilter;

        private void ProjectBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressProjectFilter) return;
            var text = ProjectBox.Text.Trim();
            if (text.Length < 2)
            {
                ProjectPopup.IsOpen = false;
                return;
            }

            var filtered = _projects
                .Where(p => p.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.DisplayName)
                .Take(20)
                .ToList();

            if (filtered.Count > 0)
            {
                ProjectList.ItemsSource = filtered;
                ProjectPopup.IsOpen = true;
            }
            else
            {
                ProjectPopup.IsOpen = false;
            }
        }

        private void SelectProject(string displayName)
        {
            _suppressProjectFilter = true;
            ProjectBox.Text = displayName;
            _suppressProjectFilter = false;
            ProjectPopup.IsOpen = false;
            ProjectBox.CaretIndex = ProjectBox.Text.Length;
        }

        private void ProjectList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (ProjectList.SelectedItem is string selected)
                SelectProject(selected);
        }

        private void ProjectList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ProjectList.SelectedItem is string selected)
            {
                SelectProject(selected);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ProjectPopup.IsOpen = false;
                ProjectBox.Focus();
                ProjectBox.CaretIndex = ProjectBox.Text.Length;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ProjectList.SelectedIndex <= 0)
            {
                // Arrow up from top of list returns focus to TextBox
                ProjectList.SelectedIndex = -1;
                ProjectBox.Focus();
                ProjectBox.CaretIndex = ProjectBox.Text.Length;
                e.Handled = true;
            }
        }

        private async void ProjectBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ProjectPopup.IsOpen && ProjectList.SelectedItem is string selected)
                {
                    SelectProject(selected);
                }
                else
                {
                    ProjectPopup.IsOpen = false;
                    PageBox.Text = "1";
                    await QueryAsync();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down && ProjectPopup.IsOpen)
            {
                ProjectList.Focus();
                if (ProjectList.Items.Count > 0)
                    ProjectList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ProjectPopup.IsOpen)
            {
                ProjectList.Focus();
                if (ProjectList.Items.Count > 0)
                    ProjectList.SelectedIndex = ProjectList.Items.Count - 1;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ProjectPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void OnOpenEmail(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b &&
                b.Tag is string path &&
                !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    // Prefer Outlook to open .msg
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "outlook.exe",
                        Arguments = $"/f \"{path}\"",
                        UseShellExecute = false
                    });
                }
                catch
                {
                    // Fallback: shell open
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
            }
        }

        // --------------------- Pager helpers ---------------------

        private int MaxPage()
        {
            if (_pageSize <= 0) return 1;
            var pages = (_totalRows + _pageSize - 1) / _pageSize;
            return Math.Max(1, pages);
        }

        private void UpdatePagerButtons()
        {
            var maxPage = MaxPage();
            PrevBtn.IsEnabled = _page > 1;
            NextBtn.IsEnabled = _page < maxPage && _totalRows > 0;
        }

        private void UpdatePagerText()
        {
            var maxPage = MaxPage();
            var totalText = _totalRows > 0
                ? $"{_totalRows:n0} result(s) — Page {_page} of {maxPage}"
                : "0 result(s)";
            TotalText.Text = totalText;
        }
    }
}
