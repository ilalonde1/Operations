using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using MsgReaderMessage = MsgReader.Outlook.Storage.Message;
using MsgReaderMimeMessage = MsgReader.Mime.Message;

namespace EmailFilerv2
{
    public partial class SearchFiledEmailsForm : Form
    {
        // ====== Core UI ======
        private DataGridView emailGrid;
        private ListBox lstMonthFolders;
        private WebView2 previewWeb;
        private PictureBox spinner;
        private Panel loadingOverlay;
        private Label spinnerStatusLabel;
        private ProgressBar indexingProgress;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel mainStatusLabel;
        private Label statusLabel; // footer left

        // ====== Modern search header UI ======
        private Panel searchHeader;
        private TextBox txtQuery;
        private CheckBox chipBody;
        private Button btnGo, btnClear, btnFilters;
        private Panel filterDrawer;
        private DateTimePicker dtFrom, dtTo;
        private TextBox txtSender;
        private Label lblResults;
        private System.Windows.Forms.Timer debounceTimer;

        // ====== Project switcher ======
        private TextBox projectSearchTextBox;
        private ListBox projectListBox;
        private Button btnSwitch, btnClearProject;

        // ====== Data / state ======
        private List<string> _allProjectNamesList = new List<string>();
        private Dictionary<string, string> _allProjectsDict = new Dictionary<string, string>();
        private const string ProjectsRoot = @"\\kor-fs01\Projects\Projects";

        private string projectPath;
        private bool isIndexing = false;
        private bool bodyIndexBuiltOnce = false;         // build body FTS only when user asks
        private List<EmailMetadata> cachedEmails = new List<EmailMetadata>();
        private EmailIndexDatabase db;

