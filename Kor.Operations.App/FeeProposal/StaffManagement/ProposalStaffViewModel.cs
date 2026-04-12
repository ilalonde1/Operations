#nullable enable
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal.StaffManagement
{
    internal sealed class ProposalStaffViewModel : ObservableObject
    {
        private readonly IProposalStaffStore _store;
        private ProposalStaffMember? _selected;
        private bool _hasChanges;

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

        public bool HasChanges
        {
            get => _hasChanges;
            private set => SetField(ref _hasChanges, value);
        }

        public ProposalStaffViewModel(IProposalStaffStore store)
        {
            _store = store;
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            Staff.Clear();
            foreach (var s in await _store.LoadAllAsync(ct).ConfigureAwait(false))
                Staff.Add(s);
        }

        public void AddNew()
        {
            var member = new ProposalStaffMember { FullName = "New Staff Member" };
            Staff.Add(member);
            Selected = member;
            HasChanges = true;
        }

        public void DeleteSelected()
        {
            if (_selected is null)
                return;

            Staff.Remove(_selected);
            Selected = null;
            HasChanges = true;
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _store.SaveAllAsync(new System.Collections.Generic.List<ProposalStaffMember>(Staff), ct).ConfigureAwait(false);
            HasChanges = false;
        }
    }
}
