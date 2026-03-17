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
        private string _sectionLabel = string.Empty;
        private string _projectName = string.Empty;
        private string _projectLocation = string.Empty;
        private string _projectDescription = string.Empty;
        private string _notes = string.Empty;
        private bool _isGenerating;
        private BrochureProject? _selectedProject;

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
            AddProjectCommand = new RelayCommand(_ =>
            {
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

                Projects.Add(new BrochureProject
                {
                    SectionLabel = SectionLabel,
                    ProjectName = ProjectName,
                    ProjectLocation = ProjectLocation,
                    ProjectDescription = ProjectDescription,
                    Photos = Photos.ToList(),
                    Stats = Stats.ToList(),
                    Notes = Notes
                });

                SectionLabel = string.Empty;
                ProjectName = string.Empty;
                ProjectLocation = string.Empty;
                ProjectDescription = string.Empty;
                Photos.Clear();
                Stats.Clear();
                Notes = string.Empty;
            });
            RemoveProjectCommand = new RelayCommand(_ =>
            {
                if (SelectedProject is null)
                    return;

                Projects.Remove(SelectedProject);
                SelectedProject = null;
            }, _ => SelectedProject is not null);
            ProduceBrochureCommand = new RelayCommand(async _ =>
            {
                if (Projects.Count == 0)
                {
                    MessageBox.Show(
                        "Add at least one project before generating",
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
                        Projects = Projects.ToList()
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

        public string ProjectLocation
        {
            get => _projectLocation;
            set => SetField(ref _projectLocation, value);
        }

        public ObservableCollection<BrochureProject> Projects { get; } = new();

        public BrochureProject? SelectedProject
        {
            get => _selectedProject;
            set
            {
                _selectedProject = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ObservableCollection<BrochurePhoto> Photos { get; } = new();

        public string ProjectDescription
        {
            get => _projectDescription;
            set => SetField(ref _projectDescription, value);
        }

        public ObservableCollection<BrochureStat> Stats { get; } = new();

        public string Notes
        {
            get => _notes;
            set => SetField(ref _notes, value);
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

        public ICommand AddProjectCommand { get; }

        public ICommand RemoveProjectCommand { get; }

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
