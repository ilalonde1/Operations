#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class PersonnelEditor : UserControl
    {
        public static readonly DependencyProperty StaffMembersProperty =
            DependencyProperty.Register(
                nameof(StaffMembers),
                typeof(ObservableCollection<ProposalStaffMember>),
                typeof(PersonnelEditor),
                new PropertyMetadata(null));

        public ObservableCollection<ProposalStaffMember> StaffMembers
        {
            get => (ObservableCollection<ProposalStaffMember>)GetValue(StaffMembersProperty);
            set => SetValue(StaffMembersProperty, value);
        }

        public PersonnelEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshAdditionalStaff();
        }

        private PersonnelBlockContent? Model => DataContext as PersonnelBlockContent;
        private IEnumerable<ProposalStaffMember> StaffOptions => StaffMembers ?? Enumerable.Empty<ProposalStaffMember>();

        private void RefreshAdditionalStaff()
        {
            if (Model is null)
                return;

            var staffIndex = StaffMembers?
                .ToDictionary(s => s.Id, s => s)
                ?? new Dictionary<string, ProposalStaffMember>();

            var resolved = Model.AdditionalStaffIds
                .Select(id => staffIndex.TryGetValue(id, out var s) ? s : null)
                .Where(s => s is not null)
                .ToList();

            AdditionalStaffList.ItemsSource = resolved;
        }

        private void AddAdditionalStaff_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || AvailableStaffCombo.SelectedItem is not ProposalStaffMember staff)
                return;

            if (!Model.AdditionalStaffIds.Contains(staff.Id))
                Model.AdditionalStaffIds.Add(staff.Id);

            RefreshAdditionalStaff();
        }

        private void RemoveAdditionalStaff_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null)
                return;

            foreach (var staff in AdditionalStaffList.SelectedItems.OfType<ProposalStaffMember>().ToList())
                Model.AdditionalStaffIds.Remove(staff.Id);

            RefreshAdditionalStaff();
        }
    }
}
