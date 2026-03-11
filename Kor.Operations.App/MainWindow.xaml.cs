using Kor.Operations.Services; // ProjectRecipientMemory + HeaderLoader
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Graph;
using Kor.Operations.Rendering;
// ---- added (safe) -----------------------------------------------------------
using Microsoft.Data.SqlClient;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration; // App.config
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
// -----------------------------------------------------------------------------

namespace Kor.Operations
{
    public partial class MainWindow : Window
    {
        private const string DefaultEmailDomain = "korstructural.com";

        private readonly WizardState _state = new();

        private CancellationTokenSource? _uploadCts;
        private readonly Dictionary<string, long> _fileProgress = new(StringComparer.OrdinalIgnoreCase);

        // File-system project index/search
        private const string ProjectsRoot = @"\\KOR-FS01\Projects\Projects";
        private const string MustContainSubfolder = null;

        private readonly Debouncer _projectSearchDebouncer = new Debouncer(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _projectSearchCts;
        private readonly ProjectIndex _projectIndex = new ProjectIndex(ProjectsRoot, MustContainSubfolder);

        // NEW: remember the folder of the currently selected project
        private string? _currentProjectFolder;

        // Recipient autocomplete (To / CC)
        private readonly Debouncer _toSearchDebouncer = new Debouncer(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _toSearchCts;

        private readonly Debouncer _ccSearchDebouncer = new Debouncer(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _ccSearchCts;

        // Per-user memory (recent projects + learned recipients)
        private ProjectRecipientMemory? _mem;

        private bool _isExecuting;
        private bool _remarksEditorReady;
        private bool _useBasicRemarksEditor;
        private string? _remarksEditorError;

        private readonly DispatcherTimer _successToastTimer;
        private readonly DispatcherTimer _successToastHideTimer;

        // User preferences (for signature and teams)
        private readonly string _userUpn;
        private readonly IUserPreferencesStore? _userPrefsStore;
        private UserPreferences? _userPrefs;

        // --- SQL ledger wiring (non-invasive) ---
        private ITransmittalsStore? TryCreateStore()
        {
            try
            {
                var cs = ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(cs)) return null;
                return new Kor.Operations.Data.SqlTransmittalsStore(cs);
            }
            catch { return null; }
        }
        // ----------------------------------------

        public MainWindow()
        {
            InitializeComponent();

            _successToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _successToastTimer.Tick += SuccessToastTimer_Tick;
            _successToastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _successToastHideTimer.Tick += SuccessToastHideTimer_Tick;

            // Lightweight defaults; real values applied by HeaderLoader on load.
            HeaderBar.UserDisplayName = Environment.UserName;
            HeaderBar.UserEmail = $"{Environment.UserName}@korstructural.com";
            DatePicker.SelectedDate = DateTime.Today;
            RefreshList();

            // Basic UPN for preferences lookups (same pattern as PreferencesWindow)
            var overrideUpn = ConfigurationManager.AppSettings["UserUpnOverride"];
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{Environment.UserName}@{DefaultEmailDomain}";

            var cs = ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString;
            if (!string.IsNullOrWhiteSpace(cs))
            {
                _userPrefsStore = new SqlUserPreferencesStore(cs);
            }
        }

        private void ShowSuccessToast(string title, string message)
        {
            SuccessToastTitle.Text = title ?? string.Empty;
            SuccessToastMessage.Text = message ?? string.Empty;

            _successToastHideTimer.Stop();
            _successToastTimer.Stop();

            SuccessToast.Visibility = Visibility.Visible;
            SuccessToast.Opacity = 0;
            SuccessToast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));

            _successToastTimer.Start();
        }

        private void SuccessToastTimer_Tick(object? sender, EventArgs e)
        {
            _successToastTimer.Stop();
            SuccessToast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            _successToastHideTimer.Start();
        }

        private void SuccessToastHideTimer_Tick(object? sender, EventArgs e)
        {
            _successToastHideTimer.Stop();
            SuccessToast.Visibility = Visibility.Collapsed;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // existing behavior
            SendViaBox_SelectionChanged(SendViaBox, null);
            await InitializeCurrentUserAsync();                 // <- ensures FromBox is populated
            _ = _projectIndex.BuildIndexAsync();

            // unify header full name + headshot via shared cached loader
            try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { /* non-fatal */ }

            // Load user preferences (for signature and future options)
            await LoadUserPreferencesAsync();

            // Initialize HTML editor for message / remarks (currently not used for send, safe stub)
            await InitializeRemarksEditorAsync();

            // --- Purpose dropdown wiring (under Project) ---
            if (PurposeBox != null)
            {
                PurposeBox.ItemsSource = new[]
                {
                    "Site Instructions",
                    "For Review",
                    "For Approval",
                    "For Information",
                    "For Comment",
                    "For Permit",
                    "For Bid",
                    "Issued for Construction (IFC)",
                };

#pragma warning disable CS0618 // using obsolete alias intentionally
                if (string.IsNullOrWhiteSpace(_state.Header.Purpose))
                    _state.Header.Purpose = "For Review";

                PurposeBox.SelectedItem = _state.Header.Purpose;
#pragma warning restore CS0618
                UpdateBookmarkNotesButtonState();
            }
        }

        // Shared autocomplete keyboard logic (Up/Down/Enter/Esc) for Project, To, and Cc fields
        private void HandleAutocompleteKeys(
            KeyEventArgs e,
            TextBox box,
            Popup popup,
            ListBox list,
            Action<object> onSelect)
        {
            bool hasList = popup.IsOpen && list.HasItems;

            if (!hasList && e.Key != Key.Escape)
                return;

            // DOWN
            if (e.Key == Key.Down)
            {
                int count = list.Items.Count;
                if (count == 0) return;

                int current = list.SelectedIndex < 0 ? 0 : (list.SelectedIndex + 1) % count;
                list.SelectedIndex = current;
                popup.IsOpen = true;
                list.Focus();
                list.ScrollIntoView(list.SelectedItem);
                e.Handled = true;
                return;
            }

            // UP
            if (e.Key == Key.Up)
            {
                int count = list.Items.Count;
                if (count == 0) return;

                int current = list.SelectedIndex < 0
                    ? count - 1
                    : (list.SelectedIndex - 1 + count) % count;
                list.SelectedIndex = current;
                popup.IsOpen = true;
                list.Focus();
                list.ScrollIntoView(list.SelectedItem);
                e.Handled = true;
                return;
            }

            // ENTER
            if (e.Key == Key.Enter)
            {
                if (hasList && list.SelectedItem != null)
                {
                    onSelect(list.SelectedItem);
                    e.Handled = true;
                }
                return;
            }

            // ESC
            if (e.Key == Key.Escape)
            {
                if (popup.IsOpen)
                {
                    popup.IsOpen = false;
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                    e.Handled = true;
                }
            }
        }


        // Window-level preview key handler (wired from XAML: Window_PreviewKeyDown)
        // Handles Up/Down/Enter/Escape for the project picker, plus Esc for To/Cc popups.
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // PROJECT
            if (ProjectSearchBox != null &&
                SuggestionsList != null &&
                SuggestionsPopup != null &&
                (ReferenceEquals(Keyboard.FocusedElement, ProjectSearchBox) ||
                 ReferenceEquals(Keyboard.FocusedElement, SuggestionsList)))
            {
                HandleAutocompleteKeys(e, ProjectSearchBox, SuggestionsPopup, SuggestionsList, obj =>
                {
                    if (obj is ProjectItem sel)
                        UseProject(sel);
                });
                if (e.Handled) return;
            }

            // TO
            if (ToBox != null &&
                ToSuggestionsPopup != null &&
                ToSuggestionsList != null &&
                (ReferenceEquals(Keyboard.FocusedElement, ToBox) ||
                 ReferenceEquals(Keyboard.FocusedElement, ToSuggestionsList)))
            {
                HandleAutocompleteKeys(e, ToBox, ToSuggestionsPopup, ToSuggestionsList, obj =>
                {
                    if (obj is EmailSuggestion sel)
                        InsertEmailSuggestionIntoBox(ToBox, sel);
                });
                if (e.Handled) return;
            }

            // CC
            if (CcBox != null &&
                CcSuggestionsPopup != null &&
                CcSuggestionsList != null &&
                (ReferenceEquals(Keyboard.FocusedElement, CcBox) ||
                 ReferenceEquals(Keyboard.FocusedElement, CcSuggestionsList)))
            {
                HandleAutocompleteKeys(e, CcBox, CcSuggestionsPopup, CcSuggestionsList, obj =>
                {
                    if (obj is EmailSuggestion sel)
                        InsertEmailSuggestionIntoBox(CcBox, sel);
                });
                if (e.Handled) return;
            }

            // ESC fallback
            if (e.Key == Key.Escape)
            {
                if (ToSuggestionsPopup != null && ToSuggestionsPopup.IsOpen)
                {
                    ToSuggestionsPopup.IsOpen = false;
                    ToBox?.Focus();
                    e.Handled = true;
                    return;
                }

                if (CcSuggestionsPopup != null && CcSuggestionsPopup.IsOpen)
                {
                    CcSuggestionsPopup.IsOpen = false;
                    CcBox?.Focus();
                    e.Handled = true;
                    return;
                }
            }
        }

