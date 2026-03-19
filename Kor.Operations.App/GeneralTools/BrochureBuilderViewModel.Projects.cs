#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.GeneralTools
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddProjectCommand { get; private set; } = null!;
        public ICommand EditProjectCommand { get; private set; } = null!;
        public ICommand SaveEditCommand { get; private set; } = null!;
        public ICommand CancelEditCommand { get; private set; } = null!;
        public ICommand MoveProjectCommand { get; private set; } = null!;
        public ICommand InsertProjectPageBreakCommand { get; private set; } = null!;
        public ICommand RemoveProjectFromSectionCommand { get; private set; } = null!;
        public ICommand RemovePhotoCommand { get; private set; } = null!;

        private void InitProjectCommands()
        {
            AddProjectCommand = new RelayCommand(ExecAddProject);
            EditProjectCommand = new RelayCommand(ExecEditProject);
            SaveEditCommand = new RelayCommand(ExecSaveEdit);
            CancelEditCommand = new RelayCommand(ExecCancelEdit);
            MoveProjectCommand = new RelayCommand(ExecMoveProject);
            InsertProjectPageBreakCommand = new RelayCommand(ExecInsertProjectPageBreak);
            RemoveProjectFromSectionCommand = new RelayCommand(ExecRemoveProjectFromSection);
            RemovePhotoCommand = new RelayCommand(ExecRemovePhoto);
        }

        private void ExecAddProject(object? _)
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

            if (!Project.ValidateForm()) return;

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
        }

        private void ExecEditProject(object? parameter)
        {
            if (parameter is not BrochureProject project) return;

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
        }

        private void ExecSaveEdit(object? _)
        {
            if (_editingProject is null) return;
            if (!Project.ValidateForm()) return;

            var block = _editingBlock;
            if (block?.Section is null) return;

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
        }

        private void ExecCancelEdit(object? _)
        {
            ClearProjectForm();
            IsEditingProject = false;
            _editingBlock = null;
            _editingProject = null;
        }

        private void ExecMoveProject(object? parameter)
        {
            if (parameter is not Tuple<int, int> moveRequest) return;
            if (SelectedBlock?.Section is not { } section) return;

            var (fromIndex, toIndex) = moveRequest;
            if (fromIndex < 0 || fromIndex >= section.Projects.Count ||
                toIndex < 0 || toIndex >= section.Projects.Count) return;

            var hadBreak = section.PageBreakAfterProjectIndex.Contains(fromIndex);
            section.PageBreakAfterProjectIndex.Remove(fromIndex);

            for (var i = 0; i < section.PageBreakAfterProjectIndex.Count; i++)
            {
                var bi = section.PageBreakAfterProjectIndex[i];
                if (bi > fromIndex) bi--;
                if (bi >= toIndex) bi++;
                section.PageBreakAfterProjectIndex[i] = bi;
            }

            if (hadBreak)
                section.PageBreakAfterProjectIndex.Add(toIndex - 1);

            section.PageBreakAfterProjectIndex.Sort();

            var project = section.Projects[fromIndex];
            section.Projects.RemoveAt(fromIndex);
            section.Projects.Insert(toIndex, project);

            RefreshBlock(SelectedBlock);
        }

        private void ExecInsertProjectPageBreak(object? parameter)
        {
            if (parameter is not int projectIndex) return;
            if (SelectedBlock?.Section is not { } section) return;

            if (section.PageBreakAfterProjectIndex.Contains(projectIndex))
                section.PageBreakAfterProjectIndex.Remove(projectIndex);
            else
                section.PageBreakAfterProjectIndex.Add(projectIndex);

            section.PageBreakAfterProjectIndex.Sort();
            NotifyProjectPageBreakStateChanged();
        }

        private void ExecRemoveProjectFromSection(object? parameter)
        {
            if (parameter is not BrochureProject project) return;
            var block = FindSectionBlockContaining(project);
            if (block?.Section is null) return;
            block.Section.Projects.Remove(project);
            RefreshBlock(block);
        }

        private void ExecRemovePhoto(object? parameter)
        {
            if (parameter is not BrochurePhoto photo) return;
            Project.Photos.Remove(photo);
        }
    }
}
