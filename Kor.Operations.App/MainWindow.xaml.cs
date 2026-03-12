#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Rendering;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
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
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Reflection;

namespace Kor.Operations
{
    public partial class MainWindow : Window
    {
        private static readonly AppConfig AppConfig = new()
        {
            ProjectsRoot = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.ProjectsRoot] ?? string.Empty).Trim()
        };
        private const string DefaultEmailDomain = "korstructural.com";
        private readonly WizardState _state = new();
        private readonly string _userUpn;
        private readonly IProjectSearchService _projectSearchService;
        private readonly IRecipientResolver _recipientResolver;
        private readonly VantagepointRepository _vantagepointRepository;
        private readonly IServiceProvider _services;
        private readonly IUploadOrchestrator _uploadOrchestrator;
        private readonly ITransmittalService _transmittalService;
        private readonly MainWindowWorkflowService _workflowService;
        private readonly Debouncer _projectSearchDebouncer = new(TimeSpan.FromMilliseconds(200));
        private readonly Debouncer _toSearchDebouncer = new(TimeSpan.FromMilliseconds(200));
        private readonly Debouncer _ccSearchDebouncer = new(TimeSpan.FromMilliseconds(200));
        private readonly DispatcherTimer _successToastTimer;
        private readonly DispatcherTimer _successToastHideTimer;
        private readonly Dictionary<string, long> _fileProgress = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _uploadCts;
        private CancellationTokenSource? _projectSearchCts;
        private CancellationTokenSource? _toSearchCts;
        private CancellationTokenSource? _ccSearchCts;
        private ProjectRecipientMemory? _mem;
        private UserPreferences? _userPrefs;
        private string? _currentProjectFolder;
        private string? _remarksEditorError;
        private bool _isExecuting;
        private bool _remarksEditorReady;
        private bool _useBasicRemarksEditor;

        public MainWindow(
            IProjectSearchService projectSearchService,
            IRecipientResolver recipientResolver,
            VantagepointRepository vantagepointRepository,
            IServiceProvider services,
            IUploadOrchestrator uploadOrchestrator,
            ITransmittalService transmittalService,
            MainWindowWorkflowService workflowService)
        {
            _projectSearchService = projectSearchService ?? throw new ArgumentNullException(nameof(projectSearchService));
            _recipientResolver = recipientResolver ?? throw new ArgumentNullException(nameof(recipientResolver));
            _vantagepointRepository = vantagepointRepository ?? throw new ArgumentNullException(nameof(vantagepointRepository));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _uploadOrchestrator = uploadOrchestrator ?? throw new ArgumentNullException(nameof(uploadOrchestrator));
            _transmittalService = transmittalService ?? throw new ArgumentNullException(nameof(transmittalService));
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
            InitializeComponent();
            _successToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _successToastTimer.Tick += SuccessToastTimer_Tick;
            _successToastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _successToastHideTimer.Tick += SuccessToastHideTimer_Tick;
            var overrideUpn = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.UserUpnOverride];
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn) ? overrideUpn.Trim() : $"{Environment.UserName}@{DefaultEmailDomain}";
            HeaderBar.UserDisplayName = Environment.UserName;
            HeaderBar.UserEmail = $"{Environment.UserName}@{DefaultEmailDomain}";
            DatePicker.SelectedDate = DateTime.Today;
            RefreshList();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SendViaBox_SelectionChanged(SendViaBox, null);
            FromBox.Text = _workflowService.InitializeCurrentUser();
            if (!string.IsNullOrWhiteSpace(FromBox.Text)) _mem = new ProjectRecipientMemory(FromBox.Text);
            _ = _projectSearchService.BuildIndexAsync();
            try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { }
            _userPrefs = await _workflowService.LoadUserPreferencesAsync();
            await InitializeRemarksEditorAsync();
            PurposeBox.ItemsSource = new[] { "Site Instructions", "For Review", "For Approval", "For Information", "For Comment", "For Permit", "For Bid", "Issued for Construction (IFC)" };
#pragma warning disable CS0618
            if (string.IsNullOrWhiteSpace(_state.Header.Purpose)) _state.Header.Purpose = "For Review";
            PurposeBox.SelectedItem = _state.Header.Purpose;
