#nullable enable
using System.Collections.ObjectModel;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal.StaffManagement
{
    internal sealed class ProposalStaffViewModel : ObservableObject
    {
        private readonly IProposalStaffStore _store;
        private ProposalStaffMember? _selected;

        public ObservableCollection<ProposalStaffMember> Staff { get; } = new();

        public ProposalStaffMember? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => _selected is not null;

        public ProposalStaffViewModel(IProposalStaffStore store)
        {
            _store = store;
            Reload();
        }

        public void Reload()
        {
            Staff.Clear();
            foreach (var s in _store.LoadAll())
                Staff.Add(s);
        }

        public void AddNew()
        {
            var member = new ProposalStaffMember { FullName = "New Staff Member" };
            Staff.Add(member);
            Selected = member;
            Save();
        }

        public void DeleteSelected()
        {
            if (_selected is null)
                return;

            Staff.Remove(_selected);
            Selected = null;
            Save();
        }

        public void Save()
        {
            _store.SaveAll(new System.Collections.Generic.List<ProposalStaffMember>(Staff));
        }
    }
}
