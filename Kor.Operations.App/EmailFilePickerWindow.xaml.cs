#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.App.Email;
using Kor.Operations.App.Options;
using Kor.Operations.Services; // HeaderLoader
using Microsoft.Win32;               // SaveFileDialog (still used elsewhere if needed)
using MessageBox = System.Windows.MessageBox;   // WPF MessageBox
using Kor.Operations.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kor.Operations
{
    public partial class EmailFilePickerWindow : Window
    {
        private readonly List<string> _incomingFiles;
        private readonly List<ProjectEntry> _allProjects = new();
        private readonly List<ProjectEntry> _filteredProjects = new();
        private readonly List<ProjectEntry> _favoriteProjects = new();

        private static readonly AppConfig AppConfig = new()
        {
            ProjectsRoot = Kor.Operations.Services.AppServices.Get<StorageOptions>().ProjectsRoot.Trim()
        };

        // Simple debug log for MsgReader behavior + indexing
        private static readonly string DebugLogPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KorTransmittals",
                "Logs",
                "EmailFilePicker_MsgReaderDebug.txt");

        private static void DebugLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath)!);

                File.AppendAllText(
                    DebugLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // ignore logging failures
            }
        }

        private sealed class EmailEntry
        {
            public string FullPath { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
        }

        // Favorites plumbing
        private readonly string _userUpn;
        private readonly FavoriteProjectsService _favoriteProjectsService;
        private ProjectEntry? _selectedProject;
        public string? SelectedProjectNo => _selectedProject?.Code;
        public bool FiledSuccessfully { get; private set; }
        public IReadOnlyList<string> FiledSourcePaths { get; private set; } = Array.Empty<string>();

        private readonly EmailSubjectExtractor _subjectExtractor;
        private readonly ProjectFolderCatalogService _catalogService;
        private readonly EmailFilingService _filingService;
        private readonly EmailAttachmentService _attachmentService;
        private readonly FolderPickerService _folderPickerService;
        private readonly ILogger<EmailFilePickerWindow> _logger;

        // Encoding bootstrap for MsgReader (.NET 8 needs this for 1252 etc.)
        private static bool _encodingsRegistered;

        private static void EnsureCodePagesEncodingRegistered()
        {
            if (_encodingsRegistered)
                return;

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _encodingsRegistered = true;
                DebugLog("CodePagesEncodingProvider registered.");
            }
            catch (Exception ex)
            {
                // If this fails, we log it once and carry on (MsgReader will still throw).
                DebugLog($"Encoding.RegisterProvider failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal EmailFilePickerWindow(FavoriteProjectsService favoriteProjectsService, EmailSubjectExtractor subjectExtractor, ProjectFolderCatalogService catalogService, EmailFilingService filingService, EmailAttachmentService attachmentService, FolderPickerService folderPickerService, ILogger<EmailFilePickerWindow> logger)
        {
            _favoriteProjectsService = favoriteProjectsService ?? throw new ArgumentNullException(nameof(favoriteProjectsService));
            _subjectExtractor = subjectExtractor ?? throw new ArgumentNullException(nameof(subjectExtractor));
            _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
            _filingService = filingService ?? throw new ArgumentNullException(nameof(filingService));
            _attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
            _folderPickerService = folderPickerService ?? throw new ArgumentNullException(nameof(folderPickerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _incomingFiles = new List<string>();

            InitializeComponent();

            // Make sure legacy codepages (Windows-1252 etc.) are available
            EnsureCodePagesEncodingRegistered();

            // User UPN (same logic as PreferencesWindow)
            var overrideUpn = Kor.Operations.Services.AppServices.Get<UserOptions>().UserUpnOverride;
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

            // existing behavior
            Loaded += EmailFilePickerWindow_Loaded;
            // header (Deltek avatar, name, email)
            Loaded += EmailFilePickerWindow_Loaded_Header;
        }

        public void SetIncomingFiles(IEnumerable<string> emailFiles)
        {
            _incomingFiles.Clear();
            if (emailFiles == null)
                return;

            _incomingFiles.AddRange(emailFiles.Where(f => !string.IsNullOrWhiteSpace(f)));
        }

        // --------------------------------------------------------------------
        // Header initialization
        // --------------------------------------------------------------------
        private async void EmailFilePickerWindow_Loaded_Header(object? sender, RoutedEventArgs e)
        {
            try
            {
                await HeaderLoader.ApplyAsync(HeaderBar);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"HeaderLoader failed in EmailFilePickerWindow: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async void EmailFilePickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateEmailList();
            LoadProjects();
            ApplyProjectFilter(string.Empty);

            // Load favorites after all-projects are known so we can map codes -> full paths
            await LoadFavoritesAsync();

            StatusText.Text = $"Ready | {_incomingFiles.Count} email(s) to file";
        }

        // Helper to clean filename-based subject when MsgReader returns nothing
        private static string CleanFilenameSubject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // Raw will look like: "2025-11-19 0915 - Nelson sent a message"
            // Strip everything up to and including the first " - "
            int dashIndex = raw.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex > 0)
            {
                // Assume prefix is date/time and drop it
                raw = raw.Substring(dashIndex + 3);
            }

            raw = raw.Replace("_", " ");
            return raw.Trim();
        }

        // ====================================================================
        // Emails list
        // ====================================================================

        private void PopulateEmailList()
        {
            var items = new List<EmailEntry>();

            foreach (var path in _incomingFiles)
            {
                if (!File.Exists(path))
                    continue;

                string subject = _subjectExtractor.ExtractSubject(path).Trim();

                // Fallback: if MsgReader could not get a subject, use a cleaned filename
                if (string.IsNullOrWhiteSpace(subject))
                {
                    var fileNameNoExt = Path.GetFileNameWithoutExtension(path);
                    DebugLog($"FALLBACK: Using filename '{fileNameNoExt}' for {path}");
                    subject = CleanFilenameSubject(fileNameNoExt);
                }

                items.Add(new EmailEntry
                {
                    FullPath = path,
                    Subject = subject
                });
            }

            EmailsList.ItemsSource = items;
        }

        // ====================================================================
        // Projects list (All Projects)
        // ====================================================================

        private void LoadProjects()
        {
            _allProjects.Clear();
            var projectsRoot = _catalogService.ProjectsRoot;

            try
            {
                _allProjects.AddRange(_catalogService.LoadProjects());

                if (_allProjects.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "No project folders found under:\n" + projectsRoot,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    StatusText.Text = "No project folders found.";
                }
                else
                {
                    StatusText.Text = $"Loaded {_allProjects.Count} projects.";
                }
            }
            catch (DirectoryNotFoundException)
            {
                MessageBox.Show(
                    this,
                    $"Projects root not found:\n{projectsRoot}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text = "Projects root not found.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Error loading projects:\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text = "Error loading projects.";
            }

            // keep your existing filtered/binding logic here...
        }


        private void ApplyProjectFilter(string term)
        {
            _filteredProjects.Clear();
            _filteredProjects.AddRange(_catalogService.ApplyProjectFilter(_allProjects, term));
            ProjectsList.ItemsSource = null;
            ProjectsList.ItemsSource = _filteredProjects;
        }

        private void ProjectSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyProjectFilter(ProjectSearchBox.Text);
        }

        private void ProjectSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ProjectsList.Items.Count > 0)
            {
                ProjectsList.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        // ====================================================================
        // Favorites (My Favorite Projects)
        // ====================================================================

        private async Task LoadFavoritesAsync()
        {
            _favoriteProjects.Clear();

            try
            {
                _favoriteProjects.AddRange(
                    await _favoriteProjectsService.LoadFavoritesAsync(_userUpn, _allProjects));

                FavoritesList.ItemsSource = null;
                FavoritesList.ItemsSource = _favoriteProjects;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not load favourite projects:\n{ex.Message}",
                    "Favourites",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // Selection helpers
        // ====================================================================

        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return "";
            var idx = user.IndexOf('\\');
            return idx >= 0 && idx < user.Length - 1 ? user[(idx + 1)..] : user;
        }

        private void SetSelectedProject(ProjectEntry? project)
        {
            _selectedProject = project;
            SelectedProjectBox.Text = project?.DisplayName ?? string.Empty;
        }

        private void ProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ProjectsList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
            }
        }

        private void FavoritesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (FavoritesList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
            }
        }

        private async void ProjectsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectsList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
                await FileSelectedEmailsAsync();
            }
        }

        private async void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FavoritesList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
                await FileSelectedEmailsAsync();
            }
        }

        // ====================================================================
        // Filing logic
        // ====================================================================

        private async void FileButton_Click(object sender, RoutedEventArgs e)
        {
            await FileSelectedEmailsAsync();
        }

        private async Task FileSelectedEmailsAsync()
        {
            if (_selectedProject == null)
            {
                MessageBox.Show(this,
                    "Please select a project (from All Projects or My Favourite Projects).",
                    "Select Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var selectedProject = _selectedProject;

            if (_incomingFiles.Count == 0)
            {
                MessageBox.Show(this,
                    "There are no email files to file.",
                    "Nothing to File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string monthFolder = Path.Combine(
                selectedProject.FullPath,
                "Newforma",
                "email",
                DateTime.Now.ToString("yyyy-MM"));

            // read checkbox once
            bool saveAttachments = SaveAttachmentsCheckBox.IsChecked == true;

            EmailFilingResult result;
            try
            {
                result = await _filingService.FileEmailsAsync(_incomingFiles, monthFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not create Newforma\\email folder:\n" + monthFolder + "\n\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            FiledSourcePaths = result.FiledSourcePaths ?? Array.Empty<string>();

            if (saveAttachments)
            {
                foreach (var destPath in result.FiledPaths)
                {
                    try
                    {
                        EnsureCodePagesEncodingRegistered();

                        if (_subjectExtractor.EmailHasAttachments(destPath))
                        {
                            string projectRoot = selectedProject.FullPath;

                            string? folder = PromptForAttachmentFolder(destPath, projectRoot);
                            if (!string.IsNullOrWhiteSpace(folder))
                            {
                                var attachmentResult = await _attachmentService.SaveAttachmentsAsync(destPath, folder);
                                StatusText.Text = $"Saved {attachmentResult.SavedCount} attachment(s), skipped {attachmentResult.SkippedCount}.";
                            }
                            else
                            {
                                DebugLog($"User cancelled attachment folder selection for {destPath}; skipping attachments.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Attachment handling failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            int requested = _incomingFiles.Count;
            bool partial = result.FiledCount > 0 && result.FiledCount < requested;
            bool none = result.FiledCount == 0;
            int notIndexed = result.FiledCount - result.IndexedCount;
            bool indexFailures = notIndexed > 0;

            if (none)
            {
                string message = "No emails were filed.";
                if (result.Errors.Count > 0)
                    message += "\n\nErrors:\n" + string.Join("\n", result.Errors.Take(5));

                MessageBox.Show(this,
                    message,
                    "Nothing Filed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = $"Filed {result.FiledCount} of {requested} email(s) to {selectedProject.DisplayName}";

            if (partial)
            {
                string message = $"Filed {result.FiledCount} of {requested} email(s) to:\n{selectedProject.DisplayName}\n\n"
                    + $"{requested - result.FiledCount} email(s) could not be saved.";
                if (result.Errors.Count > 0)
                    message += "\n\nFirst errors:\n" + string.Join("\n", result.Errors.Take(5));
                if (indexFailures)
                    message += $"\n\n{notIndexed} email(s) on disk but NOT in search index — search will not find them until reindexed.";

                MessageBox.Show(this,
                    message,
                    "Filed With Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (indexFailures)
            {
                MessageBox.Show(this,
                    $"Filed {result.FiledCount} email(s) to:\n{selectedProject.DisplayName}\n\n"
                    + $"WARNING: {notIndexed} email(s) saved to disk but NOT added to the search index. "
                    + "They are filed but search will not find them until reindexed. Check filing logs.",
                    "Filed — Search Index Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this,
                    $"Filed {result.FiledCount} email(s) to:\n{selectedProject.DisplayName}",
                    "Filed Successfully",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            FiledSuccessfully = true;
            Close();
        }

        // prompt user for where to save this email's attachments (FOLDER chooser only)
        private string? PromptForAttachmentFolder(string emailPath, string projectRootFolder)
        {
            string fileNameOnly = Path.GetFileName(emailPath);
            string preview = fileNameOnly;

            // For the description only, show subject + file name
            try
            {
                string subject = _subjectExtractor.ExtractSubject(emailPath);
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    preview = $"{subject} ({fileNameOnly})";
                }
            }
            catch
            {
                // ignore; fall back to filename only
            }

            string title = $"Choose folder to save attachments for:\n{preview}";

            // Preferred starting point: the project root UNC path
            string initialFolder = projectRootFolder;

            // Fall back to the email's folder if the project root isn't valid
            if (string.IsNullOrWhiteSpace(initialFolder) || !Directory.Exists(initialFolder))
            {
                initialFolder = Path.GetDirectoryName(emailPath) ?? string.Empty;
            }

            // Final fallback: Desktop
            if (string.IsNullOrWhiteSpace(initialFolder) || !Directory.Exists(initialFolder))
            {
                initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            return _folderPickerService.PickFolder(title, initialFolder);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
