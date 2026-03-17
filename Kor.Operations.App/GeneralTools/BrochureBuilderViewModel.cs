#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kor.Operations.GeneralTools
{
    public sealed class BrochureBuilderViewModel : INotifyPropertyChanged
    {
        private readonly IBrochureRenderer _renderer;
        private readonly ILogger<BrochureBuilderViewModel> _logger;

        private string _templateName;
        private string _coverTitle = string.Empty;
        private string _coverPhotoPath = string.Empty;
        private float _coverPhotoOpacity = 0.85f;
        private string _sectionHeading = string.Empty;
        private string _sectionBlurb = string.Empty;
        private string _projectName = string.Empty;
        private string _projectDescription = string.Empty;
        private string _client = string.Empty;
        private string _architect = string.Empty;
        private ObservableCollection<BrochurePhoto> _photos = new();
        private string _personName = string.Empty;
        private string _personCredentials = string.Empty;
        private string _personBio = string.Empty;
        private string _personPhotoPath = string.Empty;
        private string _overviewHeading = string.Empty;
        private string _overviewBody = string.Empty;
        private bool _isGenerating;
        private bool _isEditingProject;
        private BrochureProject? _editingProject;
        private BrochureSection? _selectedSection;
        private BrochureBlock? _selectedSectionBlock;

        public BrochureBuilderViewModel(
            IBrochureRenderer renderer,
            ILogger<BrochureBuilderViewModel> logger)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            TemplateOptions = new ReadOnlyCollection<string>(new[]
            {
                "Corporate Profile",
                "Project Showcase",
                "Regional Overview"
            });
            _templateName = TemplateOptions[0];
            Blocks.CollectionChanged += Blocks_CollectionChanged;

            AddSectionCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrWhiteSpace(SectionHeading))
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
                        Heading = SectionHeading,
                        Blurb = SectionBlurb
                    }
                };

                Blocks.Add(block);
                SectionHeading = string.Empty;
                SectionBlurb = string.Empty;
                SelectedSection = block.Section;
            });

            AddPersonnelBlockCommand = new RelayCommand(_ =>
            {
                Blocks.Add(new BrochureBlock
                {
                    BlockType = BrochureBlockType.Personnel,
                    People = new List<BrochurePerson>()
                });

                ClearPersonForm();
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

                if (string.IsNullOrWhiteSpace(ProjectName))
                {
                    MessageBox.Show(
                        "Project Name is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(ProjectDescription))
                {
                    MessageBox.Show(
                        "Project Description is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SelectedSection.Projects.Add(new BrochureProject
                {
                    SectionLabel = SelectedSection.Heading,
                    ProjectName = ProjectName,
                    ProjectDescription = ProjectDescription,
                    Client = Client,
                    Architect = Architect,
                    Photos = Photos.ToList()
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

                ProjectName = project.ProjectName;
                ProjectDescription = project.ProjectDescription;
                Client = project.Client;
                Architect = project.Architect;
                Photos = new ObservableCollection<BrochurePhoto>(project.Photos);
                _editingProject = project;
                IsEditingProject = true;
            });

            SaveEditCommand = new RelayCommand(_ =>
            {
                if (_editingProject is null)
                    return;

                if (string.IsNullOrWhiteSpace(ProjectName))
                {
                    MessageBox.Show(
                        "Project Name is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(ProjectDescription))
                {
                    MessageBox.Show(
                        "Project Description is required.",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var block = Blocks
                    .Where(static block => block.BlockType == BrochureBlockType.Section)
                    .FirstOrDefault(block => block.Section?.Projects.Contains(_editingProject) == true);
                if (block?.Section is null)
                    return;

                _editingProject.ProjectName = ProjectName;
                _editingProject.SectionLabel = block.Section.Heading;
                _editingProject.ProjectDescription = ProjectDescription;
                _editingProject.Client = Client;
                _editingProject.Architect = Architect;
                _editingProject.Photos = Photos.ToList();

                RefreshBlock(block);
                ClearProjectForm();
                IsEditingProject = false;
                _editingProject = null;
            });

            CancelEditCommand = new RelayCommand(_ =>
            {
                ClearProjectForm();
                IsEditingProject = false;
                _editingProject = null;
            });

            AddPersonToBlockCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.Personnel)
                    return;

                if (string.IsNullOrWhiteSpace(PersonName))
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
                    Name = PersonName,
                    Credentials = PersonCredentials,
                    Bio = PersonBio,
                    PhotoPath = PersonPhotoPath
                });

                RefreshBlock(block);
                ClearPersonForm();
            });

            AddOverviewSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.CompanyOverview)
                    return;

                if (string.IsNullOrWhiteSpace(OverviewHeading))
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
                    Heading = OverviewHeading,
                    Body = OverviewBody
                });

                RefreshBlock(block);
                OverviewHeading = string.Empty;
                OverviewBody = string.Empty;
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

            RemovePhotoCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochurePhoto photo)
                    return;

                Photos.Remove(photo);
            });

            PickCoverPhotoCommand = new RelayCommand(_ =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                    CoverPhotoPath = dialog.FileName;
            });

            PickPersonPhotoCommand = new RelayCommand(_ =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                    PersonPhotoPath = dialog.FileName;
            });

            ClearCoverPhotoCommand = new RelayCommand(_ =>
            {
                CoverPhotoPath = string.Empty;
            });

            ProduceBrochureCommand = new RelayCommand(async _ =>
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

                var sanitizedProjectName = SanitizeFileName(ProjectName);
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

                    var content = new BrochureContent
                    {
                        TemplateName = TemplateName,
                        CoverTitle = CoverTitle,
                        CoverPhotoPath = CoverPhotoPath,
                        CoverPhotoOpacity = CoverPhotoOpacity,
                        Blocks = Blocks.Select(block => new BrochureBlock
                        {
                            BlockType = block.BlockType,
                            Section = block.BlockType == BrochureBlockType.Section && block.Section is not null
                                ? new BrochureSection
                                {
                                    Heading = block.Section.Heading,
                                    Blurb = block.Section.Blurb,
                                    Projects = block.Section.Projects.ToList()
                                }
                                : null,
                            People = block.People.ToList(),
                            OverviewSections = block.OverviewSections.Select(section => new BrochureOverviewSection
                            {
                                Heading = section.Heading,
                                Body = section.Body
                            }).ToList()
                        }).ToList()
                    };

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await Task.Run(async () => await _renderer.RenderAsync(content, outputPath, cts.Token));

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outputPath,
                        UseShellExecute = true
                    });

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

        public event PropertyChangedEventHandler? PropertyChanged;

        public ReadOnlyCollection<string> TemplateOptions { get; }

        public string TemplateName
        {
            get => _templateName;
            set => SetField(ref _templateName, value);
        }

        public string CoverTitle
        {
            get => _coverTitle;
            set => SetField(ref _coverTitle, value);
        }

        public string CoverPhotoPath
        {
            get => _coverPhotoPath;
            set => SetField(ref _coverPhotoPath, value);
        }

        public float CoverPhotoOpacity
        {
            get => _coverPhotoOpacity;
            set => SetField(ref _coverPhotoOpacity, value);
        }

        public string SectionHeading
        {
            get => _sectionHeading;
            set => SetField(ref _sectionHeading, value);
        }

        public string SectionBlurb
        {
            get => _sectionBlurb;
            set => SetField(ref _sectionBlurb, value);
        }

        public string ProjectName
        {
            get => _projectName;
            set => SetField(ref _projectName, value);
        }

        public ObservableCollection<BrochureBlock> Blocks { get; } = new();

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
            }
        }

        public ObservableCollection<BrochurePhoto> Photos
        {
            get => _photos;
            set => SetField(ref _photos, value);
        }

        public string ProjectDescription
        {
            get => _projectDescription;
            set => SetField(ref _projectDescription, value);
        }

        public string Client
        {
            get => _client;
            set => SetField(ref _client, value);
        }

        public string Architect
        {
            get => _architect;
            set => SetField(ref _architect, value);
        }

        public string PersonName
        {
            get => _personName;
            set => SetField(ref _personName, value);
        }

        public string PersonCredentials
        {
            get => _personCredentials;
            set => SetField(ref _personCredentials, value);
        }

        public string PersonBio
        {
            get => _personBio;
            set => SetField(ref _personBio, value);
        }

        public string PersonPhotoPath
        {
            get => _personPhotoPath;
            set => SetField(ref _personPhotoPath, value);
        }

        public string OverviewHeading
        {
            get => _overviewHeading;
            set => SetField(ref _overviewHeading, value);
        }

        public string OverviewBody
        {
            get => _overviewBody;
            set => SetField(ref _overviewBody, value);
        }

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

        public bool CanAddProjectToSection => SelectedSection is not null;

        public int TotalProjectCount =>
            Blocks.Where(static block => block.BlockType == BrochureBlockType.Section)
                .Sum(static block => block.Section?.Projects.Count ?? 0);

        public bool HasPersonnelBlocks => Blocks.Any(static block => block.BlockType == BrochureBlockType.Personnel);

        public bool HasSections => SectionsList.Any();

        public bool HasCompanyOverview => Blocks.Any(static block => block.BlockType == BrochureBlockType.CompanyOverview);

        public bool HasContactPage => Blocks.Any(static block => block.BlockType == BrochureBlockType.Contact);

        public ICommand AddSectionCommand { get; }

        public ICommand AddPersonnelBlockCommand { get; }

        public ICommand AddCompanyOverviewCommand { get; }

        public ICommand AddContactPageCommand { get; }

        public ICommand AddPersonToBlockCommand { get; }

        public ICommand AddOverviewSectionCommand { get; }

        public ICommand RemoveOverviewSectionCommand { get; }

        public ICommand RemovePersonCommand { get; }

        public ICommand RemoveBlockCommand { get; }

        public ICommand MoveBlockCommand { get; }

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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Brochure" : sanitized;
        }

        private void ClearProjectForm()
        {
            ProjectName = string.Empty;
            ProjectDescription = string.Empty;
            Client = string.Empty;
            Architect = string.Empty;
            Photos = new ObservableCollection<BrochurePhoto>();
        }

        private void ClearPersonForm()
        {
            PersonName = string.Empty;
            PersonCredentials = string.Empty;
            PersonBio = string.Empty;
            PersonPhotoPath = string.Empty;
        }

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
            if (index >= 0)
            {
                Blocks.RemoveAt(index);
                Blocks.Insert(index, block);
            }

            if (block.BlockType == BrochureBlockType.Section && block.Section is not null)
                SelectedSection = block.Section;

            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasPersonnelBlocks));
            OnPropertyChanged(nameof(HasSections));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
        }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(TotalProjectCount));
            OnPropertyChanged(nameof(HasPersonnelBlocks));
            OnPropertyChanged(nameof(HasSections));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            OnPropertyChanged(nameof(CanAddProjectToSection));
        }

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
