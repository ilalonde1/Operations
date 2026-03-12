#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kor.Operations
{
    public partial class QuickTransferWindow : Window
    {
        private static readonly AppConfig AppConfig = new()
        {
            ProjectsRoot = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.ProjectsRoot] ?? string.Empty).Trim()
        };

        private readonly List<TransmittalFile> _files = new();

        private CancellationTokenSource? _uploadCts;

        private string _userUpn = string.Empty;
        private readonly IUserPreferencesStore? _userPrefsStore;
        private readonly QuickTransferRunner _quickTransferRunner;
        private UserPreferences? _userPrefs;

        // --------------------------------------------------------------------
        // Project search
        // --------------------------------------------------------------------

        private sealed class ProjectEntry
        {
            public string DisplayName { get; set; } = string.Empty; // "30694-01 Some Job"
            public string Code { get; set; } = string.Empty;        // first 8 chars
                                                                    // NEW: full path to the project folder
            public string FolderPath { get; set; } = string.Empty;
        }

        private readonly List<ProjectEntry> _allProjects = new();
        private readonly List<ProjectEntry> _filteredProjects = new();

        private ProjectEntry? _selectedProject;
        private int _highlightIndex = -1;

        public QuickTransferWindow(IUserPreferencesStore? userPrefsStore, QuickTransferRunner quickTransferRunner)
        {
            _userPrefsStore = userPrefsStore;
            _quickTransferRunner = quickTransferRunner ?? throw new ArgumentNullException(nameof(quickTransferRunner));
            InitializeComponent();
            InitializeRequest(null, null, null, null);

            LoadProjects();
            ApplyProjectFilter(string.Empty); // sets list to empty and collapsed
        }

        public void InitializeRequest(string? fromEmail, string? to, string? cc, string? subject)
        {
            FromBox.Text = !string.IsNullOrWhiteSpace(fromEmail)
                ? fromEmail
                : GetCurrentUserEmailFallback();

            ToBox.Text = to ?? string.Empty;
            CcBox.Text = cc ?? string.Empty;
            SubjectBox.Text = subject ?? string.Empty;

            var domainDefault = "korstructural.com";
            if (!string.IsNullOrWhiteSpace(FromBox.Text) && FromBox.Text.Contains("@"))
            {
                _userUpn = FromBox.Text.Trim();
            }
            else
            {
                var user = Environment.UserName;
                _userUpn = string.IsNullOrWhiteSpace(user)
                    ? $"noreply@{domainDefault}"
                    : $"{user}@{domainDefault}";
            }
        }

        // This is what QuickTransferRunner uses via ProjectDisplay
        public string ProjectText
        {
            get
            {
                if (_selectedProject != null &&
                    !string.IsNullOrWhiteSpace(_selectedProject.DisplayName))
                {
                    return _selectedProject.DisplayName.Trim();
                }

                return string.Empty;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeRemarksEditorAsync();
            await LoadUserPreferencesAsync();
        }

        // HTML editor setup ---------------------------------------------------

        private async Task InitializeRemarksEditorAsync()
        {
            try
            {
                await RemarksEditor.EnsureCoreWebView2Async();
                RemarksEditor.NavigationStarting += (_, e) =>
                {
                    if (!e.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                        !e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
                    {
                        e.Cancel = true;
                    }
                };

                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var htmlPath = Path.Combine(exeDir, "Assets", "QuickRemarksEditor.html");

                if (File.Exists(htmlPath))
                {
                    RemarksEditor.Source = new Uri(htmlPath);
                }
                else
                {
                    const string fallback = "<html><body><textarea style='width:100%;height:100%;'></textarea></body></html>";
                    RemarksEditor.CoreWebView2.NavigateToString(fallback);
                }
            }
            catch
            {
                // do not break window if WebView2 fails
            }
        }

        private async Task<string> GetRemarksHtmlAsync()
        {
            if (RemarksEditor?.CoreWebView2 == null)
                return string.Empty;

            try
            {
                var result = await RemarksEditor.CoreWebView2.ExecuteScriptAsync(
                    "window.getEditorHtml && window.getEditorHtml();");

                if (string.IsNullOrWhiteSpace(result) ||
                    result == "null" ||
                    result == "undefined")
                {
                    return string.Empty;
                }

                return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Preferences ---------------------------------------------------------

        private async Task LoadUserPreferencesAsync()
        {
            if (_userPrefsStore == null || string.IsNullOrWhiteSpace(_userUpn))
                return;

            try
            {
                _userPrefs = await _userPrefsStore.GetAsync(_userUpn);
            }
            catch
            {
                // non fatal
            }
        }

        // UI handlers ---------------------------------------------------------
        // Returns the folder for the currently selected project, or null if none.
        private string? GetSelectedProjectFolder()
        {
            var proj = _selectedProject;
            if (proj == null)
                return null;

            if (!string.IsNullOrWhiteSpace(proj.FolderPath) &&
                Directory.Exists(proj.FolderPath))
            {
                return proj.FolderPath;
            }

            return null;
        }

        private void AddFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All files|*.*",
                Multiselect = true
            };

            // NEW: if a project is selected, start in its folder
            var projectFolder = GetSelectedProjectFolder();
            if (!string.IsNullOrWhiteSpace(projectFolder) &&
                Directory.Exists(projectFolder))
            {
                dlg.InitialDirectory = projectFolder;
            }

            if (dlg.ShowDialog(this) == true)
            {
                foreach (var path in dlg.FileNames.Where(File.Exists))
                {
                    if (_files.Any(f => string.Equals(f.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var fi = new FileInfo(path);
                    _files.Add(new TransmittalFile
                    {
                        LocalPath = path,
                        FileName = fi.Name,
                        SizeBytes = fi.Length
                    });
                }

                RefreshFileList();
            }
        }


        private void RemoveFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = FilesList.SelectedItems.Cast<QuickFileRow>().ToList();
            if (selected.Count == 0) return;

            foreach (var row in selected)
            {
                var match = _files.FirstOrDefault(f =>
                    string.Equals(f.LocalPath, row.LocalPath, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    _files.Remove(match);
            }

            RefreshFileList();
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                SendBtn.IsEnabled = false;
                CancelUploadBtn.IsEnabled = true;
                StatusText.Text = "Sending transfer...";
                UpdateProgressBar(0);

                _uploadCts?.Cancel();
                _uploadCts = new CancellationTokenSource();

                var remarksHtml = await GetRemarksHtmlAsync();

                string? signatureHtml = null;
                if (_userPrefs != null &&
                    !string.IsNullOrWhiteSpace(_userPrefs.EmailSignatureHtml))
                {
                    signatureHtml = _userPrefs.EmailSignatureHtml;
                }

                var request = BuildRequest();
                request.RemarksHtml = remarksHtml;
                request.SignatureHtml = signatureHtml;

                var progress = new Progress<(string file, long sent, long total)>(p =>
                {
                    if (p.total <= 0) return;
                    var percent = (double)p.sent * 100.0 / p.total;
                    UpdateProgressBar(percent);
                });

                await _quickTransferRunner.RunAsync(
                    request,
                    _uploadCts.Token,
                    progress);

                StatusText.Text = "Transfer sent.";
                UpdateProgressBar(100);

                Close();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Canceled.";
                UpdateProgressBar(0);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error.";
                MessageBox.Show(this,
                    "Quick transfer failed:\n\n" + ex.Message,
                    "Quick Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SendBtn.IsEnabled = true;
                CancelUploadBtn.IsEnabled = false;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _uploadCts?.Cancel();
            Close();
        }

        private void CancelUploadBtn_Click(object sender, RoutedEventArgs e)
        {
            _uploadCts?.Cancel();
            CancelUploadBtn.IsEnabled = false;
        }

        // --------------------------------------------------------------------
        // Projects: load + filter
        // --------------------------------------------------------------------

        private void LoadProjects()
        {
            _allProjects.Clear();
            var projectsRoot = GetRequiredProjectsRoot();

            try
            {
                if (!Directory.Exists(projectsRoot))
                    return;

                foreach (var categoryDir in Directory.GetDirectories(projectsRoot))
                {
                    var categoryName = Path.GetFileName(categoryDir);

                    if (string.Equals(categoryName, "00 Templates", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] projectDirs;
                    try
                    {
                        projectDirs = Directory.GetDirectories(categoryDir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var projectDir in projectDirs)
                    {
                        var name = Path.GetFileName(projectDir);
                        if (string.IsNullOrEmpty(name) || name.Length < 8)
                            continue;

                        var prefix = name.Substring(0, Math.Min(5, name.Length));
                        if (prefix.IndexOf('x', StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        var codePart = name.Substring(0, 8);
                        if (!codePart.Contains("-"))
                            continue;

                        _allProjects.Add(new ProjectEntry
                        {
                            DisplayName = name,
                            Code = codePart,
                            FolderPath = projectDir     // NEW
                        });

                    }
                }

                _allProjects.Sort((a, b) =>
                    string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // ignore – Quick Transfer should still work even if this fails
            }
        }

        private static string GetRequiredProjectsRoot() => !string.IsNullOrWhiteSpace(AppConfig.ProjectsRoot) ? AppConfig.ProjectsRoot : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");

        private void ApplyProjectFilter(string term)
        {
            _filteredProjects.Clear();

            IEnumerable<ProjectEntry> source;

            if (string.IsNullOrWhiteSpace(term))
            {
                source = Enumerable.Empty<ProjectEntry>();
            }
            else
            {
                string t = term.Trim();
                string lower = t.ToLowerInvariant();

                source = _allProjects.Where(p =>
                    (!string.IsNullOrEmpty(p.DisplayName) &&
                        p.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.Code) &&
                        p.Code.StartsWith(t, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(p.DisplayName) &&
                        p.DisplayName.ToLowerInvariant().Contains(lower)));
            }

            _filteredProjects.AddRange(source);
            ProjectsList.ItemsSource = null;
            ProjectsList.ItemsSource = _filteredProjects;

            if (_filteredProjects.Count > 0)
            {
                _highlightIndex = 0;
                ProjectsList.SelectedIndex = _highlightIndex;
                ProjectsList.Visibility = Visibility.Visible;
                ProjectsList.ScrollIntoView(ProjectsList.SelectedItem);
            }
            else
            {
                _highlightIndex = -1;
                ProjectsList.SelectedIndex = -1;
                ProjectsList.Visibility = Visibility.Collapsed;
            }
        }

        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyProjectFilter(ProjectSearchBox.Text);
        }

        // Centralized keyboard handling: Up/Down/Enter/Esc
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Only care when focus is on search box or list
            if (!ReferenceEquals(Keyboard.FocusedElement, ProjectSearchBox) &&
                !ReferenceEquals(Keyboard.FocusedElement, ProjectsList))
            {
                return;
            }

            bool hasList = ProjectsList.Visibility == Visibility.Visible &&
                           _filteredProjects.Count > 0;

            if (!hasList && e.Key != Key.Escape)
            {
                // Nothing to navigate
                return;
            }

            if (e.Key == Key.Down)
            {
                if (!hasList) return;

                if (_highlightIndex < _filteredProjects.Count - 1)
                    _highlightIndex++;
                else
                    _highlightIndex = 0;

                ProjectsList.SelectedIndex = _highlightIndex;
                ProjectsList.ScrollIntoView(ProjectsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                if (!hasList) return;

                if (_highlightIndex > 0)
                    _highlightIndex--;
                else
                    _highlightIndex = _filteredProjects.Count - 1;

                ProjectsList.SelectedIndex = _highlightIndex;
                ProjectsList.ScrollIntoView(ProjectsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (hasList &&
                    _highlightIndex >= 0 &&
                    _highlightIndex < _filteredProjects.Count)
                {
                    SetSelectedProject(_filteredProjects[_highlightIndex]);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                // Hide suggestions, keep window open, keep focus in search box
                if (ProjectsList.Visibility == Visibility.Visible)
                {
                    ProjectsList.Visibility = Visibility.Collapsed;
                    ProjectsList.SelectedIndex = -1;
                    _highlightIndex = -1;
                    ProjectSearchBox.Focus();
                    ProjectSearchBox.CaretIndex = ProjectSearchBox.Text.Length;
                    e.Handled = true;
                }
                // If list already hidden, let Esc bubble (Cancel button etc).
                return;
            }
        }

        private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // no auto-commit here; commit is Enter / double-click
        }

        private void ProjectsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectsList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
            }
        }

        private void SetSelectedProject(ProjectEntry? project)
        {
            _selectedProject = project;

            if (project != null)
            {
                ProjectSearchBox.Text = project.DisplayName;
                ProjectSearchBox.CaretIndex = ProjectSearchBox.Text.Length;
            }

            ProjectsList.Visibility = Visibility.Collapsed;
            ProjectsList.SelectedIndex = -1;
            _highlightIndex = -1;
        }

        // --------------------------------------------------------------------
        // Other helpers
        // --------------------------------------------------------------------

        private void RefreshFileList()
        {
            var rows = _files
                .Select(f => new QuickFileRow
                {
                    FileName = f.FileName,
                    LocalPath = f.LocalPath,
                    SizeKb = f.SizeBytes / 1024
                })
                .ToList();

            FilesList.ItemsSource = null;
            FilesList.ItemsSource = rows;
        }

        private bool ValidateInputs()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ProjectText))
                errors.Add("Project is required.");

            if (_files.Count == 0)
                errors.Add("At least one file must be added.");

            if (string.IsNullOrWhiteSpace(ToBox.Text) &&
                string.IsNullOrWhiteSpace(CcBox.Text))
            {
                errors.Add("At least one recipient (To or Cc) is required.");
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join("\n", errors),
                    "Missing information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private QuickTransferRequest BuildRequest()
        {
            return new QuickTransferRequest
            {
                FromEmail = FromBox.Text?.Trim() ?? string.Empty,
                ProjectDisplay = ProjectText,
                To = ToBox.Text?.Trim() ?? string.Empty,
                Cc = CcBox.Text?.Trim() ?? string.Empty,
                Subject = SubjectBox.Text?.Trim() ?? string.Empty,
                Files = _files.ToList()
            };
        }

        private static string GetCurrentUserEmailFallback()
        {
            try
            {
                string user = Environment.UserName;

                int idx = user.IndexOf('\\');
                if (idx >= 0 && idx < user.Length - 1)
                {
                    user = user[(idx + 1)..];
                }

                if (string.IsNullOrWhiteSpace(user))
                    return string.Empty;

                return user + "@korstructural.com";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void UpdateProgressBar(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            UploadProgress.Value = percent;
            PercentText.Text = percent > 0 ? $"{percent:0}%" : string.Empty;
        }

        private sealed class QuickFileRow
        {
            public string FileName { get; set; } = string.Empty;
            public long SizeKb { get; set; }
            public string LocalPath { get; set; } = string.Empty;
        }
    }

    public sealed class QuickTransferRequest
    {
        public string FromEmail { get; set; } = string.Empty;
        public string ProjectDisplay { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Cc { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;

        public string? RemarksHtml { get; set; }
        public string? SignatureHtml { get; set; }

        public List<TransmittalFile> Files { get; set; } = new();
    }
}

