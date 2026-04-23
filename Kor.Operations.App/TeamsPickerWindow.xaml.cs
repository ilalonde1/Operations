#nullable enable
using Kor.Operations.Services;
using Kor.Operations.Data;
using Kor.Operations.Core;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kor.Operations.App.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations
{
    public partial class TeamsPickerWindow : Window
    {
        // -----------------------------
        // Models
        // -----------------------------
        public sealed class TeamMember
        {
            public string DisplayName { get; set; } = "";
            public string Email { get; set; } = "";

            // Used by the grid
            public string Label =>
                string.IsNullOrWhiteSpace(DisplayName)
                    ? Email
                    : $"{DisplayName} <{Email}>";
        }

        public sealed class TeamView
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsCommon { get; set; }
            public List<TeamMember> Members { get; set; } = new();

            public string Type => IsCommon ? "Common project team" : "My team";
            public int MemberCount => Members?.Count ?? 0;

            // What we actually want to show in the grid.
            // - For normal teams: just the Name from dbo.UserTeams
            // - For imported distro groups where Name looks like
            //     "{guid} - Field Reviews"
            //   we strip the GUID and show only "Field Reviews".
            public string TeamLabel
            {
                get
                {
                    var name = Name ?? string.Empty;

                    // GUID-ish prefix followed by " - something"
                    var m = Regex.Match(name, @"^[0-9a-fA-F\-]{36}\s*-\s*(.+)$");
                    if (m.Success)
                    {
                        var suffix = m.Groups[1].Value?.Trim();
                        if (!string.IsNullOrEmpty(suffix))
                            return suffix;
                    }

                    // Fallback: use raw name as-is
                    return name;
                }
            }

            // Human-readable list of members for the tooltip
            public string MembersTooltip
                => (Members == null || Members.Count == 0)
                    ? "(no members)"
                    : string.Join(Environment.NewLine,
                                  Members.Select(m =>
                                      string.IsNullOrWhiteSpace(m.DisplayName)
                                          ? m.Email
                                          : $"{m.DisplayName} <{m.Email}>"));
        }


        // -----------------------------
        // Fields
        // -----------------------------
        private readonly string _userUpn = string.Empty;
        private readonly PreferencesRepository _repo;
        private readonly IAuthorizationService _authorizationService;
        private readonly PeopleLookupService _peopleLookupService;

        private const string CommonTeamsUpn = "common";

        private readonly List<TeamView> _allTeams = new();

        public ObservableCollection<TeamView> Items { get; } = new();
        public List<string> SelectedEmails { get; } = new();

        // cache so we don’t re-fetch headshots/display name while app is open
        private static BitmapImage? _cachedAvatar;
        private static string? _cachedDisplayName;

        public TeamsPickerWindow(PreferencesRepository repo, IAuthorizationService authorizationService, PeopleLookupService peopleLookupService)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _peopleLookupService = peopleLookupService ?? throw new ArgumentNullException(nameof(peopleLookupService));
            InitializeComponent();

            if (!_authorizationService.IsAuthorized("TeamsPicker"))
                return;

            // UPN logic copied from PreferencesWindow
            var overrideUpn = Kor.Operations.Services.AppServices.Get<UserOptions>().UserUpnOverride;
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

            // Pre-fill header; HeaderLoader will refine (and add photo)
            HeaderBar.GetType().GetProperty("UserDisplayName")?.SetValue(
                HeaderBar,
                Environment.UserName.Replace('.', ' ').Replace('_', ' '));
            HeaderBar.GetType().GetProperty("UserEmail")?.SetValue(HeaderBar, _userUpn);

            var cs = Kor.Operations.Services.AppServices.Get<DatabaseOptions>().KorTransmittalsDb;
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("KorTransmittalsDb connection string is missing.");
            TryOpenOnceOrThrow(cs, TimeSpan.FromSeconds(3));

            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_authorizationService.IsAuthorized("TeamsPicker"))
            {
                MessageBox.Show("You are not authorized to access the Teams Picker.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            CountTextBlock.Text = "Enter 2+ characters and press Search.";

            // Header enrichment (same pattern as ContactPickerWindow / PreferencesWindow)
            try
            {
                await ApplyHeaderAsync();
            }
            catch
            {
                // non-fatal
            }

            try
            {
                await LoadTeamsAsync();
                ApplyFilter();
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

        // -----------------------------
        // Header helpers
        // -----------------------------
        private async Task ApplyHeaderAsync()
        {
            try
            {
                string display = _cachedDisplayName ?? await GetDisplayNameFromDeltekOrFallbackAsync(_userUpn);
                _cachedDisplayName = display;

                HeaderBar.GetType().GetProperty("UserDisplayName")?.SetValue(HeaderBar, display);
                HeaderBar.GetType().GetProperty("UserEmail")?.SetValue(HeaderBar, _userUpn);

                await EnsureAvatarFromDeltekAsync(_userUpn);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TeamsPicker header init failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task<string> GetDisplayNameFromDeltekOrFallbackAsync(string email)
        {
            try
            {
                var provider = new DeltekHeadshotProvider(Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>());
                var full = await provider.TryGetEmployeeDisplayNameAsync(email);
                if (!string.IsNullOrWhiteSpace(full)) return full;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Deltek name lookup failed: {ex.Message}");
            }

            // fallback from UPN
            var sam = email.Split('@').FirstOrDefault() ?? Environment.UserName;
            return sam.Replace('.', ' ').Replace('_', ' ');
        }

        private async Task EnsureAvatarFromDeltekAsync(string email)
        {
            try
            {
                if (_cachedAvatar != null)
                {
                    SetHeaderAvatar(_cachedAvatar);
                    return;
                }

                var provider = new DeltekHeadshotProvider(Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>());
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

        // -----------------------------
        // Load teams (My + Common)
        // -----------------------------
        private async Task LoadTeamsAsync()
        {
            _allTeams.Clear();

            // Load all teams + members for a given UPN in bulk
            async Task LoadForUpnAsync(string upn, bool isCommon)
            {
                // 1) Get team rows (names)
                var teamRows = await _repo.GetTeamsAsync(upn);
                if (teamRows.Count == 0)
                    return;

                // 2) Get ALL members for ALL of those teams in one query
                var memberRows = await _repo.GetMembersForUserAsync(upn);
                var membersByTeam = memberRows
                    .GroupBy(m => m.TeamId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 3) Build TeamView objects
                foreach (var (teamId, name) in teamRows)
                {
                    // Avoid duplicates if same TeamId is seen twice
                    if (_allTeams.Any(t => t.Id == teamId))
                        continue;

                    var tv = new TeamView
                    {
                        Id = teamId,
                        Name = name ?? string.Empty,
                        IsCommon = isCommon,
                        Members = new List<TeamMember>()
                    };

                    if (membersByTeam.TryGetValue(teamId, out var memberList))
                    {
                        foreach (var m in memberList)
                        {
                            var email = m.Email;
                            if (string.IsNullOrWhiteSpace(email))
                                continue;

                            var displayName = m.DisplayName;
                            var resolved =
                                !string.IsNullOrWhiteSpace(displayName)
                                    ? displayName!
                                    : _peopleLookupService.FallbackNameFromEmail(email);

                            tv.Members.Add(new TeamMember
                            {
                                Email = email,
                                DisplayName = resolved
                            });
                        }
                    }

                    // For common teams, respect the same visibility filter as PreferencesWindow
                    if (isCommon && !IsCommonProjectTeamVisible(tv))
                        continue;

                    _allTeams.Add(tv);
                }
            }

            // My teams
            await LoadForUpnAsync(_userUpn, isCommon: false);

            // Common/project teams from Newforma
            await LoadForUpnAsync(CommonTeamsUpn, isCommon: true);
        }


        // -----------------------------
        // Search / filter
        // -----------------------------
        private void ApplyFilter()
        {
            var search = (SearchBox.Text ?? string.Empty).Trim();
            var myOnly = MyTeamsOnlyCheckBox.IsChecked == true;

            // If not "My teams only" and search is too short, show nothing.
            // This avoids dumping all 1200+ common teams on screen by default.
            if (!myOnly && search.Length < 2)
            {
                Items.Clear();
                CountTextBlock.Text = "Enter 2+ characters or check 'My teams only'.";
                TotalText.Text = "Type 2+ characters to search common teams.";
                return;
            }

            var query = _allTeams.AsEnumerable();

            // My teams only → hide common teams
            if (myOnly)
                query = query.Where(t => !t.IsCommon);

            // If user typed 2+ characters, filter by team name
            if (search.Length >= 2)
            {
                query = query.Where(t =>
                    t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var list = query
                .OrderBy(t => t.IsCommon) // My teams first
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Items.Clear();
            foreach (var t in list)
                Items.Add(t);

            CountTextBlock.Text = $"{list.Count:n0} team(s) shown.";
            TotalText.Text = list.Count == 0
                ? "No teams match the current filter."
                : $"{list.Count:n0} team(s) — double-click to select or press OK.";
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            MyTeamsOnlyCheckBox.IsChecked = false;
            ApplyFilter();
            SearchBox.Focus();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ApplyFilter();
            }
        }

        // -----------------------------
        // Selection & closing
        // -----------------------------
        private void TeamsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TeamsGrid.SelectedItem is TeamView)
            {
                CommitSelection();
            }
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            CommitSelection();
        }

        private void CommitSelection()
        {
            var teams = TeamsGrid.SelectedItems
                .OfType<TeamView>()
                .ToList();

            if (teams.Count == 0 && TeamsGrid.SelectedItem is TeamView single)
            {
                teams.Add(single);
            }

            var emails = teams
                .SelectMany(t => t.Members)
                .Where(m => !string.IsNullOrWhiteSpace(m.Email))
                .Select(m => m.Email.Trim())
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

        // -----------------------------
        // Shared helpers (copied from PreferencesWindow)
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

        // Visibility filter for shared "common" project teams
        // copied from PreferencesWindow.IsCommonProjectTeamVisible
        private static bool IsCommonProjectTeamVisible(TeamView t)
        {
            if (t == null) return false;

            if (t.Members == null || t.Members.Count <= 1)
                return false;

            var name = t.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var firstToken = name
                .Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstToken))
                return false;

            if (firstToken.IndexOf('x', StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (firstToken.Length < 8)
                return false;

            if (firstToken[5] != '-' && firstToken[2] == '-')
            {
                // same “weirdness” tolerance as your original
            }

            bool LooksDigit(int idx) =>
                idx >= 0 && idx < firstToken.Length && char.IsDigit(firstToken[idx]);

            if (!LooksDigit(0) || !LooksDigit(1))
                return false;

            var prefix2 = firstToken.Substring(0, 2);
            var prefix1 = firstToken[0];

            if (prefix2 == "00")
                return true;

            if ("356789".IndexOf(prefix1) >= 0)
                return true;

            return false;
        }
    }
}