#pragma warning restore CS0618
            UpdateBookmarkNotesButtonState();
        }

        private void ShowSuccessToast(string title, string message) { SuccessToastTitle.Text = title ?? string.Empty; SuccessToastMessage.Text = message ?? string.Empty; _successToastHideTimer.Stop(); _successToastTimer.Stop(); SuccessToast.Visibility = Visibility.Visible; SuccessToast.Opacity = 0; SuccessToast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))); _successToastTimer.Start(); }
        private void SuccessToastTimer_Tick(object? sender, EventArgs e) { _successToastTimer.Stop(); SuccessToast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))); _successToastHideTimer.Start(); }
        private void SuccessToastHideTimer_Tick(object? sender, EventArgs e) { _successToastHideTimer.Stop(); SuccessToast.Visibility = Visibility.Collapsed; }
        private void SetStatus(string text) => StatusText.Text = text;
        private void UpdateProgressBar(double percent, string? detail) { UploadProgress.Value = percent; PercentText.Text = percent > 0 ? $"{percent:0}%" : ""; ProgressDetailText.Text = string.IsNullOrWhiteSpace(detail) ? "" : detail; ProgressDetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible; }
        private void RefreshList() { FilesList.ItemsSource = null; FilesList.ItemsSource = _state.Files.ToList(); }
        private void UpdateBookmarkNotesButtonState() => BookmarkNotesBtn.Visibility = ((_state.Files.Any(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) && (((PurposeBox.SelectedItem as string) ?? PurposeBox.Text ?? "").IndexOf("Site Instruction", StringComparison.OrdinalIgnoreCase) >= 0))) ? Visibility.Visible : Visibility.Collapsed;
        private void SendViaBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e) { var isEmail = UiSendVia().Equals("Email", StringComparison.OrdinalIgnoreCase); ExternalLinkCheck.IsEnabled = !isEmail; if (isEmail) ExternalLinkCheck.IsChecked = false; else if (ExternalLinkCheck.IsChecked == false) ExternalLinkCheck.IsChecked = true; }
        private string UiSendVia() { var mode = (SendViaBox.SelectedItem as ContentControl)?.Content?.ToString() ?? "SharePoint"; return mode.StartsWith("System.Windows.Controls.TextBlock", StringComparison.OrdinalIgnoreCase) ? "SharePoint" : mode; }
        private void UseBasicRemarksBtn_Click(object sender, RoutedEventArgs e) { _useBasicRemarksEditor = true; UpdateRemarksEditorUi(); RemarksBox.Focus(); }
        private void PurposeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBookmarkNotesButtonState();
        private void CancelUploadBtn_Click(object sender, RoutedEventArgs e) { _uploadCts?.Cancel(); CancelUploadBtn.IsEnabled = false; }
        private void HeaderBar_Loaded(object sender, RoutedEventArgs e) { }
        private void FromBox_TextChanged(object sender, TextChangedEventArgs e) { }
        public void LoadInitialFiles(List<string> files) { _workflowService.MergeFiles(_state, files); RefreshList(); UpdateBookmarkNotesButtonState(); }
        public void MergeFiles(List<string> files) => LoadInitialFiles(files);
        private void AddFiles_Click(object sender, RoutedEventArgs e) { var dlg = new OpenFileDialog { Filter = "All files|*.*", Multiselect = true }; if (!string.IsNullOrWhiteSpace(_currentProjectFolder) && Directory.Exists(_currentProjectFolder)) dlg.InitialDirectory = _currentProjectFolder; if (dlg.ShowDialog(this) == true) LoadInitialFiles(dlg.FileNames.ToList()); }
        private void RemoveSelected_Click(object sender, RoutedEventArgs e) { foreach (var f in FilesList.SelectedItems.Cast<TransmittalFile>().ToList()) _state.Files.Remove(f); RefreshList(); UpdateBookmarkNotesButtonState(); }
        private void Clear_Click(object sender, RoutedEventArgs e) { _state.Files.Clear(); RefreshList(); UpdateBookmarkNotesButtonState(); }
        private void BoldButton_Click(object sender, RoutedEventArgs e) { RemarksBox.Focus(); EditingCommands.ToggleBold.Execute(null, RemarksBox); }
        private void ItalicButton_Click(object sender, RoutedEventArgs e) { RemarksBox.Focus(); EditingCommands.ToggleItalic.Execute(null, RemarksBox); }
        private void UnderlineButton_Click(object sender, RoutedEventArgs e) { RemarksBox.Focus(); EditingCommands.ToggleUnderline.Execute(null, RemarksBox); }
        private void BulletButton_Click(object sender, RoutedEventArgs e) { RemarksBox.Focus(); EditingCommands.ToggleBullets.Execute(null, RemarksBox); }

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            var errors = _workflowService.ValidateRequiredFields(ProjectSearchBox.Text, ToBox.Text, SubjectBox.Text, _state.Files);
            if (_isExecuting || errors.Count > 0) { if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "Missing information", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _isExecuting = true; SendButton.IsEnabled = false; PreviewButton.IsEnabled = false;
            try { await PrepareHeaderAsync(); var tempPath = Path.Combine(Path.GetTempPath(), $"{(string.IsNullOrWhiteSpace(_state.Header.TransmittalNo) ? "Preview" : _state.Header.TransmittalNo)}-CoverPreview.pdf"); SetStatus("Generating preview..."); await _workflowService.RenderPreviewAsync(tempPath, _state.Header, _state.Files, CancellationToken.None); SetStatus("Preview ready."); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = tempPath, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "Unable to generate preview:\n" + ex.Message, "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error); SetStatus("Preview failed."); }
            finally { _isExecuting = false; SendButton.IsEnabled = true; PreviewButton.IsEnabled = true; }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var errors = _workflowService.ValidateRequiredFields(ProjectSearchBox.Text, ToBox.Text, SubjectBox.Text, _state.Files);
            if (_isExecuting || errors.Count > 0) { if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "Missing information", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _isExecuting = true; SendButton.IsEnabled = false; PreviewButton.IsEnabled = false; var closeAfterSuccess = false;
            try
            {
                var prepared = await PrepareHeaderAsync();
                var isEmail = string.Equals(_state.Header.SendVia, "Email", StringComparison.OrdinalIgnoreCase);
                _uploadCts?.Cancel(); _uploadCts = new CancellationTokenSource(); CancelUploadBtn.IsEnabled = true; _fileProgress.Clear(); foreach (var f in _state.Files) _fileProgress[f.LocalPath] = 0;
                var result = await _workflowService.SendAsync(new TransmittalSendRequest
                {
                    Header = _state.Header,
                    Files = _state.Files,
                    ToRecipients = prepared.ToRecipients,
                    CcRecipients = prepared.CcRecipients,
                    Folder = _workflowService.BuildTransmittalFolderPath(_state.Header),
                    SenderUpn = FromBox.Text ?? string.Empty,
                    RemarksHtml = prepared.RemarksHtml,
                    SignatureHtml = _userPrefs?.EmailSignatureHtml,
                    NeedExternal = !isEmail && (ExternalLinkCheck?.IsChecked ?? false),
                    AttachIfSmall = isEmail,
                    UploadProgress = new Progress<(string file, long sent, long total)>(p => { if (_fileProgress.ContainsKey(p.file)) { _fileProgress[p.file] = p.sent; var sent = _fileProgress.Values.Sum(); var total = _state.Files.Sum(x => x.SizeBytes); UpdateProgressBar(total > 0 ? sent * 100.0 / total : 0.0, ""); } }),
                    Status = new Progress<string>(SetStatus)
                }, _uploadCts.Token);
                _mem?.LearnRecipients(ProjectRecipientMemory.NormalizeProjectKey(_state.Header.ProjectNumber ?? string.Empty, _state.Header.ProjectName), result.AllRecipients);
                ShowSuccessToast("Transmittal Sent", "Your transmittal was sent successfully."); closeAfterSuccess = true; await Task.Delay(4000); Close();
            }
            catch (OperationCanceledException) { SetStatus("Upload canceled."); UpdateProgressBar(0, ""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButton.OK, MessageBoxImage.Error); SetStatus("Error."); }
            finally { _isExecuting = false; if (!closeAfterSuccess) { SendButton.IsEnabled = true; PreviewButton.IsEnabled = true; } }
        }

        private async Task<PreparedHeader> PrepareHeaderAsync()
        {
            var richHtml = _useBasicRemarksEditor ? string.Empty : await GetRemarksHtmlAsync();
            if (!_useBasicRemarksEditor && !_remarksEditorReady) throw new InvalidOperationException("Message / Remarks editor is not ready. Use 'Use basic editor' or retry.");
            return _workflowService.PrepareHeader(_state.Header, ProjectSearchBox.Text, SubjectBox.Text, UiSendVia(), DatePicker.SelectedDate, ToBox.Text, CcBox.Text, HeaderBar?.UserDisplayName, FromBox?.Text, PurposeBox?.SelectedItem as string ?? PurposeBox?.Text, _useBasicRemarksEditor, MainWindowWorkflowService.BuildHtmlFromRemarks(RemarksBox), MainWindowWorkflowService.ReadText(RemarksBox), richHtml);
        }

        private async Task InitializeRemarksEditorAsync()
        {
            _remarksEditorReady = false; _useBasicRemarksEditor = false; _remarksEditorError = null; UpdateRemarksEditorUi();
            try
            {
                var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "QuickRemarksEditor.html");
                if (!File.Exists(htmlPath)) throw new FileNotFoundException("Remarks editor HTML not found.", htmlPath);
                await RemarksEditor.EnsureCoreWebView2Async();
                RemarksEditor.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && !e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
                var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                RemarksEditor.NavigationCompleted += async (_, e) => { if (!e.IsSuccess) { _remarksEditorError = $"Remarks editor failed to load ({e.WebErrorStatus})."; UpdateRemarksEditorUi(); readyTcs.TrySetResult(false); return; } try { var probe = await RemarksEditor.CoreWebView2.ExecuteScriptAsync("!!(window.getEditorHtml || window.getSignatureHtml);"); _remarksEditorReady = string.Equals(probe, "true", StringComparison.OrdinalIgnoreCase); _remarksEditorError = _remarksEditorReady ? null : "Remarks editor loaded but is not ready."; UpdateRemarksEditorUi(); readyTcs.TrySetResult(_remarksEditorReady); } catch (Exception ex) { _remarksEditorError = $"Remarks editor script check failed: {ex.Message}"; UpdateRemarksEditorUi(); readyTcs.TrySetResult(false); } };
                RemarksEditor.Source = new Uri(htmlPath);
                if (await Task.WhenAny(readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(5))) != readyTcs.Task) { _remarksEditorError = "Remarks editor did not initialize in time."; UpdateRemarksEditorUi(); }
            }
            catch (Exception ex) { _remarksEditorError = ex.Message; UpdateRemarksEditorUi(); }
        }

        private void UpdateRemarksEditorUi()
        {
            if (_useBasicRemarksEditor) { RemarksEditor.Visibility = Visibility.Collapsed; UseBasicRemarksBtn.Visibility = Visibility.Collapsed; RemarksEditorStatus.Visibility = Visibility.Visible; RemarksEditorStatus.Text = "Using basic editor."; RemarksEditorStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151")); RemarksBox.Visibility = Visibility.Visible; RemarksBox.IsReadOnly = false; RemarksBox.Height = 320; return; }
            RemarksBox.Visibility = Visibility.Collapsed; RemarksEditor.Visibility = Visibility.Visible; RemarksEditorStatus.Visibility = _remarksEditorReady ? Visibility.Collapsed : Visibility.Visible; UseBasicRemarksBtn.Visibility = _remarksEditorReady ? Visibility.Collapsed : Visibility.Visible; if (!_remarksEditorReady) RemarksEditorStatus.Text = "Rich text editor unavailable. " + (string.IsNullOrWhiteSpace(_remarksEditorError) ? string.Empty : _remarksEditorError);
        }

        private async Task<string> GetRemarksHtmlAsync()
        {
            if (!_remarksEditorReady || RemarksEditor?.CoreWebView2 == null) return string.Empty;
            var result = await RemarksEditor.CoreWebView2.ExecuteScriptAsync("window.getEditorHtml ? window.getEditorHtml() : (window.getSignatureHtml ? window.getSignatureHtml() : '');");
            return string.IsNullOrWhiteSpace(result) || result is "null" or "undefined" ? string.Empty : JsonSerializer.Deserialize<string>(result) ?? string.Empty;
        }

        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RunProjectSearch(ProjectSearchBox.Text);
        private void ToBox_TextChanged(object sender, TextChangedEventArgs e) => RunRecipientSearch(ToBox, ToSuggestionsPopup, ToSuggestionsList, _toSearchDebouncer, true);
        private void CcBox_TextChanged(object sender, TextChangedEventArgs e) => RunRecipientSearch(CcBox, CcSuggestionsPopup, CcSuggestionsList, _ccSearchDebouncer, false);
        private void SuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (SuggestionsList.SelectedItem is ProjectSearchResult sel) ApplyProject(sel); }
        private void ToSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ToSuggestionsList.SelectedItem is EmailSuggestion sel) MergeSuggestion(ToBox, sel); }
        private void CcSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (CcSuggestionsList.SelectedItem is EmailSuggestion sel) MergeSuggestion(CcBox, sel); }
        private void SuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void ProjectSearchBox_KeyDown(object sender, KeyEventArgs e) => HandleProjectKeyDown(e);
        private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleProjectListKeyDown(e);
        private void ToBox_KeyDown(object sender, KeyEventArgs e) => HandleRecipientKeyDown(e, ToBox, ToSuggestionsPopup, ToSuggestionsList);
        private void CcBox_KeyDown(object sender, KeyEventArgs e) => HandleRecipientKeyDown(e, CcBox, CcSuggestionsPopup, CcSuggestionsList);
        private void ToSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleRecipientListKeyDown(e, ToBox, ToSuggestionsPopup, ToSuggestionsList);
        private void CcSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleRecipientListKeyDown(e, CcBox, CcSuggestionsPopup, CcSuggestionsList);
        private async void PickToBtn_Click(object sender, RoutedEventArgs e) => await OpenContactPickerAndMergeAsync(ToBox);
        private async void PickCcBtn_Click(object sender, RoutedEventArgs e) => await OpenContactPickerAndMergeAsync(CcBox);
        private void AddTeamToBtn_Click(object sender, RoutedEventArgs e) => AddTeamToBox(ToBox);
        private void AddTeamCcBtn_Click(object sender, RoutedEventArgs e) => AddTeamToBox(CcBox);
        private void RunProjectSearch(string? text) { var q = text?.Trim() ?? string.Empty; if (q.Length < 2) { SuggestionsList.ItemsSource = null; SuggestionsPopup.IsOpen = false; return; } _projectSearchDebouncer.Run(async () => { _projectSearchCts?.Cancel(); _projectSearchCts = new CancellationTokenSource(); try { var results = await _projectSearchService.SearchAsync(q, 50, _projectSearchCts.Token); await Dispatcher.InvokeAsync(() => { SuggestionsList.ItemsSource = results; SuggestionsList.SelectedIndex = results.Any() ? 0 : -1; SuggestionsPopup.IsOpen = results.Any(); }); } catch (OperationCanceledException) { } }); }
        private void ApplyProject(ProjectSearchResult project) { SuggestionsPopup.IsOpen = false; var selection = _projectSearchService.Resolve(project); ProjectSearchBox.Text = selection.DisplayName; _currentProjectFolder = selection.FolderPath; _state.Header.ProjectNumber = selection.ProjectNumber; _state.Header.ProjectName = selection.ProjectName; var projKey = ProjectRecipientMemory.NormalizeProjectKey(selection.ProjectNumber, selection.ProjectName); _mem?.TouchProject(projKey); var defaults = _mem?.GetDefaultRecipients(projKey); if (defaults?.Count > 0) CcBox.Text = string.Join("; ", MainWindowWorkflowService.ParseEmails(CcBox.Text).Concat(defaults).Distinct(StringComparer.OrdinalIgnoreCase)); ToBox.Focus(); }
        private void RunRecipientSearch(TextBox box, Popup popup, ListBox list, Debouncer debouncer, bool isTo)
        {
            var token = MainWindowWorkflowService.GetLastEmailToken(box.Text);
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            {
                popup.IsOpen = false;
                list.ItemsSource = null;
                if (isTo) _toSearchCts?.Cancel(); else _ccSearchCts?.Cancel();
                return;
            }

            debouncer.Run(async () =>
            {
                if (isTo) { _toSearchCts?.Cancel(); _toSearchCts = new CancellationTokenSource(); }
                else { _ccSearchCts?.Cancel(); _ccSearchCts = new CancellationTokenSource(); }

                var cts = isTo ? _toSearchCts! : _ccSearchCts!;

                try
                {
                    var suggestions = (await _recipientResolver.SearchSuggestionsAsync(_userUpn, token, 25, cts.Token))
                        .Select(s => new EmailSuggestion { Email = s.Email, Display = s.Display })
                        .ToList();

                    if (!cts.Token.IsCancellationRequested)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            list.ItemsSource = suggestions;
                            list.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
                            popup.IsOpen = suggestions.Count > 0;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
        private void MergeSuggestion(TextBox box, EmailSuggestion suggestion) { box.Text = string.Join("; ", MainWindowWorkflowService.ParseEmails(box.Text).Concat(new[] { suggestion.Email }).Distinct(StringComparer.OrdinalIgnoreCase)); box.CaretIndex = box.Text.Length; box.Focus(); if (box == ToBox) ToSuggestionsPopup.IsOpen = false; else CcSuggestionsPopup.IsOpen = false; }
        private async Task OpenContactPickerAndMergeAsync(TextBox targetBox) { var dlg = new ContactPickerWindow(_vantagepointRepository) { Owner = this }; await dlg.LoadAsync(); if (dlg.ShowDialog() == true) targetBox.Text = string.Join("; ", MainWindowWorkflowService.ParseEmails(targetBox.Text).Concat(dlg.SelectedEmails).Distinct(StringComparer.OrdinalIgnoreCase)); }
        private void AddTeamToBox(TextBox targetBox) { var dlg = _services.GetRequiredService<TeamsPickerWindow>(); dlg.Owner = this; if (dlg.ShowDialog() == true && dlg.SelectedEmails?.Count > 0) targetBox.Text = string.Join("; ", MainWindowWorkflowService.ParseEmails(targetBox.Text).Concat(dlg.SelectedEmails).Distinct(StringComparer.OrdinalIgnoreCase)); }
        private void BookmarkNotesBtn_Click(object sender, RoutedEventArgs e) { if (((PurposeBox.SelectedItem as string) ?? PurposeBox.Text ?? string.Empty).IndexOf("Site Instruction", StringComparison.OrdinalIgnoreCase) < 0) { MessageBox.Show(this, "Bookmark notes are only available when Purpose is set to \"Site Instructions\".", "Bookmark notes", MessageBoxButton.OK, MessageBoxImage.Information); return; } var pdfFiles = _state.Files.Where(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList(); if (pdfFiles.Count == 0) { MessageBox.Show(this, "There are no PDF files attached.", "Bookmark notes", MessageBoxButton.OK, MessageBoxImage.Information); return; } foreach (var f in pdfFiles.Where(f => f.PdfBookmarks == null || f.PdfBookmarks.Count == 0)) try { if (File.Exists(f.LocalPath)) f.PdfBookmarks = PdfBookmarkExtractor.TryGetBookmarks(f.LocalPath); } catch { } foreach (var f in pdfFiles.Where(f => f.PdfBookmarks?.Count > 0)) { f.PdfBookmarkNotes ??= new List<string>(); while (f.PdfBookmarkNotes.Count < f.PdfBookmarks!.Count) f.PdfBookmarkNotes.Add(string.Empty); } var rows = pdfFiles.Where(f => f.PdfBookmarks?.Count > 0).SelectMany(f => f.PdfBookmarks!.Select((bm, i) => new BookmarkNotesWindow.BookmarkNoteRow { File = f, Index = i, FileName = string.IsNullOrWhiteSpace(f.FileName) ? Path.GetFileName(f.LocalPath) : f.FileName, Bookmark = bm, Note = i < (f.PdfBookmarkNotes?.Count ?? 0) ? f.PdfBookmarkNotes![i] : string.Empty })).ToList(); if (rows.Count == 0) { MessageBox.Show(this, "No bookmarks were found in the attached PDFs.", "Bookmark notes", MessageBoxButton.OK, MessageBoxImage.Information); return; } var dlg = new BookmarkNotesWindow(rows) { Owner = this }; if (dlg.ShowDialog() == true) foreach (var row in dlg.GetResults()) { row.File.PdfBookmarkNotes ??= new List<string>(); while (row.File.PdfBookmarkNotes.Count < row.File.PdfBookmarks!.Count) row.File.PdfBookmarkNotes.Add(string.Empty); row.File.PdfBookmarkNotes[row.Index] = row.Note ?? string.Empty; } }
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if ((ReferenceEquals(Keyboard.FocusedElement, ProjectSearchBox) || ReferenceEquals(Keyboard.FocusedElement, SuggestionsList)) && SuggestionsPopup.IsOpen) HandleProjectKeyDown(e); if (!e.Handled && (ReferenceEquals(Keyboard.FocusedElement, ToBox) || ReferenceEquals(Keyboard.FocusedElement, ToSuggestionsList))) HandleRecipientKeyDown(e, ToBox, ToSuggestionsPopup, ToSuggestionsList); if (!e.Handled && (ReferenceEquals(Keyboard.FocusedElement, CcBox) || ReferenceEquals(Keyboard.FocusedElement, CcSuggestionsList))) HandleRecipientKeyDown(e, CcBox, CcSuggestionsPopup, CcSuggestionsList); }
        private void HandleProjectKeyDown(KeyEventArgs e) { if ((e.Key == Key.Down || e.Key == Key.Up) && SuggestionsList.HasItems) { SuggestionsPopup.IsOpen = true; SuggestionsList.Focus(); if (SuggestionsList.SelectedIndex < 0) SuggestionsList.SelectedIndex = e.Key == Key.Down ? 0 : SuggestionsList.Items.Count - 1; e.Handled = true; } else if (e.Key == Key.Enter) { if ((SuggestionsList.SelectedItem as ProjectSearchResult ?? SuggestionsList.Items.Cast<ProjectSearchResult>().FirstOrDefault()) is { } sel) { ApplyProject(sel); e.Handled = true; } } else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; } }
        private void HandleProjectListKeyDown(KeyEventArgs e) { if (e.Key == Key.Enter && SuggestionsList.SelectedItem is ProjectSearchResult sel) { ApplyProject(sel); e.Handled = true; } else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; ProjectSearchBox.Focus(); ProjectSearchBox.CaretIndex = ProjectSearchBox.Text.Length; e.Handled = true; } }
        private void HandleRecipientKeyDown(KeyEventArgs e, TextBox box, Popup popup, ListBox list) { if (e.Key == Key.Down && popup.IsOpen && list.HasItems) { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = 0; e.Handled = true; } else if (e.Key == Key.Enter && popup.IsOpen && list.SelectedItem is EmailSuggestion sel) { MergeSuggestion(box, sel); e.Handled = true; } else if (e.Key == Key.Escape && popup.IsOpen) { popup.IsOpen = false; e.Handled = true; } }
        private void HandleRecipientListKeyDown(KeyEventArgs e, TextBox box, Popup popup, ListBox list) { if (e.Key == Key.Enter && list.SelectedItem is EmailSuggestion sel) { MergeSuggestion(box, sel); e.Handled = true; } else if (e.Key == Key.Escape) { popup.IsOpen = false; box.Focus(); e.Handled = true; } }
        private static string GetRequiredProjectsRoot() => !string.IsNullOrWhiteSpace(AppConfig.ProjectsRoot) ? AppConfig.ProjectsRoot : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");
    }
}

