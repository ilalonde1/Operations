#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Kor.Operations.App.Services;
using Kor.Operations.Brochures.SubVms;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel : ObservableObject
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly IBrochureRenderer _renderer;
        private readonly IBrochureDocxRenderer _docxRenderer;
        private readonly IBrochureContactStore _contactStore;
        private readonly BrochureAnalysisService _analysisService;
        private readonly ILogger<BrochureBuilderViewModel> _logger;
        private readonly IBrochureProposalStore _proposalStore;
        private readonly IProposalStaffStore _staffStore;

        // ── Persistent state ─────────────────────────────────────────────────
        private string? _proposalId;
        private string _proposalName = string.Empty;

        // ── Navigation state ─────────────────────────────────────────────────
        private int _currentStep = 1;

        // ── Selection state ───────────────────────────────────────────────────
        private int _selectedBlockIndex = -1;
        private int _selectedProjectIndex = -1;
        private int _selectedOverviewIndex = -1;
        private BrochureSection? _selectedSection;
        private BrochureBlock? _selectedSectionBlock;

        // ── Editing state ─────────────────────────────────────────────────────
        private bool _isGenerating;
        private bool _isEditingProject;
        private bool _isEditingPerson;
        private bool _isEditingOverview;
        private BrochureProject? _editingProject;
        private BrochurePerson? _editingPerson;
        private BrochureBlock? _editingBlock;

        // ── Internal flags ────────────────────────────────────────────────────
        private bool _suppressCollectionNotifications;
        private bool _isDirty;
        private BrochureContactConfig _contactConfig = new();
        private ProposalStaffMember? _selectedRosterMember;

        // ── Dirty tracking ────────────────────────────────────────────────────
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty == value) return;
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
            }
        }

        private void SetDirty() => IsDirty = true;

        private void ClearDirty() => IsDirty = false;

        // ── Constructor ───────────────────────────────────────────────────────
        public BrochureBuilderViewModel(
            IBrochureRenderer renderer,
            IBrochureDocxRenderer docxRenderer,
            IBrochureContactStore contactStore,
            BrochureAnalysisService analysisService,
            ILogger<BrochureBuilderViewModel> logger,
            IBrochureProposalStore proposalStore,
            IProposalStaffStore staffStore)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _docxRenderer = docxRenderer ?? throw new ArgumentNullException(nameof(docxRenderer));
            _contactStore = contactStore ?? throw new ArgumentNullException(nameof(contactStore));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
            _staffStore = staffStore ?? throw new ArgumentNullException(nameof(staffStore));

            Cover = new BrochureCoverVm();
            Cover.OnChanged = SetDirty;
            Project = new BrochureProjectVm();
            Person = new BrochurePersonVm();
            Overview = new BrochureOverviewVm();
            foreach (var s in _staffStore.LoadAll())
                StaffRoster.Add(s);

            SkinOptions = new ReadOnlyCollection<string>(
                BrochureSkinRegistry.All.Select(static s => s.DisplayName).ToList());
            LayoutOptions = new ReadOnlyCollection<string>(
                BrochureLayoutTemplateCatalog.Default.All.Select(static t => t.DisplayName).ToList());

            Cover.TemplateName = SkinOptions[0];
            Cover.SkinId = BrochureSkinRegistry.All[0].Id;
            Cover.LayoutTemplateId = BrochureLayoutTemplateCatalog.Default.All[0].Id;
            Cover.PropertyChanged += Cover_PropertyChanged;

            EnsureSeedProposals();

            Blocks.CollectionChanged += Blocks_CollectionChanged;
            PreviewPages.CollectionChanged += PreviewPages_CollectionChanged;

            InitBlockCommands();
            InitProjectCommands();
            InitPersonnelCommands();
            InitOverviewCommands();
            InitClientListCommands();
            InitPersistenceCommands();
            InitPreviewCommands();
            _contactConfig = _contactStore.Load();

            QueueSetupPreviewRefresh();
        }

        // ── Sub-VMs ───────────────────────────────────────────────────────────
        public BrochureCoverVm Cover { get; }
        public BrochureProjectVm Project { get; }
        public BrochurePersonVm Person { get; }
        public BrochureOverviewVm Overview { get; }
        public BrochureContactConfig ContactConfig => _contactConfig;

        // ── Options ───────────────────────────────────────────────────────────
        public IReadOnlyList<string> SkinOptions { get; }
        public IReadOnlyList<string> LayoutOptions { get; }
        public IReadOnlyList<string> TemplateOptions => SkinOptions;

        // ── Collections ───────────────────────────────────────────────────────
        public ObservableCollection<BrochureBlock> Blocks { get; } = new();
        public ObservableCollection<BitmapSource> PreviewPages { get; } = new();
        public ObservableCollection<ProposalStaffMember> StaffRoster { get; } = new();

        public ProposalStaffMember? SelectedRosterMember
        {
            get => _selectedRosterMember;
            set => SetField(ref _selectedRosterMember, value);
        }

        public IEnumerable<BrochureSection> SectionsList =>
            Blocks
                .Where(static b => b.BlockType == BrochureBlockType.Section && b.Section is not null)
                .Select(static b => b.Section!);

        // ── Navigation ────────────────────────────────────────────────────────
        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (!SetField(ref _currentStep, value)) return;
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsOnFinalStep));
            }
        }

        public bool CanGoBack => CurrentStep > 1;
        public bool CanGoNext => CurrentStep < 4;
        public bool IsOnFinalStep => CurrentStep == 4;

        public ICommand NextStepCommand => new RelayCommand(_ => CurrentStep++, _ => CanGoNext);
        public ICommand PrevStepCommand => new RelayCommand(_ => CurrentStep--, _ => CanGoBack);

        // ── Selection ─────────────────────────────────────────────────────────
        public int SelectedBlockIndex
        {
            get => _selectedBlockIndex;
            set
            {
                if (!SetField(ref _selectedBlockIndex, value)) return;
                OnPropertyChanged(nameof(SelectedBlock));
            }
        }

        public BrochureBlock? SelectedBlock =>
            SelectedBlockIndex >= 0 && SelectedBlockIndex < Blocks.Count
                ? Blocks[SelectedBlockIndex]
                : null;

        public BrochureSection? SelectedSection
        {
            get => _selectedSection;
            set
            {
                _selectedSection = value;
                _selectedSectionBlock = Blocks.FirstOrDefault(b => ReferenceEquals(b.Section, value));
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddProjectToSection));
                OnPropertyChanged(nameof(SelectedBlock));
            }
        }

        public int SelectedProjectIndex
        {
            get => _selectedProjectIndex;
            set
            {
                if (!SetField(ref _selectedProjectIndex, value)) return;
                OnPropertyChanged(nameof(SelectedProject));
            }
        }

        public int SelectedOverviewIndex
        {
            get => _selectedOverviewIndex;
            set => SetField(ref _selectedOverviewIndex, value);
        }

        public BrochureProject? SelectedProject =>
            SelectedBlock?.BlockType == BrochureBlockType.Section &&
            SelectedBlock.Section is not null &&
            SelectedProjectIndex >= 0 &&
            SelectedProjectIndex < SelectedBlock.Section.Projects.Count
                ? SelectedBlock.Section.Projects[SelectedProjectIndex]
                : null;

        // ── Editing state ─────────────────────────────────────────────────────
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (!SetField(ref _isGenerating, value)) return;
                OnPropertyChanged(nameof(CanProduce));
                OnPropertyChanged(nameof(CanGenerate));
            }
        }

        public bool CanProduce => !IsGenerating;

        public bool IsEditingProject
        {
            get => _isEditingProject;
            set => SetField(ref _isEditingProject, value);
        }

        public bool IsEditingPerson
        {
            get => _isEditingPerson;
            set => SetField(ref _isEditingPerson, value);
        }

        public bool IsEditingOverview
        {
            get => _isEditingOverview;
            set => SetField(ref _isEditingOverview, value);
        }

        // ── Computed state ────────────────────────────────────────────────────
        public bool CanAddProjectToSection => SelectedSection is not null;

        public bool HasPageBreakAfter(int projectIndex)
            => SelectedBlock?.Section?.PageBreakAfterProjectIndex.Contains(projectIndex) ?? false;

        public int TotalProjectCount =>
            Blocks.Where(static b => b.BlockType == BrochureBlockType.Section)
                  .Sum(static b => b.Section?.Projects.Count ?? 0);

        public bool HasPersonnelBlocks => Blocks.Any(static b => b.BlockType == BrochureBlockType.Personnel);
        public bool HasSections => SectionsList.Any();
        public bool HasCompanyOverview => Blocks.Any(static b => b.BlockType == BrochureBlockType.CompanyOverview);
        public bool HasContactPage => Blocks.Any(static b => b.BlockType == BrochureBlockType.Contact);
        public bool HasPreview => PreviewPages.Count > 0;
        public bool IsPreviewEmpty => PreviewPages.Count == 0;
        public bool CanGenerate => Blocks.Count > 0 && !IsGenerating;

        public string ProposalName
        {
            get => _proposalName;
            private set
            {
                if (SetField(ref _proposalName, value))
                    OnPropertyChanged(nameof(WindowTitle));
            }
        }

        public string WindowTitle => string.IsNullOrEmpty(ProposalName)
            ? "Sales Brochure Builder"
            : IsDirty
                ? $"Sales Brochure Builder  {ProposalName} *"
                : $"Sales Brochure Builder  {ProposalName}";

        // ── Collection change handlers ────────────────────────────────────────
        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressCollectionNotifications) return;
            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasPersonnelBlocks));
            OnPropertyChanged(nameof(HasSections));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            OnPropertyChanged(nameof(CanAddProjectToSection));
            OnPropertyChanged(nameof(CanGenerate));
            _estimatedPageCount = null;
            OnPropertyChanged(nameof(EstimatedPageCount));
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(SelectedProject));
            SetDirty();
        }

        private void PreviewPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(IsPreviewEmpty));
        }

        // ── Block helpers ─────────────────────────────────────────────────────
        private void RefreshBlock(BrochureBlock block)
        {
            var index = Blocks.IndexOf(block);
            if (index < 0) return;
            var savedSelectedIndex = SelectedBlockIndex;
            _suppressCollectionNotifications = true;
            Blocks.RemoveAt(index);
            Blocks.Insert(index, block);
            _suppressCollectionNotifications = false;
            // Blocks.RemoveAt causes the TwoWay-bound ListBox to push SelectedIndex = -1
            // back to SelectedBlockIndex; restore it now that the item is back in place.
            if (SelectedBlockIndex != savedSelectedIndex)
                SelectedBlockIndex = savedSelectedIndex;
            SetDirty();
            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            _estimatedPageCount = null;
            OnPropertyChanged(nameof(EstimatedPageCount));
        }

        private BrochureBlock? FindSectionBlockContaining(BrochureProject project)
            => Blocks.FirstOrDefault(b =>
                b.BlockType == BrochureBlockType.Section &&
                b.Section?.Projects.Contains(project) == true);

        private BrochureBlock? FindPersonnelBlockContaining(BrochurePerson person)
            => Blocks.FirstOrDefault(b =>
                b.BlockType == BrochureBlockType.Personnel &&
                b.People.Contains(person));

        private void NotifyProjectPageBreakStateChanged()
        {
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(SelectedProject));
            _estimatedPageCount = null;
            OnPropertyChanged(nameof(EstimatedPageCount));
        }

        private void NotifyOverviewPageBreakStateChanged()
        {
            OnPropertyChanged(nameof(SelectedBlock));
            _estimatedPageCount = null;
            OnPropertyChanged(nameof(EstimatedPageCount));
        }

        private void NotifyOverviewEditStateChanged() =>
            OnPropertyChanged(nameof(SelectedBlock));

        // ── Misc helpers ──────────────────────────────────────────────────────
        private static Window? GetOwnerWindow() =>
            Application.Current.Windows.OfType<BrochureBuilderWindow>().FirstOrDefault();

        internal void ClearProjectForm()
        {
            Project.ClearForm();
            _editingBlock = null;
        }

        // ── Nested types ──────────────────────────────────────────────────────
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;

            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
        }
    }
}
