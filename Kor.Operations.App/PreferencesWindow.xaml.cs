#nullable enable
#pragma warning disable CA1416
using Kor.Operations.Services;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;   // for CancelEventArgs
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kor.Operations.Core;
using Kor.Operations.App.Options;
using Serilog;

namespace Kor.Operations
{
    public partial class PreferencesWindow : Window
    {
        // -----------------------------
        // Fields
        // -----------------------------
        private readonly string _userUpn = string.Empty;
        private readonly PreferencesRepository _repo;
        private readonly PreferencesFavoritesService _favoritesService;
        private readonly PreferencesTeamsService _teamsService;
        private readonly PeopleLookupService _peopleLookupService;
        private readonly SignatureEditorService _signatureEditorService;
        private readonly IUserPreferencesStore _userPrefsStore;
        private readonly VantagepointRepository _vantagepointRepository;
        private readonly IAuthorizationService _authorizationService;
        private readonly UserOptions _userOptions;
        private readonly DatabaseOptions _databaseOptions;
        private readonly AppConfig _appConfig;
        private readonly ProjectIndex _projectIndex;

        // Shared imported Newforma teams UPN
        private const string CommonTeamsUpn = "common";

        private UserPreferences? _userPrefs;

        private const string MustContainSubfolder = null;
        private readonly Debouncer _projectDebouncer = new(TimeSpan.FromMilliseconds(200));
        private CancellationTokenSource? _projectSearchCts;

        private readonly ObservableCollection<FavoriteProject> _favorites = new();
        private readonly ObservableCollection<Team> _teams = new();

        private ContextMenu? _peopleMenu;
        private DateTime _lastProjQuery = DateTime.MinValue;
        private DateTime _lastPeopleQuery = DateTime.MinValue;

        private string _pickedDisplayName = string.Empty;
        private string _pickedEmail = string.Empty;

        // -----------------------------
        // Ctor
        // -----------------------------
        public PreferencesWindow(PreferencesRepository repo, PreferencesFavoritesService favoritesService, PreferencesTeamsService teamsService, PeopleLookupService peopleLookupService, SignatureEditorService signatureEditorService, IUserPreferencesStore userPrefsStore, VantagepointRepository vantagepointRepository, IAuthorizationService authorizationService, UserOptions userOptions, DatabaseOptions databaseOptions, StorageOptions storageOptions)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
            _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
            _peopleLookupService = peopleLookupService ?? throw new ArgumentNullException(nameof(peopleLookupService));
            _signatureEditorService = signatureEditorService ?? throw new ArgumentNullException(nameof(signatureEditorService));
            _userPrefsStore = userPrefsStore ?? throw new ArgumentNullException(nameof(userPrefsStore));
            _vantagepointRepository = vantagepointRepository ?? throw new ArgumentNullException(nameof(vantagepointRepository));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _userOptions = userOptions ?? throw new ArgumentNullException(nameof(userOptions));
            _databaseOptions = databaseOptions ?? throw new ArgumentNullException(nameof(databaseOptions));
            _appConfig = new AppConfig
            {
                ProjectsRoot = (storageOptions ?? throw new ArgumentNullException(nameof(storageOptions))).ProjectsRoot.Trim()
            };
            _projectIndex = new ProjectIndex(GetRequiredProjectsRoot(), MustContainSubfolder);
            InitializeComponent();

            if (!_authorizationService.IsAuthorized("Preferences"))
                return;

            var overrideUpn = _userOptions.UserUpnOverride;
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

            // Pre-fill header; HeaderLoader will refine (and add photo)
            HeaderBar.UserDisplayName = Environment.UserName.Replace('.', ' ').Replace('_', ' ');
            HeaderBar.UserEmail = _userUpn;

            var cs = _databaseOptions.KorTransmittalsDb;
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("KorTransmittalsDb connection string is missing.");
            TryOpenOnceOrThrow(cs, TimeSpan.FromSeconds(3));

