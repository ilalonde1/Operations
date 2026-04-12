#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Serilog;
using FeeProposalModel = Kor.Operations.Core.Models.Proposal.FeeProposal;

namespace Kor.Operations.App.FeeProposal
{
    public sealed class FeeProposalBuilderViewModel : ObservableObject
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly IFeeProposalStore _proposalStore;
        private readonly IProposalBlockLibraryStore _libraryStore;
        private readonly IProposalStaffStore _staffStore;

        private FeeProposalModel _proposal = new();
        private FeeProposalBlockViewModel? _selectedBlock;
        private string _documentName = "Untitled Proposal";
        private string? _selectedBlockTypeName;
        private bool _isDirty;
        private bool _isGenerating;
        private int _currentStep = 1;

        public ObservableCollection<FeeProposalBlockViewModel> Blocks { get; } = new();
        public ObservableCollection<ProposalStaffMember> StaffMembers { get; } = new();
        public ObservableCollection<ProposalBlockTemplate> LibraryTemplates { get; } = new();
        public ObservableCollection<ProposalLibraryCategoryViewModel> LibraryCategories { get; } = new();
        public ObservableCollection<string> BlockTypeNames { get; } = new();
        public ObservableCollection<BitmapSource> PreviewPages { get; } = new();
        public FeeProposalModel CurrentProposal => _proposal;
        public ICommand? GeneratePreviewCommand { get; internal set; }

        public bool IsGenerating
        {
            get => _isGenerating;
            private set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
        }

