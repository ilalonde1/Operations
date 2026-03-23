#nullable enable
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class CoverEditor : UserControl
    {
        public static readonly DependencyProperty StaffMembersProperty =
            DependencyProperty.Register(
                nameof(StaffMembers),
                typeof(ObservableCollection<ProposalStaffMember>),
                typeof(CoverEditor),
                new PropertyMetadata(null));

        public ObservableCollection<ProposalStaffMember> StaffMembers
        {
            get => (ObservableCollection<ProposalStaffMember>)GetValue(StaffMembersProperty);
            set => SetValue(StaffMembersProperty, value);
        }

        public CoverEditor()
        {
            InitializeComponent();
        }

        private void LookupProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProposalProjectPickerDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                ProjectNameBox.Text = dlg.SelectedProjectName ?? string.Empty;
            }
        }

        private void LookupAttention_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProposalContactPickerDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.SelectedContact is { } c)
            {
                AttentionNameBox.Text = c.Name;
                AttentionTitleBox.Text = c.Title;
                AttentionEmailBox.Text = c.Email;
                if (string.IsNullOrWhiteSpace(ClientCompanyBox.Text))
                    ClientCompanyBox.Text = c.Company;
            }
        }

        private void LookupCc_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProposalContactPickerDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.SelectedContact is { } c)
            {
                CcNameBox.Text = c.Name;
                CcTitleBox.Text = c.Title;
                CcEmailBox.Text = c.Email;
            }
        }
    }
}
