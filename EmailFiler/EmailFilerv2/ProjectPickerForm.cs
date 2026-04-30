using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    public partial class ProjectPickerForm : Form
    {
        private readonly Dictionary<string, string> projectMap;
        private readonly List<string> allDisplayNames;
        private readonly string favoritesFile;
        private readonly string recentsFile;
        private readonly SqlFavoritesRepository favoritesRepo;
        private readonly string userUpn;

        private bool isTypingInSearchBox = false;

        public string SelectedProject { get; private set; }

        public ProjectPickerForm(Dictionary<string, string> projectMap, string favoritesFile, string recentsFile)
        {
            InitializeComponent();

            EnableCandystriping(listBoxAll);
            EnableCandystriping(listBoxFavorites);
            EnableCandystriping(listBoxRecents);

            listBoxFavorites.SelectedIndexChanged += PopulateSearchFromListBox;
            listBoxRecents.SelectedIndexChanged += PopulateSearchFromListBox;
            listBoxAll.SelectedIndexChanged += PopulateSearchFromListBox;

            btnAddToFavorites.Image = Image.FromFile(@"\\kor-fs01\Projects\Reporting\Scripts\Assets\star.png");
            btnAddToFavorites.ImageAlign = ContentAlignment.MiddleCenter;

            btnRemoveFromFavorites.Image = Image.FromFile(@"\\kor-fs01\Projects\Reporting\Scripts\Assets\x.png");
            btnRemoveFromFavorites.ImageAlign = ContentAlignment.MiddleCenter;

            btnAddToFavorites.Click += btnAddToFavorites_Click;
            btnRemoveFromFavorites.Click += btnRemoveFromFavorites_Click;

            this.projectMap = projectMap ?? throw new ArgumentNullException(nameof(projectMap));
            this.favoritesFile = favoritesFile ?? string.Empty;
            this.recentsFile = recentsFile ?? string.Empty;

            // Ensure %AppData%\KOR exists so recents file still works
            var korDir = Path.GetDirectoryName(this.favoritesFile);
            if (!string.IsNullOrEmpty(korDir) && !Directory.Exists(korDir))
                Directory.CreateDirectory(korDir);

            // Recents file is still used, so ensure it exists
            if (!string.IsNullOrEmpty(this.recentsFile) && !File.Exists(this.recentsFile))
                File.WriteAllText(this.recentsFile, string.Empty);

            allDisplayNames = projectMap.Values.ToList();

            // NEW: init repository + current user UPN and load favorites FROM SQL
            favoritesRepo = new SqlFavoritesRepository();
            userUpn = GetCurrentUserUpn();
            LoadFavoritesFromDatabase();

            // Load Recents (unchanged – still file-based)
            if (!string.IsNullOrEmpty(this.recentsFile) && File.Exists(this.recentsFile))
            {
                var recentKeys = File.ReadAllLines(this.recentsFile).Distinct();
                var recentDisplay = recentKeys
                    .Where(k => projectMap.ContainsKey(k))
                    .Select(k => projectMap[k])
                    .ToArray();
                listBoxRecents.Items.Clear();
                listBoxRecents.Items.AddRange(recentDisplay);
            }

            // Search behaviour (unchanged)
            searchBox.TextChanged += (s, e) =>
            {
                isTypingInSearchBox = true;

                string filter = searchBox.Text.Trim().ToLowerInvariant();
                listBoxAll.Items.Clear();
                var matches = allDisplayNames
                    .Where(name => name.ToLowerInvariant().Contains(filter))
                    .ToArray();

                listBoxAll.Items.AddRange(matches);
                if (matches.Length > 0 && !string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    listBoxAll.SelectedIndex = 0;
                }

                isTypingInSearchBox = false;
            };

            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && listBoxAll.Items.Count > 0)
                {
                    listBoxAll.SelectedIndex = 0;
                    btnSelect.PerformClick();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        // ----------------- Favorites from SQL -----------------

        private void LoadFavoritesFromDatabase()
        {
            listBoxFavorites.Items.Clear();

            try
            {
                var dbFavs = favoritesRepo.GetFavorites(userUpn);
                if (dbFavs == null || dbFavs.Count == 0)
                    return;

                var display = new List<string>();

                foreach (var fav in dbFavs)
                {
                    if (string.IsNullOrWhiteSpace(fav.ProjectNo))
                        continue;

                    // First try direct key match (ProjectNo == dictionary key)
                    string key;
                    if (projectMap.ContainsKey(fav.ProjectNo))
                    {
                        key = fav.ProjectNo;
                    }
                    else
                    {
                        // Fallback: find a key whose display name starts with the project number
                        key = projectMap
                            .FirstOrDefault(kvp =>
                                kvp.Value.StartsWith(fav.ProjectNo, StringComparison.OrdinalIgnoreCase))
                            .Key;
                    }

                    if (!string.IsNullOrEmpty(key) && projectMap.ContainsKey(key))
                    {
                        string folderName = projectMap[key];
                        if (!string.IsNullOrEmpty(folderName) && !display.Contains(folderName))
                            display.Add(folderName);
                    }
                }

                if (display.Count > 0)
                    listBoxFavorites.Items.AddRange(display.ToArray());
            }
            catch (Exception ex)
            {
                // If SQL fails for any reason, just leave My Projects empty (do not break filing)
                System.Diagnostics.Debug.WriteLine("LoadFavoritesFromDatabase failed: " + ex);
            }
        }

        // Best-effort way to get the same UPN the WPF app uses
        private static string GetCurrentUserUpn()
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app != null)
                {
                    Outlook.AddressEntry addr = app.Session?.CurrentUser?.AddressEntry;
                    if (addr != null)
                    {
                        if (addr.Type == "EX")
                        {
                            Outlook.ExchangeUser exUser = addr.GetExchangeUser();
                            if (exUser != null && !string.IsNullOrEmpty(exUser.PrimarySmtpAddress))
                                return exUser.PrimarySmtpAddress.ToLowerInvariant();
                        }
                        else if (!string.IsNullOrEmpty(addr.Address))
                        {
                            return addr.Address.ToLowerInvariant();
                        }
                    }
                }
            }
            catch
            {
                // fall through to environment-based default
            }

            // Fallback: environment user + korstructural.com
            string env = Environment.UserName ?? string.Empty;
            if (!env.Contains("@"))
                env += "@korstructural.com";
            return env.ToLowerInvariant();
        }

        // ----------------- Existing behaviour -----------------

        private void btnSelect_Click(object sender, EventArgs e)
        {
            string selection = listBoxAll.SelectedItem?.ToString() ?? searchBox.Text.Trim();
            if (!string.IsNullOrEmpty(selection))
            {
                var match = projectMap.FirstOrDefault(x => x.Value == selection);
                if (!string.IsNullOrEmpty(match.Key))
                {
                    SelectedProject = match.Key;
                    this.DialogResult = DialogResult.OK;

                    // Recents still tracked via text file
                    var lines = !string.IsNullOrEmpty(recentsFile) && File.Exists(recentsFile)
                        ? File.ReadAllLines(recentsFile).ToList()
                        : new List<string>();

                    lines.RemoveAll(l => l == match.Key);
                    lines.Insert(0, match.Key);
                    if (!string.IsNullOrEmpty(recentsFile))
                        File.WriteAllLines(recentsFile, lines.Take(20));

                    this.Close();
                }
                else
                {
                    MessageBox.Show("No matching project found.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void btnAddToFavorites_Click(object sender, EventArgs e)
        {
            string selection = listBoxAll.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selection))
                return;

            var match = projectMap.FirstOrDefault(x => x.Value == selection);
            if (string.IsNullOrEmpty(match.Key))
                return;

            try
            {
                // Persist to SQL
                favoritesRepo.AddFavorite(userUpn, match.Key, selection);

                // Update UI if not already present
                if (!listBoxFavorites.Items.Contains(selection))
                    listBoxFavorites.Items.Add(selection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AddFavorite failed: " + ex);
                MessageBox.Show("Could not save favourite project.",
                    "Favourites",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void btnRemoveFromFavorites_Click(object sender, EventArgs e)
        {
            string selection = listBoxFavorites.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selection))
                return;

            var match = projectMap.FirstOrDefault(x => x.Value == selection);
            if (string.IsNullOrEmpty(match.Key))
                return;

            try
            {
                favoritesRepo.RemoveFavorite(userUpn, match.Key);
                listBoxFavorites.Items.Remove(selection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RemoveFavorite failed: " + ex);
                MessageBox.Show("Could not remove favourite project.",
                    "Favourites",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            searchBox.Text = "";
        }

        private void PopulateSearchFromListBox(object sender, EventArgs e)
        {
            if (isTypingInSearchBox) return;

            if (sender is ListBox listBox && listBox.SelectedItem != null)
            {
                searchBox.Text = listBox.SelectedItem.ToString();
            }
        }

        private void EnableCandystriping(ListBox listBox)
        {
            listBox.DrawMode = DrawMode.OwnerDrawFixed;

            // Set item height based on font size
            listBox.ItemHeight = TextRenderer.MeasureText("Sample", listBox.Font).Height + 4;

            listBox.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;

                // Determine alternating background color
                Color bgColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                    ? SystemColors.Highlight
                    : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(240, 240, 240));

                using (SolidBrush backgroundBrush = new SolidBrush(bgColor))
                {
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                }

                // Draw the item text
                string text = listBox.Items[e.Index].ToString().Replace("&", "&&");
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    listBox.Font,
                    e.Bounds,
                    (e.State & DrawItemState.Selected) == DrawItemState.Selected
                        ? SystemColors.HighlightText
                        : listBox.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                );

                // Draw focus rectangle if selected
                if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                {
                    e.DrawFocusRectangle();
                }
            };
        }
    }
}