        private async Task LoadUserPreferencesAsync()
        {
            if (_userPrefsStore == null)
                return;

            try
            {
                _userPrefs = await _userPrefsStore.GetAsync(_userUpn);
            }
            catch
            {
                // Do not block the window if prefs fail
            }
        }


        // ---------- HTML editor (WebView2) for Remarks ----------
        private async Task InitializeRemarksEditorAsync()
        {
            _remarksEditorReady = false;
            _useBasicRemarksEditor = false;
            _remarksEditorError = null;
            UpdateRemarksEditorUi();

            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var htmlPath = Path.Combine(exeDir, "Assets", "QuickRemarksEditor.html");

                if (!File.Exists(htmlPath))
                    throw new FileNotFoundException("Remarks editor HTML not found.", htmlPath);

                await RemarksEditor.EnsureCoreWebView2Async();

                RemarksEditor.NavigationStarting += (_, e) =>
                {
                    if (!e.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                        !e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
                    {
                        e.Cancel = true;
                    }
                };

                var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                RemarksEditor.NavigationCompleted += async (_, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        _remarksEditorReady = false;
                        _remarksEditorError = $"Remarks editor failed to load ({e.WebErrorStatus}).";
                        UpdateRemarksEditorUi();
                        readyTcs.TrySetResult(false);
                        return;
                    }

                    try
                    {
                        var probe = await RemarksEditor.CoreWebView2.ExecuteScriptAsync(
                            "!!(window.getEditorHtml || window.getSignatureHtml);");

                        _remarksEditorReady = string.Equals(probe, "true", StringComparison.OrdinalIgnoreCase);
                        _remarksEditorError = _remarksEditorReady
                            ? null
                            : "Remarks editor loaded but is not ready.";
                        UpdateRemarksEditorUi();
                        readyTcs.TrySetResult(_remarksEditorReady);
                    }
                    catch (Exception ex2)
                    {
                        _remarksEditorReady = false;
                        _remarksEditorError = $"Remarks editor script check failed: {ex2.Message}";
                        UpdateRemarksEditorUi();
                        readyTcs.TrySetResult(false);
                    }
                };

                RemarksEditor.Source = new Uri(htmlPath);

                var completed = await Task.WhenAny(readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                if (completed != readyTcs.Task)
                {
                    _remarksEditorReady = false;
                    _remarksEditorError = "Remarks editor did not initialize in time.";
                    UpdateRemarksEditorUi();
                }
            }
            catch (Exception ex)
            {
                _remarksEditorReady = false;
                _remarksEditorError = ex.Message;
                UpdateRemarksEditorUi();
            }
        }

        private void UpdateRemarksEditorUi()
        {
            if (_useBasicRemarksEditor)
            {
                if (RemarksEditor != null) RemarksEditor.Visibility = Visibility.Collapsed;
                if (UseBasicRemarksBtn != null) UseBasicRemarksBtn.Visibility = Visibility.Collapsed;
                if (RemarksEditorStatus != null)
                {
                    RemarksEditorStatus.Visibility = Visibility.Visible;
                    RemarksEditorStatus.Text = "Using basic editor.";
                    RemarksEditorStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
                }

                if (RemarksBox != null)
                {
                    RemarksBox.Visibility = Visibility.Visible;
                    RemarksBox.IsReadOnly = false;
                    RemarksBox.Height = 320;
                }
                return;
            }

            if (RemarksBox != null) RemarksBox.Visibility = Visibility.Collapsed;
            if (RemarksEditor != null) RemarksEditor.Visibility = Visibility.Visible;

            if (_remarksEditorReady)
            {
                if (RemarksEditorStatus != null) RemarksEditorStatus.Visibility = Visibility.Collapsed;
                if (UseBasicRemarksBtn != null) UseBasicRemarksBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (RemarksEditorStatus != null)
                {
                    RemarksEditorStatus.Visibility = Visibility.Visible;
                    RemarksEditorStatus.Text = "Rich text editor unavailable. " +
                        (string.IsNullOrWhiteSpace(_remarksEditorError) ? string.Empty : _remarksEditorError);
                }

                if (UseBasicRemarksBtn != null) UseBasicRemarksBtn.Visibility = Visibility.Visible;
            }
        }

        private void UseBasicRemarksBtn_Click(object sender, RoutedEventArgs e)
        {
            _useBasicRemarksEditor = true;
            UpdateRemarksEditorUi();
            RemarksBox?.Focus();
        }

