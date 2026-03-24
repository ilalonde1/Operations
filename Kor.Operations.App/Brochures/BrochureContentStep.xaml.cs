#nullable enable
using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public partial class BrochureContentStep : UserControl
    {
        public BrochureContentStep()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) =>
            {
                if (DataContext is BrochureBuilderViewModel vm)
                    vm.PropertyChanged -= Vm_PropertyChanged;
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => UpdateProjectEditorVisibility();

        private void AddPhoto_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Select Photos",
                Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (var fileName in dialog.FileNames)
                vm.Project.Photos.Add(new BrochurePhoto
                {
                    FilePath = fileName,
                    ImageBytes = File.ReadAllBytes(fileName),
                    Caption = string.Empty
                });
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.Project.Photos.Clear();
        }

        private void ProjectTab_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            if (sender is not FrameworkElement element || element.DataContext is not BrochureProject project)
                return;

            if (vm.SelectedBlock?.Section is null)
                return;

            if (vm.EditProjectCommand.CanExecute(project))
            {
                vm.EditProjectCommand.Execute(project);
                UpdateProjectEditorVisibility(showForm: true);
            }
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.SelectedProjectIndex = -1;
            vm.IsEditingProject = false;
            vm.ClearProjectForm();
            UpdateProjectEditorVisibility(showForm: true);
        }

        private void AddPersonButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.Person.ClearForm();
            PersonForm.Visibility = Visibility.Visible;
        }

        private void EditPersonButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            if (e.Source is Button btn && btn.DataContext is BrochurePerson person)
            {
                vm.BeginEditPersonCommand.Execute(person);
                PersonForm.Visibility = Visibility.Visible;
            }
        }

        private void ProjectFormAction_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateProjectEditorVisibility()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is BrochureBuilderViewModel oldVm)
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            if (e.NewValue is BrochureBuilderViewModel newVm)
                newVm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BrochureBuilderViewModel.SelectedBlockIndex)
                && PersonForm.Visibility == Visibility.Visible)
            {
                if (DataContext is BrochureBuilderViewModel vm)
                    vm.CancelPersonEditCommand.Execute(null);
                PersonForm.Visibility = Visibility.Collapsed;
            }
        }

        private void PersonFormAction_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(() => PersonForm.Visibility = Visibility.Collapsed),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateProjectEditorVisibility(bool? showForm = null)
        {
            if (ProjectEditorForm is null || ProjectEditorEmptyState is null)
                return;

            var shouldShowForm = showForm ?? false;
            ProjectEditorForm.Visibility = shouldShowForm ? Visibility.Visible : Visibility.Collapsed;
            ProjectEditorEmptyState.Visibility = shouldShowForm ? Visibility.Collapsed : Visibility.Visible;
        }

    }

    public sealed class ProjectPageBreakAfterConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[1] is not int index)
                return false;

            if (values[0] is IEnumerable indices)
            {
                foreach (var item in indices)
                {
                    if (item is int breakIndex && breakIndex == index)
                        return true;
                }
            }

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class ReferenceEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return false;

            return ReferenceEquals(values[0], values[1]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

