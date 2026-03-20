#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class SignaturePageEditor : UserControl
    {
        public static readonly DependencyProperty StaffMembersProperty =
            DependencyProperty.Register(
                nameof(StaffMembers),
                typeof(ObservableCollection<ProposalStaffMember>),
                typeof(SignaturePageEditor),
                new PropertyMetadata(null));

        public ObservableCollection<ProposalStaffMember> StaffMembers
        {
            get => (ObservableCollection<ProposalStaffMember>)GetValue(StaffMembersProperty);
            set => SetValue(StaffMembersProperty, value);
        }

        public SignaturePageEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshSignatories();
        }

        private SignaturePageBlockContent? Model => DataContext as SignaturePageBlockContent;
        private IEnumerable<ProposalStaffMember> StaffOptions => StaffMembers ?? Enumerable.Empty<ProposalStaffMember>();

        private void RefreshSignatories()
        {
            if (Model is null)
                return;

            SignatoriesList.ItemsSource = Model.SignatoryStaffIds
                .Select(id => StaffOptions.FirstOrDefault(s => s.Id == id))
                .Where(s => s is not null)
                .ToList();
        }

        private void AddSignatory_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || AvailableSignatoryCombo.SelectedItem is not ProposalStaffMember staff)
                return;

            if (!Model.SignatoryStaffIds.Contains(staff.Id))
                Model.SignatoryStaffIds.Add(staff.Id);

            RefreshSignatories();
        }

        private void RemoveSignatory_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null)
                return;

            foreach (var staff in SignatoriesList.SelectedItems.OfType<ProposalStaffMember>().ToList())
                Model.SignatoryStaffIds.Remove(staff.Id);

            RefreshSignatories();
        }
    }
}