        public bool HasPreview => PreviewPages.Count > 0;
        public bool IsPreviewEmpty => PreviewPages.Count == 0;
        public bool CanGenerate => Blocks.Count > 0 && !IsGenerating;
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
            }
        }

        public string WindowTitle => IsDirty ? $"{DocumentName} *" : DocumentName;

        public int CurrentStep
        {
            get => _currentStep;
            private set
            {
                _currentStep = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
                OnPropertyChanged(nameof(IsOnFinalStep));
            }
        }

        public bool CanGoBack => CurrentStep > 1;

        public bool CanGoForward => CurrentStep switch
        {
            1 => true,
            2 => Blocks.Count > 0,
            3 => true,
            _ => false
        };

        public bool IsOnFinalStep => CurrentStep == 4;

        public FeeProposalBlockViewModel CoverBlockVm
        {
            get
            {
                var existing = Blocks.FirstOrDefault(b => b.Block.BlockType == ProposalBlockType.Cover);
                if (existing != null)
                    return existing;
                InsertBlankBlock(ProposalBlockType.Cover);
                // Move to first position
                var inserted = Blocks.FirstOrDefault(b => b.Block.BlockType == ProposalBlockType.Cover);
                if (inserted != null && Blocks.Count > 1)
                {
                    var idx = Blocks.IndexOf(inserted);
                    if (idx > 0) Blocks.Move(idx, 0);
                }
                return Blocks.FirstOrDefault(b => b.Block.BlockType == ProposalBlockType.Cover)
                    ?? throw new InvalidOperationException("Cover block failed to insert.");
            }
        }

        public string BlockSummaryText
        {
            get
            {
                var count = Blocks.Count;
                if (count == 0) return "No blocks added yet.";
                var names = string.Join(", ", Blocks.Select(b => b.Block.BlockType.ToString()));
                return $"{count} block{(count == 1 ? "" : "s")} — {names}";
            }
        }

        public string DocumentName
        {
            get => _documentName;
            set
            {
                _documentName = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
                IsDirty = true;
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
            IFeeProposalStore proposalStore,
            IProposalBlockLibraryStore libraryStore,
            IProposalStaffStore staffStore)
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

            Blocks.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanGoForward));
                OnPropertyChanged(nameof(BlockSummaryText));
                IsDirty = true;
            };
            SelectedBlockTypeName = BlockTypeNames.FirstOrDefault();
        }

        public async Task RefreshLibraryAsync(CancellationToken ct = default)
        {
            LibraryTemplates.Clear();
            LibraryCategories.Clear();

            var templates = await _libraryStore.LoadAllAsync(ct).ConfigureAwait(false);

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
            try
            {
                var json = JsonSerializer.Serialize(template.Content, JsonOptions);
                try
                {
                    var copy = JsonSerializer.Deserialize<FeeProposalBlock>(json, JsonOptions)!;
                    copy.InstanceId = Guid.NewGuid().ToString("N");
                    copy.TemplateId = template.Id;
                    copy.TemplateName = template.Name;
                    var vm = new FeeProposalBlockViewModel(copy);
                    Blocks.Add(vm);
                    SelectedBlock = vm;
                    IsDirty = true;
                }
                catch (Exception ex)
                {
                    Log.ForContext<FeeProposalBuilderViewModel>().Error(ex, "Failed to deserialize template '{TemplateName}'. Skipping.", template.Name);
                    System.Windows.MessageBox.Show($"Could not load template \"{template.Name}\".\n\nIt may be corrupted. Try removing and re-adding it from the library.", "Template Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.ForContext<FeeProposalBuilderViewModel>().Error(ex, "Failed to insert proposal block template. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
                MessageBox.Show($"Template insert failed:\n{ex.Message}", "Template Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            IsDirty = true;
        }

        public void MoveUp(FeeProposalBlockViewModel vm)
        {
            var i = Blocks.IndexOf(vm);
            if (i > 0)
            {
                Blocks.Move(i, i - 1);
                IsDirty = true;
            }
        }

        public void MoveDown(FeeProposalBlockViewModel vm)
        {
            var i = Blocks.IndexOf(vm);
            if (i >= 0 && i < Blocks.Count - 1)
            {
                Blocks.Move(i, i + 1);
                IsDirty = true;
            }
        }

        public void GoNext()
        {
            if (CurrentStep < 4) CurrentStep++;
        }

        public void GoPrev()
        {
            if (CurrentStep > 1) CurrentStep--;
        }

        public void DeleteBlock(FeeProposalBlockViewModel vm)
        {
            Blocks.Remove(vm);
            if (SelectedBlock == vm)
                SelectedBlock = null;

            IsDirty = true;
        }

        public async Task<bool> SaveAsTemplateAsync(FeeProposalBlockViewModel vm, string templateName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return false;

            var template = new ProposalBlockTemplate
            {
                Name = templateName,
                Category = vm.Block.BlockType.ToString(),
                BlockType = vm.Block.BlockType,
                Content = vm.Block,
            };
            await _libraryStore.SaveAsync(template, ct).ConfigureAwait(false);
            await RefreshLibraryAsync(ct).ConfigureAwait(false);
            return true;
        }

        public void NewProposal()
        {
            _proposal = new FeeProposalModel();
            DocumentName = "Untitled Proposal";
            Blocks.Clear();
            SelectedBlock = null;
            IsDirty = false;
            CurrentStep = 1;
            _ = CoverBlockVm;
        }

        public void SaveProposal()
        {
            _proposal.Name = DocumentName;
            _proposal.Blocks = Blocks.Select(b => b.Block).ToList();
            _proposalStore.Save(_proposal);
            IsDirty = false;
        }

        public void SaveProposalAs(string newName)
        {
            var clone = new FeeProposalModel
            {
                Name = newName,
                Blocks = Blocks.Select(b => b.Block).ToList(),
            };
            _proposalStore.Save(clone);
            _proposal = clone;
            DocumentName = newName;
            IsDirty = false;
        }

        public void OpenProposal(FeeProposalModel proposal)
        {
            _proposal = proposal;
            DocumentName = proposal.Name;
            Blocks.Clear();
            foreach (var b in proposal.Blocks ?? new List<FeeProposalBlock>())
            {
                try
                {
                    Blocks.Add(new FeeProposalBlockViewModel(b));
                }
                catch (Exception ex)
                {
                    Log.ForContext<FeeProposalBuilderViewModel>().Warning(ex, "Skipping malformed proposal block while opening proposal. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
                }
            }

            SelectedBlock = null;
            IsDirty = false;
            CurrentStep = 1;
        }

        public void BeginGenerating() => IsGenerating = true;
        public void EndGenerating() => IsGenerating = false;

        public void SetPreviewPages(IReadOnlyList<byte[]> pages)
        {
            PreviewPages.Clear();
            foreach (var p in pages)
                PreviewPages.Add(CreateBitmap(p));
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(IsPreviewEmpty));
        }

        private static BitmapSource CreateBitmap(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }

    public sealed class ProposalLibraryCategoryViewModel
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
