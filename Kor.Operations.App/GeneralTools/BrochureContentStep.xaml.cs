#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.GeneralTools
{
    public partial class BrochureContentStep : UserControl
    {
        public BrochureContentStep()
        {
            InitializeComponent();
        }

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
                vm.Photos.Add(new BrochurePhoto { FilePath = fileName, Caption = string.Empty });
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.Photos.Clear();
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
                vm.EditProjectCommand.Execute(project);
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.SelectedProjectIndex = -1;
            vm.IsEditingProject = false;
            vm.ClearProjectFormPublic();
        }

        private void AddPersonButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            vm.PersonName = string.Empty;
            vm.PersonCredentials = string.Empty;
            vm.PersonBio = string.Empty;
            vm.PersonPhotoPath = string.Empty;
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
    }
}