        public SearchFiledEmailsForm(string selectedProjectPath)
        {
            projectPath = selectedProjectPath;
            db = new EmailIndexDatabase(Path.Combine(projectPath, "Emails", ".email_index.db"));

            BuildUI();

            // Lazy body index creation the first time the user checks "Search body"
            chipBody.CheckedChanged += async (s, e) =>
            {
                if (chipBody.Checked && !bodyIndexBuiltOnce)
                {
                    await ShowOverlayAsync("Building body index...");
                    try
                    {
                        await IndexDeltaAsync(true); // include bodies for new/changed
                        bodyIndexBuiltOnce = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Body indexing failed: " + ex.Message, "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        chipBody.Checked = false;
                    }
                    finally
                    {
                        HideOverlay();
                        RefreshFooterStats();
                    }
                }
            };

            // First index + hardened WebView2 init
            this.Shown += async (s, e) =>
            {
                CenterOverlay();

                // --- WebView2 initialization with guaranteed-writable user-data folder ---
                try
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KOR", "EmailFiler", "WebView2");

                    Directory.CreateDirectory(userDataFolder);

                    previewWeb.CreationProperties = new CoreWebView2CreationProperties
                    {
                        UserDataFolder = userDataFolder
                    };

                    _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); // throws if runtime missing

                    await previewWeb.EnsureCoreWebView2Async(null);

                    var settings = previewWeb.CoreWebView2.Settings;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;

                    previewWeb.CoreWebView2.NewWindowRequested += (sender, args) =>
                    {
                        args.Handled = true;
                        try { Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true }); } catch { }
                    };
                }
                catch (WebView2RuntimeNotFoundException)
                {
                    MessageBox.Show(
                        "WebView2 Runtime is not installed. Please ask IT to install the Microsoft Edge WebView2 Runtime (Evergreen).",
                        "WebView2 Runtime missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(
                        "WebView2 could not access its data folder. If Defender Controlled Folder Access is enabled, " +
                        "ask IT to allow this app or choose a different user data path.\n\n" + ex.Message,
                        "WebView2 access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("WebView2 initialization failed:\n" + ex.Message, "WebView2",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // --- initial (fast) delta index ---
                await ShowOverlayAsync("Indexing...");
                try
                {
                    await IndexDeltaAsync(false); // headers only
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Indexing failed: " + ex.Message, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    HideOverlay();
                    RefreshFooterStats();
                }
            };
        }

        // ============================================================
        // UI
        // ============================================================
        private void BuildUI()
        {
            // Form
            this.Text = "KOR Structural - Search Filed Emails";
            this.Width = 1100;
            this.Height = 850;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 8.25f);

            this.Controls.Clear();

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.White,
                Padding = new Padding(5)
            };
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));      // project label
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));      // modern search header
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // main split
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));      // footer

            // Project label
            var lblProject = new Label
            {
                Text = "PROJECT: " + Path.GetFileName(projectPath).Replace("&", "&&"),
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#435363"),
                TextAlign = ContentAlignment.MiddleLeft
            };
            outerLayout.Controls.Add(lblProject, 0, 0);

            // ===== Modern unified search header =====
            searchHeader = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 2,
                BackColor = Color.White,
                Padding = new Padding(0, 10, 0, 6)
            };

            // Columns (project switcher visible on top row)
            headerLayout.ColumnStyles.Clear();
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));   // 0: left pad
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // 1: query box
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));  // 2: Search
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // 3: Search body chip
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // 4: Filters
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380)); // 5: RIGHT SIDE (project switcher + results)
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));   // 6: right pad

            headerLayout.RowStyles.Clear();
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); // row 0 = visible top bar
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));  // row 1 = hidden drawer (filters)

            // Query
            txtQuery = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.Gray,
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "Search subject, sender…"
            };
            txtQuery.GotFocus += (s, e) => { if (txtQuery.Text == "Search subject, sender…") { txtQuery.Text = ""; txtQuery.ForeColor = Color.Black; } };
            txtQuery.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(txtQuery.Text)) { txtQuery.Text = "Search subject, sender…"; txtQuery.ForeColor = Color.Gray; } };
            txtQuery.TextChanged += (s, e) => DebounceSearch();
            txtQuery.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await PerformSearch(); } };

            // Search button
            btnGo = MakeFlatButton("Search");
            btnGo.Click += async (s, e) => await PerformSearch();

            // Chip
            chipBody = MakeChip("Search body");
            chipBody.Checked = false;

            // Filters button
            btnFilters = MakeFlatButton("Filters", 80);
            btnFilters.BackColor = Color.DimGray;
            btnFilters.Click += (s, e) =>
            {
                bool show = headerLayout.RowStyles[1].Height == 0;
                headerLayout.RowStyles[1].Height = show ? 36 : 0;
                filterDrawer.Visible = show;
            };

            // Clear results button (clears search/filter fields)
            btnClear = MakeFlatButton("Clear", 70);
            btnClear.BackColor = Color.Gray;
            btnClear.Click += (s, e) =>
            {
                txtQuery.Text = "";
                chipBody.Checked = false;
                if (txtSender.ForeColor == Color.Black) txtSender.Text = "";
                dtFrom.Value = DateTime.Today.AddYears(-10);
                dtTo.Value = DateTime.Today.AddDays(1);
                _ = PerformSearch();
            };

            // Results label
            lblResults = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8.25f),
                Margin = new Padding(12, 8, 0, 0)
            };

            // Filter drawer
            filterDrawer = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.White };
            var drawer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

            dtFrom = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddYears(-10) };
            dtTo = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1) };
            txtSender = new TextBox { Width = 180 };
            // Watermark behavior for Sender (for .NET Framework)
            txtSender.Tag = "Sender contains…";
            txtSender.Text = (string)txtSender.Tag;
            txtSender.ForeColor = Color.Gray;
            txtSender.GotFocus += (s, e) =>
            {
                if (txtSender.Text == (string)txtSender.Tag) { txtSender.Text = ""; txtSender.ForeColor = Color.Black; }
            };
            txtSender.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSender.Text)) { txtSender.Text = (string)txtSender.Tag; txtSender.ForeColor = Color.Gray; }
            };
            txtSender.TextChanged += (s, e) => { if (txtSender.ForeColor == Color.Black) DebounceSearch(); };
            dtFrom.ValueChanged += (s, e) => DebounceSearch();
            dtTo.ValueChanged += (s, e) => DebounceSearch();

            drawer.Controls.Add(new Label { Text = "From:", AutoSize = true, Padding = new Padding(6, 8, 6, 0) });
            drawer.Controls.Add(dtFrom);
            drawer.Controls.Add(new Label { Text = "To:", AutoSize = true, Padding = new Padding(12, 8, 6, 0) });
            drawer.Controls.Add(dtTo);
            drawer.Controls.Add(new Label { Text = "Sender:", AutoSize = true, Padding = new Padding(12, 8, 6, 0) });
            drawer.Controls.Add(txtSender);
            drawer.Controls.Add(btnClear);
            filterDrawer.Controls.Add(drawer);

            // ===== Project switcher (visible on the top row) =====
            projectSearchTextBox = new TextBox
            {
                Width = 210,
                Font = new Font("Segoe UI", 8.25f),
                BackColor = Color.WhiteSmoke,
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "Search for another project..."
            };
            projectSearchTextBox.GotFocus += (s, e) =>
            {
                if (projectSearchTextBox.Text == "Search for another project...")
                {
                    projectSearchTextBox.Text = "";
                    projectSearchTextBox.ForeColor = Color.Black;
                }
            };
            projectSearchTextBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(projectSearchTextBox.Text))
                {
                    projectSearchTextBox.Text = "Search for another project...";
                    projectSearchTextBox.ForeColor = Color.Gray;
                }
            };

            projectListBox = new ListBox { Font = new Font("Segoe UI", 8.25f), Height = 120, Visible = false };
            EnableCandystriping(projectListBox);
            this.Controls.Add(projectListBox);
            projectListBox.BringToFront();

            projectSearchTextBox.TextChanged += (s, e) =>
            {
                string typed = projectSearchTextBox.Text.Trim().ToLower();
                var matches = _allProjectsDict.Keys.Where(p => p.ToLower().Contains(typed)).OrderBy(p => p).ToArray();

                projectListBox.Items.Clear();
                projectListBox.Items.AddRange(matches);
                projectListBox.Visible = matches.Length > 0;

                if (matches.Length > 0)
                {
                    var textboxLocation = projectSearchTextBox.PointToScreen(Point.Empty);
                    var relativeLocation = this.PointToClient(textboxLocation);
                    projectListBox.Location = new Point(relativeLocation.X, relativeLocation.Y + projectSearchTextBox.Height);
                    projectListBox.Width = projectSearchTextBox.Width;
                }
            };
            projectListBox.SelectedIndexChanged += (s, e) =>
            {
                if (projectListBox.SelectedItem != null)
                {
                    projectSearchTextBox.Text = projectListBox.SelectedItem.ToString();
                    projectListBox.Visible = false;
                }
            };
            projectListBox.DoubleClick += (s, e) =>
            {
                if (projectListBox.SelectedItem != null)
                {
                    projectSearchTextBox.Text = projectListBox.SelectedItem.ToString();
                    projectListBox.Visible = false;
                    btnSwitch.PerformClick();
                }
            };

            projectSearchTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (projectListBox.Visible && projectListBox.Items.Count > 0)
                    {
                        projectSearchTextBox.Text = projectListBox.Items[0].ToString();
                        projectListBox.Visible = false;
                    }
                    btnSwitch.PerformClick();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            btnSwitch = MakeFlatButton("Switch", 80);
            btnSwitch.Click += (s, e) =>
            {
                string typed = projectSearchTextBox.Text?.Trim() ?? "";
                if (!TryGetProjectPath(typed, out var targetPath))
                {
                    MessageBox.Show($"Project '{typed}' not found in the directory index.",
                        "Project Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var emailsPath = Path.Combine(targetPath, "Emails");
                if (!Directory.Exists(emailsPath))
                {
                    MessageBox.Show($"Project found, but Emails folder is missing:\n{emailsPath}",
                        "Emails Folder Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    var newForm = new SearchFiledEmailsForm(targetPath);
                    newForm.Show();
                    newForm.TopMost = true; newForm.Focus(); newForm.BringToFront(); newForm.TopMost = false;
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open project:\n{targetPath}\n\n{ex.Message}",
                        "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnClearProject = MakeFlatButton("X", 40);
            btnClearProject.BackColor = Color.Gray;
            btnClearProject.Click += (s, e) => projectSearchTextBox.Text = "";

            // Right side (project switcher + results)
            var rightPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 3, 0, 0)
            };
            rightPanel.Controls.Add(new Label { Text = "Project:", AutoSize = true, Padding = new Padding(0, 7, 6, 0) });
            rightPanel.Controls.Add(projectSearchTextBox);
            rightPanel.Controls.Add(btnSwitch);
            rightPanel.Controls.Add(btnClearProject);
            rightPanel.Controls.Add(new Label { Width = 10 });
            rightPanel.Controls.Add(lblResults);

            // Place top row
            headerLayout.Controls.Add(new Panel(), 0, 0);  // left pad
            headerLayout.Controls.Add(txtQuery, 1, 0);
            headerLayout.Controls.Add(btnGo, 2, 0);
            headerLayout.Controls.Add(chipBody, 3, 0);
            headerLayout.Controls.Add(btnFilters, 4, 0);
            headerLayout.Controls.Add(rightPanel, 5, 0);
            headerLayout.Controls.Add(new Panel(), 6, 0);  // right pad

            // Drawer row
            filterDrawer.Visible = false;
            headerLayout.Controls.Add(new Panel(), 0, 1);
            headerLayout.Controls.Add(filterDrawer, 1, 1);
            headerLayout.SetColumnSpan(filterDrawer, 5);
            headerLayout.Controls.Add(new Panel(), 6, 1);

            searchHeader.Controls.Add(headerLayout);
            outerLayout.Controls.Add(searchHeader, 0, 1);

            // ===== Main split (months | emails/preview) =====
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 120,
                IsSplitterFixed = true,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.White
            };

            var lblFolders = new Label
            {
                Text = "BY MONTH",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                BackColor = ColorTranslator.FromHtml("#f2f1f1"),
                Padding = new Padding(5, 0, 0, 0)
            };

            lstMonthFolders = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.25f), ItemHeight = 18, BorderStyle = BorderStyle.None };
            lstMonthFolders.SelectedIndexChanged += (s, e) =>
            {
                if (lstMonthFolders.SelectedItem != null)
                    LoadFiledEmails(Path.Combine(projectPath, "Emails", lstMonthFolders.SelectedItem.ToString()));
            };
            var monthPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            monthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            monthPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            monthPanel.Controls.Add(lblFolders, 0, 0);
            monthPanel.Controls.Add(lstMonthFolders, 0, 1);
            splitMain.Panel1.Controls.Add(monthPanel);

            var splitContent = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Color.White };

            // Emails grid
            var lblEmails = new Label
            {
                Text = "EMAILS",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                BackColor = ColorTranslator.FromHtml("#f2f1f1"),
                Padding = new Padding(5, 0, 0, 0)
            };

            emailGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8f),
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                ColumnHeadersHeight = 24,
                RowTemplate = { Height = 20 },
                ReadOnly = true,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false
            };
            emailGrid.SelectionChanged += EmailGrid_SelectionChanged;
            emailGrid.CellDoubleClick += EmailGrid_CellDoubleClick;
            emailGrid.Columns.Add("FileName", "SUBJECT");
            emailGrid.Columns.Add("SendDate", "SEND DATE");
            emailGrid.Columns.Add("Sender", "SENDER");
            emailGrid.Columns[0].FillWeight = 60;
            emailGrid.Columns[1].FillWeight = 20;
            emailGrid.Columns[2].FillWeight = 20;
            emailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            emailGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

            // highlight matches in grid cells
            emailGrid.CellPainting += EmailGrid_CellPainting_Highlight;

            var emailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            emailPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            emailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            emailPanel.Controls.Add(lblEmails, 0, 0);
            emailPanel.Controls.Add(emailGrid, 0, 1);
            splitContent.Panel1.Controls.Add(emailPanel);

            // Preview (WebView2)
            var lblPreview = new Label
            {
                Text = "PREVIEW",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                BackColor = ColorTranslator.FromHtml("#f2f1f1"),
                Padding = new Padding(5, 0, 0, 0)
            };

            previewWeb = new WebView2
            {
                Dock = DockStyle.Fill,
                AllowExternalDrop = false,
                DefaultBackgroundColor = Color.White
            };

            var previewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewPanel.Controls.Add(lblPreview, 0, 0);
            previewPanel.Controls.Add(previewWeb, 0, 1);
            splitContent.Panel2.Controls.Add(previewPanel);

            splitMain.Panel2.Controls.Add(splitContent);
            outerLayout.Controls.Add(splitMain, 0, 2);

            // ===== Footer =====
            indexingProgress = new ProgressBar { Dock = DockStyle.Bottom, Height = 12, Visible = false, Margin = new Padding(10) };
            statusStrip = new StatusStrip { ForeColor = Color.White, Font = new Font("Segoe UI", 8.25f), Height = 10 };
            mainStatusLabel = new ToolStripStatusLabel
            {
                Text = "Ready | Project: " + Path.GetFileName(projectPath).Replace("&", "&&"),
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusStrip.Items.Add(mainStatusLabel);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 15,
                ForeColor = Color.White,
                BackColor = ColorTranslator.FromHtml("#435363")
            };

            var footerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = ColorTranslator.FromHtml("#f2f1f1") };
            footerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
            footerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            footerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
            footerPanel.Controls.Add(indexingProgress, 0, 0);
            footerPanel.Controls.Add(statusLabel, 0, 1);
            footerPanel.Controls.Add(statusStrip, 0, 2);

            outerLayout.Controls.Add(footerPanel, 0, 3);

            // ===== Overlay =====
            loadingOverlay = new Panel { BackColor = Color.FromArgb(180, Color.White), Dock = DockStyle.Fill, Visible = false };
            spinner = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(64, 64),
                Image = Image.FromFile(@"\\kor-fs01\Projects\Reporting\Scripts\Assets\loading.gif"),
                Anchor = AnchorStyles.None
            };
            loadingOverlay.Controls.Add(spinner);

            spinnerStatusLabel = new Label
            {
                AutoSize = true,
                Text = "Indexing...",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.DimGray,
                BackColor = Color.Transparent
            };
            loadingOverlay.Controls.Add(spinnerStatusLabel);

            this.Controls.Add(loadingOverlay);
            this.Controls.Add(outerLayout);

            loadingOverlay.Resize += (s, e) => CenterOverlay();
            this.Resize += (s, e) => CenterOverlay();

            LoadMonthFolders();
            LoadProjectAutocompleteData();
        }

        // Chip & flat button makers
        private CheckBox MakeChip(string text)
        {
            var c = new CheckBox
            {
                Text = text,
                AutoSize = true,
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(6, 0, 0, 0)
            };
            c.FlatAppearance.BorderSize = 1;
            c.FlatAppearance.BorderColor = Color.Silver;
            c.BackColor = Color.White;
            c.CheckedChanged += (s, e) =>
            {
                c.BackColor = c.Checked ? Color.FromArgb(230, 240, 255) : Color.White;
                c.FlatAppearance.BorderColor = c.Checked ? Color.FromArgb(130, 160, 220) : Color.Silver;
            };
            return c;
        }
        private Button MakeFlatButton(string text, int w = 80)
        {
            var b = new Button
            {
                Text = text,
                Width = w,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#435363"),
                ForeColor = Color.White,
                Margin = new Padding(6, 0, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // Debounced search
        private void DebounceSearch()
        {
            if (debounceTimer == null)
            {
                debounceTimer = new System.Windows.Forms.Timer();
                debounceTimer.Interval = 300;
                debounceTimer.Tick += async (s, e) =>
                {
                    debounceTimer.Stop();
                    await PerformSearch();
                };
            }
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        // ============================================================
        // Indexing (delta)
        // ============================================================
        private async Task IndexDeltaAsync(bool includeBodiesOnThisRun)
        {
            string emailsRoot = Path.Combine(projectPath, "Emails");
            if (!Directory.Exists(emailsRoot)) return;

            isIndexing = true;

            var currentFiles = Directory.GetDirectories(emailsRoot)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.msg"))
                .Concat(Directory.GetDirectories(emailsRoot).SelectMany(dir => Directory.EnumerateFiles(dir, "*.eml")))
                .ToList();

            var currentSet = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);
            var known = db.GetKnownFilesMap();
            var toParse = new ConcurrentBag<string>();

            await Task.Run(() =>
            {
                Parallel.ForEach(currentFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                    path =>
                    {
                        try
                        {
                            var fi = new FileInfo(path);
                            long mtime = fi.LastWriteTimeUtc.Ticks;
                            long len = fi.Length;

                            if (!known.TryGetValue(path, out var prior) || prior.length != len || prior.mtimeTicks != mtime)
                            {
                                toParse.Add(path);
                            }
                        }
                        catch
                        {
                            // ignore per-file errors and continue
                        }
                    });
            });

            var parsed = new ConcurrentBag<(EmailMetadata meta, long len, long mt)>();

            await Task.Run(() =>
            {
                Parallel.ForEach(toParse,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                    path =>
                    {
                        try
                        {
                            var fi = new FileInfo(path);
                            var meta = FastHeaderOnly(path);
                            parsed.Add((meta, fi.Length, fi.LastWriteTimeUtc.Ticks));
                        }
                        catch
                        {
                            // ignore per-file errors
                        }
                    });
            });

            db.BeginTransaction();
            try
            {
                using var up = db.CreateCommand();
                foreach (var p in parsed)
                    db.UpsertHeader(p.meta, p.len, p.mt, up);
                db.CommitTransaction();
            }
            catch
            {
                db.RollbackTransaction();
                throw;
            }

            if (includeBodiesOnThisRun)
            {
                var bodyTargets = parsed.Select(p => p.meta.FilePath).ToList();
                var bodies = new ConcurrentBag<(string path, string body)>();

                await Task.Run(() =>
                {
                    Parallel.ForEach(bodyTargets,
                        new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                        path =>
                        {
                            try { bodies.Add((path, TryExtractBody(path))); }
                            catch { }
                        });
                });

                db.BeginTransaction();
                try
                {
                    using var up = db.CreateCommand();
                    foreach (var b in bodies)
                        db.UpsertBodyFts(b.path, b.body, up);
                    db.CommitTransaction();
                }
                catch
                {
                    db.RollbackTransaction();
                    throw;
                }
            }

            db.DeleteMissingExcept(currentSet);

            cachedEmails = db.LoadAllMetadata();
            PopulateEmailGrid();

            UpdateMainStatus(
                "Delta index: scanned " + currentFiles.Count.ToString("N0") +
                ", changed " + toParse.Count.ToString("N0") +
                ", updated " + parsed.Count.ToString("N0") +
                " | " + Path.GetFileName(projectPath));

            isIndexing = false;
        }

        private EmailMetadata FastHeaderOnly(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string sender = "";
            string dateStr = "";
            string subj = fileName;

            try
            {
                if (filePath.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var msg = new MsgReaderMessage(filePath))
                    {
                        sender = msg.Sender != null ? (msg.Sender.Email ?? msg.Sender.DisplayName ?? "") : "";
                        dateStr = msg.SentOn != null ? msg.SentOn.Value.ToString("yyyy-MM-dd HH:mm") : "";
                        subj = string.IsNullOrWhiteSpace(msg.Subject) ? fileName : msg.Subject;
                    }
                }
                else
                {
                    using (var fs = File.OpenRead(filePath))
                    {
                        var eml = new MsgReaderMimeMessage(fs);
                        sender = eml.Headers != null && eml.Headers.From != null
                            ? (eml.Headers.From.Address ?? eml.Headers.From.DisplayName ?? "")
                            : "";
                        dateStr = eml.Headers != null ? eml.Headers.DateSent.ToString("yyyy-MM-dd HH:mm") : "";
                        subj = eml.Headers != null && !string.IsNullOrWhiteSpace(eml.Headers.Subject) ? eml.Headers.Subject : fileName;
                    }
                }
            }
            catch { }

            return new EmailMetadata
            {
                FilePath = filePath,
                FileName = subj,
                Sender = sender,
                SendDate = dateStr,
                BodyPreview = null
            };
        }

        private string TryExtractBody(string filePath)
        {
            try
            {
                if (filePath.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var msg = new MsgReaderMessage(filePath))
                    {
                        if (!string.IsNullOrEmpty(msg.BodyText)) return msg.BodyText;
                        return msg.BodyHtml ?? "";
                    }
                }
                else
                {
                    using (var fs = File.OpenRead(filePath))
                    {
                        var eml = new MsgReaderMimeMessage(fs);
                        var text = eml.TextBody != null ? eml.TextBody.GetBodyAsText() : "";
                        var html = eml.HtmlBody != null ? eml.HtmlBody.GetBodyAsText() : "";
                        return string.IsNullOrEmpty(text) ? html : text + "\n" + html;
                    }
                }
            }
            catch { return ""; }
        }

        // ============================================================
        // Search (with unified header)
        // ============================================================
        private async Task PerformSearch()
        {
            if (isIndexing)
            {
                UpdateUI(() => statusLabel.Text = "Please wait - indexing in progress");
                return;
            }

            string query = (txtQuery.Text ?? "").Trim();
            if (query == "Search subject, sender…") query = "";
            bool includeContents = chipBody.Checked;

            // Filters
            DateTime from = dtFrom.Value.Date;
            DateTime to = dtTo.Value.Date.AddDays(1);
            string senderFilter = (txtSender.ForeColor == Color.Gray) ? "" : (txtSender.Text ?? "").Trim();

            await ShowOverlayAsync("Searching...");
            try
            {
                List<EmailMetadata> results;

                if (includeContents && query.Length >= 2)
                {
                    results = db.SearchBodyFts(query) ?? new List<EmailMetadata>();

                    // extra filters
                    results = results.Where(e =>
                        (string.IsNullOrEmpty(senderFilter) ||
                            (!string.IsNullOrEmpty(e.Sender) && e.Sender.IndexOf(senderFilter, StringComparison.OrdinalIgnoreCase) >= 0)) &&
                        (DateTime.TryParse(e.SendDate, out var d) ? d >= from && d < to : true)
                    ).ToList();

                    if (results.Count == 0)
                        results = HeaderSearchFallback(query, true);
                }
                else
                {
                    results = HeaderSearchFallback(query, false);

                    results = results.Where(e =>
                        (string.IsNullOrEmpty(senderFilter) ||
                            (!string.IsNullOrEmpty(e.Sender) && e.Sender.IndexOf(senderFilter, StringComparison.OrdinalIgnoreCase) >= 0)) &&
                        (DateTime.TryParse(e.SendDate, out var d) ? d >= from && d < to : true)
                    ).ToList();
                }

                UpdateUI(() =>
                {
                    emailGrid.Rows.Clear();
                    foreach (var meta in results)
                    {
                        var row = new DataGridViewRow();
                        row.CreateCells(emailGrid, meta.FileName, meta.SendDate, meta.Sender);
                        row.Tag = meta.FilePath;
                        emailGrid.Rows.Add(row);
                    }

                    lblResults.Text = $"{results.Count:N0} results";
                    UpdateMainStatus("Found " + results.Count.ToString("N0") +
                        " emails | Project: " + Path.GetFileName(projectPath).Replace("&", "&&"));
                });
            }
            catch (Exception ex)
            {
                UpdateUI(() => statusLabel.Text = "Search error: " + ex.Message);
            }
            finally
            {
                HideOverlay();
            }
        }

        private List<EmailMetadata> HeaderSearchFallback(string query, bool includeBody)
        {
            return cachedEmails.Where(e =>
                    (string.IsNullOrEmpty(query) ||
                        (!string.IsNullOrEmpty(e.FileName) && e.FileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(e.Sender) && e.Sender.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) ||
                    (includeBody && !string.IsNullOrEmpty(e.BodyPreview) && e.BodyPreview.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
        }

        private void EmailGrid_CellPainting_Highlight(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string q = (txtQuery.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q) || q == "Search subject, sender…") return;

            var val = e.FormattedValue?.ToString();
            if (string.IsNullOrEmpty(val)) return;

            int idx = val.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            e.Handled = true;
            e.PaintBackground(e.CellBounds, true);

            using (var highlight = new SolidBrush(Color.DarkOrange))
            {
                var bounds = e.CellBounds;
                bounds.Inflate(-4, 0);

                string before = val.Substring(0, idx);
                string match = val.Substring(idx, q.Length);
                string after = val.Substring(idx + q.Length);

                var pt = new Point(bounds.Left, bounds.Top + (bounds.Height - e.CellStyle.Font.Height) / 2);

                TextRenderer.DrawText(e.Graphics, before, e.CellStyle.Font, pt, e.CellStyle.ForeColor);
                pt.X += Math.Max(0, TextRenderer.MeasureText(before, e.CellStyle.Font).Width - 6);

                TextRenderer.DrawText(e.Graphics, match, e.CellStyle.Font, pt, Color.DarkOrange);
                pt.X += Math.Max(0, TextRenderer.MeasureText(match, e.CellStyle.Font).Width - 6);

                TextRenderer.DrawText(e.Graphics, after, e.CellStyle.Font, pt, e.CellStyle.ForeColor);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
            }
        }

        private void PopulateEmailGrid()
        {
            if (cachedEmails == null || cachedEmails.Count == 0)
            {
                UpdateMainStatus("No emails indexed | Project: " + Path.GetFileName(projectPath).Replace("&", "&&"));
                emailGrid.Rows.Clear();
                lblResults.Text = "0 results";
                return;
            }

            emailGrid.Rows.Clear();
            foreach (var meta in cachedEmails)
            {
                var row = new DataGridViewRow();
                row.CreateCells(emailGrid, meta.FileName, meta.SendDate, meta.Sender);
                row.Tag = meta.FilePath;
                emailGrid.Rows.Add(row);
            }

            lblResults.Text = $"{cachedEmails.Count:N0} results";
            UpdateMainStatus("Loaded " + cachedEmails.Count.ToString("N0") +
                " emails | Project: " + Path.GetFileName(projectPath).Replace("&", "&&"));
        }

        // ============================================================
        // Lists / Preview
        // ============================================================
        private void LoadMonthFolders()
        {
            string emailsRoot = Path.Combine(projectPath, "Emails");
            if (!Directory.Exists(emailsRoot)) return;

            try
            {
                var folders = Directory.GetDirectories(emailsRoot)
                    .Select(Path.GetFileName)
                    .OrderByDescending(f => f)
                    .ToArray();

                lstMonthFolders.Items.Clear();
                lstMonthFolders.Items.AddRange(folders);

                if (folders.Length > 0)
                    lstMonthFolders.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading folders: " + ex.Message);
            }
        }

        private void LoadProjectAutocompleteData()
        {
            Task.Run(() =>
            {
                try
                {
                    string[] validCategories = {
                        "01 Small Jobs","03 Residential","04 Commercial","05 Office",
                        "06 Hotel","07 Industrial-Garage","08 Inst-Rec-Church","09 Reno-Seismic-Resto"
                    };

                    var dict = new Dictionary<string, string>();

                    foreach (var category in validCategories)
                    {
                        string categoryPath = Path.Combine(ProjectsRoot, category);
                        if (!Directory.Exists(categoryPath)) continue;

                        foreach (var projPath in Directory.EnumerateDirectories(categoryPath))
                        {
                            string name = Path.GetFileName(projPath);
                            dict[name] = projPath;
                        }
                    }

                    UpdateUI(() =>
                    {
                        _allProjectsDict = dict;
                        _allProjectNamesList = dict.Keys.OrderBy(p => p).ToList();

                        projectListBox.BeginUpdate();
                        projectListBox.Items.Clear();
                        projectListBox.Items.AddRange(_allProjectNamesList.ToArray());
                        projectListBox.EndUpdate();
                    });
                }
                catch (Exception ex)
                {
                    UpdateUI(() =>
                        MessageBox.Show("Error loading projects: " + ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
                }
            });
        }

        private void LoadFiledEmails(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                UpdateMainStatus("Folder not found: " + Path.GetFileName(folderPath));
                return;
            }

            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*.msg")
                    .Concat(Directory.EnumerateFiles(folderPath, "*.eml"))
                    .ToList();

                UpdateUI(() =>
                {
                    emailGrid.Rows.Clear();
                    foreach (var file in files)
                    {
                        var meta = cachedEmails.FirstOrDefault(m => string.Equals(m.FilePath, file, StringComparison.OrdinalIgnoreCase));
                        if (meta != null)
                        {
                            var row = new DataGridViewRow();
                            row.CreateCells(emailGrid, meta.FileName, meta.SendDate, meta.Sender);
                            row.Tag = meta.FilePath;
                            emailGrid.Rows.Add(row);
                        }
                    }
                    UpdateMainStatus("Showing " + files.Count.ToString("N0") +
                        " emails from " + Path.GetFileName(folderPath));
                });
            }
            catch (Exception ex)
            {
                UpdateMainStatus("Error loading emails: " + ex.Message);
            }
        }

        private void EmailGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && emailGrid.Rows[e.RowIndex].Tag is string path && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open file:\n" + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void EmailGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (emailGrid.SelectedRows.Count == 0)
            {
                RenderHtml("<div style='padding:20px;color:#666;'>No message selected</div>");
                return;
            }

            string filePath = emailGrid.SelectedRows[0].Tag as string;
            if (!string.IsNullOrEmpty(filePath))
                DisplayEmailPreview(filePath);
        }

        private void DisplayEmailPreview(string filePath)
        {
            if (!File.Exists(filePath))
            {
                RenderHtml("<div style='padding:20px;color:red;'>File not found</div>");
                return;
            }

            try
            {
                string content;
                if (filePath.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var msg = new MsgReaderMessage(filePath))
                    {
                        content = !string.IsNullOrWhiteSpace(msg.BodyHtml)
                            ? msg.BodyHtml
                            : "<pre>" + HttpUtility.HtmlEncode(msg.BodyText) + "</pre>";
                    }
                }
                else
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var eml = new MsgReaderMimeMessage(stream);
                        content = (eml.HtmlBody != null && !string.IsNullOrWhiteSpace(eml.HtmlBody.GetBodyAsText()))
                            ? eml.HtmlBody.GetBodyAsText()
                            : "<pre>" + HttpUtility.HtmlEncode(eml.TextBody != null ? eml.TextBody.GetBodyAsText() : "") + "</pre>";
                    }
                }

                RenderHtml(content);
            }
            catch (Exception ex)
            {
                RenderHtml("<div style='padding:20px;color:red;'>Preview error: " +
                           HttpUtility.HtmlEncode(ex.Message) + "</div>");
            }
        }

        private void RenderHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                html = "<div style='padding:20px;color:#666;'>No content available</div>";

            if (previewWeb != null && previewWeb.CoreWebView2 != null)
                previewWeb.CoreWebView2.NavigateToString("<!doctype html><meta charset='utf-8'>" + html);
        }

        // ============================================================
        // Overlay / Footer / Helpers
        // ============================================================
        private async Task ShowOverlayAsync(string message)
        {
            await Task.Yield();
            UpdateUI(() =>
            {
                spinnerStatusLabel.Text = message;
                loadingOverlay.Visible = true;
                loadingOverlay.BringToFront();
                spinner.Visible = true;
                CenterOverlay();
            });
        }

        private void HideOverlay()
        {
            UpdateUI(() =>
            {
                spinner.Visible = false;
                loadingOverlay.Visible = false;
            });
        }

        private void CenterOverlay()
        {
            if (loadingOverlay == null || spinner == null || spinnerStatusLabel == null) return;

            var cs = loadingOverlay.ClientSize;
            var x = (cs.Width - spinner.Width) / 2;
            var y = (cs.Height - spinner.Height) / 2 - 12;
            spinner.Location = new Point(Math.Max(0, x), Math.Max(0, y));

            var labelWidth = spinnerStatusLabel.PreferredWidth;
            spinnerStatusLabel.Size = new Size(labelWidth, spinnerStatusLabel.Height);
            var lx = (cs.Width - spinnerStatusLabel.Width) / 2;
            var ly = spinner.Bottom + 8;
            spinnerStatusLabel.Location = new Point(Math.Max(0, lx), Math.Max(0, ly));
        }

        private void RefreshFooterStats()
        {
            try
            {
                string emailsRoot = Path.Combine(projectPath, "Emails");
                int folderCount = Directory.Exists(emailsRoot) ? Directory.GetDirectories(emailsRoot).Length : 0;

                var stats = db.GetStats();
                string sizeText = FormatBytes(stats.TotalBytes);

                UpdateUI(() =>
                {
                    statusLabel.Text = "Folders: " + folderCount.ToString("N0") +
                                       "  |  Emails: " + stats.EmailCount.ToString("N0") +
                                       "  |  Total size: " + sizeText;
                });
            }
            catch (Exception ex)
            {
                UpdateUI(() => statusLabel.Text = "Stats error: " + ex.Message);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024.0; u++; }
            return v.ToString("0.##") + " " + units[u];
        }

        private void EnableCandystriping(ListBox listBox)
        {
            listBox.DrawMode = DrawMode.OwnerDrawFixed;
            listBox.ItemHeight = TextRenderer.MeasureText("Sample", listBox.Font).Height + 4;
            listBox.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;

                var bg = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                    ? SystemColors.Highlight
                    : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(240, 240, 240));

                using (var brush = new SolidBrush(bg))
                    e.Graphics.FillRectangle(brush, e.Bounds);

                string text = listBox.Items[e.Index].ToString().Replace("&", "&&");
                TextRenderer.DrawText(e.Graphics, text, listBox.Font, e.Bounds,
                    (e.State & DrawItemState.Selected) == DrawItemState.Selected ? SystemColors.HighlightText : listBox.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                    e.DrawFocusRectangle();
            };
        }

        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { txtQuery.Focus(); return true; }
            if (keyData == Keys.Enter && txtQuery.Focused) { _ = PerformSearch(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { txtSender.Focus(); return true; }
            if (keyData == (Keys.Control | Keys.D)) { btnFilters.PerformClick(); return true; }
            if (keyData == Keys.Escape) { emailGrid.ClearSelection(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===== Robust project matching =====
        private static string ExtractCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            input = input.Trim();
            int paren = input.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) return input.Substring(0, paren).Trim();
            return input;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // SearchFiledEmailsForm
            // 
            this.ClientSize = new System.Drawing.Size(282, 253);
            this.Name = "SearchFiledEmailsForm";
            this.Load += new System.EventHandler(this.SearchFiledEmailsForm_Load);
            this.ResumeLayout(false);

        }

        private void SearchFiledEmailsForm_Load(object sender, EventArgs e)
        {

        }

        private bool TryGetProjectPath(string input, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string typed = input.Trim();
            string codeOnly = ExtractCode(typed);

            if (_allProjectsDict.TryGetValue(typed, out fullPath)) return true;
            if (!string.IsNullOrEmpty(codeOnly) && _allProjectsDict.TryGetValue(codeOnly, out fullPath)) return true;

            var kv = _allProjectsDict.FirstOrDefault(k =>
                k.Key.StartsWith(codeOnly, StringComparison.OrdinalIgnoreCase) ||
                k.Key.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(kv.Value)) { fullPath = kv.Value; return true; }

            kv = _allProjectsDict.FirstOrDefault(k =>
                k.Key.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(kv.Value)) { fullPath = kv.Value; return true; }

            kv = _allProjectsDict.FirstOrDefault(k =>
                k.Value.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(kv.Value)) { fullPath = kv.Value; return true; }

            return false;
        }

        private void SwitchProject(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("Please enter a project number or name.", "Input Needed",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryGetProjectPath(input, out var fullPath))
            {
                MessageBox.Show($"Project '{input}' not found in the directory index.",
                    "Project Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var emailsPath = Path.Combine(fullPath, "Emails");
            if (!Directory.Exists(emailsPath))
            {
                MessageBox.Show($"Project found, but Emails folder is missing:\n{emailsPath}",
                    "Emails Folder Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newForm = new SearchFiledEmailsForm(fullPath);
            newForm.Show();
            newForm.BringToFront();
            newForm.Activate();
            this.Close();
        }

        private void UpdateUI(Action action)
        {
            if (InvokeRequired) Invoke(new MethodInvoker(action));
            else action();
        }

        private void UpdateMainStatus(string text)
        {
            UpdateUI(() => mainStatusLabel.Text = text);
        }
    }
}
