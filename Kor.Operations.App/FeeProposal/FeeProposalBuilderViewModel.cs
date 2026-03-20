#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using FeeProposalModel = Kor.Operations.Core.Models.Proposal.FeeProposal;

namespace Kor.Operations.App.FeeProposal
{
    internal sealed class FeeProposalBuilderViewModel : ObservableObject
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly FeeProposalStore _proposalStore;
        private readonly ProposalBlockLibraryStore _libraryStore;
        private readonly ProposalStaffStore _staffStore;

        private FeeProposalModel _proposal = new();
        private FeeProposalBlockViewModel? _selectedBlock;
        private string _documentName = "Untitled Proposal";
        private string? _selectedBlockTypeName;

        public ObservableCollection<FeeProposalBlockViewModel> Blocks { get; } = new();
        public ObservableCollection<ProposalStaffMember> StaffMembers { get; } = new();
        public ObservableCollection<ProposalBlockTemplate> LibraryTemplates { get; } = new();
        public ObservableCollection<ProposalLibraryCategoryViewModel> LibraryCategories { get; } = new();
        public ObservableCollection<string> BlockTypeNames { get; } = new();
        public FeeProposalModel CurrentProposal => _proposal;
        public bool CanGenerate => Blocks.Count > 0;

        public string DocumentName
        {
            get => _documentName;
            set
            {
                _documentName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public FeeProposalBlockViewModel? SelectedBlock
        {
            get => _selectedBlock;
            set
            {
                _selectedBlock = value;
                OnPropertyChanged();
            }
        }

        public string? SelectedBlockTypeName
        {
            get => _selectedBlockTypeName;
            set
            {
                _selectedBlockTypeName = value;
                OnPropertyChanged();
            }
        }

        public FeeProposalBuilderViewModel(
            FeeProposalStore proposalStore,
            ProposalBlockLibraryStore libraryStore,
            ProposalStaffStore staffStore)
        {
            _proposalStore = proposalStore;
            _libraryStore = libraryStore;
            _staffStore = staffStore;

            foreach (var name in Enum.GetNames<ProposalBlockType>())
            {
                if (name == nameof(ProposalBlockType.PageBreak))
                    continue;
                BlockTypeNames.Add(name);
            }

            foreach (var staff in _staffStore.LoadAll())
                StaffMembers.Add(staff);

            Blocks.CollectionChanged += Blocks_CollectionChanged;
            SelectedBlockTypeName = BlockTypeNames.FirstOrDefault();
            RefreshLibrary();
        }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanGenerate));
        }

        public void RefreshLibrary()
        {
            LibraryTemplates.Clear();
            LibraryCategories.Clear();

            var templates = _libraryStore.LoadAll();
            foreach (var t in templates)
                LibraryTemplates.Add(t);

            foreach (var group in templates
                         .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Uncategorized" : t.Category)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                LibraryCategories.Add(new ProposalLibraryCategoryViewModel(group.Key, group));
            }
        }

        public void ReloadStaff()
        {
            StaffMembers.Clear();
            foreach (var s in _staffStore.LoadAll())
                StaffMembers.Add(s);
        }

        public void InsertFromTemplate(ProposalBlockTemplate template)
        {
            var json = JsonSerializer.Serialize(template.Content, JsonOptions);
            var copy = JsonSerializer.Deserialize<FeeProposalBlock>(json, JsonOptions)!;
            copy.InstanceId = Guid.NewGuid().ToString("N");
            copy.TemplateId = template.Id;
            copy.TemplateName = template.Name;
            var vm = new FeeProposalBlockViewModel(copy);
            Blocks.Add(vm);
            SelectedBlock = vm;
        }

        public void InsertBlankBlock(ProposalBlockType type)
        {
            var block = new FeeProposalBlock
            {
                BlockType = type,
                TemplateName = type.ToString(),
            };

            switch (type)
            {
                case ProposalBlockType.Cover: block.Cover = new CoverBlockContent(); break;
                case ProposalBlockType.Introduction: block.Introduction = new IntroductionBlockContent(); break;
                case ProposalBlockType.Company: block.Company = new CompanyBlockContent(); break;
                case ProposalBlockType.Personnel: block.Personnel = new PersonnelBlockContent(); break;
                case ProposalBlockType.References: block.References = new ReferencesBlockContent(); break;
                case ProposalBlockType.ProjectDescription: block.ProjectDescription = new ProjectDescriptionBlockContent(); break;
                case ProposalBlockType.FeeTable: block.FeeTable = new FeeTableBlockContent(); break;
                case ProposalBlockType.Scope: block.Scope = new ScopeBlockContent(); break;
                case ProposalBlockType.ExcludedServices: block.ExcludedServices = new ExcludedServicesBlockContent(); break;
                case ProposalBlockType.ApprovalToProceed: block.ApprovalToProceed = new ApprovalToProceedBlockContent(); break;
                case ProposalBlockType.SignaturePage: block.SignaturePage = new SignaturePageBlockContent(); break;
                case ProposalBlockType.RatesTable: block.RatesTable = new RatesTableBlockContent(); break;
                case ProposalBlockType.FreeText: block.FreeText = new FreeTextBlockContent(); break;
                case ProposalBlockType.PageBreak: block.PageBreak = new PageBreakBlockContent(); break;
            }

            var vm = new FeeProposalBlockViewModel(block);
            Blocks.Add(vm);
            SelectedBlock = vm;
        }

        public void MoveUp(FeeProposalBlockViewModel vm)
        {
            var i = Blocks.IndexOf(vm);
            if (i > 0)
                Blocks.Move(i, i - 1);
        }

        public void MoveDown(FeeProposalBlockViewModel vm)
        {
            var i = Blocks.IndexOf(vm);
            if (i >= 0 && i < Blocks.Count - 1)
                Blocks.Move(i, i + 1);
        }

        public void DeleteBlock(FeeProposalBlockViewModel vm)
        {
            Blocks.Remove(vm);
            if (SelectedBlock == vm)
                SelectedBlock = null;
        }

        public void SaveAsTemplate(FeeProposalBlockViewModel vm, string name)
        {
            var template = new ProposalBlockTemplate
            {
                Name = name,
                Category = vm.Block.BlockType.ToString(),
                BlockType = vm.Block.BlockType,
                Content = vm.Block,
            };
            _libraryStore.Save(template);
            RefreshLibrary();
        }

        public void NewProposal()
        {
            _proposal = new FeeProposalModel();
            DocumentName = "Untitled Proposal";
            Blocks.Clear();
            SelectedBlock = null;
        }

        public void SaveProposal()
        {
            _proposal.Name = DocumentName;
            _proposal.Blocks = Blocks.Select(b => b.Block).ToList();
            _proposalStore.Save(_proposal);
        }

        public void OpenProposal(FeeProposalModel proposal)
        {
            _proposal = proposal;
            DocumentName = proposal.Name;
            Blocks.Clear();
            foreach (var b in proposal.Blocks)
                Blocks.Add(new FeeProposalBlockViewModel(b));

            SelectedBlock = null;
        }

        public FeeProposalModel? FindProposalByIdOrName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = _proposalStore.LoadAll().FirstOrDefault(p =>
                string.Equals(p.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
            return match;
        }

        public string BuildOpenPromptText()
        {
            var proposals = _proposalStore.LoadAll();
            if (proposals.Count == 0)
                return string.Empty;

            return string.Join(Environment.NewLine, proposals.Select(p => $"{p.Name} [{p.Id}]"));
        }
    }

    internal sealed class ProposalLibraryCategoryViewModel
    {
        public string Category { get; }
        public ObservableCollection<ProposalBlockTemplate> Templates { get; }

        public ProposalLibraryCategoryViewModel(string category, System.Collections.Generic.IEnumerable<ProposalBlockTemplate> templates)
        {
            Category = category;
            Templates = new ObservableCollection<ProposalBlockTemplate>(templates);
        }
    }
}
