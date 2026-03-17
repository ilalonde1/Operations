#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.GeneralTools
{
    public sealed class BrochureBuilderViewModel : INotifyPropertyChanged
    {
        private string _templateName;
        private string _companyName = string.Empty;
        private string _projectName = string.Empty;
        private string _logoPath = string.Empty;
        private string _projectDescription = string.Empty;
        private string _notes = string.Empty;
        private bool _isGenerating;

        public BrochureBuilderViewModel()
        {
            TemplateOptions = new ReadOnlyCollection<string>(new[]
            {
                "Corporate Profile",
                "Project Showcase",
                "Regional Overview"
            });

            _templateName = TemplateOptions[0];
            ProduceBrochureCommand = new RelayCommand(_ =>
            {
                MessageBox.Show(
                    "Generation not yet implemented",
                    "Sales Brochure Builder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ReadOnlyCollection<string> TemplateOptions { get; }

        public string TemplateName
        {
            get => _templateName;
            set => SetField(ref _templateName, value);
        }

        public string CompanyName
        {
            get => _companyName;
            set => SetField(ref _companyName, value);
        }

        public string ProjectName
        {
            get => _projectName;
            set => SetField(ref _projectName, value);
        }

        public string LogoPath
        {
            get => _logoPath;
            set => SetField(ref _logoPath, value);
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