        /// <summary>
        /// Gets the current HTML from the TinyMCE editor hosted in RemarksEditor.
        /// Reuses the same window.getSignatureHtml() helper used in Preferences.
        /// Currently not wired into send, but kept ready.
        /// </summary>
        private async Task<string> GetRemarksHtmlAsync()
        {
            if (!_remarksEditorReady || RemarksEditor?.CoreWebView2 == null)
                return string.Empty;

            try
            {
                var result = await RemarksEditor.CoreWebView2.ExecuteScriptAsync(
                    "window.getEditorHtml ? window.getEditorHtml() : (window.getSignatureHtml ? window.getSignatureHtml() : '');");

                if (string.IsNullOrWhiteSpace(result) ||
                    result == "null" ||
                    result == "undefined")
                {
                    return string.Empty;
                }

                // WebView2 returns JSON-encoded string
                return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Very simple HTML -> plain text used for the cover sheet.
        /// </summary>
        private static string StripHtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var withoutTags = Regex.Replace(html, "<.*?>", string.Empty);
            return WebUtility.HtmlDecode(withoutTags).Trim();
        }

        /// <summary>
        /// Shared helper (same as QuickTransferRunner) that builds the final
        /// HTML email body:
        ///
        ///   remarks
        ///   <br/><br/>
        ///   <b>View files: <a href="...">Click here to view the files</a></b>
        ///   <br/><br/>
        ///   signature
        ///   (optional hidden tracking pixel)
        /// </summary>
        private static string BuildEmailBodyHtml(
            string? remarksHtml,
            string? signatureHtml,
            string? linkUrl,
            string? pixelUrl,
            string? toRecipients,
            string? ccRecipients)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(remarksHtml))
            {
                sb.Append(remarksHtml.Trim());
                sb.Append("<br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(toRecipients) || !string.IsNullOrWhiteSpace(ccRecipients))
            {
                if (!string.IsNullOrWhiteSpace(toRecipients))
                {
                    sb.Append("<b>To:</b> ")
                      .Append(WebUtility.HtmlEncode(toRecipients))
                      .Append("<br/>");
                }

                if (!string.IsNullOrWhiteSpace(ccRecipients))
                {
                    sb.Append("<b>Cc:</b> ")
                      .Append(WebUtility.HtmlEncode(ccRecipients))
                      .Append("<br/>");
                }

                sb.Append("<br/>");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                var encoded = WebUtility.HtmlEncode(linkUrl);
                sb.Append("<b>View files: <a href=\"")
                  .Append(encoded)
                  .Append("\">Click here to view the files</a></b><br/><br/>");
            }

            if (!string.IsNullOrWhiteSpace(signatureHtml))
            {
                sb.Append(signatureHtml.Trim());
            }

            if (!string.IsNullOrWhiteSpace(pixelUrl))
            {
                sb.Append("<img src=\"")
                  .Append(WebUtility.HtmlEncode(pixelUrl))
                  .Append("\" alt=\"\" style=\"display:none;width:1px;height:1px;\" />");
            }

            return sb.ToString();
        }

        // ---------- Required field validation (Project, To, Subject, Files) ----------
        private bool ValidateRequiredFields()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ProjectSearchBox.Text))
                errors.Add("Project is required.");

            var toList = ParseEmails(ToBox.Text);
            if (toList.Count == 0)
                errors.Add("At least one recipient in the To field is required.");

            if (string.IsNullOrWhiteSpace(SubjectBox.Text))
                errors.Add("Subject is required.");

            if (_state.Files == null || _state.Files.Count == 0)
                errors.Add("At least one file must be added.");

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join("\n", errors),
                    "Missing information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void UpdateBookmarkNotesButtonState()
        {
            if (BookmarkNotesBtn == null)
                return;

            // SelectedItem is a string because ItemsSource = string[]
            var purposeText = (PurposeBox.SelectedItem as string)
                              ?? PurposeBox.Text
                              ?? string.Empty;

            bool isSiteInstructions =
                purposeText.IndexOf("Site Instruction", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasPdf =
                _state.Files.Any(f =>
                    f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

            bool shouldShow = isSiteInstructions && hasPdf;

            BookmarkNotesBtn.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            BookmarkNotesBtn.IsEnabled = shouldShow;
        }


        // ---------- Files ----------
        public void LoadInitialFiles(List<string> files) => MergeFiles(files);

        public void MergeFiles(List<string> files)
        {
            foreach (var path in files.Where(File.Exists))
            {
                if (_state.Files.Any(f => string.Equals(f.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fi = new FileInfo(path);
                _state.Files.Add(new TransmittalFile
                {
                    LocalPath = path,
                    FileName = fi.Name,
                    SizeBytes = fi.Length
                });
            }
            RefreshList();
        }

        private void RefreshList()
        {
            FilesList.ItemsSource = null;
            FilesList.ItemsSource = _state.Files.ToList();
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All files|*.*",
                Multiselect = true
            };

            // NEW: start in the selected project folder if we have one
            if (!string.IsNullOrWhiteSpace(_currentProjectFolder) &&
                Directory.Exists(_currentProjectFolder))
            {
                dlg.InitialDirectory = _currentProjectFolder;
            }

            if (dlg.ShowDialog(this) == true)
                MergeFiles(dlg.FileNames.ToList());

            // NEW
            UpdateBookmarkNotesButtonState();
        }


        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = FilesList.SelectedItems.Cast<TransmittalFile>().ToList();
            foreach (var f in selected)
                _state.Files.Remove(f);
            RefreshList();
            // NEW
            UpdateBookmarkNotesButtonState();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _state.Files.Clear();
            RefreshList();
            // NEW
            UpdateBookmarkNotesButtonState();
        }
        private void PurposeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBookmarkNotesButtonState();
        }

        // ---------- Send Via ----------
        private void SendViaBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (ExternalLinkCheck == null || SendViaBox == null) return;
            var mode = (SendViaBox.SelectedItem as ContentControl)?.Content?.ToString() ?? "SharePoint";
            if (string.Equals(mode, "Email", StringComparison.OrdinalIgnoreCase))
            {
                ExternalLinkCheck.IsChecked = false;
                ExternalLinkCheck.IsEnabled = false;
            }
            else
            {
                ExternalLinkCheck.IsEnabled = true;
                if (ExternalLinkCheck.IsChecked == false) ExternalLinkCheck.IsChecked = true;
            }
        }

        // Normalize "Send Via" and fix possible TextBlock artifact
        private string UiSendVia()
        {
            var mode = (SendViaBox.SelectedItem as ContentControl)?.Content?.ToString() ?? "SharePoint";
            if (mode.StartsWith("System.Windows.Controls.TextBlock", StringComparison.OrdinalIgnoreCase))
                mode = "SharePoint";
            return mode;
        }

        // ---------- PREVIEW ----------
        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (_isExecuting)
                return;

            _isExecuting = true;
            SendButton.IsEnabled = false;
            PreviewButton.IsEnabled = false;

            try
            {
                if (!ValidateRequiredFields())
                    return;

                try
                {
                    var name = string.IsNullOrWhiteSpace(_state.Header.TransmittalNo)
                        ? "Preview"
                        : _state.Header.TransmittalNo;
                    var tempPath = Path.Combine(Path.GetTempPath(), $"{name}-CoverPreview.pdf");

                    _state.Header.Subject = SubjectBox.Text?.Trim();
                    _state.Header.SendVia = UiSendVia();

                    var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                    var pacDate = (DatePicker.SelectedDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacific).Date);
                    var unspecified = DateTime.SpecifyKind(pacDate, DateTimeKind.Unspecified);
                    _state.Header.DateUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, pacific);

                    var to = ParseEmails(ToBox.Text);
                    var cc = ParseEmails(CcBox.Text);

                    _state.Header.Recipients.Clear();
                    foreach (var addr in to) _state.Header.Recipients.Add(new Recipient { Email = addr });
                    foreach (var addr in cc) _state.Header.Recipients.Add(new Recipient { Email = addr });

                    try { _state.Header.GetType().GetProperty("ToRecipients")?.SetValue(_state.Header, to); } catch { }
                    try { _state.Header.GetType().GetProperty("CcRecipients")?.SetValue(_state.Header, cc); } catch { }

                    _state.Header.FromName = HeaderBar?.UserDisplayName;
                    _state.Header.FromEmail = FromBox?.Text;

                    string remarksHtml;
                    string bodyPlain;
                    if (_useBasicRemarksEditor)
                    {
                        remarksHtml = BuildHtmlFromRemarks(RemarksBox);
                        bodyPlain = ReadText(RemarksBox);
                    }
                    else
                    {
                        if (!_remarksEditorReady)
                        {
                            MessageBox.Show(this,
                                "Message / Remarks editor is not ready. Use 'Use basic editor' or retry.",
                                "Remarks Editor",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        remarksHtml = await GetRemarksHtmlAsync();
                        bodyPlain = StripHtmlToPlainText(remarksHtml);
                    }

                    _state.Header.Remarks = bodyPlain;

                    if (string.IsNullOrWhiteSpace(_state.Header.ProjectNumber))
                        _state.Header.ProjectNumber = "—";
                    if (string.IsNullOrWhiteSpace(_state.Header.ProjectName))
                        _state.Header.ProjectName = "—";

#pragma warning disable CS0618
                    _state.Header.Purpose = PurposeBox?.SelectedItem as string ?? PurposeBox?.Text ?? _state.Header.Purpose;
#pragma warning restore CS0618

                    var files = _state.Files ?? new List<TransmittalFile>();

                    SetStatus("Generating preview...");
                    await CoverSheetRenderer.RenderAsync(
                        tempPath,
                        _state.Header,
                        files);

                    SetStatus("Preview ready.");

                    using var proc = new System.Diagnostics.Process();
                    proc.StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    };
                    proc.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Unable to generate preview:\n" + ex.Message,
                        "Preview Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    SetStatus("Preview failed.");
                }
            }
            finally
            {
                _isExecuting = false;
                SendButton.IsEnabled = true;
                PreviewButton.IsEnabled = true;
            }
        }

        // ---------- SEND ----------
        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (_isExecuting)
                return;

            _isExecuting = true;
            SendButton.IsEnabled = false;
            PreviewButton.IsEnabled = false;
            var closeAfterSuccess = false;

            try
            {
                if (!ValidateRequiredFields())
                    return;

                var to = ParseEmails(ToBox.Text);
                var cc = ParseEmails(CcBox.Text);

                _state.Header.Subject = SubjectBox.Text?.Trim();
                _state.Header.SendVia = UiSendVia();

                var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                var pacDate = (DatePicker.SelectedDate ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacific).Date);
                var unspecified = DateTime.SpecifyKind(pacDate, DateTimeKind.Unspecified);
                _state.Header.DateUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, pacific);

                _state.Header.Recipients.Clear();
                foreach (var addr in to.Concat(cc))
                    _state.Header.Recipients.Add(new Recipient { Email = addr });

                try { _state.Header.GetType().GetProperty("ToRecipients")?.SetValue(_state.Header, to); } catch { }
                try { _state.Header.GetType().GetProperty("CcRecipients")?.SetValue(_state.Header, cc); } catch { }

                if (string.IsNullOrWhiteSpace(_state.Header.ProjectNumber))
                    _state.Header.ProjectNumber = "TEST-001";
                if (string.IsNullOrWhiteSpace(_state.Header.ProjectName))
                    _state.Header.ProjectName = "Sample Project";

                _state.Header.FromName = HeaderBar?.UserDisplayName;
                _state.Header.FromEmail = FromBox?.Text;

#pragma warning disable CS0618
                _state.Header.Purpose = PurposeBox?.SelectedItem as string ?? PurposeBox?.Text ?? _state.Header.Purpose;
