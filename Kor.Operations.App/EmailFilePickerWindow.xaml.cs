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
using Kor.Operations.Data;
using MsgReader.Outlook;              // gives you Storage.Message
using Kor.EmailCommon;               // EmailParser
using Microsoft.Win32;               // SaveFileDialog (still used elsewhere if needed)
using MessageBox = System.Windows.MessageBox;   // WPF MessageBox
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment; // alias for Attachment type
using System.Runtime.InteropServices; // for folder picker P/Invoke
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
            ProjectsRoot = ((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<StorageOptions>().ProjectsRoot.Trim()
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

        private sealed class ProjectEntry
        {
            public string FullPath { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty; // first 8 chars
        }

        // Favorites plumbing
        private readonly string _userUpn;
        private readonly PreferencesRepository _prefsRepo;
        private ProjectEntry? _selectedProject;
        public string? SelectedProjectNo => _selectedProject?.Code;

        // KorEmailIndex store (for DB inserts)
        private readonly SqlEmailIndexStore? _emailIndexStore;
        private readonly EmailSubjectExtractor _subjectExtractor;
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

        internal EmailFilePickerWindow(PreferencesRepository preferencesRepository, SqlEmailIndexStore? emailIndexStore, EmailSubjectExtractor subjectExtractor, ILogger<EmailFilePickerWindow> logger)
        {
            _prefsRepo = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
            _emailIndexStore = emailIndexStore;
            _subjectExtractor = subjectExtractor ?? throw new ArgumentNullException(nameof(subjectExtractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _incomingFiles = new List<string>();

            InitializeComponent();

            // Make sure legacy codepages (Windows-1252 etc.) are available
            EnsureCodePagesEncodingRegistered();

            // User UPN (same logic as PreferencesWindow)
            var overrideUpn = ((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<UserOptions>().UserUpnOverride;
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

            if (_emailIndexStore == null)
                DebugLog("KorEmailIndex connection string missing – indexing disabled.");

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
            if (_prefsRepo != null)
            {
                await LoadFavoritesAsync();
            }

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
            var projectsRoot = GetRequiredProjectsRoot();

            try
            {
                if (!Directory.Exists(projectsRoot))
                {
                    MessageBox.Show(
                        this,
                        $"Projects root not found:\n{projectsRoot}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    StatusText.Text = "Projects root not found.";
                    return;
                }

                var categories = Directory.GetDirectories(projectsRoot);

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

                        var entry = new ProjectEntry
                        {
                            FullPath = folder,
                            DisplayName = name,
                            Code = codePart
                        };

                        _allProjects.Add(entry);
                    }

                }

                _allProjects.Sort((a, b) =>
                    string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

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

        private static string GetRequiredProjectsRoot() => !string.IsNullOrWhiteSpace(AppConfig.ProjectsRoot) ? AppConfig.ProjectsRoot : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");


        private void ApplyProjectFilter(string term)
        {
            _filteredProjects.Clear();

            IEnumerable<ProjectEntry> source = _allProjects;

            if (!string.IsNullOrWhiteSpace(term))
            {
                string t = term.Trim();
                string lower = t.ToLowerInvariant();

                source = source.Where(p =>
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
                var rows = await _prefsRepo!.GetFavoritesAsync(_userUpn);

                foreach (var (ProjectNo, ProjectName) in rows)
                {
                    if (string.IsNullOrWhiteSpace(ProjectNo))
                        continue;

                    // Map favorite code to a real project entry from _allProjects
                    var match = _allProjects
                        .FirstOrDefault(p => p.Code.Equals(ProjectNo, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        _favoriteProjects.Add(match);
                    }
                }

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

        private void ProjectsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectsList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
                FileSelectedEmails();
            }
        }

        private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FavoritesList.SelectedItem is ProjectEntry p)
            {
                SetSelectedProject(p);
                FileSelectedEmails();
            }
        }

        // ====================================================================
        // Filing logic
        // ====================================================================

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            FileSelectedEmails();
        }

        private void FileSelectedEmails()
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

            try
            {
                Directory.CreateDirectory(monthFolder);
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

            int copied = 0;
            var errors = new List<string>();

            // read checkbox once
            bool saveAttachments = SaveAttachmentsCheckBox.IsChecked == true;

            foreach (var src in _incomingFiles)
            {
                try
                {
                    if (!File.Exists(src))
                        continue;

                    // Only .msg / .eml – extra safety, though caller should already filter
                    if (!(src.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
                          src.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string fileName = Path.GetFileName(src);
                    string destPath = Path.Combine(monthFolder, fileName);

                    // Avoid overwriting by adding numeric suffix if needed
                    destPath = EnsureUniquePath(destPath);

                    File.Copy(src, destPath);
                    copied++;

                    // per-email attachment handling (UI thread)
                    if (saveAttachments)
                    {
                        try
                        {
                            EnsureCodePagesEncodingRegistered();

                            if (_subjectExtractor.EmailHasAttachments(destPath))
                            {
                                // Start the folder picker at the project root UNC path
                                string projectRoot = selectedProject.FullPath;

                                string? folder = PromptForAttachmentFolder(destPath, projectRoot);
                                if (!string.IsNullOrWhiteSpace(folder))
                                {
                                    SaveAttachmentsForEmail(destPath, folder);
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


                    // Fire-and-forget indexing into KorEmailIndex
                    if (_emailIndexStore != null)
                    {
                        string projectNumber = selectedProject.Code;

                        Task.Run(async () =>
                        {
                            try
                            {
                                // Ensure encodings are registered in this worker too
                                EnsureCodePagesEncodingRegistered();

                                var parsed = EmailParser.Parse(destPath);

                                string subject = parsed.Subject ?? string.Empty;
                                string fromEmail = parsed.FromEmail ?? string.Empty;
                                DateTime? sentOnUtc = parsed.SentOnUtc;
                                int attachmentCount = parsed.AttachmentCount;
                                bool hasAttachments = parsed.HasAttachments;

                                // extra metadata
                                string fromDisplay = parsed.FromDisplay ?? string.Empty;
                                string toList = parsed.ToList ?? string.Empty;
                                string ccList = parsed.CcList ?? string.Empty;
                                string bccList = parsed.BccList ?? string.Empty;
                                string bodyText = parsed.BodyText ?? string.Empty;
                                DateTime? receivedOn = parsed.ReceivedOnUtc;

                                await _emailIndexStore.InsertEmailAsync(
                                    projectNumber,
                                    destPath,
                                    subject,
                                    fromEmail,
                                    sentOnUtc,
                                    attachmentCount,
                                    hasAttachments,
                                    fromDisplay,
                                    toList,
                                    ccList,
                                    bccList,
                                    bodyText,
                                    receivedOn);
                            }
                            catch (Exception ex)
                            {
                                DebugLog($"Indexing failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
                            }
                        }).GetAwaiter().GetResult();
                    }
                    else
                    {
                        DebugLog("Email filed but index store is null; not indexed.");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(src + " -> " + ex.Message);
                }
            }

            if (copied > 0)
            {
                StatusText.Text = $"Filed {copied} email(s) to {selectedProject.DisplayName}";
                MessageBox.Show(this,
                    $"Filed {copied} email(s) to:\n{selectedProject.DisplayName}",
                    "Filed Successfully",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Write selected project number so Outlook add-in can tag originals
                try
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var korDir = Path.Combine(appData, "KOR");
                    Directory.CreateDirectory(korDir);

                    var resultPath = Path.Combine(korDir, "EmailFilePickerResult.txt");
                    File.WriteAllText(resultPath, selectedProject.Code ?? string.Empty);
                }
                catch
                {
                    // best-effort only; if this fails, originals just will not be tagged
                }

                DialogResult = true;
                Close();
            }
            else
            {
                string message = "No emails were filed.";
                if (errors.Count > 0)
                    message += "\n\nErrors:\n" + string.Join("\n", errors.Take(5));

                MessageBox.Show(this,
                    message,
                    "Nothing Filed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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

            return FolderPicker.PickFolder(title, initialFolder);
        }


        // save all attachments for a single email into the given folder
        private void SaveAttachmentsForEmail(string emailPath, string attachmentFolder)
        {
            if (!File.Exists(emailPath))
                return;

            string extension = Path.GetExtension(emailPath);

            try
            {
                Directory.CreateDirectory(attachmentFolder);
                var options = EmailIndexOptions.FromAppConfig();

                if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using var msg = new Storage.Message(emailPath);

                    if (msg.Attachments == null || msg.Attachments.Count == 0)
                    {
                        DebugLog($"No MSG attachments found for {emailPath}");
                        return;
                    }

                    // explicitly cast each element so we get Attachment.FileName/Data
                    foreach (var obj in msg.Attachments)
                    {
                        try
                        {
                            if (obj is not OutlookAttachment attach)
                                continue;

                            string attName = attach.FileName;
                            if (string.IsNullOrWhiteSpace(attName))
                                attName = "Attachment.bin";

                            var ext = Path.GetExtension(attName);
                            if (options.BlockedExtensions.Contains(ext))
                            {
                                _logger.LogWarning(
                                    "Skipping blocked attachment {FileName} (extension {Ext}).",
                                    attName, ext);
                                continue;
                            }

                            var data = attach.Data;
                            if (data == null || data.Length == 0)
                                continue;

                            var fileSize = data.Length;
                            if (fileSize > options.MaxAttachmentBytes)
                            {
                                _logger.LogWarning(
                                    "Skipping oversized attachment {FileName} ({Bytes} bytes, limit {Limit}).",
                                    attName, fileSize, options.MaxAttachmentBytes);
                                continue;
                            }

                            string targetPath = Path.Combine(attachmentFolder, attName);
                            targetPath = EnsureUniquePath(targetPath);

                            File.WriteAllBytes(targetPath, data);
                            DebugLog($"Saved MSG attachment to {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"Failed to save MSG attachment for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                }
                else if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
                {
                    var fileInfo = new FileInfo(emailPath);
                    var eml = MsgReader.Mime.Message.Load(fileInfo);

                    if (eml.Attachments == null || eml.Attachments.Count == 0)
                    {
                        DebugLog($"No EML attachments found for {emailPath}");
                        return;
                    }

                    foreach (var part in eml.Attachments)
                    {
                        try
                        {
                            if (part == null || !part.IsAttachment)
                                continue;

                            string attName = part.FileName;
                            if (string.IsNullOrWhiteSpace(attName))
                                attName = "Attachment.bin";

                            var ext = Path.GetExtension(attName);
                            if (options.BlockedExtensions.Contains(ext))
                            {
                                _logger.LogWarning(
                                    "Skipping blocked attachment {FileName} (extension {Ext}).",
                                    attName, ext);
                                continue;
                            }

                            var data = part.Body;
                            if (data == null || data.Length == 0)
                                continue;

                            var fileSize = data.Length;
                            if (fileSize > options.MaxAttachmentBytes)
                            {
                                _logger.LogWarning(
                                    "Skipping oversized attachment {FileName} ({Bytes} bytes, limit {Limit}).",
                                    attName, fileSize, options.MaxAttachmentBytes);
                                continue;
                            }

                            string targetPath = Path.Combine(attachmentFolder, attName);
                            targetPath = EnsureUniquePath(targetPath);

                            File.WriteAllBytes(targetPath, data);
                            DebugLog($"Saved EML attachment to {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"Failed to save EML attachment for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    DebugLog($"SaveAttachmentsForEmail: unsupported extension for {emailPath}");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"SaveAttachmentsForEmail general error for {emailPath}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            int i = 1;
            string candidate;

            do
            {
                candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));

            return candidate;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ====================================================================
        // Native folder picker (no WinForms), with initial folder support
        // ====================================================================
        private static class FolderPicker
        {
            private const uint BIF_RETURNONLYFSDIRS = 0x0001;
            private const uint BIF_NEWDIALOGSTYLE = 0x0040;

            private const int BFFM_INITIALIZED = 1;
            private const uint BFFM_SETSELECTIONW = 0x0467;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            private struct BROWSEINFO
            {
                public IntPtr hwndOwner;
                public IntPtr pidlRoot;
                public IntPtr pszDisplayName;
                [MarshalAs(UnmanagedType.LPTStr)]
                public string lpszTitle;
                public uint ulFlags;
                public IntPtr lpfn;
                public IntPtr lParam;
                public int iImage;
            }

            private delegate int BrowseCallbackProc(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData);

            [DllImport("shell32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO bi);

            [DllImport("shell32.dll", CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

            [DllImport("ole32.dll")]
            private static extern void CoTaskMemFree(IntPtr ptr);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

            // keep these alive while the dialog is open
            private static string? _initialPath;
            private static BrowseCallbackProc? _callback;

            public static string? PickFolder(string title, string initialFolder)
            {
                // Only set initial folder if it exists; otherwise let caller's fallback logic handle it
                _initialPath = Directory.Exists(initialFolder) ? initialFolder : null;
                _callback = new BrowseCallbackProc(BrowseCallback);

                IntPtr displayNamePtr = IntPtr.Zero;
                IntPtr pidl = IntPtr.Zero;

                try
                {
                    displayNamePtr = Marshal.AllocHGlobal(260 * Marshal.SystemDefaultCharSize);

                    var bi = new BROWSEINFO
                    {
                        hwndOwner = IntPtr.Zero, // could use owner window handle if you want
                        pidlRoot = IntPtr.Zero,
                        pszDisplayName = displayNamePtr,
                        lpszTitle = title,
                        ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
                        lpfn = Marshal.GetFunctionPointerForDelegate(_callback),
                        lParam = IntPtr.Zero,
                        iImage = 0
                    };

                    pidl = SHBrowseForFolder(ref bi);
                    if (pidl == IntPtr.Zero)
                        return null;

                    var sb = new StringBuilder(260);
                    bool ok = SHGetPathFromIDList(pidl, sb);
                    if (!ok)
                        return null;

                    string path = sb.ToString();
                    if (string.IsNullOrWhiteSpace(path))
                        return null;

                    return path;
                }
                finally
                {
                    if (pidl != IntPtr.Zero)
                        CoTaskMemFree(pidl);

                    if (displayNamePtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(displayNamePtr);

                    // allow GC after dialog closes
                    _callback = null;
                    _initialPath = null;
                }
            }

            private static int BrowseCallback(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData)
            {
                if (uMsg == BFFM_INITIALIZED && !string.IsNullOrEmpty(_initialPath))
                {
                    // tell the dialog to select our initial path
                    SendMessage(hwnd, BFFM_SETSELECTIONW, new IntPtr(1), _initialPath);
                }

                return 0;
            }
        }

    }
}