            FavoritesGrid.ItemsSource = _favorites;
            TeamsList.ItemsSource = _teams;
            MembersGrid.ItemsSource = null;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authorizationService.IsAuthorized("Preferences"))
            {
                MessageBox.Show("You are not authorized to access Preferences.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            try { await HeaderLoader.ApplyAsync(HeaderBar); } catch { /* non-fatal */ }

            _ = _projectIndex.BuildIndexAsync()
                .ContinueWith(
                    t => Log.Warning(t.Exception, "Project index build failed in PreferencesWindow."),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

            try
            {
                // Load central user prefs (AutoFile, ItemsToFile, Signature)
                await LoadUserPreferencesAsync();

                // Initialize HTML signature editor (WebView2 + TinyMCE)
                await InitializeSignatureEditorAsync();

                await LoadFavoritesAsync();
                await LoadTeamsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Initial load failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------
        // Helpers
        // -----------------------------
        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return "";
            var idx = user.IndexOf('\\');
            return idx >= 0 && idx < user.Length - 1 ? user[(idx + 1)..] : user;
        }

        private static void TryOpenOnceOrThrow(string cs, TimeSpan timeout)
        {
            var cb = new SqlConnectionStringBuilder(cs) { ConnectTimeout = Math.Max(2, (int)timeout.TotalSeconds) };
            using var cnn = new SqlConnection(cb.ConnectionString);
            cnn.Open();
            using var cmd = cnn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = "SELECT 1";
            cmd.CommandType = System.Data.CommandType.Text;
            _ = cmd.ExecuteScalar();
        }

        // -----------------------------
        // Load user preferences
        // -----------------------------
        private async Task LoadUserPreferencesAsync()
        {
            try
            {
                _userPrefs = await _userPrefsStore.GetAsync(_userUpn);

                if (_userPrefs == null)
                {
                    // First time: create defaults (prompt ON, Items-to-File ON)
                    _userPrefs = new UserPreferences
                    {
                        UserUpn = _userUpn,
                        AutoFileOnSend = true,
                        ItemsToFileEnabled = true,
                        EmailSignatureHtml = null
                    };

                    AutoFileOnSendCheckBox.IsChecked = true;
                    ItemsToFileCheckBox.IsChecked = true;
                    // Signature HTML will be pushed into the editor once it initializes
                }
                else
                {
                    AutoFileOnSendCheckBox.IsChecked = _userPrefs.AutoFileOnSend;
                    ItemsToFileCheckBox.IsChecked = _userPrefs.ItemsToFileEnabled;
                    // SignatureEditor gets populated in InitializeSignatureEditorAsync's NavigationCompleted
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not load user preferences:\n{ex.Message}",
                    "Preferences",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string GetRequiredProjectsRoot() => !string.IsNullOrWhiteSpace(_appConfig.ProjectsRoot) ? _appConfig.ProjectsRoot : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");

        // Save preferences back to SQL when closing
        private async Task SaveUserPreferencesAsync()
        {
            if (_userPrefs == null)
                return;

            _userPrefs.AutoFileOnSend = AutoFileOnSendCheckBox.IsChecked == true;
            _userPrefs.ItemsToFileEnabled = ItemsToFileCheckBox.IsChecked == true;

            string? html = null;

            try
            {
                if (SignatureEditor?.CoreWebView2 != null)
                {
                    html = await _signatureEditorService.GetHtmlAsync(SignatureEditor);
                }
            }
            catch
            {
                // best effort; if this fails, we'll just keep whatever was already in _userPrefs.EmailSignatureHtml
            }

            if (!string.IsNullOrWhiteSpace(html))
                _userPrefs.EmailSignatureHtml = html;
            else
                _userPrefs.EmailSignatureHtml = null;

            try
            {
                await _userPrefsStore.SaveAsync(_userPrefs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not save user preferences:\n{ex.Message}",
                    "Preferences",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // -----------------------------
        // Load data
        // -----------------------------
        private async Task LoadFavoritesAsync()
        {
            _favorites.Clear();
            foreach (var favorite in await _favoritesService.LoadFavoritesAsync(_userUpn))
                _favorites.Add(favorite);
        }

        private async Task LoadTeamsAsync()
        {
            _teams.Clear();
            foreach (var team in await _teamsService.LoadTeamsAsync(_userUpn))
                _teams.Add(team);

            // We intentionally DO NOT load CommonTeamsUpn here anymore, so
            // the list only shows "My Teams" as before.

            if (_teams.Any())
            {
                var first = _teams.First();
                TeamsList.SelectedItem = first;
                MembersGrid.ItemsSource = first.Members;
            }
            else
            {
                MembersGrid.ItemsSource = null;
            }
        }

        // -----------------------------
        // Favorites
        // -----------------------------
        private async void AddFavorite_Click(object sender, RoutedEventArgs e)
        {
            var text = ProjectSearchBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Type a project number first.", "Info");
                return;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var projNo = parts[0];
            var projName = parts.Length > 1 ? parts[1] : "";

            if (_favorites.Any(f => f.ProjectNo.Equals(projNo, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Already in favorites.", "Info");
                return;
            }

            try
            {
                await _favoritesService.AddFavoriteAsync(_userUpn, projNo, projName);
                _favorites.Add(new FavoriteProject { ProjectNo = projNo, ProjectName = projName });
                ProjectSearchBox.Clear();
                ProjectSuggestionsPopup.IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not add favourite:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (FavoritesGrid.SelectedItem is not FavoriteProject fp) return;

            try
            {
                await _favoritesService.RemoveFavoriteAsync(_userUpn, fp.ProjectNo);
                _favorites.Remove(fp);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not remove favorite:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProjectSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddFavorite_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Project-style autocomplete for Favorite Projects
        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = ProjectSearchBox.Text?.Trim() ?? string.Empty;

            if (term.Length < 2)
            {
                ProjectSuggestionsPopup.IsOpen = false;
                ProjectSuggestionsList.ItemsSource = null;
                return;
            }

            var now = DateTime.UtcNow;
            _lastProjQuery = now;

            _projectDebouncer.Run(async () =>
            {
                _projectSearchCts?.Cancel();
                _projectSearchCts = new CancellationTokenSource();

                try
                {
                    var results = await _projectIndex.SearchAsync(term, limit: 100, _projectSearchCts.Token);

                    var names = results
                        .Select(p => p.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if ((ProjectSearchBox.Text?.Trim() ?? string.Empty).Length < 2)
                        {
                            ProjectSuggestionsPopup.IsOpen = false;
                            ProjectSuggestionsList.ItemsSource = null;
                            return;
                        }

                        ProjectSuggestionsList.ItemsSource = names;
                        ProjectSuggestionsPopup.IsOpen = names.Count > 0;
                    });
                }
                catch (OperationCanceledException)
                {
                    // user kept typing; ignore
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ProjectSuggestionsPopup.IsOpen = false;
                        ProjectSuggestionsList.ItemsSource = null;
                    });
                }
            });
        }

        private void CommitProjectSuggestion()
        {
            var selected = ProjectSuggestionsList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected)) return;

            ProjectSearchBox.Text = selected;
            ProjectSearchBox.CaretIndex = selected.Length;
            ProjectSuggestionsPopup.IsOpen = false;
            ProjectSearchBox.Focus();
        }

        private void ProjectSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CommitProjectSuggestion();
        }

        private void ProjectSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitProjectSuggestion();
                e.Handled = true;
            }
        }

        // -----------------------------
        // Teams / Members
        // -----------------------------
        private async void AddTeam_Click(object sender, RoutedEventArgs e)
        {
            var name = NewTeamNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Type a team name.", "Info");
                return;
            }

            if (_teams.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Team already exists.", "Info");
                return;
            }

            try
            {
                var t = await _teamsService.AddTeamAsync(_userUpn, name);
                _teams.Add(t);
                TeamsList.SelectedItem = t;
                MembersGrid.ItemsSource = t.Members;
                NewTeamNameBox.Clear();
                TeamSuggestionsPopup.IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not add team:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewTeamNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTeam_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Team textbox suggestions (from projects)
        private void NewTeamNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = NewTeamNameBox.Text?.Trim() ?? string.Empty;

            if (term.Length < 2)
            {
                TeamSuggestionsPopup.IsOpen = false;
                TeamSuggestionsList.ItemsSource = null;
                return;
            }

            _projectDebouncer.Run(async () =>
            {
                try
                {
                    var results = await _projectIndex.SearchAsync(term, limit: 100, CancellationToken.None);

                    var names = results
                        .Select(p => p.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if ((NewTeamNameBox.Text?.Trim() ?? string.Empty).Length < 2)
                        {
                            TeamSuggestionsPopup.IsOpen = false;
                            TeamSuggestionsList.ItemsSource = null;
                            return;
                        }

                        TeamSuggestionsList.ItemsSource = names;
                        TeamSuggestionsPopup.IsOpen = names.Count > 0;
                    });
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        TeamSuggestionsPopup.IsOpen = false;
                        TeamSuggestionsList.ItemsSource = null;
                    });
                }
            });
        }

        private void CommitTeamSuggestion()
        {
            var selected = TeamSuggestionsList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected)) return;

            NewTeamNameBox.Text = selected;
            NewTeamNameBox.CaretIndex = selected.Length;
            TeamSuggestionsPopup.IsOpen = false;
            NewTeamNameBox.Focus();
        }

