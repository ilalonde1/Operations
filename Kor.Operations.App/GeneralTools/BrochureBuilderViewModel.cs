#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private string _sectionHeading = string.Empty;
        private string _sectionBlurb = string.Empty;
        private string _sectionLabel = string.Empty;
        private string _projectName = string.Empty;
        private string _projectDescription = string.Empty;
        private string _client = string.Empty;
        private string _architect = string.Empty;
        private ObservableCollection<BrochurePhoto> _photos = new();
        private bool _isGenerating;
        private bool _isEditingProject;
        private BrochureProject? _editingProject;
        private BrochureSection? _selectedSection;

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

                var section = new BrochureSection
                {
                    Heading = SectionHeading,
                    Blurb = SectionBlurb
                };

                Sections.Add(section);
                SectionHeading = string.Empty;
                SectionBlurb = string.Empty;
                SelectedSection = section;
                OnPropertyChanged(nameof(TotalProjectCount));
            });

            RemoveSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureSection section)
                    return;

                Sections.Remove(section);
                if (ReferenceEquals(SelectedSection, section))
                    SelectedSection = Sections.FirstOrDefault();

                OnPropertyChanged(nameof(TotalProjectCount));
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

                if (SelectedSection is null)
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

                RefreshSection(SelectedSection);
                ClearProjectForm();
            });

            EditProjectCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureProject project)
                    return;

                ProjectName = project.ProjectName;
                SectionLabel = project.SectionLabel;
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

                var section = FindContainingSection(_editingProject);
                _editingProject.ProjectName = ProjectName;
                _editingProject.SectionLabel = section?.Heading ?? _editingProject.SectionLabel;
                _editingProject.ProjectDescription = ProjectDescription;
                _editingProject.Client = Client;
                _editingProject.Architect = Architect;
                _editingProject.Photos = Photos.ToList();

                if (section is not null)
                    RefreshSection(section);

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

            RemoveProjectFromSectionCommand = new RelayCommand(parameter =>
            {
                if (parameter is not BrochureProject project)
                    return;

                var section = FindContainingSection(project);
                if (section is null)
                    return;

                section.Projects.Remove(project);
                RefreshSection(section);
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

            ClearCoverPhotoCommand = new RelayCommand(_ =>
            {
                CoverPhotoPath = string.Empty;
            });

            ProduceBrochureCommand = new RelayCommand(async _ =>
            {
                if (Sections.Count == 0)
                {
                    MessageBox.Show(
                        "Add at least one section before generating",
                        "Missing Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (Sections.Any(static section => section.Projects.Count == 0))
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
                        Sections = Sections.Select(section => new BrochureSection
                        {
                            Heading = section.Heading,
                            Blurb = section.Blurb,
                            Projects = section.Projects.ToList()
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

        public string SectionLabel
        {
            get => _sectionLabel;
            set => SetField(ref _sectionLabel, value);
        }

        public string ProjectName
        {
            get => _projectName;
            set => SetField(ref _projectName, value);
        }

        public ObservableCollection<BrochureSection> Sections { get; } = new();

        public BrochureSection? SelectedSection
        {
            get => _selectedSection;
            set
            {
                _selectedSection = value;
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

        // Kept only for existing code-behind compatibility in this pass.
        public ObservableCollection<BrochureStat> Stats { get; } = new();

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

        public int TotalProjectCount => Sections.Sum(static section => section.Projects.Count);

        public ICommand AddSectionCommand { get; }

        public ICommand RemoveSectionCommand { get; }

        public ICommand AddProjectCommand { get; }

        public ICommand EditProjectCommand { get; }

        public ICommand SaveEditCommand { get; }

        public ICommand CancelEditCommand { get; }

        public ICommand RemoveProjectFromSectionCommand { get; }

        public ICommand RemovePhotoCommand { get; }

        public ICommand PickCoverPhotoCommand { get; }

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
            SectionLabel = string.Empty;
            ProjectDescription = string.Empty;
            Client = string.Empty;
            Architect = string.Empty;
            Photos = new ObservableCollection<BrochurePhoto>();
        }

        private BrochureSection? FindContainingSection(BrochureProject project)
            => Sections.FirstOrDefault(section => section.Projects.Contains(project));

        private void RefreshSection(BrochureSection section)
        {
            var index = Sections.IndexOf(section);
            if (index >= 0)
            {
                Sections.RemoveAt(index);
                Sections.Insert(index, section);
                SelectedSection = section;
            }

            OnPropertyChanged(nameof(TotalProjectCount));
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
