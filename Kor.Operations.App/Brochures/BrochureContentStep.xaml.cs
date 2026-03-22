#nullable enable
using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public partial class BrochureContentStep : UserControl
    {
        private static readonly Brush DragHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C36"));
        private static readonly Brush DefaultProjectBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
        private int _dragFromProjectIndex = -1;
        private bool _isDraggingProject;
        private int _dragFromPersonIndex = -1;
        private bool _isDraggingPerson;
        private int _dragFromOverviewIndex = -1;
        private bool _isDraggingOverview;

        public BrochureContentStep()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
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
            vm.ClearProjectFormPublic();
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

        private void ProjectDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingProject || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var contentPresenter = FindAncestor<ContentPresenter>(handle);
            if (contentPresenter?.DataContext is not BrochureProject project)
                return;

            var index = ProjectItemsControl.Items.IndexOf(project);
            if (index < 0)
                return;

            _dragFromProjectIndex = index;
            _isDraggingProject = true;

            try
            {
                DragDrop.DoDragDrop(handle, index, DragDropEffects.Move);
            }
            finally
            {
                _isDraggingProject = false;
            }
        }

        private void ProjectItems_Drop(object sender, DragEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null || _dragFromProjectIndex < 0)
                return;

            var point = e.GetPosition(ProjectItemsControl);
            var targetIndex = GetProjectIndexFromPoint(point);
            if (targetIndex < 0 || _dragFromProjectIndex == targetIndex)
            {
                _dragFromProjectIndex = -1;
                return;
            }

            var moveRequest = new Tuple<int, int>(_dragFromProjectIndex, targetIndex);
            if (vm.MoveProjectCommand.CanExecute(moveRequest))
                vm.MoveProjectCommand.Execute(moveRequest);

            _dragFromProjectIndex = -1;
        }

        private void ProjectItems_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DragHighlightBrush;
            border.BorderThickness = new Thickness(1, 2, 1, 1);
        }

        private void ProjectItems_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DefaultProjectBrush;
            border.BorderThickness = new Thickness(1);
        }

        private int GetProjectIndexFromPoint(Point point)
        {
            for (var i = 0; i < ProjectItemsControl.Items.Count; i++)
            {
                if (ProjectItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;

                var bounds = new Rect(
                    container.TranslatePoint(new Point(0, 0), ProjectItemsControl),
                    new Size(container.ActualWidth, container.ActualHeight));

                if (bounds.Contains(point))
                    return i;
            }

            return ProjectItemsControl.Items.Count > 0 ? ProjectItemsControl.Items.Count - 1 : -1;
        }

        private void PersonDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingPerson || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var contentPresenter = FindAncestor<ContentPresenter>(handle);
            if (contentPresenter?.DataContext is not BrochurePerson person)
                return;

            var index = PersonItemsControl.Items.IndexOf(person);
            if (index < 0)
                return;

            _dragFromPersonIndex = index;
            _isDraggingPerson = true;

            try
            {
                DragDrop.DoDragDrop(handle, index, DragDropEffects.Move);
            }
            finally
            {
                _isDraggingPerson = false;
            }
        }

        private void PersonItems_Drop(object sender, DragEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null || _dragFromPersonIndex < 0)
                return;

            var point = e.GetPosition(PersonItemsControl);
            var targetIndex = GetPersonIndexFromPoint(point);
            if (targetIndex < 0 || _dragFromPersonIndex == targetIndex)
            {
                _dragFromPersonIndex = -1;
                return;
            }

            var moveRequest = new Tuple<int, int>(_dragFromPersonIndex, targetIndex);
            if (vm.MovePersonCommand.CanExecute(moveRequest))
                vm.MovePersonCommand.Execute(moveRequest);

            _dragFromPersonIndex = -1;
        }

        private void PersonItems_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DragHighlightBrush;
            border.BorderThickness = new Thickness(1, 2, 1, 1);
        }

        private void PersonItems_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DefaultProjectBrush;
            border.BorderThickness = new Thickness(1);
        }

        private int GetPersonIndexFromPoint(Point point)
        {
            for (var i = 0; i < PersonItemsControl.Items.Count; i++)
            {
                if (PersonItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;

                var bounds = new Rect(
                    container.TranslatePoint(new Point(0, 0), PersonItemsControl),
                    new Size(container.ActualWidth, container.ActualHeight));

                if (bounds.Contains(point))
                    return i;
            }

            return PersonItemsControl.Items.Count > 0 ? PersonItemsControl.Items.Count - 1 : -1;
        }

        private void OverviewDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingOverview || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var contentPresenter = FindAncestor<ContentPresenter>(handle);
            if (contentPresenter?.DataContext is not BrochureOverviewSection overviewSection)
                return;

            var index = OverviewItemsControl.Items.IndexOf(overviewSection);
            if (index < 0)
                return;

            _dragFromOverviewIndex = index;
            _isDraggingOverview = true;

            try
            {
                DragDrop.DoDragDrop(handle, index, DragDropEffects.Move);
            }
            finally
            {
                _isDraggingOverview = false;
            }
        }

        private void OverviewItems_Drop(object sender, DragEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null || _dragFromOverviewIndex < 0)
                return;

            var point = e.GetPosition(OverviewItemsControl);
            var targetIndex = GetOverviewIndexFromPoint(point);
            if (targetIndex < 0 || _dragFromOverviewIndex == targetIndex)
            {
                _dragFromOverviewIndex = -1;
                return;
            }

            var moveRequest = new Tuple<int, int>(_dragFromOverviewIndex, targetIndex);
            if (vm.MoveOverviewSectionCommand.CanExecute(moveRequest))
                vm.MoveOverviewSectionCommand.Execute(moveRequest);

            _dragFromOverviewIndex = -1;
        }

        private void OverviewItems_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DragHighlightBrush;
            border.BorderThickness = new Thickness(1, 2, 1, 1);
        }

        private void OverviewItems_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DefaultProjectBrush;
            border.BorderThickness = new Thickness(1);
        }

        private int GetOverviewIndexFromPoint(Point point)
        {
            for (var i = 0; i < OverviewItemsControl.Items.Count; i++)
            {
                if (OverviewItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;

                var bounds = new Rect(
                    container.TranslatePoint(new Point(0, 0), OverviewItemsControl),
                    new Size(container.ActualWidth, container.ActualHeight));

                if (bounds.Contains(point))
                    return i;
            }

            return OverviewItemsControl.Items.Count > 0 ? OverviewItemsControl.Items.Count - 1 : -1;
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

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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

