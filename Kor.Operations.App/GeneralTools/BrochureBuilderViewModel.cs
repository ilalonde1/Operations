#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;
using Kor.Operations.GeneralTools.SubVms;
using Kor.Operations.Rendering.Brochure;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kor.Operations.GeneralTools
{
    public sealed class BrochureBuilderViewModel : ObservableObject
    {
        private readonly IBrochureRenderer _renderer;
        private readonly ILogger<BrochureBuilderViewModel> _logger;
        private readonly IBrochureProposalStore _proposalStore;
        private string? _proposalId;
        private string _proposalName = string.Empty;


        private int _currentStep = 1;
        private int _selectedBlockIndex = -1;
        private int _selectedProjectIndex = -1;
        private int _selectedOverviewIndex = -1;
        private bool _isGenerating;
        private bool _isEditingProject;
        private bool _isEditingPerson;
        private bool _isEditingOverview;
        private bool _suppressCollectionNotifications;
        private BrochureProject? _editingProject;
        private BrochureBlock? _editingBlock;
        private BrochureSection? _selectedSection;
        private BrochureBlock? _selectedSectionBlock;

        public BrochureBuilderViewModel(
            IBrochureRenderer renderer,
            ILogger<BrochureBuilderViewModel> logger,
            IBrochureProposalStore proposalStore)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
            Cover = new BrochureCoverVm();
            Project = new BrochureProjectVm();
            Person = new BrochurePersonVm();
            Overview = new BrochureOverviewVm();
            TemplateOptions = new ReadOnlyCollection<string>(new[]
            {
                "Corporate Profile",
                "Project Showcase",
                "Regional Overview"
            });
            Cover.TemplateName = TemplateOptions[0];
            Blocks.CollectionChanged += Blocks_CollectionChanged;
            PreviewPages.CollectionChanged += PreviewPages_CollectionChanged;

            AddSectionCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrWhiteSpace(Overview.SectionHeading))
                {
                    MessageBox.Show(
                        "Section Heading is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var block = new BrochureBlock
                {
                    BlockType = BrochureBlockType.Section,
                    Section = new BrochureSection
                    {
                        Heading = Overview.SectionHeading,
                        Blurb = Overview.SectionBlurb
                    }
                };

                Blocks.Add(block);
                Overview.ClearSectionForm();
                SelectedSection = block.Section;
            });

            AddPersonnelBlockCommand = new RelayCommand(_ =>
            {
                Blocks.Add(new BrochureBlock
                {
                    BlockType = BrochureBlockType.Personnel,
                    People = new List<BrochurePerson>()
                });

                Person.ClearForm();
            });

            AddCompanyOverviewCommand = new RelayCommand(_ =>
            {
                if (HasCompanyOverview)
                {
                    MessageBox.Show(
                        "A company overview block already exists",
                        "Duplicate Block",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Blocks.Insert(0, new BrochureBlock
                {
                    BlockType = BrochureBlockType.CompanyOverview,
                    OverviewSections = new List<BrochureOverviewSection>()
                });

                OnPropertyChanged(nameof(HasCompanyOverview));
                OnPropertyChanged(nameof(SectionsList));
            });

            AddPageBreakCommand = new RelayCommand(_ =>
            {
                var insertAt = SelectedBlockIndex >= 0
                    ? SelectedBlockIndex + 1
                    : Blocks.Count;

                Blocks.Insert(insertAt, new BrochureBlock { BlockType = BrochureBlockType.PageBreak });
            });

            AddContactPageCommand = new RelayCommand(_ =>
            {
                if (HasContactPage)
                {
                    MessageBox.Show(
                        "A contact page already exists",
                        "Duplicate Block",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Blocks.Add(new BrochureBlock
                {
                    BlockType = BrochureBlockType.Contact
                });

                OnPropertyChanged(nameof(HasContactPage));
            });

            AddProjectCommand = new RelayCommand(_ =>
            {
                if (IsEditingProject)
                {
                    MessageBox.Show(
                        "Finish editing the current project first - click Save Changes or Cancel Edit.",
                        "Edit In Progress",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (SelectedSection is null || _selectedSectionBlock is null)
                {
                    MessageBox.Show(
                        "Select or create a section before adding a project",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!Project.ValidateForm())
                    return;

                SelectedSection.Projects.Add(new BrochureProject
                {
                    SectionLabel = SelectedSection.Heading,
                    ProjectName = Project.ProjectName,
                    ProjectDescription = Project.ProjectDescription,
                    Client = Project.Client,
                    Architect = Project.Architect,
                    Photos = Project.Photos.ToList()
                });

                RefreshBlock(_selectedSectionBlock);
                ClearProjectForm();
            });

            EditProjectCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureProject project)
                    return;

                var block = FindSectionBlockContaining(project);
                if (block?.Section is not null)
                    SelectedSection = block.Section;

                Project.ProjectName = project.ProjectName;
                Project.ProjectDescription = project.ProjectDescription;
                Project.Client = project.Client;
                Project.Architect = project.Architect;
                Project.Photos = new ObservableCollection<BrochurePhoto>(project.Photos);
                block = Blocks
                    .Where(static b => b.BlockType == BrochureBlockType.Section)
                    .FirstOrDefault(b => b.Section is not null && b.Section.Projects.Contains(project));
                if (block?.Section is not null)
                    SelectedProjectIndex = block.Section.Projects.IndexOf(project);
                _editingBlock = block;
                _editingProject = project;
                IsEditingProject = true;
            });

            SaveEditCommand = new RelayCommand(_ =>
            {
                if (_editingProject is null)
                    return;

                if (!Project.ValidateForm())
                    return;

                var block = _editingBlock;
                if (block?.Section is null)
                    return;

                _editingProject.ProjectName = Project.ProjectName;
                _editingProject.SectionLabel = block.Section.Heading;
                _editingProject.ProjectDescription = Project.ProjectDescription;
                _editingProject.Client = Project.Client;
                _editingProject.Architect = Project.Architect;
                _editingProject.Photos = Project.Photos.ToList();

                RefreshBlock(block);
                ClearProjectForm();
                IsEditingProject = false;
                _editingProject = null;
            });

            CancelEditCommand = new RelayCommand(_ =>
            {
                ClearProjectForm();
                IsEditingProject = false;
                _editingBlock = null;
                _editingProject = null;
            });

            AddPersonToBlockCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.Personnel)
                    return;

                if (string.IsNullOrWhiteSpace(Person.PersonName))
                {
                    MessageBox.Show(
                        "Name is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                block.People.Add(new BrochurePerson
                {
                    Name = Person.PersonName,
                    Credentials = Person.PersonCredentials,
                    Bio = Person.PersonBio,
                    PhotoPath = Person.PersonPhotoPath
                });

                RefreshBlock(block);
                Person.ClearForm();
                IsEditingPerson = false;
            });

            BeginEditPersonCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochurePerson person)
                    return;

                Person.PersonName = person.Name;
                Person.PersonCredentials = person.Credentials;
                Person.PersonBio = person.Bio;
                Person.PersonPhotoPath = person.PhotoPath;

                var block = Blocks.FirstOrDefault(b =>
                    b.BlockType == BrochureBlockType.Personnel &&
                    b.People.Contains(person));
                if (block is null)
                    return;

                block.People.Remove(person);
                RefreshBlock(block);
                IsEditingPerson = true;
            });

            AddOverviewSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.CompanyOverview)
                    return;

                if (IsEditingOverview)
                    return;

                if (string.IsNullOrWhiteSpace(Overview.OverviewHeading))
                {
                    MessageBox.Show(
                        "Section Heading is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                block.OverviewSections.Add(new BrochureOverviewSection
                {
                    Heading = Overview.OverviewHeading,
                    Body = Overview.OverviewBody
                });

                RefreshBlock(block);
                Overview.ClearOverviewForm();
            });

            BeginEditOverviewCommand = new RelayCommand(parameter =>
            {
                if (parameter is not int overviewIndex)
                    return;

                if (SelectedBlock is not { BlockType: BrochureBlockType.CompanyOverview } block)
                    return;

                if (overviewIndex < 0 || overviewIndex >= block.OverviewSections.Count)
                    return;

                var overviewSection = block.OverviewSections[overviewIndex];
                SelectedOverviewIndex = overviewIndex;
                Overview.OverviewHeading = overviewSection.Heading;
                Overview.OverviewBody = overviewSection.Body;
                IsEditingOverview = true;
            });

            SaveOverviewEditCommand = new RelayCommand(_ =>
            {
                if (!IsEditingOverview || SelectedOverviewIndex < 0)
                    return;

                if (SelectedBlock is not { BlockType: BrochureBlockType.CompanyOverview } block)
                    return;

                if (SelectedOverviewIndex >= block.OverviewSections.Count)
                    return;

                if (string.IsNullOrWhiteSpace(Overview.OverviewHeading))
                {
                    MessageBox.Show(
                        "Section Heading is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var overviewSection = block.OverviewSections[SelectedOverviewIndex];
                overviewSection.Heading = Overview.OverviewHeading;
                overviewSection.Body = Overview.OverviewBody;

                SelectedOverviewIndex = -1;
                IsEditingOverview = false;
                Overview.ClearOverviewForm();
                NotifyOverviewEditStateChanged();
            });

            CancelOverviewEditCommand = new RelayCommand(_ =>
            {
                SelectedOverviewIndex = -1;
                IsEditingOverview = false;
                Overview.ClearOverviewForm();
            });

            RemoveOverviewSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureOverviewSection overviewSection)
                    return;

                var block = Blocks.FirstOrDefault(candidate =>
                    candidate.BlockType == BrochureBlockType.CompanyOverview &&
                    candidate.OverviewSections.Contains(overviewSection));
                if (block is null)
                    return;

                block.OverviewSections.Remove(overviewSection);
                RefreshBlock(block);
            });

            RemovePersonCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochurePerson person)
                    return;

                var block = FindPersonnelBlockContaining(person);
                if (block is null)
                    return;

                block.People.Remove(person);
                RefreshBlock(block);
            });

            RemoveProjectFromSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureProject project)
                    return;

                var block = FindSectionBlockContaining(project);
                if (block?.Section is null)
                    return;

                block.Section.Projects.Remove(project);
                RefreshBlock(block);
            });

            RemoveBlockCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureBlock block)
                    return;

                Blocks.Remove(block);

                if (block.BlockType == BrochureBlockType.Section && ReferenceEquals(SelectedSection, block.Section))
                {
                    SelectedSection = SectionsList.FirstOrDefault();
                }
            });

            MoveBlockCommand = new RelayCommand(parameter =>
            {
                if (parameter is not Tuple<int, int> moveRequest)
                    return;

                var fromIndex = moveRequest.Item1;
                var toIndex = moveRequest.Item2;

                if (fromIndex < 0 || fromIndex >= Blocks.Count || toIndex < 0 || toIndex >= Blocks.Count)
                    return;

                var item = Blocks[fromIndex];
                Blocks.RemoveAt(fromIndex);
                Blocks.Insert(toIndex, item);

                OnPropertyChanged(nameof(SectionsList));
                OnPropertyChanged(nameof(HasCompanyOverview));
                OnPropertyChanged(nameof(HasContactPage));
            });

            MoveProjectCommand = new RelayCommand(parameter =>
            {
                if (parameter is not Tuple<int, int> moveRequest)
                    return;

                if (SelectedBlock?.Section is not { } section)
                    return;

                var fromIndex = moveRequest.Item1;
                var toIndex = moveRequest.Item2;

                if (fromIndex < 0 || fromIndex >= section.Projects.Count || toIndex < 0 || toIndex >= section.Projects.Count)
                    return;

                var hadBreakAfterMovedProject = section.PageBreakAfterProjectIndex.Contains(fromIndex);
                section.PageBreakAfterProjectIndex.Remove(fromIndex);

                for (var i = 0; i < section.PageBreakAfterProjectIndex.Count; i++)
                {
                    var breakIndex = section.PageBreakAfterProjectIndex[i];
                    if (breakIndex > fromIndex)
                        breakIndex--;

                    if (breakIndex >= toIndex)
                        breakIndex++;

                    section.PageBreakAfterProjectIndex[i] = breakIndex;
                }

                if (hadBreakAfterMovedProject)
                    section.PageBreakAfterProjectIndex.Add(toIndex - 1);

                section.PageBreakAfterProjectIndex.Sort();

                var project = section.Projects[fromIndex];
                section.Projects.RemoveAt(fromIndex);
                section.Projects.Insert(toIndex, project);

                RefreshBlock(SelectedBlock);
            });

            MovePersonCommand = new RelayCommand(parameter =>
            {
                if (parameter is not Tuple<int, int> moveRequest)
                    return;

                if (SelectedBlock is not { } block)
                    return;

                var fromIndex = moveRequest.Item1;
                var toIndex = moveRequest.Item2;

                if (fromIndex < 0 || fromIndex >= block.People.Count || toIndex < 0 || toIndex >= block.People.Count)
                    return;

                var person = block.People[fromIndex];
                block.People.RemoveAt(fromIndex);
                block.People.Insert(toIndex, person);

                RefreshBlock(block);
            });

            InsertProjectPageBreakCommand = new RelayCommand(parameter =>
            {
                if (parameter is not int projectIndex)
                    return;

                if (SelectedBlock?.Section is not { } section)
                    return;

                if (section.PageBreakAfterProjectIndex.Contains(projectIndex))
                    section.PageBreakAfterProjectIndex.Remove(projectIndex);
                else
                    section.PageBreakAfterProjectIndex.Add(projectIndex);

                section.PageBreakAfterProjectIndex.Sort();
                NotifyProjectPageBreakStateChanged();
            });

            MoveOverviewSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not Tuple<int, int> moveRequest)
                    return;

                if (SelectedBlock is not { } block)
                    return;

                var fromIndex = moveRequest.Item1;
                var toIndex = moveRequest.Item2;

                if (fromIndex < 0 || fromIndex >= block.OverviewSections.Count || toIndex < 0 || toIndex >= block.OverviewSections.Count)
                    return;

                var hadBreakAfterMovedSection = block.PageBreakAfterOverviewIndex.Contains(fromIndex);
                block.PageBreakAfterOverviewIndex.Remove(fromIndex);

                for (var i = 0; i < block.PageBreakAfterOverviewIndex.Count; i++)
                {
                    var breakIndex = block.PageBreakAfterOverviewIndex[i];
                    if (breakIndex > fromIndex)
                        breakIndex--;

                    if (breakIndex >= toIndex)
                        breakIndex++;

                    block.PageBreakAfterOverviewIndex[i] = breakIndex;
                }

                if (hadBreakAfterMovedSection)
                    block.PageBreakAfterOverviewIndex.Add(toIndex - 1);

                block.PageBreakAfterOverviewIndex.Sort();

                var overviewSection = block.OverviewSections[fromIndex];
                block.OverviewSections.RemoveAt(fromIndex);
                block.OverviewSections.Insert(toIndex, overviewSection);

                RefreshBlock(block);
            });

            InsertOverviewPageBreakCommand = new RelayCommand(parameter =>
            {
                if (parameter is not int overviewIndex)
                    return;

                if (SelectedBlock is not { } block)
                    return;

                if (block.PageBreakAfterOverviewIndex.Contains(overviewIndex))
                    block.PageBreakAfterOverviewIndex.Remove(overviewIndex);
                else
                    block.PageBreakAfterOverviewIndex.Add(overviewIndex);

                block.PageBreakAfterOverviewIndex.Sort();
                NotifyOverviewPageBreakStateChanged();
            });

            RemovePhotoCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochurePhoto photo)
                    return;

                Project.Photos.Remove(photo);
            });

            PickCoverPhotoCommand = new RelayCommand(_ =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                    Cover.CoverPhotoPath = dialog.FileName;
            });

            PickPersonPhotoCommand = new RelayCommand(_ =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                    Person.PersonPhotoPath = dialog.FileName;
            });

            ClearCoverPhotoCommand = new RelayCommand(_ =>
            {
                Cover.CoverPhotoPath = string.Empty;
            });

            SaveProposalCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrEmpty(ProposalName))
                {
                    var nameDialog = new BrochureProposalNameDialog(ProposalName)
                    {
                        Owner = GetOwnerWindow()
                    };
                    if (nameDialog.ShowDialog() != true)
                        return;
                    ProposalName = nameDialog.ProposalName;
                }

                _proposalId ??= Guid.NewGuid().ToString("N");

                _proposalStore.Save(new BrochureProposal
                {
                    Id = _proposalId,
                    Name = ProposalName,
                    Content = BuildBrochureContent()
                });

                MessageBox.Show(
                    $"\"{ProposalName}\" saved.",
                    "Proposal Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            SaveProposalAsCommand = new RelayCommand(_ =>
            {
                var nameDialog = new BrochureProposalNameDialog(ProposalName)
                {
                    Owner = GetOwnerWindow()
                };
                if (nameDialog.ShowDialog() != true)
                    return;

                ProposalName = nameDialog.ProposalName;
                _proposalId = Guid.NewGuid().ToString("N");

                _proposalStore.Save(new BrochureProposal
                {
                    Id = _proposalId,
                    Name = ProposalName,
                    Content = BuildBrochureContent()
                });

                MessageBox.Show(
                    $"\"{ProposalName}\" saved.",
                    "Proposal Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            LoadProposalCommand = new RelayCommand(_ =>
            {
                var picker = new BrochureProposalPickerWindow(_proposalStore)
                {
                    Owner = GetOwnerWindow()
                };
                if (picker.ShowDialog() != true || picker.SelectedProposal is null)
                    return;

                LoadFromProposal(picker.SelectedProposal, picker.IsClone);
            });

            ProduceBrochureCommand = new AsyncRelayCommand(async _ =>
            {
                if (Blocks.Count == 0)
                {
                    MessageBox.Show(
                        "Add at least one section or personnel block",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (Blocks.Any(static block => block.BlockType == BrochureBlockType.Section && (block.Section?.Projects.Count ?? 0) == 0))
                {
                    MessageBox.Show(
                        "All sections must have at least one project",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var sanitizedProjectName = SanitizeFileName(Project.ProjectName);
                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Brochure As",
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = sanitizedProjectName + " - Brochure.pdf"
                };

                if (saveDialog.ShowDialog() != true)
                    return;

                var outputPath = saveDialog.FileName;

                try
                {
                    IsGenerating = true;

                    var content = BuildBrochureContent();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var (pdfPath, previewPages) = await _renderer.RenderWithPreviewAsync(
                        content,
                        outputPath,
                        280,
                        cts.Token);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = pdfPath,
                        UseShellExecute = true
                    });

                    try
                    {
                        PreviewPages.Clear();

                        foreach (var pageBytes in previewPages)
                        {
                            using var stream = new MemoryStream(pageBytes);
                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = stream;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();
                            PreviewPages.Add(bitmapImage);
                        }

                        OnPropertyChanged(nameof(HasPreview));
                        OnPropertyChanged(nameof(IsPreviewEmpty));
                    }
                    catch (Exception previewEx)
                    {
                        _logger.LogError(previewEx, "Brochure preview generation failed");
                    }

                    MessageBox.Show(
                        "Brochure generated successfully.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Brochure generation failed");
                    MessageBox.Show(
                        "Failed to generate brochure. Check the log for details.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    IsGenerating = false;
                }
            });
        }

        public BrochureCoverVm Cover { get; }

        public BrochureProjectVm Project { get; }

        public BrochurePersonVm Person { get; }

        public BrochureOverviewVm Overview { get; }

        public ReadOnlyCollection<string> TemplateOptions { get; }


        public ObservableCollection<BrochureBlock> Blocks { get; } = new();

        public ObservableCollection<BitmapSource> PreviewPages { get; } = new();

        public IEnumerable<BrochureSection> SectionsList =>
            Blocks.Where(static block => block.BlockType == BrochureBlockType.Section && block.Section is not null)
                .Select(static block => block.Section!);

        public BrochureSection? SelectedSection
        {
            get => _selectedSection;
            set
            {
                _selectedSection = value;
                _selectedSectionBlock = Blocks.FirstOrDefault(block => ReferenceEquals(block.Section, value));
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddProjectToSection));
                OnPropertyChanged(nameof(SelectedBlock));
            }
        }


        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (!SetField(ref _currentStep, value))
                    return;

                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsOnFinalStep));
            }
        }

        public int SelectedBlockIndex
        {
            get => _selectedBlockIndex;
            set
            {
                if (!SetField(ref _selectedBlockIndex, value))
                    return;

                OnPropertyChanged(nameof(SelectedBlock));
            }
        }

        public BrochureBlock? SelectedBlock =>
            SelectedBlockIndex >= 0 && SelectedBlockIndex < Blocks.Count
                ? Blocks[SelectedBlockIndex]
                : null;

        public int SelectedProjectIndex
        {
            get => _selectedProjectIndex;
            set
            {
                if (!SetField(ref _selectedProjectIndex, value))
                    return;

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

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (!SetField(ref _isGenerating, value))
                    return;

                OnPropertyChanged(nameof(CanProduce));
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

        public bool CanAddProjectToSection => SelectedSection is not null;

        public bool HasPageBreakAfter(int projectIndex)
            => SelectedBlock?.Section?.PageBreakAfterProjectIndex.Contains(projectIndex) ?? false;

        public int TotalProjectCount =>
            Blocks.Where(static block => block.BlockType == BrochureBlockType.Section)
                .Sum(static block => block.Section?.Projects.Count ?? 0);

        public bool HasPersonnelBlocks => Blocks.Any(static block => block.BlockType == BrochureBlockType.Personnel);

        public bool HasSections => SectionsList.Any();

        public bool HasCompanyOverview => Blocks.Any(static block => block.BlockType == BrochureBlockType.CompanyOverview);

        public bool HasContactPage => Blocks.Any(static block => block.BlockType == BrochureBlockType.Contact);

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
            : $"Sales Brochure Builder — {ProposalName}";

        public bool HasPreview => PreviewPages.Count > 0;

        public bool IsPreviewEmpty => PreviewPages.Count == 0;

        public bool CanGoBack => CurrentStep > 1;

        public bool CanGoNext => CurrentStep < 4;

        public bool IsOnFinalStep => CurrentStep == 4;

        public int EstimatedPageCount
        {
            get
            {
                var pageCount = 1;

                foreach (var block in Blocks)
                {
                    pageCount += block.BlockType switch
                    {
                        BrochureBlockType.Section => block.Section is null
                            ? 0
                            : (int)Math.Ceiling(block.Section.Projects.Count / 2d) + 1,
                        BrochureBlockType.Personnel => (int)Math.Ceiling(block.People.Count / 2d),
                        BrochureBlockType.CompanyOverview => (int)Math.Ceiling(block.OverviewSections.Count / 2d),
                        BrochureBlockType.Contact => 1,
                        BrochureBlockType.PageBreak => 0,
                        _ => 0
                    };
                }

                return Math.Max(1, pageCount);
            }
        }

        public ICommand AddSectionCommand { get; }

        public ICommand AddPersonnelBlockCommand { get; }

        public ICommand AddCompanyOverviewCommand { get; }

        public ICommand AddContactPageCommand { get; }

        public ICommand AddPageBreakCommand { get; }

        public ICommand AddPersonToBlockCommand { get; }

        public ICommand BeginEditPersonCommand { get; }

        public ICommand AddOverviewSectionCommand { get; }

        public ICommand BeginEditOverviewCommand { get; }

        public ICommand SaveOverviewEditCommand { get; }

        public ICommand CancelOverviewEditCommand { get; }

        public ICommand RemoveOverviewSectionCommand { get; }

        public ICommand RemovePersonCommand { get; }

        public ICommand RemoveBlockCommand { get; }

        public ICommand MoveBlockCommand { get; }

        public ICommand MoveProjectCommand { get; }

        public ICommand MovePersonCommand { get; }

        public ICommand InsertProjectPageBreakCommand { get; }

        public ICommand MoveOverviewSectionCommand { get; }

        public ICommand InsertOverviewPageBreakCommand { get; }

        public ICommand NextStepCommand => new RelayCommand(
            _ => CurrentStep++,
            _ => CanGoNext);

        public ICommand PrevStepCommand => new RelayCommand(
            _ => CurrentStep--,
            _ => CanGoBack);

        public ICommand AddProjectCommand { get; }

        public ICommand EditProjectCommand { get; }

        public ICommand SaveEditCommand { get; }

        public ICommand CancelEditCommand { get; }

        public ICommand RemoveProjectFromSectionCommand { get; }

        public ICommand RemovePhotoCommand { get; }

        public ICommand PickCoverPhotoCommand { get; }

        public ICommand PickPersonPhotoCommand { get; }

        public ICommand ClearCoverPhotoCommand { get; }

        public ICommand ProduceBrochureCommand { get; }
        public ICommand SaveProposalCommand { get; }
        public ICommand SaveProposalAsCommand { get; }
        public ICommand LoadProposalCommand { get; }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Brochure" : sanitized;
        }


        private void ClearProjectForm()
        {
            Project.ClearForm();
            _editingBlock = null;
        }

        public void ClearProjectFormPublic() => ClearProjectForm();


        private BrochureBlock? FindSectionBlockContaining(BrochureProject project)
            => Blocks.FirstOrDefault(block =>
                block.BlockType == BrochureBlockType.Section &&
                block.Section?.Projects.Contains(project) == true);

        private BrochureBlock? FindPersonnelBlockContaining(BrochurePerson person)
            => Blocks.FirstOrDefault(block =>
                block.BlockType == BrochureBlockType.Personnel &&
                block.People.Contains(person));

        private void RefreshBlock(BrochureBlock block)
        {
            var index = Blocks.IndexOf(block);
            if (index < 0)
                return;

            _suppressCollectionNotifications = true;
            Blocks.RemoveAt(index);
            Blocks.Insert(index, block);
            _suppressCollectionNotifications = false;

            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            OnPropertyChanged(nameof(EstimatedPageCount));
            OnPropertyChanged(nameof(Blocks));
        }

        private void NotifyProjectPageBreakStateChanged()
        {
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(SelectedProject));
            OnPropertyChanged(nameof(EstimatedPageCount));
        }

        private void NotifyOverviewPageBreakStateChanged()
        {
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(EstimatedPageCount));
        }

        private void NotifyOverviewEditStateChanged()
        {
            OnPropertyChanged(nameof(SelectedBlock));
        }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressCollectionNotifications)
                return;

            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasPersonnelBlocks));
            OnPropertyChanged(nameof(HasSections));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            OnPropertyChanged(nameof(CanAddProjectToSection));
            OnPropertyChanged(nameof(EstimatedPageCount));
            OnPropertyChanged(nameof(SelectedBlock));
            OnPropertyChanged(nameof(SelectedProject));
        }

        private void PreviewPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(IsPreviewEmpty));
        }

        private BrochureContent BuildBrochureContent() => new()
        {
            TemplateName = Cover.TemplateName,
            CoverTitle = Cover.CoverTitle,
            CoverPhotoPath = Cover.CoverPhotoPath,
            CoverPhotoOpacity = Cover.CoverPhotoOpacity,
            CoverYear = Cover.CoverYear,
            Blocks = Blocks.Select(block => new BrochureBlock
            {
                BlockType = block.BlockType,
                Section = block.BlockType == BrochureBlockType.Section && block.Section is not null
                    ? new BrochureSection
                    {
                        Heading = block.Section.Heading,
                        Blurb = block.Section.Blurb,
                        Projects = block.Section.Projects.ToList(),
                        PageBreakAfterProjectIndex = block.Section.PageBreakAfterProjectIndex.ToList()
                    }
                    : null,
                People = block.People.ToList(),
                OverviewSections = block.OverviewSections.Select(static s => new BrochureOverviewSection
                {
                    Heading = s.Heading,
                    Body = s.Body
                }).ToList(),
                PageBreakAfterOverviewIndex = block.PageBreakAfterOverviewIndex.ToList()
            }).ToList()
        };

        private void LoadFromProposal(BrochureProposal proposal, bool asClone)
        {
            var content = proposal.Content;

            ClearProjectForm();
            Person.ClearForm();
            Overview.ClearSectionForm();
            Overview.ClearOverviewForm();
            SelectedBlockIndex = -1;
            SelectedProjectIndex = -1;
            SelectedOverviewIndex = -1;
            IsEditingOverview = false;
            PreviewPages.Clear();

            Blocks.Clear();
            _selectedSection = null;
            _selectedSectionBlock = null;
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(CanAddProjectToSection));

            Cover.TemplateName = string.IsNullOrEmpty(content.TemplateName) ? TemplateOptions[0] : content.TemplateName;
            Cover.CoverTitle = content.CoverTitle;
            Cover.CoverPhotoPath = content.CoverPhotoPath;
            Cover.CoverPhotoOpacity = content.CoverPhotoOpacity;
            Cover.CoverYear = content.CoverYear;

            foreach (var block in content.Blocks)
                Blocks.Add(block);

            _proposalId = asClone ? null : proposal.Id;
            ProposalName = asClone ? proposal.Name + " (Copy)" : proposal.Name;

            CurrentStep = 1;
        }

        private static Window? GetOwnerWindow() =>
            Application.Current.Windows.OfType<BrochureBuilderWindow>().FirstOrDefault();

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