#pragma warning restore CS0618

                string remarksHtml;
                string bodyPlain;
                if (_useBasicRemarksEditor)
                {
                    remarksHtml = BuildHtmlFromRemarks(RemarksBox);
                    bodyPlain = ReadText(RemarksBox);
                }
                else
                {
                    if (!_remarksEditorReady)
                    {
                        MessageBox.Show(this,
                            "Message / Remarks editor is not ready. Use 'Use basic editor' or retry.",
                            "Remarks Editor",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    remarksHtml = await GetRemarksHtmlAsync();
                    bodyPlain = StripHtmlToPlainText(remarksHtml);
                }

                // IMPORTANT: do NOT append the signature here –
                // the cover sheet / PDF should NOT contain the email signature.
                _state.Header.Remarks = bodyPlain;

                // 2) HTML for the outgoing email (remarks only; signature handled separately)
                var emailRemarksHtml = remarksHtml ?? string.Empty;

                // Signature HTML (if requested and available)
                string? signatureHtmlForEmail = null;
                if (_userPrefs != null && !string.IsNullOrWhiteSpace(_userPrefs.EmailSignatureHtml))
                {
                    signatureHtmlForEmail = _userPrefs.EmailSignatureHtml;
                }

                var isEmail = string.Equals(_state.Header.SendVia, "Email", StringComparison.OrdinalIgnoreCase);
                var needExternal = !isEmail && (ExternalLinkCheck?.IsChecked ?? false);
                var attachIfSmall = isEmail;

                var remarksBuilder = new StringBuilder(emailRemarksHtml ?? string.Empty);

                try
                {
                    await RunTransmittalAsync(needExternal, attachIfSmall, remarksBuilder, signatureHtmlForEmail);
                    ShowSuccessToast("Transmittal Sent", "Your transmittal was sent successfully.");
                    closeAfterSuccess = true;
                    await Task.Delay(4000);
                    Close();
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Upload canceled.");
                    UpdateProgressBar(0, "");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetStatus("Error.");
                }
            }
            finally
            {
                _isExecuting = false;
                if (!closeAfterSuccess)
                {
                    SendButton.IsEnabled = true;
                    PreviewButton.IsEnabled = true;
                }
            }
        }

        // ---------- CORE SEND / TRACKING PIPELINE ----------
        private async Task RunTransmittalAsync(
            bool needExternal,
            bool attachIfSmall,
            StringBuilder remarksBuilder,
            string? signatureHtmlForEmail)
        {
            UploadProgress.Value = 0;
            PercentText.Text = "";
            ProgressDetailText.Text = "";
            SetStatus("Preparing...");
            CancelUploadBtn.IsEnabled = true;

            _uploadCts?.Cancel();
            _uploadCts = new CancellationTokenSource();

            // Base HTML remarks used for each recipient
            var baseRemarksHtml = remarksBuilder?.ToString() ?? string.Empty;

            _state.Header.TransmittalNo = await GraphFacade.Instance
                .ReserveTransmittalNumberAsync(_state.Header.ProjectNumber);

            var folder = BuildTransmittalFolderPath();
            _state.Header.SharePointFolderPath = folder;

            // Prepare store + transmittal id, but DO NOT log to SQL yet
            var store = TryCreateStore();
            Guid? transmittalId = null;
            var subjectNow = _state.Header.Subject ?? string.Empty;
            if (store != null)
            {
                transmittalId = Guid.NewGuid();
            }

            var overallTotal = _state.Files.Sum(f => f.SizeBytes);
            _fileProgress.Clear();

            // ---------- Upload all attached files ----------
            foreach (var f in _state.Files)
            {
                SetStatus($"Uploading {f.FileName}...");
                _fileProgress[f.LocalPath] = 0;

                var perFile = new Progress<(string file, long sent, long total)>(p =>
                {
                    _fileProgress[f.LocalPath] = p.sent;

                    var sentSum = _fileProgress.Values.Sum();
                    var percent = overallTotal > 0 ? (sentSum * 100.0 / overallTotal) : 0.0;
                    UpdateProgressBar(percent, "");
                });

                var sp = await GraphFacade.Instance.UploadWithProgressAsync(
                    folder, f.FileName, f.LocalPath, perFile, _uploadCts.Token);
                f.SharePointPath = sp;
            }

            // ---------- Create and upload cover sheet ----------
            SetStatus("Creating cover sheet...");

            var fallbackName = $"{_state.Header.TransmittalNo}-Cover.pdf";
            var coverLocal = Path.Combine(Path.GetTempPath(), fallbackName);

            // Uses the plain-text header.Remarks set earlier
            await CoverSheetRenderer.RenderAsync(
                coverLocal,
                _state.Header,
                _state.Files
            );

            var coverFileName = string.IsNullOrWhiteSpace(_state.Header.CoverSheetFileName)
                ? fallbackName
                : _state.Header.CoverSheetFileName;
            _state.Header.CoverSheetFileName = coverFileName;

            SetStatus("Uploading cover sheet...");

            // Capture the actual SharePoint URL for the cover sheet
            var coverSpUrl = await GraphFacade.Instance.UploadWithProgressAsync(
                folder, coverFileName, coverLocal,
                new Progress<(string file, long sent, long total)>(p => { UpdateProgressBar(100, ""); }),
                _uploadCts.Token);

            // ---------- NOW log the transmittal row using the real PDF URL ----------
            if (store != null && transmittalId.HasValue)
            {
                try
                {
                    await store.LogTransmittalAsync(
                        id: transmittalId.Value,
                        projectNo: _state.Header.ProjectNumber ?? string.Empty,
                        subject: subjectNow,
                        driveId: "drv",
                        itemId: "itm",
                        sharePointUrl: coverSpUrl ?? string.Empty,
                        createdUtc: DateTime.UtcNow,
                        createdBy: FromBox.Text ?? string.Empty,
                        appVersion: typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                        ct: _uploadCts.Token
                    );
                }
                catch
                {
                    // do not block UX
                }
            }

            // ---------- Create SharePoint link(s) ----------
            SetStatus("Creating links...");
            var links = await GraphFacade.Instance.CreateLinksAsync(folder, needExternal, _uploadCts.Token);
            _state.Header.InternalLink = links.InternalLink;
            _state.Header.ExternalLink = links.ExternalLink;

            // Base target for redirector; prefer external, fall back to internal
            var sharePointUrl = _state.Header.ExternalLink ?? _state.Header.InternalLink ?? string.Empty;

            // ---------- Build recipient list ----------
            var toForSend = ParseEmails(ToBox.Text);
            var ccForSend = ParseEmails(CcBox.Text);
            var allRecipients = toForSend.Concat(ccForSend)
                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                         .ToList();
            var toRecipientsDisplay = string.Join("; ", toForSend);
            var ccRecipientsDisplay = string.Join("; ", ccForSend);

            var redirectorBase = (ConfigurationManager.AppSettings["RedirectorBaseUrl"] ?? string.Empty).TrimEnd('/');
            var cs = ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString;
            var hasRedirector = !string.IsNullOrWhiteSpace(redirectorBase);

            // ---------- Per-recipient tracking + send ----------
            foreach (var email in allRecipients)
            {
                _uploadCts.Token.ThrowIfCancellationRequested();

                Guid? linkIdForRecipient = null;

                // Insert RedirectTargets row (if DB is configured)
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    var lid = Guid.NewGuid();
                    linkIdForRecipient = lid;

                    try
                    {
                        await InsertRedirectTargetsAsync(
                            cs!,
                            transmittalId,
                            new[] { (lid, email, sharePointUrl) },
                            _uploadCts.Token);
                    }
                    catch
                    {
                        // Do not block send on logging failure
                    }
                }

                // Build tracking URLs
                var clickUrl = sharePointUrl;
                string? pixelUrl = null;

                if (hasRedirector && linkIdForRecipient.HasValue)
                {
                    clickUrl = $"{redirectorBase}/t/{linkIdForRecipient.Value}";
                    pixelUrl = $"{redirectorBase}/o/{linkIdForRecipient.Value}/{Uri.EscapeDataString(email)}";
                }

                // Build final HTML email body exactly like QuickTransferRunner
                var bodyHtml = BuildEmailBodyHtml(
                    remarksHtml: baseRemarksHtml,
                    signatureHtml: signatureHtmlForEmail,
                    linkUrl: clickUrl,
                    pixelUrl: pixelUrl,
                    toRecipients: toRecipientsDisplay,
                    ccRecipients: ccRecipientsDisplay);

                // For email body, use HTML; for cover sheet we already rendered earlier
                _state.Header.Remarks = bodyHtml;

                // Override links used in the template so they go through the redirector
                if (!string.IsNullOrWhiteSpace(clickUrl))
                {
                    _state.Header.ExternalLink = clickUrl;
                    _state.Header.InternalLink = clickUrl;
                }

                // Send a single email for this recipient
                await GraphFacade.Instance.SendMailAsync(
                    _state.Header,
                    $"{folder}/{_state.Header.CoverSheetFileName}",
                    coverLocal,
                    attachIfSmall && _state.Files.Sum(x => x.SizeBytes) < 10 * 1024 * 1024,
                    _uploadCts.Token,
                    FromBox?.Text ?? string.Empty,
                    new[] { email }
                );
            }

            // ---------- Mark sent (once per transmittal) ----------
            if (store != null && transmittalId.HasValue)
            {
                try
                {
                    await store.MarkSentAsync(
                        transmittalId.Value,
                        DateTime.UtcNow,
                        FromBox?.Text ?? string.Empty,
                        typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                        _uploadCts.Token
                    );
                }
                catch
                {
                    // ignore
                }
            }

            // ---------- Learn recipients for project memory ----------
            var projKey = ProjectRecipientMemory.NormalizeProjectKey(
                _state.Header.ProjectNumber ?? string.Empty, _state.Header.ProjectName);
            _mem?.LearnRecipients(projKey, allRecipients);

            SetStatus("Done.");
            UpdateProgressBar(100, "Completed");
            CancelUploadBtn.IsEnabled = false;
            // Close behavior is handled by the caller (Send_Click).
        }

        // Insert RedirectTargets rows
        private static async Task InsertRedirectTargetsAsync(
            string connString,
            Guid? transmittalId,
            IEnumerable<(Guid LinkId, string Email, string TargetUrl)> rows,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.RedirectTargets (LinkId, TransmittalId, RecipientEmail, TargetUrl)
VALUES (@lid, @tid, @email, @url);";

            await using var cnn = new SqlConnection(connString);
            await cnn.OpenAsync(ct);

            foreach (var r in rows)
            {
                await using var cmd = new SqlCommand(sql, cnn);
                cmd.Parameters.AddWithValue("@lid", r.LinkId);
                cmd.Parameters.AddWithValue("@tid", (object?)transmittalId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@email", r.Email ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@url", r.TargetUrl ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // ---------- Identity ----------
        private async Task InitializeCurrentUserAsync()
        {
            try
            {
                var cfgEmail = ConfigurationManager.AppSettings["DefaultFromEmail"];
                if (!string.IsNullOrWhiteSpace(cfgEmail))
                {
                    FromBox.Text = cfgEmail!;
                }
                else
                {
                    string? windowsDerived = null;

                    if (OperatingSystem.IsWindows())
                        windowsDerived = TryGetEmailFromWindowsIdentity() ?? TryGuessEmailFromWindows();

                    if (!string.IsNullOrWhiteSpace(windowsDerived))
                        FromBox.Text = windowsDerived!;
                    else
                        FromBox.Text = GuessFromAppSettingsOrDefault();
                }

                if (!string.IsNullOrWhiteSpace(FromBox.Text))
                    _mem = new ProjectRecipientMemory(FromBox.Text);
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(FromBox.Text))
                    FromBox.Text = GuessFromAppSettingsOrDefault();
            }

            await Task.CompletedTask;
        }

        private static string GuessFromAppSettingsOrDefault()
        {
            var domain = ConfigurationManager.AppSettings["DefaultFromDomain"];
            if (string.IsNullOrWhiteSpace(domain))
                domain = DefaultEmailDomain;
            var user = Environment.UserName;
            return string.IsNullOrWhiteSpace(user) ? $"noreply@{domain}" : $"{user}@{domain}";
        }

        [SupportedOSPlatform("windows")]
        private static string? TryGetEmailFromWindowsIdentity()
        {
            try
            {
                using var wi = WindowsIdentity.GetCurrent();
                if (wi == null) return null;

                var email = wi.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email || c.Type.EndsWith("/emailaddress", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(email)) return email;

                var upn = wi.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Upn || c.Type.EndsWith("/upn", StringComparison.OrdinalIgnoreCase))?.Value;
                return string.IsNullOrWhiteSpace(upn) ? null : upn;
            }
            catch { return null; }
        }

        [SupportedOSPlatform("windows")]
        private static string? TryGuessEmailFromWindows()
        {
            try
            {
                var user = Environment.UserName;
                if (string.IsNullOrWhiteSpace(user)) return null;

                var domain = DefaultEmailDomain;
                var rawDomain = Environment.UserDomainName;
                if (!string.IsNullOrWhiteSpace(rawDomain) &&
                    !rawDomain.Contains('\\') &&
                    !rawDomain.Contains(' ') &&
                    rawDomain.Contains('.'))
                {
                    domain = rawDomain.ToLowerInvariant();
                }
                return $"{user}@{domain}";
            }
            catch { return null; }
        }

        // ---------- Folder helpers ----------
        private static string SanitizeSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
            cleaned = cleaned.Trim().Trim('.', ' ');
            return string.IsNullOrEmpty(cleaned) ? "Unknown" : cleaned;
        }

        private string BuildTransmittalFolderPath()
        {
            var projNo = SanitizeSegment(_state.Header.ProjectNumber ?? "Unknown");
            var projNam = SanitizeSegment(_state.Header.ProjectName ?? "Unknown");
            var projectFolder = string.IsNullOrWhiteSpace(projNam) ? projNo : $"{projNo} - {projNam}";
            var year = DateTime.UtcNow.ToString("yyyy");
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
            return $"{projectFolder}/Transmittals/{year}/{stamp}";
        }

        // ---------- Progress UI helpers ----------
        private void SetStatus(string text) => StatusText.Text = text;

        private void UpdateProgressBar(double percent, string? detail)
        {
            UploadProgress.Value = percent;
            PercentText.Text = percent > 0 ? $"{percent:0}%" : "";

            if (string.IsNullOrWhiteSpace(detail))
            {
                ProgressDetailText.Text = "";
                ProgressDetailText.ToolTip = null;
                ProgressDetailText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ProgressDetailText.Text = detail!;
                ProgressDetailText.ToolTip = detail!;
                ProgressDetailText.Visibility = Visibility.Visible;
            }
        }

        private void CancelUploadBtn_Click(object sender, RoutedEventArgs e)
        {
            _uploadCts?.Cancel();
            CancelUploadBtn.IsEnabled = false;
        }

        private void BookmarkNotesBtn_Click(object sender, RoutedEventArgs e)
        {
            // Re-check conditions just in case the button is clicked in a weird state
            var purposeText =
                (PurposeBox.SelectedItem as string) ??
                PurposeBox.Text ??
                string.Empty;

            bool isSiteInstructions =
                purposeText.IndexOf("Site Instruction", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isSiteInstructions)
            {
                MessageBox.Show(this,
                    "Bookmark notes are only available when Purpose is set to \"Site Instructions\".",
                    "Bookmark notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var files = _state.Files ?? new List<TransmittalFile>();
            if (files.Count == 0)
            {
                MessageBox.Show(this,
                    "There are no files attached.",
                    "Bookmark notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // PDFs only
            var pdfFiles = files
                .Where(f =>
                {
                    if (!string.IsNullOrWhiteSpace(f.FileName) &&
                        f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (!string.IsNullOrWhiteSpace(f.LocalPath) &&
                        Path.GetExtension(f.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        return true;

                    return false;
                })
                .ToList();

            if (pdfFiles.Count == 0)
            {
                MessageBox.Show(this,
                    "There are no PDF files attached.",
                    "Bookmark notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Ensure bookmarks (and notes list length) for each PDF
            foreach (var f in pdfFiles)
            {
                if (f.PdfBookmarks == null || f.PdfBookmarks.Count == 0)
                {
                    var path = f.LocalPath;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        try
                        {
                            f.PdfBookmarks = PdfBookmarkExtractor.TryGetBookmarks(path);
                        }
                        catch
                        {
                            // non-fatal: just skip this file's bookmarks
                        }
                    }
                }

                if (f.PdfBookmarks != null && f.PdfBookmarks.Count > 0)
                {
                    if (f.PdfBookmarkNotes == null)
                        f.PdfBookmarkNotes = new List<string>();

                    while (f.PdfBookmarkNotes.Count < f.PdfBookmarks.Count)
                        f.PdfBookmarkNotes.Add(string.Empty);
                }
            }

            // Flatten into rows for the window
            var rows = new List<BookmarkNotesWindow.BookmarkNoteRow>();

            foreach (var f in pdfFiles)
            {
                if (f.PdfBookmarks == null || f.PdfBookmarks.Count == 0)
                    continue;

                var fileName = string.IsNullOrWhiteSpace(f.FileName)
                    ? Path.GetFileName(f.LocalPath)
                    : f.FileName;

                var notes = f.PdfBookmarkNotes ?? new List<string>();

                for (int i = 0; i < f.PdfBookmarks.Count; i++)
                {
                    var bm = f.PdfBookmarks[i];
                    var note = (i < notes.Count) ? notes[i] ?? string.Empty : string.Empty;

                    rows.Add(new BookmarkNotesWindow.BookmarkNoteRow
                    {
                        File = f,
                        Index = i,
                        FileName = fileName ?? string.Empty,
                        Bookmark = bm,
                        Note = note
                    });
                }
            }

            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "No bookmarks were found in the attached PDFs.",
                    "Bookmark notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Show editor
            var dlg = new BookmarkNotesWindow(rows)
            {
                Owner = this
            };

            var ok = dlg.ShowDialog() == true;
            if (!ok)
                return;

            var editedRows = dlg.GetResults();

            // Push updated notes back into their TransmittalFile
            foreach (var row in editedRows)
            {
                var f = row.File;
                if (f.PdfBookmarks == null)
                    continue;

                if (f.PdfBookmarkNotes == null)
                    f.PdfBookmarkNotes = new List<string>();

                while (f.PdfBookmarkNotes.Count < f.PdfBookmarks.Count)
                    f.PdfBookmarkNotes.Add(string.Empty);

                if (row.Index >= 0 && row.Index < f.PdfBookmarkNotes.Count)
                    f.PdfBookmarkNotes[row.Index] = row.Note ?? string.Empty;
            }
        }

        // ---------- ODBC: CONFIG-ONLY factory ----------
        private VantagepointRepository BuildRepo()
        {
            var dsn = ConfigurationManager.AppSettings["Vp.Dsn"] ?? "Deltek";
            var user = ConfigurationManager.AppSettings["Vp.User"] ?? string.Empty;
            var pwd = ConfigurationManager.AppSettings["Vp.Password"] ?? string.Empty;

            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            return new VantagepointRepository(factory);
        }

        // ---------- Recipient search (Deltek + internal) for autocomplete ----------

        // Suggest contacts + employees for To/CC autocomplete.
        // Uses the same VantagepointRepository search logic as the Contact Picker,
        // but only via reflection so we don't introduce hard compile-time deps.

        private async Task<List<EmailSuggestion>> SearchRecipientSuggestionsAsync(
            string query,
            int maxResults,
            CancellationToken ct)
        {
            var results = new List<EmailSuggestion>();

            if (string.IsNullOrWhiteSpace(query))
                return results;

            try
            {
                var repo = BuildRepo();
                if (repo == null)
                    return results;

                var repoType = repo.GetType();

                // ------------------------------------------------------------
                // 1) Preferred: combined contacts+employees method
                //    e.g. SearchContactsAndEmployeesAsync(...)
                // ------------------------------------------------------------
                var combinedMethod = repoType.GetMethod(
                    "SearchContactsAndEmployeesAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (combinedMethod != null)
                {
                    var combined = await InvokeRepoSearchMethodAsync(repo, combinedMethod, query, maxResults, ct);
                    if (combined != null)
                    {
                        results.AddRange(ExtractEmailSuggestionsFromEnumerable(combined, maxResults));
                    }
                }

                // ------------------------------------------------------------
                // 2) Fallback: any "Search*Contact*" or "Search*Employee*" methods
                // ------------------------------------------------------------
                var allSearchMethods = repoType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m =>
                        m.Name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        (m.Name.IndexOf("contact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.Name.IndexOf("employee", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                foreach (var method in allSearchMethods)
                {
                    // Skip if we already used it as the combined method
                    if (combinedMethod != null && method == combinedMethod)
                        continue;

                    var enumerable = await InvokeRepoSearchMethodAsync(repo, method, query, maxResults, ct);
                    if (enumerable == null)
                        continue;

                    results.AddRange(ExtractEmailSuggestionsFromEnumerable(enumerable, maxResults));
                }

                // Deduplicate + trim
                results = results
                    .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Take(maxResults)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                // Ignore – caller handles cancellation.
            }
            catch
            {
                // Swallow – autocomplete is non-critical.
            }

            return results;
        }

        /// <summary>
        /// Invokes a repo search method (contacts/employees/etc.) in a signature-agnostic way.
        /// It inspects parameters and fills in query/page/size/CancellationToken as appropriate,
        /// then returns whatever IEnumerable the Task produces.
        /// </summary>
        private static async Task<IEnumerable?> InvokeRepoSearchMethodAsync(
            object repo,
            MethodInfo method,
            string query,
            int maxResults,
            CancellationToken ct)
        {
            try
            {
                var parameters = method.GetParameters();
                var args = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    var pt = p.ParameterType;

                    if (pt == typeof(string))
                    {
                        // Assume search/query text
                        args[i] = query;
                    }
                    else if (pt == typeof(int))
                    {
                        // Heuristic: "page" gets 1, anything else gets maxResults
                        if (p.Name != null &&
                            p.Name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            args[i] = 1;
                        }
                        else
                        {
                            args[i] = maxResults;
                        }
                    }
                    else if (pt == typeof(CancellationToken))
                    {
                        args[i] = ct;
                    }
                    else if (pt.IsValueType)
                    {
                        // Reasonable default for any other value types
                        args[i] = Activator.CreateInstance(pt);
                    }
                    else
                    {
                        // Reference types we don’t know – just pass null
                        args[i] = null;
                    }
                }

                var invokeResult = method.Invoke(repo, args);
                if (invokeResult == null)
                    return null;

                // If it returns a Task or Task<T>, await it and pull Result if present
                if (invokeResult is Task task)
                {
                    await task.ConfigureAwait(false);

                    var taskType = task.GetType();
                    if (taskType.IsGenericType &&
                        taskType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var resultProp = taskType.GetProperty("Result");
                        var enumerable = resultProp?.GetValue(task) as IEnumerable;
                        return enumerable;
                    }

                    // Non-generic Task – nothing useful to read
                    return null;
                }

                // If the method returns IEnumerable directly
                if (invokeResult is IEnumerable enumerableDirect)
                    return enumerableDirect;
            }
            catch
            {
                // Any reflection/Invoke failures just give "no results" instead of killing autocomplete
            }

            return null;
        }


        /// <summary>
        /// Shared helper to turn whatever the repo returns (contacts, employees, etc.)
        /// into EmailSuggestion rows using reflection only.
        /// </summary>
        private static IEnumerable<EmailSuggestion> ExtractEmailSuggestionsFromEnumerable(
            IEnumerable source,
            int maxResults)
        {
            var list = new List<EmailSuggestion>();

            foreach (var item in source)
            {
                if (item == null) continue;

                var t = item.GetType();

                // Try common email property names
                var emailProp =
                    t.GetProperty("Email") ??
                    t.GetProperty("EmailAddress") ??
                    t.GetProperty("PrimaryEmail") ??
                    t.GetProperty("WorkEmail");

                var email = emailProp?.GetValue(item) as string;
                if (string.IsNullOrWhiteSpace(email))
                    continue;

                email = email.Trim();

                // Try to build a nice display name
                string? display = null;

                var nameProp =
                    t.GetProperty("Name") ??
                    t.GetProperty("FullName") ??
                    t.GetProperty("DisplayName");

                if (nameProp != null)
                {
                    display = nameProp.GetValue(item) as string;
                }
                else
                {
                    var first = t.GetProperty("FirstName")?.GetValue(item) as string;
                    var last = t.GetProperty("LastName")?.GetValue(item) as string;
                    var company = t.GetProperty("CompanyName")?.GetValue(item) as string;

                    if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                    {
                        display = (first + " " + last).Trim();
                        if (!string.IsNullOrWhiteSpace(company))
                            display += " — " + company;
                    }
                    else if (!string.IsNullOrWhiteSpace(company))
                    {
                        display = company;
                    }
                }

                if (string.IsNullOrWhiteSpace(display))
                    display = email;

                list.Add(new EmailSuggestion
                {
                    Email = email,
                    Display = $"{display}  <{email}>"
                });

                if (list.Count >= maxResults)
                    break;
            }

            return list;
        }

        // ---------- Autocomplete ----------
        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ProjectSearchBox == null || SuggestionsList == null) return;
            var q = ProjectSearchBox.Text?.Trim() ?? string.Empty;

            if (q.Length < 2)
            {
                SuggestionsList.ItemsSource = null;
                SuggestionsPopup.IsOpen = false;
                return;
            }

            _projectSearchDebouncer.Run(async () =>
            {
                _projectSearchCts?.Cancel();
                _projectSearchCts = new CancellationTokenSource();

                try
                {
                    var results = await _projectIndex.SearchAsync(q, limit: 50, _projectSearchCts.Token);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (SuggestionsList == null || SuggestionsPopup == null) return;

                        SuggestionsList.ItemsSource = results;
                        SuggestionsList.SelectedIndex = results.Any() ? 0 : -1;
                        SuggestionsPopup.IsOpen = results.Any();
                    });

                }
                catch (OperationCanceledException) { /* ignore */ }
            });
        }

        private void ProjectSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Move into the list with Up/Down
            if ((e.Key == Key.Down || e.Key == Key.Up) &&
                SuggestionsList != null &&
                SuggestionsList.HasItems)
            {
                // Ensure popup is open and list has focus
                SuggestionsPopup.IsOpen = true;
                SuggestionsList.Focus();

                if (SuggestionsList.SelectedIndex < 0 && SuggestionsList.HasItems)
                {
                    // First time: choose first for Down, last for Up
                    SuggestionsList.SelectedIndex =
                        (e.Key == Key.Down)
                            ? 0
                            : SuggestionsList.Items.Count - 1;
                }

                e.Handled = true;
                return;
            }

            // Enter from the textbox: pick current selection (or first item)
            if (e.Key == Key.Enter)
            {
                var sel = SuggestionsList?.SelectedItem as ProjectItem
                          ?? SuggestionsList?.Items.Cast<ProjectItem>().FirstOrDefault();

                if (sel != null)
                {
                    UseProject(sel);
                    e.Handled = true;
                }

                return;
            }

            // Escape closes just the popup
            if (e.Key == Key.Escape)
            {
                SuggestionsPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        // Project suggestions list key handling (same as QuickTransfer: only Enter/Escape)
        private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter = use highlighted project
            if (e.Key == Key.Enter)
            {
                if (SuggestionsList.SelectedItem is ProjectItem sel)
                {
                    UseProject(sel);
                    e.Handled = true;
                }
                return;
            }

            // Escape = close popup and return to search box
            if (e.Key == Key.Escape)
            {
                SuggestionsPopup.IsOpen = false;
                ProjectSearchBox.Focus();
                ProjectSearchBox.CaretIndex = ProjectSearchBox.Text.Length;
                e.Handled = true;
                return;
            }

            // Up/Down are NOT handled here – ListBox handles arrow navigation.
        }

        private void SuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var sel = SuggestionsList?.SelectedItem as ProjectItem;
            if (sel == null) return;
            UseProject(sel);
        }

        private void UseProject(ProjectItem project)
        {
            SuggestionsPopup.IsOpen = false;
            ProjectSearchBox.Text = project.Name;

            // NEW: remember the full folder path so the file picker can start here
            if (!string.IsNullOrWhiteSpace(project.Path) && Directory.Exists(project.Path))
                _currentProjectFolder = project.Path;
            else
                _currentProjectFolder = null;

            ParseFolderName(project.Name, out var projNo, out var projName);
            _state.Header.ProjectNumber = projNo ?? project.Name;
            _state.Header.ProjectName = projName ?? project.Name;

            var projKey = ProjectRecipientMemory.NormalizeProjectKey(_state.Header.ProjectNumber!, _state.Header.ProjectName);
            _mem?.TouchProject(projKey);

            var defaults = _mem?.GetDefaultRecipients(projKey);
            if (defaults != null && defaults.Count > 0)
            {
                var existing = ParseEmails(CcBox.Text);
                var merged = existing.Concat(defaults).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                CcBox.Text = string.Join("; ", merged);
            }

            ToBox.Focus();
        }

        private static void ParseFolderName(string folderName, out string? projNo, out string? projName)
        {
            projNo = null;
            projName = null;
            var numEnd = folderName.IndexOfAny(new[] { ' ', '(' });
            if (numEnd > 0) projNo = folderName.Substring(0, numEnd).Trim();
            else projNo = folderName.Trim();

            var open = folderName.IndexOf('(');
            var close = folderName.LastIndexOf(')');
            if (open >= 0 && close > open)
                projName = folderName.Substring(open + 1, close - open - 1).Trim();
        }

        // ---------- TO autocomplete ----------

        private void ToBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var token = GetLastEmailToken(ToBox.Text);
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            {
                ToSuggestionsPopup.IsOpen = false;
                ToSuggestionsList.ItemsSource = null;
                _toSearchCts?.Cancel();
                return;
            }

            _toSearchDebouncer.Run(async () =>
            {
                _toSearchCts?.Cancel();
                _toSearchCts = new CancellationTokenSource();
                var ct = _toSearchCts.Token;

                List<EmailSuggestion> suggestions;
                try
                {
                    suggestions = await SearchRecipientSuggestionsAsync(token, 25, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    ToSuggestionsList.ItemsSource = suggestions;
                    ToSuggestionsList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
                    ToSuggestionsPopup.IsOpen = suggestions.Count > 0;
                });
            });
        }

        private void ToBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ToSuggestionsPopup.IsOpen && ToSuggestionsList.HasItems)
            {
                ToSuggestionsList.Focus();
                if (ToSuggestionsList.SelectedIndex < 0)
                    ToSuggestionsList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && ToSuggestionsPopup.IsOpen &&
                     ToSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(ToBox, sel);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ToSuggestionsPopup.IsOpen)
            {
                ToSuggestionsPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void ToSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ToSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(ToBox, sel);
            }
        }

        // NEW: To suggestions list keyboard handler (wired from XAML: ToSuggestionsList_PreviewKeyDown)
        private void ToSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ToSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(ToBox, sel);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToSuggestionsPopup.IsOpen = false;
                ToBox.Focus();
                e.Handled = true;
            }
            // Up/Down are NOT handled here – ListBox handles navigation itself.
        }

        // ---------- CC autocomplete ----------

        private void CcBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var token = GetLastEmailToken(CcBox.Text);
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            {
                CcSuggestionsPopup.IsOpen = false;
                CcSuggestionsList.ItemsSource = null;
                _ccSearchCts?.Cancel();
                return;
            }

            _ccSearchDebouncer.Run(async () =>
            {
                _ccSearchCts?.Cancel();
                _ccSearchCts = new CancellationTokenSource();
                var ct = _ccSearchCts.Token;

                List<EmailSuggestion> suggestions;
                try
                {
                    suggestions = await SearchRecipientSuggestionsAsync(token, 25, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    CcSuggestionsList.ItemsSource = suggestions;
                    CcSuggestionsList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
                    CcSuggestionsPopup.IsOpen = suggestions.Count > 0;
                });
            });
        }

        private void CcBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && CcSuggestionsPopup.IsOpen && CcSuggestionsList.HasItems)
            {
                CcSuggestionsList.Focus();
                if (CcSuggestionsList.SelectedIndex < 0)
                    CcSuggestionsList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && CcSuggestionsPopup.IsOpen &&
                     CcSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(CcBox, sel);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && CcSuggestionsPopup.IsOpen)
            {
                CcSuggestionsPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void CcSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CcSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(CcBox, sel);
            }
        }

        private void CcSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CcSuggestionsList.SelectedItem is EmailSuggestion sel)
            {
                InsertEmailSuggestionIntoBox(CcBox, sel);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CcSuggestionsPopup.IsOpen = false;
                CcBox.Focus();
                e.Handled = true;
            }
        }

        // -------- Teams -> To / CC -----------------------------------------

        private async void PickToBtn_Click(object sender, RoutedEventArgs e)
            => await OpenContactPickerAndMergeAsync(ToBox);

        private async void PickCcBtn_Click(object sender, RoutedEventArgs e)
            => await OpenContactPickerAndMergeAsync(CcBox);

        private async Task OpenContactPickerAndMergeAsync(TextBox targetBox)
        {
            try
            {
                var repo = BuildRepo();
                var dlg = new ContactPickerWindow(repo) { Owner = this };
                await dlg.LoadAsync();
                if (dlg.ShowDialog() == true)
                {
                    MergeEmailsIntoBox(targetBox, dlg.SelectedEmails);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Contacts", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MergeEmailsIntoBox(TextBox box, IEnumerable<string> emails)
        {
            var existing = ParseEmails(box.Text);
            var merged = existing.Concat(emails)
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            box.Text = string.Join("; ", merged);
        }

        private void AddTeamToBtn_Click(object sender, RoutedEventArgs e)
        {
            AddTeamToBox(ToBox, sender as FrameworkElement);
        }

        private void AddTeamCcBtn_Click(object sender, RoutedEventArgs e)
        {
            AddTeamToBox(CcBox, sender as FrameworkElement);
        }

        private void AddTeamToBox(TextBox targetBox, FrameworkElement? button)
        {
            if (targetBox == null)
                return;

            try
            {
                // Open the new Teams picker window
                var dlg = new TeamsPickerWindow
                {
                    Owner = this
                };

                // TeamsPickerWindow does its own loading in Window_Loaded
                var result = dlg.ShowDialog();

                if (result == true &&
                    dlg.SelectedEmails != null &&
                    dlg.SelectedEmails.Count > 0)
                {
                    // Reuse your existing merge logic
                    MergeEmailsIntoBox(targetBox, dlg.SelectedEmails);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not load teams:\n{ex.Message}",
                    "Teams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Gets all email teams for the current user.
        /// First tries to read directly from dbo.UserTeams / dbo.UserTeamMembers.
        /// Falls back to the UserPreferences.EmailTeams property (via reflection) if needed.
        /// </summary>
        private IList<object> GetEmailTeams()
        {
            var list = new List<object>();

            // 1) Primary path: read from SQL tables
            foreach (var t in LoadEmailTeamsFromDatabase())
            {
                list.Add(t);
            }

            if (list.Count > 0)
                return list;

            // 2) Fallback: use _userPrefs.EmailTeams (whatever shape it has)
            if (_userPrefs == null)
                return Array.Empty<object>();

            var prop = _userPrefs.GetType().GetProperty("EmailTeams");
            if (prop == null)
                return Array.Empty<object>();

            var raw = prop.GetValue(_userPrefs) as IEnumerable;
            if (raw == null)
                return Array.Empty<object>();

            foreach (var item in raw)
            {
                if (item != null)
                    list.Add(item);
            }

            return list;
        }

        private static string GetTeamName(object team)
        {
            // Support our simple DTO first
            if (team is SimpleTeam simple)
            {
                return string.IsNullOrWhiteSpace(simple.Name)
                    ? "(Unnamed team)"
                    : simple.Name;
            }

            // Fallback to reflection for whatever type UserPreferences uses
            var t = team.GetType();
            var nameProp =
                t.GetProperty("Name") ??
                t.GetProperty("TeamName") ??
                t.GetProperty("DisplayName");

            var name = nameProp?.GetValue(team) as string;
            return string.IsNullOrWhiteSpace(name) ? "(Unnamed team)" : name;
        }

        private IEnumerable<string> GetTeamEmails(object team)
        {
            if (team == null)
                yield break;

            // First handle our simple DTO
            if (team is SimpleTeam simpleTeam)
            {
                foreach (var e in simpleTeam.Emails)
                {
                    if (!string.IsNullOrWhiteSpace(e))
                        yield return e.Trim();
                }
                yield break;
            }

            // Fallback to reflection for whatever type UserPreferences uses
            var t = team.GetType();
            // Try common property names
            var prop =
                t.GetProperty("Members") ??
                t.GetProperty("Emails") ??
                t.GetProperty("EmailAddresses");

            if (prop == null)
                yield break;

            var value = prop.GetValue(team);
            if (value == null)
                yield break;

            // If it is a single string, parse it (semicolon / comma / space separated)
            if (value is string s)
            {
                foreach (var e in ParseEmails(s))
                    yield return e;
            }
            // If it is already a collection of strings, use it directly
            else if (value is IEnumerable<string> stringEnum)
            {
                foreach (var e in stringEnum)
                {
                    if (!string.IsNullOrWhiteSpace(e))
                        yield return e.Trim();
                }
            }
            // Any other enumerable, try ToString on each
            else if (value is IEnumerable objEnum)
            {
                foreach (var e in objEnum)
                {
                    var s2 = e?.ToString();
                    if (!string.IsNullOrWhiteSpace(s2))
                    {
                        foreach (var parsed in ParseEmails(s2))
                            yield return parsed;
                    }
                }
            }
        }

        // Simple DTO used when we read teams directly from SQL

        internal sealed class EmailSuggestion
        {
            public string Email { get; init; } = string.Empty;
            public string Display { get; init; } = string.Empty; // used by ListBox.DisplayMemberPath
        }

        private sealed class SimpleTeam
        {
            public Guid TeamId { get; init; }
            public string Name { get; init; } = string.Empty;
            public List<string> Emails { get; } = new();
        }

        /// <summary>
        /// Directly loads teams and member emails for the current user
        /// from dbo.UserTeams and dbo.UserTeamMembers.
        /// </summary>
        /// 

        private List<SimpleTeam> LoadEmailTeamsFromDatabase()
        {
            var results = new List<SimpleTeam>();

            try
            {
                // Try KorTransmittalsDb first, then KorTransmittals as a fallback
                var cs =
                    ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString ??
                    ConfigurationManager.ConnectionStrings["KorTransmittals"]?.ConnectionString;

                if (string.IsNullOrWhiteSpace(cs) || string.IsNullOrWhiteSpace(_userUpn))
                    return results;

                using var conn = new SqlConnection(cs);
                conn.Open();

                // Load teams for this UPN
                var teamCmd = new SqlCommand(
                    "SELECT TeamId, Name FROM dbo.UserTeams WHERE UserUpn = @upn ORDER BY Name;",
                    conn);
                teamCmd.Parameters.AddWithValue("@upn", _userUpn);

                var dict = new Dictionary<Guid, SimpleTeam>();
                using (var r = teamCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = r.GetGuid(0);
                        var name = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                        dict[id] = new SimpleTeam
                        {
                            TeamId = id,
                            Name = name ?? string.Empty
                        };
                    }
                }

                if (dict.Count == 0)
                    return results;

                // Build IN (...) list for member query
                var ids = dict.Keys.ToList();
                var sb = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("@id" + i);
                }

                var membersSql =
                    $"SELECT TeamId, Email FROM dbo.UserTeamMembers WHERE TeamId IN ({sb}) ORDER BY Email;";

                using var memCmd = new SqlCommand(membersSql, conn);
                for (int i = 0; i < ids.Count; i++)
                {
                    memCmd.Parameters.AddWithValue("@id" + i, ids[i]);
                }

                using (var r2 = memCmd.ExecuteReader())
                {
                    while (r2.Read())
                    {
                        var teamId = r2.GetGuid(0);
                        var email = r2.IsDBNull(1) ? null : r2.GetString(1);
                        if (string.IsNullOrWhiteSpace(email))
                            continue;

                        if (dict.TryGetValue(teamId, out var team))
                        {
                            team.Emails.Add(email);
                        }
                    }
                }

                results.AddRange(dict.Values);
            }
            catch
            {
                // If anything goes wrong, we just return an empty list and
                // the caller will fall back to _userPrefs.EmailTeams.
            }

            return results;
        }

        // ---------- Email parsing ----------
        private static readonly Regex EmailSplit = new(@"[;, \r\n]+", RegexOptions.Compiled);

        // New: robust email pattern
        private static readonly Regex EmailExtract = new(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        private static List<string> ParseEmails(string? raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            foreach (var token in EmailSplit.Split(raw))
            {
                var s = token.Trim();
                if (s.Length == 0)
                    continue;

                // If in the form 'Name <email@domain>' or '<email@domain>'
                var lt = s.IndexOf('<');
                var gt = (lt >= 0) ? s.IndexOf('>', lt + 1) : -1;
                if (lt >= 0 && gt > lt + 1)
                {
                    s = s.Substring(lt + 1, gt - lt - 1).Trim();
                }

                // Extract just the email part
                var match = EmailExtract.Match(s);
                if (!match.Success)
                    continue;

                var email = match.Value.Trim();

                if (!list.Contains(email, StringComparer.OrdinalIgnoreCase))
                    list.Add(email);
            }

            return list;
        }

        private static string GetLastEmailToken(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var parts = EmailSplit.Split(raw);
            if (parts.Length == 0)
                return string.Empty;

            return parts[^1].Trim();
        }

        private void InsertEmailSuggestionIntoBox(TextBox box, EmailSuggestion suggestion)
        {
            if (suggestion == null) return;

            MergeEmailsIntoBox(box, new[] { suggestion.Email });
            box.CaretIndex = box.Text.Length;
            box.Focus();

            if (box == ToBox)
                ToSuggestionsPopup.IsOpen = false;
            else if (box == CcBox)
                CcSuggestionsPopup.IsOpen = false;
        }

        private static string ReadText(RichTextBox rtb)
        {
            var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            return range.Text.Trim();
        }

        // ---------- HTML editor toolbar handlers (legacy RichTextBox) ----------
        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            RemarksBox.Focus();
            EditingCommands.ToggleBold.Execute(null, RemarksBox);
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            RemarksBox.Focus();
            EditingCommands.ToggleItalic.Execute(null, RemarksBox);
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            RemarksBox.Focus();
            EditingCommands.ToggleUnderline.Execute(null, RemarksBox);
        }

        private void BulletButton_Click(object sender, RoutedEventArgs e)
        {
            RemarksBox.Focus();
            EditingCommands.ToggleBullets.Execute(null, RemarksBox);
        }

        // ---------- FlowDocument -> HTML (for email body) ----------
        private static string BuildHtmlFromRemarks(RichTextBox rtb)
        {
            var doc = rtb.Document;
            var sb = new StringBuilder();

            foreach (var block in doc.Blocks)
            {
                AppendBlockHtml(sb, block);
            }

            return sb.ToString();
        }

        private static void AppendBlockHtml(StringBuilder sb, Block block)
        {
            if (block is Paragraph p)
            {
                sb.Append("<p>");
                foreach (Inline inline in p.Inlines)
                {
                    AppendInlineHtml(sb, inline);
                }
                sb.Append("</p>");
            }
            else if (block is List list)
            {
                sb.Append("<ul>");
                foreach (ListItem li in list.ListItems)
                {
                    foreach (Block inner in li.Blocks)
                    {
                        sb.Append("<li>");
                        AppendBlockHtml(sb, inner);
                        sb.Append("</li>");
                    }
                }
                sb.Append("</ul>");
            }
        }

        private static void AppendInlineHtml(StringBuilder sb, Inline inline)
        {
            var range = new TextRange(inline.ContentStart, inline.ContentEnd);
            var text = range.Text;
            if (string.IsNullOrEmpty(text))
                return;

            var encoded = WebUtility.HtmlEncode(text);

            bool isBold = inline is Bold || (inline.FontWeight == FontWeights.Bold);
            bool isItalic = inline is Italic || (inline.FontStyle == FontStyles.Italic);
            bool isUnderline = inline.TextDecorations == TextDecorations.Underline;

            if (isBold) sb.Append("<strong>");
            if (isItalic) sb.Append("<em>");
            if (isUnderline) sb.Append("<u>");

            encoded = encoded
                .Replace("\r\n", "<br/>")
                .Replace("\n", "<br/>")
                .Replace("\r", "<br/>");

            sb.Append(encoded);

            if (isUnderline) sb.Append("</u>");
            if (isItalic) sb.Append("</em>");
            if (isBold) sb.Append("</strong>");
        }

        private void HeaderBar_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void FromBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }
    }

    public sealed class DescRow
    {
        public int? No { get; set; }
        public string? Description { get; set; }
        public string? Sheets { get; set; }
        public string? Date { get; set; }
        public string? Revision { get; set; }
    }

    internal sealed class ProjectItem
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    internal sealed class ProjectIndex
    {
        private readonly string _root;
        private readonly string? _mustContainSubfolder;
        private volatile List<ProjectItem> _items = new List<ProjectItem>();

        public ProjectIndex(string root, string? mustContainSubfolder)
        {
            _root = root; _mustContainSubfolder = mustContainSubfolder;
        }

        public async Task BuildIndexAsync()
        {
            var list = await Task.Run(() =>
            {
                var results = new List<ProjectItem>(capacity: 5000);

                foreach (var category in SafeEnumDirs(_root))
                {
                    var catName = Path.GetFileName(category);
                    if (catName.StartsWith("00", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var proj in SafeEnumDirs(category))
                    {
                        if (!string.IsNullOrWhiteSpace(_mustContainSubfolder))
                        {
                            var child = Path.Combine(proj, _mustContainSubfolder);
                            if (!Directory.Exists(child)) continue;
                        }

                        var name = Path.GetFileName(proj);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        results.Add(new ProjectItem { Name = name, Path = proj });
                    }
                }

                results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                return results;
            });

            _items = list;
        }

        public Task<IReadOnlyList<ProjectItem>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            return Task.Run<IReadOnlyList<ProjectItem>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var q = query.Trim();
                if (q.Length == 0 || _items.Count == 0) return Array.Empty<ProjectItem>();

                var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var results = _items
                    .Where(p => tokens.All(t => p.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(limit)
                    .ToList();

                return results;
            }, ct);
        }

        private static IEnumerable<string> SafeEnumDirs(string path)
        {
            try { return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly); }
            catch { return Array.Empty<string>(); }
        }
    }


    internal sealed class Debouncer
    {
        private readonly TimeSpan _delay;
        private int _version;

        public Debouncer(TimeSpan delay) => _delay = delay;

        public void Run(Func<Task> action)
        {
            var current = Interlocked.Increment(ref _version);
            _ = Task.Run(async () =>
            {
                await Task.Delay(_delay);
                if (current != _version) return;
                await action();
            });
        }
    }
}
