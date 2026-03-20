#nullable enable
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class IntroductionEditor : UserControl
    {
        public static readonly DependencyProperty StaffMembersProperty =
            DependencyProperty.Register(
                nameof(StaffMembers),
                typeof(ObservableCollection<ProposalStaffMember>),
                typeof(IntroductionEditor),
                new PropertyMetadata(null));

        public ObservableCollection<ProposalStaffMember> StaffMembers
        {
            get => (ObservableCollection<ProposalStaffMember>)GetValue(StaffMembersProperty);
            set => SetValue(StaffMembersProperty, value);
        }

        public IntroductionEditor()
        {
            InitializeComponent();
        }
    }
}
