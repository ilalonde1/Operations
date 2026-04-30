
namespace EmailFilerv2
{
    partial class ProjectPickerForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabFavorites;
        private System.Windows.Forms.TabPage tabRecents;
        private System.Windows.Forms.TabPage tabAll;
        private System.Windows.Forms.ListBox listBoxFavorites;
        private System.Windows.Forms.ListBox listBoxRecents;
        private System.Windows.Forms.ListBox listBoxAll;
        private System.Windows.Forms.Button btnSelect;
        private System.Windows.Forms.Button btnAddToFavorites;
        private System.Windows.Forms.Button btnRemoveFromFavorites;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.ToolTip toolTip1;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabFavorites = new System.Windows.Forms.TabPage();
            this.tabRecents = new System.Windows.Forms.TabPage();
            this.tabAll = new System.Windows.Forms.TabPage();
            this.listBoxFavorites = new System.Windows.Forms.ListBox();
            this.listBoxRecents = new System.Windows.Forms.ListBox();
            this.listBoxAll = new System.Windows.Forms.ListBox();
            this.btnSelect = new System.Windows.Forms.Button();
            this.btnClear = new System.Windows.Forms.Button();
            this.btnClear.Text = "Clear";
            this.btnClear.Location = new System.Drawing.Point(500, 12); // right beside Select
            this.btnClear.Size = new System.Drawing.Size(60, 23);
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);
            this.btnAddToFavorites = new System.Windows.Forms.Button();
            this.btnRemoveFromFavorites = new System.Windows.Forms.Button();
            this.searchBox = new System.Windows.Forms.TextBox();
            this.searchLabel = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);

            this.tabControl1.SuspendLayout();
            this.tabFavorites.SuspendLayout();
            this.tabRecents.SuspendLayout();
            this.tabAll.SuspendLayout();

            this.tabControl1.Controls.Add(this.tabFavorites);
            this.tabControl1.Controls.Add(this.tabRecents);
            this.tabControl1.Controls.Add(this.tabAll);
            this.tabControl1.Location = new System.Drawing.Point(12, 45);
            this.tabControl1.Size = new System.Drawing.Size(580, 300);
            this.tabControl1.TabIndex = 0;

            this.tabFavorites.Controls.Add(this.listBoxFavorites);
            this.tabFavorites.Text = "My Projects";

            this.tabRecents.Controls.Add(this.listBoxRecents);
            this.tabRecents.Text = "Recents";

            this.tabAll.Controls.Add(this.listBoxAll);
            this.tabAll.Text = "All Projects";

            this.listBoxFavorites.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxRecents.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxAll.Dock = System.Windows.Forms.DockStyle.Fill;

            this.btnSelect.Text = "Select";
            this.btnSelect.Location = new System.Drawing.Point(420, 12);
            this.btnSelect.Click += new System.EventHandler(this.btnSelect_Click);

            this.btnAddToFavorites.Location = new System.Drawing.Point(600, 70);
            this.btnAddToFavorites.Size = new System.Drawing.Size(32, 32);
            this.btnAddToFavorites.Click += new System.EventHandler(this.btnAddToFavorites_Click);

            this.btnRemoveFromFavorites.Location = new System.Drawing.Point(600, 105);
            this.btnRemoveFromFavorites.Size = new System.Drawing.Size(32, 32);
            this.btnRemoveFromFavorites.Click += new System.EventHandler(this.btnRemoveFromFavorites_Click);

            this.searchLabel.Text = "Search:";
            this.searchLabel.Location = new System.Drawing.Point(12, 15);
            this.searchLabel.AutoSize = true;

            this.searchBox.Location = new System.Drawing.Point(65, 12);
            this.searchBox.Width = 340;

            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.btnSelect);
            this.Controls.Add(this.btnAddToFavorites);
            this.Controls.Add(this.btnRemoveFromFavorites);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.searchBox);
            this.Controls.Add(this.searchLabel);

            this.ClientSize = new System.Drawing.Size(830, 360);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Text = "Select Project";

            this.tabControl1.ResumeLayout(false);
            this.tabFavorites.ResumeLayout(false);
            this.tabRecents.ResumeLayout(false);
            this.tabAll.ResumeLayout(false);
        }
    }
}
