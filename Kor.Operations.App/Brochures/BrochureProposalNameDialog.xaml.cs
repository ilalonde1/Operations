#nullable enable
using System.Windows;

namespace Kor.Operations.Brochures
{
    public partial class BrochureProposalNameDialog : Window
    {
        public string ProposalName => NameBox.Text.Trim();

        public BrochureProposalNameDialog(string existingName = "")
        {
            InitializeComponent();
            NameBox.Text = existingName;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(
                    "Enter a proposal name.",
                    "Brochure Builder — Proposal Name Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) =>
            DialogResult = false;
    }
}