        private void TeamSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CommitTeamSuggestion();
        }

        private void TeamSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTeamSuggestion();
                e.Handled = true;
            }
        }

        private async void DeleteTeam_Click(object sender, RoutedEventArgs e)
        {
            if (TeamsList.SelectedItem is not Team t) return;

            if (t.IsCommon)
            {
                MessageBox.Show(this,
                    "Shared project teams (common) are managed centrally and cannot be deleted here.",
                    "Teams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                await _teamsService.DeleteTeamAsync(t.Id);
                _teams.Remove(t);
                MembersGrid.ItemsSource = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not delete team:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TeamsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MembersGrid.ItemsSource = (TeamsList.SelectedItem as Team)?.Members;
        }

        // -----------------------------
        // Add Member
        // -----------------------------
        private async void AddMember_Click(object sender, RoutedEventArgs e)
        {
            if (TeamsList.SelectedItem is not Team t)
            {
                MessageBox.Show(this, "Select a team first.", "Info");
                return;
            }

            if (t.IsCommon)
            {
                MessageBox.Show(this,
                    "Shared project teams (common) are read-only here.",
                    "Teams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var email = MemberEmailBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show(this, "Email required.", "Info");
                return;
            }

            if (t.Members.Any(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Member already present.", "Info");
                return;
            }

            // Always resolve full real name from Deltek (same source as picker)
            var resolved = await _peopleLookupService.ResolveDisplayNameForEmailAsync(email);

            try
            {
                t.Members.Add(await _teamsService.AddMemberAsync(t.Id, email, resolved));

                MemberEmailBox.Clear();
                _pickedDisplayName = _pickedEmail = string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not add member:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // -----------------------------
        // Remove Member
        // -----------------------------
        private async void RemoveMember_Click(object sender, RoutedEventArgs e)
        {
            if (TeamsList.SelectedItem is not Team t) return;

            if (t.IsCommon)
            {
                MessageBox.Show(this,
                    "Shared project teams (common) are read-only here.",
                    "Teams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (MembersGrid.SelectedItem is not TeamMember m) return;

            try
            {
                await _teamsService.RemoveMemberAsync(t.Id, m.Email);
                t.Members.Remove(m);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not remove member:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // -----------------------------
        // Type-Ahead People Search
        // -----------------------------
        private async void MemberEmailBox_KeyUp(object sender, KeyEventArgs e)
        {
            var term = MemberEmailBox.Text?.Trim() ?? "";
            if (term.Length < 2)
            {
                _peopleMenu.Hide();
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastPeopleQuery).TotalMilliseconds < 150) return;
            _lastPeopleQuery = now;

            try
            {
                var items = await _peopleLookupService.SearchPeopleAsync(_userUpn, term, 10);

                var menu = new ContextMenu();
                foreach (var (Email, DisplayName) in items)
                {
                    var label = string.IsNullOrWhiteSpace(DisplayName)
                        ? Email
                        : $"{DisplayName} <{Email}>";

                    var mi = new MenuItem { Header = label };
                    mi.Click += (_, __) =>
                    {
                        _pickedEmail = Email ?? "";
                        _pickedDisplayName = DisplayName ?? "";
                        MemberEmailBox.Text = _pickedEmail;
                        MemberEmailBox.CaretIndex = _pickedEmail.Length;
                        _peopleMenu.Hide();
                    };

                    menu.Items.Add(mi);
                }

                if (menu.Items.Count == 0)
                {
                    _peopleMenu.Hide();
                    return;
                }

                _peopleMenu = menu;
                _peopleMenu.PlacementTarget = MemberEmailBox;
                _peopleMenu.IsOpen = true;
            }
            catch
            {
                _peopleMenu.Hide();
            }
        }

        // -----------------------------
        // Resolve Name (Master Fix)
        // -----------------------------
        // -----------------------------
        // Contact Picker (reuse Vantagepoint repo)
        // -----------------------------
        private async void PickMemberBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TeamsList.SelectedItem is not Team team)
            {
                MessageBox.Show(this, "Select a team first.", "Info");
                return;
            }

            if (team.IsCommon)
            {
                MessageBox.Show(this,
                    "Shared project teams (common) are read-only here.",
                    "Teams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var vpRepo = _vantagepointRepository;
                var dlg = new ContactPickerWindow(vpRepo) { Owner = this };
                await dlg.LoadAsync();

                if (dlg.ShowDialog() != true) return;

                var pickedAny = false;

                foreach (var email in dlg.SelectedEmails)
                {
                    if (string.IsNullOrWhiteSpace(email)) continue;
                    if (team.Members.Any(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var name = await _peopleLookupService.ResolveDisplayNameForEmailAsync(email);

                    var effectiveName =
                        !string.IsNullOrWhiteSpace(name)
                            ? name
                            : _peopleLookupService.FallbackNameFromEmail(email);

                    team.Members.Add(await _teamsService.AddMemberAsync(team.Id, email,
                        string.IsNullOrWhiteSpace(name) ? null : name));

                    pickedAny = true;
                }

                if (pickedAny)
                    MembersGrid.ItemsSource = team.Members;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not add from picker:\n{ex.Message}",
                    "Contacts", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Visibility filter for shared "common" project teams
        // - must have > 1 member
        // - project number token (first word) must:
        //      * not contain 'x' / 'X'
        //      * be in categories: 00xxx-xx, 3xxxx-xx, 5xxxx-xx, 6xxxx-xx, 7xxxx-xx, 8xxxx-xx, 9xxxx-xx
        private static bool IsCommonProjectTeamVisible(Team t)
        {
            if (t == null) return false;

            // Only show if more than one member
            if (t.Members == null || t.Members.Count <= 1)
                return false;

            var name = t.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Project number is assumed to be the first token before a space
            var firstToken = name
                .Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstToken))
                return false;

            // Exclude anything with 'x' in the project number token
            if (firstToken.IndexOf('x', StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            // Basic sanity: expect something like "30720-01"
            if (firstToken.Length < 8)
                return false;

            // Expect a hyphen in position 3: "NNNNN-NN"
            if (firstToken[5] != '-' && firstToken[2] == '-')
            {
                // handle some weirdness, but don't be too strict; fall through to further checks
            }

            // Validate basic pattern: digits, hyphen, digits where we care
            // We won't over-validate, just enough to avoid trash.
            bool LooksDigit(int idx) =>
                idx >= 0 && idx < firstToken.Length && char.IsDigit(firstToken[idx]);

            // Ensure first two chars are digits (for "00" or "3", "5", etc.)
            if (!LooksDigit(0) || !LooksDigit(1))
                return false;

            // Extract prefix
            var prefix2 = firstToken.Substring(0, 2);
            var prefix1 = firstToken[0];

            // Allowed categories:
            //  - 00xxx-xx   (prefix2 == "00")
            //  - 3xxxx-xx, 5xxxx-xx, 6xxxx-xx, 7xxxx-xx, 8xxxx-xx, 9xxxx-xx (prefix1 in set)
            if (prefix2 == "00")
                return true;

            if ("356789".IndexOf(prefix1) >= 0)
                return true;

            return false;
        }

        // Utility used in other windows; kept for compatibility
        private static void MergeEmailsIntoBox(TextBox box, IEnumerable<string> newEmails)
        {
            var existing = (box.Text ?? "")
                .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            foreach (var e in newEmails ?? Array.Empty<string>())
            {
                if (!existing.Any(x => x.Equals(e, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Add(e);
                }
            }

            box.Text = string.Join("; ", existing);
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            // Save prefs whenever the window is closed (Close button or X),
            // but don't cancel closing if it fails.
            await SaveUserPreferencesAsync();
        }

        private async Task InitializeSignatureEditorAsync()
        {
            try
            {
                await _signatureEditorService.InitializeAsync(
                    SignatureEditor,
                    _userPrefs?.EmailSignatureHtml);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not initialize signature editor:\n{ex.Message}",
                    "Signature Editor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------------
        // Autocomplete keyboard navigation for Favorite Projects
        // -----------------------------------------------------------------------------
        private void HandleAutocompleteKeys(KeyEventArgs e, TextBox box, Popup popup, ListBox list, Action onEnter)
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
                    onEnter();
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

        // -----------------------------------------------------------------------------
        // Window-level key preview for Favorite Projects field
        // -----------------------------------------------------------------------------
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ProjectSearchBox != null &&
                ProjectSuggestionsPopup != null &&
                ProjectSuggestionsList != null &&
                (ReferenceEquals(Keyboard.FocusedElement, ProjectSearchBox) ||
                 ReferenceEquals(Keyboard.FocusedElement, ProjectSuggestionsList)))
            {
                HandleAutocompleteKeys(e, ProjectSearchBox, ProjectSuggestionsPopup, ProjectSuggestionsList, CommitProjectSuggestion);
                if (e.Handled) return;
            }
        }
        // Tracks whether the signature editor is expanded
        private bool _signatureEditorExpanded = false;

        private void SignatureExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _signatureEditorExpanded = !_signatureEditorExpanded;

            // Toggle height between compact and expanded
            if (SignatureEditorBorder != null)
            {
                SignatureEditorBorder.Height = _signatureEditorExpanded ? 450 : 180;
            }

            if (sender is Button btn)
            {
                btn.Content = _signatureEditorExpanded ? "Shrink" : "Expand";
            }
        }

    }
}
#pragma warning restore CA1416
